using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            // 2. 根据判断，瞬间给原生加载层上色
            if (App.AppStateManager.IsDarkTheme)
            {
                LoadingOverlay.BackgroundColor = Color.FromArgb("#131313"); // 深色背景
                LoadingSpinner.Color = Color.FromArgb("#4da8da");           // 蓝色圈圈
                LoadingText.TextColor = Color.FromArgb("#9ca3af");          // 浅灰文字
            }
            else
            {
                LoadingOverlay.BackgroundColor = Colors.White;              // 白色背景
                LoadingSpinner.Color = Color.FromArgb("#0078d7");           // 深蓝圈圈
                LoadingText.TextColor = Color.FromArgb("#6b7280");          // 深灰文字
            }
        }
        public void HideLoadingScreen()
        {
            // 强制把下面的动作扔给 UI 主线程去执行
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                LoadingOverlay.IsVisible = false;
            });
        }
    }
}
