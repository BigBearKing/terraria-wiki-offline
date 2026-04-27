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
            //根据判断，瞬间给原生加载层上色

            Application.Current.UserAppTheme = App.AppStateManager.IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
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
                    // 使用 Dispatcher 异步调度你的静态服务逻辑
                    // 这样不会阻塞当前的物理按键事件分发线程
                    _dispatcher.Dispatch(async () =>
                    {
                        // 调用你的静态方法（因为是异步，所以加个 _ = 忽略返回值）
                        _ = BackEventsService.BackEvents();
                    });

                    return true;
                }
                return false;
            }
        }
#endif
    }

}

