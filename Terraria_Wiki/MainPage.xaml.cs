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

#if ANDROID
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
            {
                // 传入当前页面的 Dispatcher
                androidWebView.SetOnKeyListener(new WebViewBackInterceptor(this.Dispatcher));
            }
#endif
        }

        protected override bool OnBackButtonPressed()
        {
#if ANDROID
            // 将异步操作分发到主线程执行
            Dispatcher.Dispatch(async () =>
            {
                _ = BackEventsService.BackEvents();
            });
#endif
            return true;
        }

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

    }
}

