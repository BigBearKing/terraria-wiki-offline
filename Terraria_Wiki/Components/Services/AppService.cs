using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using Terraria_Wiki.Models;
#if ANDROID
using Terraria_Wiki.Platforms.Android;
#endif


#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
#endif


namespace Terraria_Wiki.Services
{
    public class AppService
    {
        private static NavigationManager _navManager;
        private static IJSRuntime _js;
        private const int MaxCachedPages = 32;
        private static readonly SemaphoreSlim _wikiSwitchLock = new(1, 1);
        private static readonly SemaphoreSlim _storageSwitchLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, Lazy<Task<WikiPage?>>> _pageCache = new();


        public AppService()
        {
            RegisterIframeActions();
        }

        public static async Task<bool> SwitchStorageLocationAsync(StorageLocationMode mode, string? customPath = null)
        {
            if (!await _storageSwitchLock.WaitAsync(0))
                return false;

            var storage = App.StoragePath!;
            var oldMode = storage.LocationMode;
            var oldCustomPath = storage.CustomPath;
            var oldRoot = storage.RootPath;
            var activeBook = App.AppStateManager!.ActiveWikiBook;
            MainPage? loadingPage = null;

            try
            {
                if (App.AppStateManager.HasActiveTasks)
                {
                    App.AppStateManager.TriggerAlert(
                        App.Localization!.Get("Common.Notice"),
                        App.Localization.Get("Settings.DataLocationBusy"));
                    return false;
                }

                var targetRoot = storage.ResolvePath(mode, customPath);
                if (string.Equals(Path.GetFullPath(oldRoot), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase))
                {
                    storage.SaveLocation(mode, customPath);
                    return true;
                }

                App.LogManager!.Info(App.Localization!.Get("AppService.DataMigrationStarted", oldRoot, targetRoot));
                await App.LogManager.FlushAsync();

                if (Application.Current?.Windows[0].Page is MainPage mainPage)
                {
                    loadingPage = mainPage;
                    loadingPage.ShowLoadingPopup(
                        App.Localization!.Get("Settings.DataLocationMigrationTitle"),
                        App.Localization.Get("Settings.DataLocationMigrationMessage"));
                }

                // 注意：R4 之后没有本地 HTTP 服务器需要停。迁移期间会关闭并重开数据库连接，
                // 若此时 iframe 正好在取内容，WikiContentProvider 会抛异常并由虚拟主机层
                // 返回 500（已在 logger 里留痕），不会静默失败。
                await App.ContentDb!.CloseConnection();
                InvalidatePageCache();
                await App.ManagerDb!.CloseConnection();
                await storage.MigrateAsync(mode, customPath);
                App.LogManager.Info(App.Localization.Get("AppService.DataMigrationCopied"));
                App.AppStateManager.SetDataRootPath(storage.RootPath);
                await App.LogManager!.SwitchStorageRootAsync(storage.RootPath);
                App.LogManager.Info(App.Localization.Get("AppService.DataMigrationSwitched", storage.RootPath));

                await App.ManagerDb.SwitchDatabaseAsync(Path.Combine(targetRoot, "Manager.db"));
                await App.ManagerDb.Init(true);

                if (activeBook != null)
                {
                    await App.ContentDb.SwitchDatabaseAsync(Path.Combine(targetRoot, activeBook.DataFolder, "data.db"));
                    await App.ContentDb.Init(true, activeBook);
                }

                App.DataManager!.InitializeSettings();
                if (activeBook != null)
                    await RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);

                App.AppStateManager.ResetWikiNavigation();
                App.AppStateManager.NotifyWikiBookSwitched();
                App.LogManager.Info(App.Localization.Get("AppService.DataMigrationCompleted"));

                loadingPage?.HideLoadingPopup();
                bool deleteOldData = await Application.Current!.Windows[0].Page!.DisplayAlertAsync(
                    App.Localization!.Get("Settings.DeleteOldDataTitle"),
                    App.Localization.Get("Settings.DeleteOldDataDescription"),
                    App.Localization.Get("Settings.DeleteOldData"),
                    App.Localization.Get("Settings.KeepOldData"));

                if (deleteOldData)
                {
                    try
                    {
                        // 递归删整份旧数据：文件多时要几百毫秒，别压在 UI 线程上。
                        await Task.Run(() => Directory.Delete(oldRoot, true));
                    }
                    catch (Exception deleteException)
                    {
                        App.AppStateManager.TriggerAlert(
                            App.Localization.Get("Common.Error"),
                            App.Localization.Get("Settings.DeleteOldDataFailed", deleteException.Message));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                App.LogManager?.Error(App.Localization!.Get("AppService.DataMigrationFailed"), ex);
                storage.SaveLocation(oldMode, oldCustomPath);
                App.AppStateManager.SetDataRootPath(oldRoot);

                try
                {
                    await App.LogManager!.SwitchStorageRootAsync(oldRoot);
                    await App.ManagerDb!.SwitchDatabaseAsync(Path.Combine(oldRoot, "Manager.db"));
                    await App.ManagerDb.Init(true);
                    if (activeBook != null)
                    {
                        await App.ContentDb!.SwitchDatabaseAsync(Path.Combine(oldRoot, activeBook.DataFolder, "data.db"));
                        await App.ContentDb.Init(true, activeBook);
                    }
                    App.DataManager!.InitializeSettings();
                }
                catch
                {
                }

                App.AppStateManager.TriggerAlert(
                    App.Localization!.Get("Common.Error"),
                    App.Localization.Get("Settings.DataLocationMigrationFailed", ex.Message));
                return false;
            }
            finally
            {
                loadingPage?.HideLoadingPopup();
                _storageSwitchLock.Release();
            }
        }

        private void RegisterIframeActions()
        {
            IframeBridge.StructuredActions["PageRedirectAsync"] = async title => await PageRedirectAsync(title);
            IframeBridge.Actions["SyncCurrentWikiPage"] = SyncCurrentWikiPageAsync;
            IframeBridge.Actions["GetRedirectedTitleAndAnchorAsync"] = GetRedirectedTitleAndAnchorAsync;
            IframeBridge.Actions["SaveToTabHistory"] = SaveToTabHistoryAsync;
            IframeBridge.Actions["WikiBackAsync"] = WikiBackActionAsync;
            IframeBridge.Actions["OpenInNewTab"] = OpenInNewTabAsync;
            IframeBridge.Actions["OpenExternalWebsite"] = OpenExternalWebsiteAsync;
            IframeBridge.Actions["CopyTextToClipboard"] = CopyTextToClipboardAsync;
            IframeBridge.Actions["CopyImageToClipboard"] = CopyImageToClipboardAsync;
            IframeBridge.Actions["GetIframeLocalization"] = GetIframeLocalizationAsync;
        }

        private Task<string> GetIframeLocalizationAsync(string _)
        {
            var translations = App.Localization!.GetWebTranslations();
            return Task.FromResult(IframeBridge.ObjToJson(translations));
        }

        private Task<string> SyncCurrentWikiPageAsync(string title)
        {
            App.AppStateManager.CurrentWikiPage = title;

            var tab = App.AppStateManager.GetActiveTab();
            if (tab != null)
                tab.CurrentPage = new PageViewInfo { Title = title, Position = 0 };

            return Task.FromResult<string>(null);
        }

        private async Task<WikiPageStringTime?> PageRedirectAsync(string title)
        {
            var cacheKey = GetPageCacheKey(title);
            var page = await GetCachedPageAsync(title, cacheKey);

            if (page == null)
            {
                App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization!.Get("AppService.PageNotFound"));
                return null;
            }

            var result = new WikiPageStringTime
            {
                Title = page.Title,
                Content = page.Content,
                LastModified = page.LastModified.ToString(App.Localization!.Get("AppService.DateFormat"))
            };
            App.AppStateManager.CurrentWikiPage = page.Title;

            var tab = App.AppStateManager.GetActiveTab();
            if (tab != null)
                tab.CurrentPage = new PageViewInfo { Title = page.Title, Position = 0 };

            if (page.Title != App.AppStateManager.ActiveWikiBook.DefaultPageTitle)
                Task.Run(async () => await SaveToHistoryAsync(page.Title));

            return result;
        }

