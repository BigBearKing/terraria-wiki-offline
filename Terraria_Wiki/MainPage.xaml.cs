#if ANDROID
using Android.Views;
using Android.Window;

#endif
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            bool isDark = App.AppStateManager.IsDarkTheme;
            //根据判断，瞬间给原生加载层上色
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
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

    }
}

