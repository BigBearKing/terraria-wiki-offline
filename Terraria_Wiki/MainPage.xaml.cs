#if ANDROID
using Android.Views;
using Android.Window;

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
    }
}