        private static string GetPageCacheKey(string title)
        {
            var bookKey = App.AppStateManager.ActiveWikiBook?.DataFolder ?? string.Empty;
            return $"{bookKey}\n{title}";
        }

        private static async Task<WikiPage?> GetCachedPageAsync(string title, string cacheKey)
        {
            if (_pageCache.Count >= MaxCachedPages && !_pageCache.ContainsKey(cacheKey))
                _pageCache.Clear();

            var lazyPage = _pageCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<WikiPage?>>(
                    () => LoadPageAsync(title),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                var page = await lazyPage.Value;
                if (page == null)
                    _pageCache.TryRemove(cacheKey, out _);
                return page;
            }
            catch
            {
                _pageCache.TryRemove(cacheKey, out _);
                throw;
            }
        }

        private static readonly System.Text.RegularExpressions.Regex LazyImgRegex =
            new(@"<img(?![^>]*\bloading\s*=)", System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                               | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// 给页面里的 &lt;img&gt; 补上 <c>loading="lazy"</c>。
        ///
        /// ★ 为什么需要：爬虫在 <c>DataService.ProcessImages</c> 里把 loading 属性一并删掉了
        ///   （与 data-file-width / srcset 一起清），所以库里存的 HTML **一张懒加载图都没有**。
        ///   泰拉中文"盔甲"页有 1134 个 &lt;img&gt;、灾厄 "2.1.0" 页有 3790 个，全部一次性解码 ——
        ///   安卓上渲染进程会因内存不足被系统回收，而 Chromium 在"没人接管 onRenderProcessGone"
        ///   时会**连带杀掉整个应用**，表现就是"切到某页卡一下然后闪退"。
        ///   加 lazy 之后只有进入视口附近的图片才解码，重页面的渲染内存大幅下降。
        ///
        /// 放在这里（页面交给 iframe 之前）而不是只改爬虫，因为：
        ///   ① 对**已经下载好的旧数据**立即生效，用户不用重新下几百 MB；
        ///   ② 只在一页第一次加载时做一次（结果留在 _pageCache 里），不是每次渲染都算。
        /// 已经有 loading 的标签不会重复添加。
        /// </summary>
        private static string AddLazyLoading(string? html)
        {
            if (string.IsNullOrEmpty(html))
                return html ?? string.Empty;

            // 绝大多数页面一个 loading 都没有 —— 走 Replace 快路径，避免整页跑正则。
            if (html.IndexOf("loading=", StringComparison.OrdinalIgnoreCase) < 0)
                return html.Replace("<img ", "<img loading=\"lazy\" ", StringComparison.OrdinalIgnoreCase);

            return LazyImgRegex.Replace(html, "<img loading=\"lazy\"");
        }

        private static async Task<WikiPage?> LoadPageAsync(string title)
        {
            var page = await App.ContentDb.GetItemAsync<WikiPage>(title);
            if (page != null)
            {
                // 只处理一次；结果随 WikiPage 一起留在 _pageCache 里。
                page.Content = AddLazyLoading(page.Content);
                return page;
            }

            var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(title);
            if (redirect == null)
                return null;

            var target = await App.ContentDb.GetItemAsync<WikiPage>(redirect.ToTarget);
            if (target != null)
                target.Content = AddLazyLoading(target.Content);

            return target;
        }

        public static void InvalidatePageCache(string? title = null)
        {
            if (title == null)
            {
                _pageCache.Clear();
                // 整库替换/页面更新时，资源缓存也必须一起失效，否则会继续返回旧图片。
                WikiContentProvider.InvalidateCache();
                return;
            }

            _pageCache.TryRemove(GetPageCacheKey(title), out _);
        }

        private async Task<string> GetRedirectedTitleAndAnchorAsync(string input)
        {
            if (input.IndexOf('#') == -1 && await App.ContentDb.ItemExistsAsync<WikiRedirect>(input))
            {
                var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(input);
                input = redirect.ToTarget;
            }

            var parts = input.Split(new[] { '#' }, 2);
            return IframeBridge.ObjToJson(new TitleWithAnchor
            {
                Title = parts[0],
                Anchor = parts.Length > 1 ? parts[1] : null
            });
        }

        private Task<string> SaveToTabHistoryAsync(string args)
        {
            var pageViewInfo = IframeBridge.JsonToObj<PageViewInfo>(args);
            var tab = App.AppStateManager.GetActiveTab();
            if (tab != null)
                tab.TabHistory.Add(pageViewInfo);
            return Task.FromResult<string>(null);
        }

        private async Task<string> WikiBackActionAsync(string args)
        {
            if (Preferences.Default.Get("IsSideButtonBack", true))
                await WikiBackAsync();
            return null;
        }

        private async Task<string> OpenInNewTabAsync(string args)
        {
            var pageViewInfo = IframeBridge.JsonToObj<PageViewInfo>(args);
            if (pageViewInfo != null && !string.IsNullOrEmpty(pageViewInfo.Title))
            {
                var title = pageViewInfo.Title;
                int hashIndex = title.IndexOf('#');
                if (hashIndex != -1) title = title.Substring(0, hashIndex);

                bool exists = await App.ContentDb.ItemExistsAsync<WikiPage>(title)
                              || await App.ContentDb.ItemExistsAsync<WikiRedirect>(title);
                if (!exists)
                {
                    App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization.Get("AppService.PageNotFound"));
                    return null;
                }
            }

            var tab = await AddTabAsync(pageViewInfo);
            await SwitchToTabAsync(tab.Id);
            return null;
        }

