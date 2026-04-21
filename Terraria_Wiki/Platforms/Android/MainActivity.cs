using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using AndroidX.Core.View;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    [Activity(Theme = "@style/Maui.SplashTheme",
              MainLauncher = true,
              WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {

        private AppState _appState;
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // 恢复正常模式
            ResetToStandardMode();
            // 提取全局 AppState
            _appState = IPlatformApplication.Current.Services.GetService<AppState>();

            if (_appState != null)
            {
                // 假设你的 AppState 里有一个事件叫做 OnChange 或者 PropertyChanged
                // 你需要在这里订阅它。只要 TaskId 变化了，这个事件就会触发。
                _appState.OnChange += CheckAndToggleProcessingService;
            }
        }

        private void ResetToStandardMode()
        {
            if (Window == null) return;

            // 1. 重要：设置为 true，意味着布局会自动避开状态栏和导航栏
            WindowCompat.SetDecorFitsSystemWindows(Window, true);

            // 2. 获取控制器
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);

            // 3. 显示系统栏（状态栏 + 导航栏）
            controller.Show(WindowInsetsCompat.Type.SystemBars());
        }

        private void CheckAndToggleProcessingService()
        {
            // 必须切回主线程操作 Android Context
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var intent = new Intent(this, typeof(Platforms.Android.ProcessingService));

                if (_appState.ProcessingTaskId != 0)
                {
                    // TaskId 有值，启动（或更新）通知栏
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                        StartForegroundService(intent);
                    else
                        StartService(intent);
                }
                else
                {
                    // TaskId 归零，发一个空 intent 过去，Service 内部判断 == 0 后会自我销毁
                    // 或者直接调用 StopService(intent);
                    StopService(intent);
                }
            });
        }

        protected override void OnDestroy()
        {
            if (_appState != null)
            {
                _appState.OnChange -= CheckAndToggleProcessingService; // 防内存泄漏
            }
            base.OnDestroy();
        }

    }
}