using Terraria_Wiki.Models;
using Terraria_Wiki.Services;
using System.Runtime.InteropServices;

namespace Terraria_Wiki
{
    public partial class App : Application
    {
        public static ManagerDbService? ManagerDb { get; private set; }
        public static ContentDbService? ContentDb { get; private set; }
        public static LocalWebServer? WebServer { get; private set; }
        public static DataService? DataManager { get; private set; }
        public static LogService? LogManager { get; private set; }
        public static AppState? AppStateManager { get; private set; }

#if WINDOWS
        // 引入纯底层 Win32 API，AOT 绝对无法裁剪它们
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd); // 判断是否最大化

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd); // 判断是否最小化

        private const int SW_MAXIMIZE = 3; // 最大化指令
#endif

        public App(LocalWebServer webServer, ManagerDbService managerDb,
        ContentDbService contentDb, DataService dataService, LogService logService, AppState appState, AppService appService
#if IOS
        ,BurnInProtectionService burnInProtectionService)
#else
        )
#endif
        {
            WebServer = webServer;
            ManagerDb = managerDb;
            ContentDb = contentDb;
            DataManager = dataService;
            LogManager = logService;
            AppStateManager = appState;
            ThemeService.InitTheme();
            _ = InitializeAsync();
            InitializeComponent();
#if IOS
            MainPage = new MainPage(burnInProtectionService);
#else
            MainPage = new MainPage();
#endif
        }

        private async Task InitializeAsync()
        {
            WebServer.Start();
            await ManagerDb.Init();
            await ContentDb.Init();
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
        }

#if WINDOWS
        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window window = base.CreateWindow(activationState);
            window.Title = AppInfo.Current.Name;
#if RELEASE
            window.MinimumWidth = 400;
            window.MinimumHeight = 300;
#endif
            // 1. 立即读取历史偏好
            bool isMaximized = Preferences.Default.Get("IsMaximized", false);
            double width = Preferences.Default.Get("WindowWidth", 1000.0);
            double height = Preferences.Default.Get("WindowHeight", 650.0);
            double x = Preferences.Default.Get("WindowX", 100.0);
            double y = Preferences.Default.Get("WindowY", 100.0);

            window.Width = width;
            window.Height = height;
            window.X = x >= 0 ? x : 100;
            window.Y = y >= 0 ? y : 100;

            // 2. 在 Handler 绑定后，直接用 Win32 API 暴力最大化
            window.HandlerChanged += (s, e) =>
            {
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow != null && nativeWindow.AppWindow != null)
                {
                    // 拿到真实的物理句柄 HWND
                    IntPtr hwnd = (IntPtr)nativeWindow.AppWindow.Id.Value;

                    if (isMaximized && hwnd != IntPtr.Zero)
                    {
                        // 将指令扔给系统的消息队列，彻底绕过 MAUI 的布局覆盖
                        nativeWindow.DispatcherQueue.TryEnqueue(() =>
                        {
                            ShowWindow(hwnd, SW_MAXIMIZE);
                        });
                    }
                }
            };

            // 3. 注册销毁事件
            window.Destroying += OnWindowDestroying;

            return window;
        }

        private void OnWindowDestroying(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                bool isMaximized = false;
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;

                if (nativeWindow != null && nativeWindow.AppWindow != null)
                {
                    IntPtr hwnd = (IntPtr)nativeWindow.AppWindow.Id.Value;

                    if (hwnd != IntPtr.Zero)
                    {
                        // 使用 Win32 API 侦测窗口状态，彻底抛弃 OverlappedPresenter
                        if (IsIconic(hwnd))
                        {
                            // 如果是最小化状态关的，直接退出，不保存任何坐标
                            return;
                        }

                        isMaximized = IsZoomed(hwnd);
                        Preferences.Default.Set("IsMaximized", isMaximized);
                    }
                }

                // 非最大化时保存尺寸
                if (!isMaximized)
                {
                    if (window.X < -1000 || window.Y < -1000) return;

                    Preferences.Default.Set("WindowWidth", window.Width);
                    Preferences.Default.Set("WindowHeight", window.Height);
                    Preferences.Default.Set("WindowX", window.X);
                    Preferences.Default.Set("WindowY", window.Y);
                }
            }
        }
#endif
    }
}