using Terraria_Wiki.Services;

namespace Terraria_Wiki;

public sealed class WebViewUnavailablePage : ContentPage, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly Label _title;
    private readonly Label _message;
    private readonly Button _exitButton;
#if WINDOWS
    private readonly Button _installButton;
#endif

    public WebViewUnavailablePage(LocalizationService localization)
    {
        _localization = localization;
        _localization.OnChange += ApplyLocalization;

        _title = new Label
        {
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Colors.Black
        };

        _message = new Label
        {
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = Colors.DarkGray
        };

        _exitButton = new Button
        {
            HorizontalOptions = LayoutOptions.Center
        };
        _exitButton.Clicked += (_, _) => Application.Current?.Quit();

        var actions = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center
        };
#if WINDOWS
        _installButton = new Button();
        _installButton.Clicked += async (_, _) => await OpenWebViewInstallationPageAsync();
        actions.Children.Add(_installButton);
#endif
        actions.Children.Add(_exitButton);

        Title = _localization.Get("MainPage.WebViewUnavailableTitle");
        BackgroundColor = Colors.White;
        ApplyLocalization();

        Content = new Grid
        {
            Padding = 24,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 20,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { _title, _message, actions }
                }
            }
        };
    }

#if WINDOWS
    private static async Task OpenWebViewInstallationPageAsync()
    {
        await Browser.Default.OpenAsync(
            "https://developer.microsoft.com/microsoft-edge/webview2/",
            BrowserLaunchMode.SystemPreferred);
    }
#endif

    private void ApplyLocalization()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Title = _localization.Get("MainPage.WebViewUnavailableTitle");
            _title.Text = _localization.Get("MainPage.WebViewUnavailableTitle");
            _message.Text = _localization.Get("MainPage.WebViewUnavailableDescription");
            _exitButton.Text = _localization.Get("MainPage.WebViewExit");
#if WINDOWS
            _installButton.Text = _localization.Get("MainPage.WebViewInstall");
#endif
        });
    }

    public void Dispose() => _localization.OnChange -= ApplyLocalization;
}
