using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    [Activity(Theme = "@style/Maui.SplashTheme",
              MainLauncher = true,
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {

        private AppState _appState;
        private BackPressedCallback? _backPressedCallback;
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _backPressedCallback = new BackPressedCallback();
            OnBackPressedDispatcher.AddCallback(this, _backPressedCallback);
            // 提取全局 AppState
            _appState = IPlatformApplication.Current.Services.GetService<AppState>();

            if (_appState != null)
            {
                // 只关心活动任务集合变化，按属性名过滤
                _appState.PropertyChanged += OnAppStatePropertyChanged;
            }
            Window.SetSoftInputMode(SoftInput.AdjustNothing);
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window, false);
            WindowCompat.SetDecorFitsSystemWindows(Window, false);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P) // Android 9.0+
            {
                Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            }
            if (Window != null)
            {
                // 2. 强制将状态栏和导航栏的背景颜色设置为透明
                Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
                Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
            }
            ChangeStatusBarColor();


        }
        public void ChangeStatusBarColor()
        {
            // 3. 处理图标文字的颜色（和之前一样）
            var windowInsetsController = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (windowInsetsController != null)
            {
                // true = 深色文字/图标，false = 白色文字/图标
                windowInsetsController.AppearanceLightStatusBars = !App.AppStateManager.IsDarkTheme;
                windowInsetsController.AppearanceLightNavigationBars = !App.AppStateManager.IsDarkTheme;

            }
        }
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            // 检查是不是我们刚才发起的保存文件请求 (请求码 4321)
            if (requestCode == 4321)
            {
                // 如果用户点击了保存，并且返回了有效的数据
                if (resultCode == Result.Ok && data?.Data != null)
                {
                    AndroidFileSaver.tcs?.TrySetResult(data.Data);
                }
                else
                {
                    // 用户取消了操作，返回 null
                    AndroidFileSaver.tcs?.TrySetResult(null);
                }
            }
            else if (requestCode == AndroidFilePicker.RequestCode)
            {
                if (resultCode == Result.Ok && data?.Data != null)
                {
                    try
                    {
                        ContentResolver.TakePersistableUriPermission(
                            data.Data,
                            ActivityFlags.GrantReadUriPermission);
                    }
                    catch
                    {
                        // 部分文档提供器不支持持久化授权，但当前 Activity 授权仍然有效。
                    }
                    AndroidFilePicker.Complete(data.Data);
                }
                else
                {
                    AndroidFilePicker.Complete(null);
                }
            }
        }


        private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.ActiveTasks))
                CheckAndToggleProcessingService();
            else if (e.PropertyName == nameof(AppState.IsDarkTheme))
                RunOnUiThread(ChangeStatusBarColor);
        }

        private void CheckAndToggleProcessingService()
        {
            if (_appState.HasActiveTasks)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var intent = new Intent(this, typeof(Platforms.Android.ProcessingService));
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                        StartForegroundService(intent);
                    else
                        StartService(intent);
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var intent = new Intent(this, typeof(Platforms.Android.ProcessingService));
                    StopService(intent);
                });
            }
        }

        public override bool DispatchKeyEvent(KeyEvent? e)
        {
            if (e?.KeyCode == Keycode.Back)
            {
                if (e.Action == KeyEventActions.Down && e.RepeatCount == 0)
                    HandleBackNavigation();

                return true;
            }

            return base.DispatchKeyEvent(e);
        }

        private static void HandleBackNavigation()
        {
            _ = MainThread.InvokeOnMainThreadAsync(BackEventsService.BackEvents);
        }

        protected override void OnDestroy()
        {
            _backPressedCallback?.Remove();
            _backPressedCallback = null;
            if (_appState != null)
            {
                _appState.PropertyChanged -= OnAppStatePropertyChanged; // 防内存泄漏
            }
            base.OnDestroy();
        }

        private sealed class BackPressedCallback : OnBackPressedCallback
        {
            public BackPressedCallback() : base(true)
            {
            }

            public override void HandleOnBackPressed()
            {
                HandleBackNavigation();
            }
        }





    }
}