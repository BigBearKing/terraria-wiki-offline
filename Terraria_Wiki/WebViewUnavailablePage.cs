namespace Terraria_Wiki;

public sealed class WebViewUnavailablePage : ContentPage
{
    public WebViewUnavailablePage()
    {
        Title = "WebView 不可用";
        BackgroundColor = Colors.White;

        var title = new Label
        {
            Text = "无法启动应用",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Colors.Black
        };

        var message = new Label
        {
            Text = "请在 Android 系统设置中启用 Android System WebView 或 Chrome，然后重新打开应用。",
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Colors.DarkGray
        };

        var closeButton = new Button
        {
            Text = "退出应用",
            HorizontalOptions = LayoutOptions.Center
        };
        closeButton.Clicked += (_, _) => Application.Current?.Quit();

        Content = new Grid
        {
            Padding = 24,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 20,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { title, message, closeButton }
                }
            }
        };
    }
}
