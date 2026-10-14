
namespace Terraria_Wiki.Services
{
    public class BackEventsService
    {
        private static DateTime _lastBackPressTime = DateTime.MinValue;

        private static readonly TimeSpan _exitTimeWindow = TimeSpan.FromSeconds(1.5);

        public static async Task BackEvents()
        {
            // 原生遮罩加载框（启动加载层 / 导入·迁移进度框）显示期间：返回键彻底吞掉，
            // 既不关浮层，也不导航或退出——加载中退回主页既没意义，还可能打断数据迁移。
            if (BackLayerCoordinator.IsBackBlocked)
            {
                return;
            }

            if (BackLayerCoordinator.TryCloseTop())
            {
                return;
            }

            if (App.AppStateManager.LogPanelIsOpen)
            {
                App.AppStateManager.LogPanelIsOpen = false;
                return;
            }

            if (!string.IsNullOrEmpty(App.AppStateManager.SearchQuery))
            {
                App.AppStateManager.SearchQuery = "";
                return;
            }



            string currentPage = App.AppStateManager?.CurrentPage ?? "";
            if (currentPage == "home")
            {
                if (App.AppStateManager?.TabHistory.Count == 0)
                {
                    _ = ExitApp();
                }
                else
                {
                    _ = AppService.WikiBackAsync();
                }
            }
            else if (currentPage.Contains('/'))
            {
                string targetPage = "home";
                int lastSlashIndex = currentPage.LastIndexOf('/');

                targetPage = currentPage.Substring(0, lastSlashIndex);
                AppService.NavigateTo(targetPage);
            }
            else
            {
                _ = ExitApp();
            }
        }

        private async static Task ExitApp()
        {
            var currentTime = DateTime.Now;

            // 如果当前时间距离上次按下不足 2 秒
            if (currentTime - _lastBackPressTime <= _exitTimeWindow)
            {
                // 确认退出！直接关闭整个 MAUI 应用
                Application.Current?.Quit();
            }

            // 1. 更新上一次按下的时间
            _lastBackPressTime = currentTime;

            // 2. 弹出 Android 原生 Toast 提示
            Android.Widget.Toast.MakeText(
                Android.App.Application.Context,
                App.Localization!.Get("BackEventsService.PressAgain"),
                Android.Widget.ToastLength.Short
            )?.Show();
        }
    }
}
