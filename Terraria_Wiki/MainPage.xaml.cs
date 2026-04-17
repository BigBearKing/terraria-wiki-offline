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
    }
}
