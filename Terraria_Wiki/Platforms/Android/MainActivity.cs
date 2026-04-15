using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Terraria_Wiki
{
    [Activity(Theme = "@style/Maui.SplashTheme",
              MainLauncher = true,
              WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // 恢复正常模式
            ResetToStandardMode();
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

        // 删除之前的 OnWindowFocusChanged 重写，
        // 否则每次切回来它可能又会去跑之前的全屏逻辑
    }
}