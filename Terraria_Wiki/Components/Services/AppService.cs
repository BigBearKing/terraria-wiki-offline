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


        public AppService()
        {
            IframeBridge.Actions["PageRedirectAsync"] = async (title) =>
            {
                WikiPage page;
                if (await App.ContentDb.ItemExistsAsync<WikiPage>(title))
                {
                    page = await App.ContentDb.GetItemAsync<WikiPage>(title);
                }
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

                if (page != null)
                {


                    WikiPageStringTime result = new WikiPageStringTime();
                    result.Title = page.Title;
                    result.Content = page.Content;
                    result.LastModified = page.LastModified.ToString(App.Localization!.Get("AppService.DateFormat"));
                    App.AppStateManager.CurrentWikiPage = page.Title;

                    // 更新当前 tab 的 CurrentPage（暂时设为 null，等 JS 返回位置后会更新）
                    var tab = App.AppStateManager.GetActiveTab();
                    if (tab != null)
                    {
                        tab.CurrentPage = new TempHistory
                        {
                            Title = page.Title,
                            Position = 0
                        };
                    }

                    if (page.Title != App.AppStateManager.ActiveWikiBook.DefaultPageTitle)
                        Task.Run(async () => await SaveToHistoryAsync(page.Title));


                    return IframeBridge.ObjToJson(result);
                }
                else
                {
                    return null;
                }

            };

            IframeBridge.Actions["GetRedirectedTitleAndAnchorAsync"] = async (input) =>
            {
                // 1. 如果没有锚点，先检查是否需要重定向，如果需要则替换 input
                if (input.IndexOf('#') == -1 && await App.ContentDb.ItemExistsAsync<WikiRedirect>(input))
                {
                    var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(input);
                    input = redirect.ToTarget; // 此时 input 变成了目标字符串（可能带#，也可能不带）
                }

                // 2. 统一处理分割逻辑 (Split只需写一次)
                // 限制只分割成2部分，确保只取第一个#之后的内容作为锚点
                var parts = input.Split(new[] { '#' }, 2);

                var result = new TitleWithAnchor
                {
                    Title = parts[0],
                    Anchor = parts.Length > 1 ? parts[1] : null
                };

                return IframeBridge.ObjToJson(result);
            };

            IframeBridge.Actions["SaveToTempHistory"] = async (args) =>
            {
                TempHistory tempHistory = IframeBridge.JsonToObj<TempHistory>(args);

                // 获取当前激活 tab 并直接操作其 TempHistory
                var tab = App.AppStateManager.GetActiveTab();
                if (tab != null)
                {
                    tab.TempHistory.Add(tempHistory);
                    // 可选：触发通知（如果需要 UI 更新）
                    // App.AppStateManager.OnPropertyChanged(nameof(App.AppStateManager.TempHistory));
                }

                return null;
            };

            IframeBridge.Actions["WikiBackAsync"] = async (args) =>
            {
                if (Preferences.Default.Get("IsSideButtonBack", true))
                    await WikiBackAsync();
                return null;
            };

            IframeBridge.Actions["OpenExternalWebsite"] = async (url) =>
            {
                try
                {
                    await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
                }
                catch (Exception ex)
                {
                    App.AppStateManager.TriggerAlert(App.Localization!.Get("Common.Notice"), App.Localization!.Get("AppService.CannotOpenLink", ex.Message));
                }
                return null;
            };

            IframeBridge.Actions["CopyTextToClipboard"] = async (text) =>
            {
                Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
                return null;
            };

            IframeBridge.Actions["CopyImageToClipboard"] = async (src) =>
            {

                WikiAsset asset = await App.ContentDb.GetItemAsync<WikiAsset>(src);
                byte[] imageBytes = asset?.Data;
                if (imageBytes != null)
                {
#if WINDOWS
                    CopyImageToClipboardWindowsAsync(imageBytes);
#endif
                }
                return null;
            };
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
            var list = App.AppStateManager.TempHistory;
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
            var list = App.AppStateManager.TempHistory;
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
            App.AppStateManager.TempHistory.Clear();
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

                currentTab.CurrentPage = new TempHistory
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
            else if (tab.TempHistory.Count > 0)
            {
                var lastHistory = tab.TempHistory[^1];
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

        public static async Task AddTabAsync()
        {
            var tab = App.AppStateManager.AddTab();
            if (tab == null)
            {
                App.AppStateManager.TriggerAlert(
                    App.Localization!.Get("Common.Notice"),
                    App.Localization!.Get("TabBar.MaxReached"));
                return;
            }

            // 切换到新 tab（保存旧 tab 状态/恢复新 tab 状态由 SwitchToTabAsync 统一处理）
            await SwitchToTabAsync(tab.Id);
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

        //重启软件
        public static void RestartApp()
        {

            string exePath = Environment.ProcessPath;
            System.Diagnostics.Process.Start(exePath);
            Application.Current.Quit();

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
