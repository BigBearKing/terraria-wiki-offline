#if ANDROID
using Android.Views;
using Android.Window;
using AndroidX.Core.View;
using Microsoft.Maui.Devices;
#endif

using Terraria_Wiki.Services;
using Microsoft.AspNetCore.Components.WebView;

namespace Terraria_Wiki
{
    public partial class MainPage : ContentPage
    {
#if WINDOWS
        private bool _windowsIntegrationInitialized;
        private bool _dragBridgeRegistered;
        private bool _titleBarConfigured;
#endif
#if IOS
        private readonly BurnInProtectionService _burnInService;
        private float _originalBrightness = 0.5f;
#endif

#if IOS
        public MainPage(BurnInProtectionService burnInService)
#else
        public MainPage() // Android/Windows 版本
#endif
        {
            InitializeComponent();
            bool isDark = App.AppStateManager.IsDarkTheme;
            //根据判断，瞬间给原生加载层上色
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
#if IOS
            _burnInService = burnInService;

            // 订阅状态改变事件
            _burnInService.OnStateChanged += (isActive) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (isActive) EnableProtectionUI(); else DisableProtectionUI();
                });
            };
#endif
            this.Loaded += MainPage_Loaded;
            DeviceDisplay.Current.MainDisplayInfoChanged += Current_MainDisplayInfoChanged;
        }


        private void MainPage_Loaded(object sender, EventArgs e)
        {
            UpdateSafeAreaToWeb();
#if WINDOWS
            InitializeWindowsIntegration();
#endif
        }
        private void Current_MainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
        {
            // 稍微延迟一下，等待安卓底层的 Insets 刷新完毕再读取
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50);
                UpdateSafeAreaToWeb();
            });
        }
        public void HideLoadingScreen()
        {

            LoadingOverlay.IsVisible = false;

        }
        public async Task<bool> WaitForWebViewAsync(TimeSpan timeout)
        {
#if WINDOWS
            try
            {
                if (string.IsNullOrWhiteSpace(
                        Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()))
                    return false;
            }
            catch
            {
                return false;
            }
#endif

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                if (blazorWebView.Handler?.PlatformView != null)
                    return true;

                await Task.Delay(100);
            }

            return blazorWebView.Handler?.PlatformView != null;
        }

        public async Task ShowWebViewMissingAlertAsync()
        {
#if WINDOWS
            bool install = await DisplayAlertAsync(
                App.Localization!.Get("MainPage.WebViewMissingTitle"),
                App.Localization.Get("MainPage.WebViewMissingDescription"),
                App.Localization.Get("MainPage.WebViewInstall"),
                App.Localization.Get("Common.Cancel"));

            if (install)
            {
                await Browser.Default.OpenAsync(
                    "https://developer.microsoft.com/microsoft-edge/webview2/",
                    BrowserLaunchMode.SystemPreferred);
            }
#else
            await DisplayAlertAsync(
                App.Localization!.Get("MainPage.WebViewMissingTitle"),
                App.Localization.Get("MainPage.WebViewUnavailableDescription"),
                App.Localization.Get("Common.OK"));
#endif
            Application.Current.Quit();
        }

        public void ShowLoadingPopup(string title, string message)
        {
            AlertTitle.Text = title;
            AlertMessage.Text = message;
            CustomAlertMask.IsVisible = true;
        }

        // 关闭弹窗
        public void HideLoadingPopup()
        {
            CustomAlertMask.IsVisible = false;
        }

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

#if WINDOWS
            InitializeWindowsIntegration();
#endif

#if ANDROID
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
            {
                // 传入当前页面的 Dispatcher
                androidWebView.SetOnKeyListener(new WebViewBackInterceptor(this.Dispatcher));
            }
#endif
        }

#if WINDOWS
        private const int TabBarHeightDip = 32;

        private void InitializeWindowsIntegration()
        {
            if (blazorWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webView)
                return;

            if (!_windowsIntegrationInitialized)
            {
                webView.SizeChanged += (s, e) => UpdateTabBarDragRectangle();
                _windowsIntegrationInitialized = true;
            }

            if (webView.CoreWebView2 != null)
            {
                RegisterDragBridge(webView.CoreWebView2);
            }
            else
            {
                webView.CoreWebView2Initialized += (s, e) =>
                {
                    if (e.Exception == null && webView.CoreWebView2 != null)
                        RegisterDragBridge(webView.CoreWebView2);
                };
            }

            ConfigureTabBarTitleBar();
        }

        private void ConfigureTabBarTitleBar()
        {
            var mauiWindow = Application.Current?.Windows[0];
            if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
                nativeWindow.AppWindow?.TitleBar == null)
                return;

            nativeWindow.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            if (!_titleBarConfigured)
            {
                nativeWindow.AppWindow.Changed += OnAppWindowChanged;
                _titleBarConfigured = true;
            }

            nativeWindow.DispatcherQueue.TryEnqueue(UpdateTabBarDragRectangle);
        }

        private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange)
                UpdateTabBarDragRectangle();
        }

        private void UpdateTabBarDragRectangle()
        {
            var mauiWindow = Application.Current?.Windows[0];
            if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
                nativeWindow.Content == null ||
                blazorWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webView ||
                webView.ActualWidth <= 0)
                return;

            var appWindow = nativeWindow.AppWindow;
            if (appWindow?.TitleBar == null)
                return;

            double scale = nativeWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;
            var webViewOrigin = webView.TransformToVisual(nativeWindow.Content)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            var tabBarDragRect = new Windows.Graphics.RectInt32(
                (int)Math.Round(webViewOrigin.X * scale),
                (int)Math.Round(webViewOrigin.Y * scale),
                (int)Math.Round(webView.ActualWidth * scale),
                (int)Math.Round(TabBarHeightDip * scale));

            appWindow.TitleBar.SetDragRectangles(new[] { tabBarDragRect });
        }

        private void RegisterDragBridge(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            if (_dragBridgeRegistered)
                return;

            try
            {
                // 暴露给 JS：window.chrome.webview.hostObjects.sync.dragBridge
                core.AddHostObjectToScript("dragBridge", new DragBridge());
                _dragBridgeRegistered = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RegisterDragBridge failed: {ex}");
            }
        }