        private async Task<string> OpenExternalWebsiteAsync(string url)
        {
            try
            {
                await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization.Get("AppService.CannotOpenLink", ex.Message));
            }
            return null;
        }

        private Task<string> CopyTextToClipboardAsync(string text)
        {
            Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
            return Task.FromResult<string>(null);
        }

        private async Task<string> CopyImageToClipboardAsync(string src)
        {
            WikiAsset asset = await App.ContentDb.GetItemAsync<WikiAsset>(src);
            byte[] imageBytes = asset?.Data;
            if (imageBytes != null)
            {
#if WINDOWS
                await CopyImageToClipboardWindowsAsync(imageBytes);
#endif
            }
            return null;
        }
        public static void Init(NavigationManager navManager, IJSRuntime js)
        {
            _navManager = navManager;
            _js = js;


        }


        private static async Task SaveToHistoryAsync(string title)
        {
            var history = new WikiHistory
            {
                WikiTitle = title,
                ReadAt = DateTime.Now,
                DateKey = DateTime.Now.ToString("yyyy-MM-dd")
            };
            await App.ContentDb.SaveHistoryAsync(history);

        }


        public static async Task WikiBackAsync()
        {
            var list = App.AppStateManager.TabHistory;
            var listcount = list.Count;
            if (listcount != 0)
            {
                await IframeBridge.CallJsAsync("BackToPage", IframeBridge.ObjToJson(list[listcount - 1]));
                list.RemoveAt(listcount - 1);
            }
            else
            {
                App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization!.Get("AppService.AlreadyHome"));
            }

        }

        public static async Task WikiBackHomeAsync()
        {
            var list = App.AppStateManager.TabHistory;
            var currentPage = App.AppStateManager.CurrentWikiPage;
            var defaultPage = App.AppStateManager.ActiveWikiBook?.DefaultPageTitle
                ?? App.AppStateManager.ActiveWikiBook?.Title
                ?? string.Empty;
            var isCurrentPageHome = string.IsNullOrWhiteSpace(currentPage)
                || string.Equals(currentPage, defaultPage, StringComparison.OrdinalIgnoreCase);

            if (list.Count != 0 || !isCurrentPageHome)
            {
                await IframeBridge.CallJsAsync("BackHome", "");
                list.Clear();
                App.AppStateManager.CurrentWikiPage = defaultPage;

                var activeTab = App.AppStateManager.GetActiveTab();
                if (activeTab != null)
                {
                    activeTab.CurrentPage = new PageViewInfo
                    {
                        Title = defaultPage,
                        Position = 0
                    };
                }
            }
            else
            {
                App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization!.Get("AppService.AlreadyHome"));
            }

        }

        public static async Task OpenPageAsync(string title)
        {
            await IframeBridge.CallJsAsync("GotoPage", title);
            AppService.NavigateTo("home");
        }

        public static async Task WikiRefreshAsync()
        {
            // 任务体现在跑在线程池上（见 AppTaskRunner），而这里要动 MainPage 的原生 WebView
            // 并且通过 JS 通知 iframe —— 都必须在主线程。
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                App.AppStateManager.TabHistory.Clear();
                if (Application.Current?.Windows.FirstOrDefault()?.Page is MainPage mainPage)
                    await mainPage.ClearWebViewCacheAsync();
                await IframeBridge.CallJsAsync("ClearPage", "");
                await IframeBridge.CallJsAsync("BackHome", "");
            });
        }

        public static async Task SwitchToTabAsync(string tabId)
        {
            // 1. 保存当前激活 tab 的当前页面状态
            var currentTab = App.AppStateManager.GetActiveTab();
            if (currentTab != null && App.AppStateManager.CurrentWikiPage != null)
            {
                // 通过 JS 获取当前滚动位置并保存
                var positionStr = await _js.InvokeAsync<string>("getCurrentIframePosition");
                float position = string.IsNullOrEmpty(positionStr) ? 0 : float.Parse(positionStr);

                currentTab.CurrentPage = new PageViewInfo
                {
                    Title = App.AppStateManager.CurrentWikiPage,
                    Position = position
                };
            }

            // 2. 切换到新 tab（切换决策由服务层负责）
            App.AppStateManager.ActiveTabId = tabId;
            var tab = App.AppStateManager.GetActiveTab();
            if (tab == null) return;

            // 3. 恢复新 tab 的当前页面状态
            if (tab.CurrentPage != null)
            {
                await IframeBridge.CallJsAsync("BackToPage", IframeBridge.ObjToJson(tab.CurrentPage));
            }
            else if (tab.TabHistory.Count > 0)
            {
                var lastHistory = tab.TabHistory[^1];
                await IframeBridge.CallJsAsync("BackToPage", IframeBridge.ObjToJson(lastHistory));
            }
            else
            {
                await IframeBridge.CallJsAsync("BackHome", "");
            }
        }

        public static async Task CloseTabAsync(string tabId)
        {
            // 1. 计算关闭 tab 的位置（用于确定关闭后激活的相邻 tab）
            var tabs = App.AppStateManager.Tabs;
            var closingIndex = -1;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Id == tabId) { closingIndex = i; break; }
            }
            if (closingIndex == -1 || tabs.Count <= 1) return;

            var isClosingActive = tabId == App.AppStateManager.ActiveTabId;

            // 2. 关闭 tab
            App.AppStateManager.CloseTab(tabId);

            // 3. 若关闭的是当前激活 tab，切换到相邻 tab（保存/恢复逻辑由 SwitchToTabAsync 统一处理）
            if (isClosingActive)
            {
                var newIndex = Math.Min(closingIndex, tabs.Count - 1);
                await SwitchToTabAsync(tabs[newIndex].Id);
            }
        }

        public static async Task<TabModel> AddTabAsync(PageViewInfo pageViewInfo = null)
        {
            var tab = App.AppStateManager.AddTab();
            if (tab == null)
            {
                App.AppStateManager.TriggerAlert(
                    App.Localization!.Get("Common.Notice"),
                    App.Localization!.Get("TabBar.MaxReached"));

            }
            if (pageViewInfo != null)
            {
                tab.CurrentPage = pageViewInfo;
            }
            else
            {
                var defaultPageTitle = App.AppStateManager.ActiveWikiBook?.DefaultPageTitle
                    ?? App.AppStateManager.ActiveWikiBook?.Title;

                if (!string.IsNullOrWhiteSpace(defaultPageTitle))
                {
                    tab.Title = defaultPageTitle;
                    tab.CurrentPage = new PageViewInfo
                    {
                        Title = defaultPageTitle,
                        Position = 0
                    };
                }
            }
            return tab;
        }

        // 跳转页面
        public static void NavigateTo(string pageName)
        {
            pageName = pageName.Trim('/');
            if (App.AppStateManager.CurrentPage == pageName)
                return;
            if (App.AppStateManager.IsSmallScreen)
            {
                App.AppStateManager.SidebarIsExpanded = false;
            }
            App.AppStateManager.CurrentPage = pageName;
            _navManager.NavigateTo($"/{pageName}", false, true);
        }

        public static async Task<bool> SwitchWikiBookAsync(int wikiBookId)
        {
            if (!await _wikiSwitchLock.WaitAsync(0))
                return false;

            try
            {
                if (App.AppStateManager.HasActiveTasks)
                {
                    App.AppStateManager.TriggerAlert(
                        App.Localization!.Get("Common.Notice"),
                        App.Localization.Get("AppService.SwitchWikiBusy"));
                    return false;
                }

                var book = await App.ManagerDb.GetItemAsync<WikiBook>(wikiBookId);
                if (book == null)
                    return false;

                var contentDbPath = Path.Combine(
                    App.StoragePath!.RootPath,
                    book.DataFolder,
                    "data.db");

                InvalidatePageCache();
                await App.ContentDb.SwitchDatabaseAsync(contentDbPath);
                await App.ContentDb.Init(false, book);

                App.AppStateManager.ActiveWikiBookId = book.Id;
                App.AppStateManager.ActiveWikiBook = book;
                await RestoreDownloadTaskStateAsync(book.Id);
                // ★ 不拉回 home：用户是在数据页点的切换，切完应该留在原地（该页自己会刷新新书的状态）。
                //   以前这里既把 CurrentPage 设成 home，又 NavigateTo("/home")，所以切完就被弹回主界面。
                App.AppStateManager.ResetWikiNavigation(goHome: false);
                await RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                App.AppStateManager.NotifyWikiBookSwitched();
                return true;
            }
            catch (Exception ex)
            {
                App.AppStateManager.TriggerAlert(
                    App.Localization!.Get("Common.Notice"),
                    App.Localization.Get("AppService.SwitchWikiFailed", ex.Message));
                return false;
            }
            finally
            {
                _wikiSwitchLock.Release();
            }
        }

        public static async Task RestoreDownloadTaskStateAsync(int wikiId)
        {
            var task = (await App.ManagerDb!.GetItemsAsync<AppTask>())
                .Where(t => t.WikiId == wikiId &&
                            t.IsDownloadTask() &&
                            t.Status is AppTaskStatus.Paused or AppTaskStatus.Interrupted)
                .OrderByDescending(t => t.UpdatedTime)
                .FirstOrDefault();

            App.AppStateManager!.SetCurrentDownloadTask(task);
        }



        //刷新数据库
        public static async Task RefreshWikiBookAsync(DatabaseService wikiBook, DatabaseService wikiContent)
        {
            var book = await wikiBook.GetItemAsync<WikiBook>(App.AppStateManager.ActiveWikiBookId);
            book.PageCount = await wikiContent.GetCountAsync<WikiPage>();
            book.RedirectCount = await wikiContent.GetCountAsync<WikiRedirect>();
            book.ResourceCount = await wikiContent.GetCountAsync<WikiAsset>();
            book.DataSize = FileHelper.GetSizeBytes(wikiContent.DatabasePath);
            await wikiBook.SaveItemAsync(book);
            App.AppStateManager.ActiveWikiBook = book; // 回写缓存
        }

        //复制图片到剪切板
#if WINDOWS

        public async Task CopyImageToClipboardWindowsAsync(byte[] imageBytes)
        {
            try
            {

                // 2. 创建 Windows 剪贴板数据包
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();

                dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

                // 3. 将 byte[] 转为 Windows 随机访问流 (InMemoryRandomAccessStream)
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);

                // 4. 设置剪贴板位图内容
                var streamRef = RandomAccessStreamReference.CreateFromStream(stream);
                dataPackage.SetBitmap(streamRef);

                // 5. 写入系统剪贴板
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                // 这一步很重要：刷新剪贴板，确保程序关闭后内容依然存在
                Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"复制图片失败: {ex.Message}");
            }
        }
#endif


    }
}
