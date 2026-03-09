using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Terraria_Wiki
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, WindowSoftInputMode = Android.Views.SoftInput.AdjustResize, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // 1. 确保内容不延伸到系统窗口（状态栏和导航栏）下方
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window, true);

            // 2. 明确清除全屏标志（如果被意外设置的话）
            Window?.ClearFlags(Android.Views.WindowManagerFlags.Fullscreen);
        }
    }
}
