using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
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
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
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
        }


        private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.ActiveTasks))
                CheckAndToggleProcessingService();
            else if (e.PropertyName == nameof(AppState.IsDarkTheme))
                RunOnUiThread(ChangeStatusBarColor);
        }

        private async void CheckAndToggleProcessingService()
        {
            if (_appState.HasActiveTasks)
            {
                await RequestNotificationPermissionAsync();

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

        private static async Task RequestNotificationPermissionAsync()
        {

            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();

            // 2. 如果还没有被授予权限
            if (status != PermissionStatus.Granted)
            {
                // 3. 唤起系统弹窗，向用户正式请求权限
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }

        }
        protected override void OnDestroy()
        {
            if (_appState != null)
            {
                _appState.PropertyChanged -= OnAppStatePropertyChanged; // 防内存泄漏
            }
            base.OnDestroy();
        }





    }
}