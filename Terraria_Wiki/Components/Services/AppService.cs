using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
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
        private static readonly SemaphoreSlim _wikiSwitchLock = new(1, 1);
        private static readonly SemaphoreSlim _storageSwitchLock = new(1, 1);


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

                App.WebServer?.Stop();
                await App.ContentDb!.CloseConnection();
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

                await App.WebServer!.Start();
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
                        Directory.Delete(oldRoot, true);
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
                    await App.WebServer!.Start();
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
            IframeBridge.Actions["PageRedirectAsync"] = PageRedirectAsync;
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

        private async Task<string> PageRedirectAsync(string title)
        {
            WikiPage page;
            if (await App.ContentDb.ItemExistsAsync<WikiPage>(title))
                page = await App.ContentDb.GetItemAsync<WikiPage>(title);
            else if (await App.ContentDb.ItemExistsAsync<WikiRedirect>(title))
            {
                var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(title);
                page = await App.ContentDb.GetItemAsync<WikiPage>(redirect.ToTarget);
            }
            else
            {
                App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization!.Get("AppService.PageNotFound"));
                return null;
            }

            if (page == null) return null;

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

            return IframeBridge.ObjToJson(result);
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
            var listcount = list.Count;
            if (listcount != 0)
            {
                await IframeBridge.CallJsAsync("BackHome", "");
                list.Clear();
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
            App.AppStateManager.TabHistory.Clear();
            await IframeBridge.CallJsAsync("ClearPage", "");
            await IframeBridge.CallJsAsync("BackHome", "");
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
                tab.CurrentPage = null;
            }
            return tab;
        }

        // 跳转页面
        public static void NavigateTo(string pageName)
        {
            if (App.AppStateManager.CurrentPage == pageName)
                return;
            if (App.AppStateManager.IsSmallScreen)
            {
                App.AppStateManager.SidebarIsExpanded = false;
            }
            App.AppStateManager.CurrentPage = pageName;
            _navManager.NavigateTo(App.AppStateManager.CurrentPage);
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

                await App.ContentDb.SwitchDatabaseAsync(contentDbPath);
                await App.ContentDb.Init(false, book);

                App.AppStateManager.ActiveWikiBookId = book.Id;
                App.AppStateManager.ActiveWikiBook = book;
                await RestoreDownloadTaskStateAsync(book.Id);
                App.AppStateManager.ResetWikiNavigation();
                await RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                App.AppStateManager.NotifyWikiBookSwitched();
                _navManager.NavigateTo("home");
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
                            t.Status is AppTaskStatus.Paused or AppTaskStatus.Interrupted or AppTaskStatus.Failed)
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
