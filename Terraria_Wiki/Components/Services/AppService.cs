using Microsoft.AspNetCore.Components;
using Terraria_Wiki.Models;
using Microsoft.JSInterop;
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
                    App.AppStateManager.TriggerAlert("提示", "页面不存在");
                    return null;

                }

                if (page != null)
                {


                    WikiPageStringTime result = new WikiPageStringTime();
                    result.Title = page.Title;
                    result.Content = page.Content;
                    result.LastModified = page.LastModified.ToString("yyyy年MM月dd日 HH:mm");
                    App.AppStateManager.CurrentWikiPage = page.Title;
                    if (page.Title != "Terraria Wiki")
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
                App.AppStateManager.TempHistory.Add(tempHistory);

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
                    App.AppStateManager.TriggerAlert("提示", $"无法打开链接: {ex.Message}");
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
        public static void Init(NavigationManager navManager) 
        { 
            _navManager = navManager;

        
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
                App.AppStateManager.TriggerAlert("提示", "这已经是首页");
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
                App.AppStateManager.TriggerAlert("提示", "这已经是首页");
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
            var book = await wikiBook.GetItemAsync<WikiBook>(1);
            book.PageCount = await wikiContent.GetCountAsync<WikiPage>();
            book.RedirectCount = await wikiContent.GetCountAsync<WikiRedirect>();
            book.ResourceCount = await wikiContent.GetCountAsync<WikiAsset>();
            book.DataSize = FileHelper.GetSizeBytes(wikiContent.DatabasePath);
            await wikiBook.SaveItemAsync(book);
        }

        //窗口置顶
        public static void SetAlwaysOnTop(Window window, bool isAlwaysOnTop)
        {
            if (window?.Handler?.PlatformView == null) return;

#if WINDOWS
            // 获取 Windows 原生窗口实例
            var nativeWindow = window.Handler.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow != null)
            {
                // 通过 Win32 互操作获取 AppWindow
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                // 设置置顶属性
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = isAlwaysOnTop;
                }
            }
#elif MACCATALYST
            // 获取 Mac Catalyst 的底层 UIWindow
            var nativeWindow = window.Handler.PlatformView as UIKit.UIWindow;
            if (nativeWindow != null)
            {
                if (isAlwaysOnTop)
                {
                    // 将窗口层级调高（超过普通弹窗层级），实现置顶
                    nativeWindow.WindowLevel = UIKit.UIWindowLevel.Alert + 1;
                }
                else
                {
                    // 恢复普通层级
                    nativeWindow.WindowLevel = UIKit.UIWindowLevel.Normal;
                }
            }
#endif
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
