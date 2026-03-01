using System.Diagnostics;
using Terraria_Wiki.Services;
using Terraria_Wiki.Models;
using Microsoft.UI.Windowing;
namespace Terraria_Wiki
{
    public partial class App : Application
    {
        public static ManagerDbService? ManagerDb { get; private set; }
        public static ContentDbService? ContentDb { get; private set; }
        private readonly LocalWebServer _webServer;
        public static DataService? DataManager { get; private set; }
        public static LogService? LogManager { get; private set; }
        public static AppState? AppStateManager { get; private set; }
        public App(LocalWebServer webServer, ManagerDbService managerDb,   // 注入管理库
        ContentDbService contentDb, DataService dataService, LogService logService, AppState appState, AppService appService)
        {
            InitializeComponent();
            _webServer = webServer;
            ManagerDb = managerDb;
            ContentDb = contentDb;
            DataManager = dataService;
            LogManager = logService;
            AppStateManager = appState;


        }
        protected override async void OnStart()
        {
            base.OnStart();
            _webServer.Start();
            await ManagerDb.Init();
            await ContentDb.Init();
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
            DataManager.OnLog += (msg) => LogManager.AppendLog(msg);
            Debug.WriteLine($"[App] 启动完成！数据库路径：{ManagerDb.DatabasePath}，{ContentDb.DatabasePath}");

        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window window = new Window(new MainPage());
            window.Title = AppInfo.Current.Name;
            // 1. 恢复普通大小和位置（非最大化时的尺寸）
            RestoreWindowBounds(window);

            // 2. 窗口句柄（Handler）创建完成后，才能调用底层的 Windows API 去最大化
            window.Created += OnWindowCreated;

            // 3. 窗口销毁前保存状态
            window.Destroying += OnWindowDestroying;


            return window;

        }
        private void RestoreWindowBounds(Window window)
        {
            if (Preferences.Default.ContainsKey("WindowWidth"))
            {
                window.Width = Preferences.Default.Get("WindowWidth", 1000.0);
                window.Height = Preferences.Default.Get("WindowHeight", 650.0);

                double x = Preferences.Default.Get("WindowX", 100.0);
                double y = Preferences.Default.Get("WindowY", 100.0);

                window.X = x >= 0 ? x : 100;
                window.Y = y >= 0 ? y : 100;
            }
        }

        private void OnWindowCreated(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
#if WINDOWS
                // 获取 Windows 原生窗口对象
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow != null)
                {
                    // 获取 WinUI 3 的 AppWindow
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                    // 控制窗口状态的 Presenter
                    if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                    {
                        // 读取是否最大化
                        bool isMaximized = Preferences.Default.Get("IsMaximized", false);
                        if (isMaximized)
                        {
                            presenter.Maximize();
                        }
                    }
                }
#endif
            }
        }

        private void OnWindowDestroying(object? sender, EventArgs e)
        {
            if (sender is Window window)
            {
                bool isMaximized = false;

#if WINDOWS
                var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (nativeWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);

                    if (appWindow.Presenter is OverlappedPresenter presenter)
                    {
                        // ★ 修改这里：使用 OverlappedPresenterState.Maximized
                        isMaximized = presenter.State == OverlappedPresenterState.Maximized;
                        Preferences.Default.Set("IsMaximized", isMaximized);

                        // ★ 修改这里：使用 OverlappedPresenterState.Minimized
                        if (presenter.State == OverlappedPresenterState.Minimized)
                        {
                            return;
                        }
                    }
                }
#endif

                // 只有在【非最大化】的情况下，才保存尺寸和坐标。
                // 这样当你解除最大化时，窗口还能恢复到你之前调整的正常大小。
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

    }
}