#endif

#if ANDROID
        // 专门为 Android WebView 编写的按键拦截器
        private class WebViewBackInterceptor : Java.Lang.Object, Android.Views.View.IOnKeyListener
        {
            private readonly IDispatcher _dispatcher;

            // 构造函数：接收来自页面的 Dispatcher
            public WebViewBackInterceptor(IDispatcher dispatcher)
            {
                _dispatcher = dispatcher;
            }

            public bool OnKey(Android.Views.View? v, [Android.Runtime.GeneratedEnum] Android.Views.Keycode keyCode, Android.Views.KeyEvent? e)
            {
                if (keyCode == Android.Views.Keycode.Back && e?.Action == Android.Views.KeyEventActions.Down)
                {
                    _dispatcher.Dispatch(() =>
                    {
                        _ = BackEventsService.BackEvents();
                    });

                    return true; // 表示拦截了按键事件
                }
                return false;
            }
        }
#endif

        private void BlazorWebView_UrlLoading(object sender, UrlLoadingEventArgs e)
        {
            // 如果主机名是 127.0.0.1 ，强制在应用内打开
            if (e.Url.Host == "127.0.0.1")
            {
                e.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            }
        }
#if IOS
        private void EnableProtectionUI()
        {
            BurnInProtectionOverlay.IsVisible = true;
            _originalBrightness = (float)UIKit.UIScreen.MainScreen.Brightness;
            UIKit.UIScreen.MainScreen.Brightness = 0.0f; // 调到最暗
            StartFloatingAnimation();
        }

        private void DisableProtectionUI()
        {
            UIKit.UIScreen.MainScreen.Brightness = _originalBrightness;
            BurnInProtectionOverlay.IsVisible = false;
            // 恢复亮度逻辑...
        }

        private void OnProtectionMaskTapped(object sender, TappedEventArgs e)
        {
            _burnInService.Deactivate();
            _burnInService.ResetTimer();
        }
        private async void StartFloatingAnimation()
        {
            while (_burnInService.IsActive)
            {
                await FloatingText.TranslateTo(0, -60, 4000, Easing.SinInOut);
                await FloatingText.TranslateTo(0, 60, 4000, Easing.SinInOut);
            }
            FloatingText.TranslationY = 0;
        }
#else
        private void OnProtectionMaskTapped(object sender, TappedEventArgs e)
        {

        }
#endif

        private void UpdateSafeAreaToWeb()
        {
#if ANDROID
            // 1. 正确获取 Android 的 Window 对象
            var window = Platform.CurrentActivity?.Window;
            var decorView = window?.DecorView;

            if (decorView == null) return;

            // 2. 读取安全区
            var insets = ViewCompat.GetRootWindowInsets(decorView);
            if (insets != null)
            {
                var statusInsets = insets.GetInsets(WindowInsetsCompat.Type.StatusBars());
                var navInsets = insets.GetInsets(WindowInsetsCompat.Type.NavigationBars());
                var cutoutInsets = insets.GetInsets(WindowInsetsCompat.Type.DisplayCutout());

                // 获取屏幕密度进行换算
                var density = DeviceDisplay.Current.MainDisplayInfo.Density;
                if (density <= 0) density = 1; // 防止除以0

                double topDp = Math.Max(statusInsets.Top, cutoutInsets.Top) / density;
                double bottomDp = navInsets.Bottom / density;
                // 横屏时的刘海会变成 Left 或 Right
                double leftDp = cutoutInsets.Left / density;
                double rightDp = Math.Max(navInsets.Right, cutoutInsets.Right) / density;

                // 3. 注入给前端 CSS 变量
                System.Diagnostics.Debug.WriteLine($"安全区 - 上: {topDp}dp, 下: {bottomDp}dp");
                System.Diagnostics.Debug.WriteLine($"安全区 - 左: {leftDp}dp, 右: {rightDp}dp");
                App.AppStateManager.SafeAreaTop = topDp;
                App.AppStateManager.SafeAreaBottom = bottomDp;
                App.AppStateManager.SafeAreaLeft = leftDp;
                App.AppStateManager.SafeAreaRight = rightDp;
            }
#elif IOS
            // 1. 获取 iOS 当前的 UIViewController
            var viewController = Platform.GetCurrentUIViewController();
            var view = viewController?.View;

            if (view != null)
            {
                // 2. 直接读取 iOS 的 SafeAreaInsets
                var insets = view.SafeAreaInsets;

                // 重点注意：iOS 的返回值已经是逻辑像素 (Points/DP) 了！
                // 绝对不能像 Android 那样再去除非以屏幕密度 (Density)，直接用即可！
                double topDp = insets.Top;
                double bottomDp = insets.Bottom;
                double leftDp = insets.Left;
                double rightDp = insets.Right;

                App.AppStateManager.SafeAreaTop = topDp;
                App.AppStateManager.SafeAreaBottom = bottomDp;
                App.AppStateManager.SafeAreaLeft = leftDp;
                App.AppStateManager.SafeAreaRight = rightDp;
            }
#endif
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            KeyboardService.Default.Start();

        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            KeyboardService.Default.Stop();

        }


    }
}

