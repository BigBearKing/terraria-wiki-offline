using AndroidApplication = Android.App.Application;
using AndroidWebView = Android.Webkit.WebView;

namespace Terraria_Wiki.Services;

public static class AndroidSystemWebViewService
{
    private static readonly Lazy<bool> Availability = new(DetectAvailability);

    public static bool IsAvailable => Availability.Value;

    private static bool DetectAvailability()
    {
        try
        {
            using var webView = new AndroidWebView(AndroidApplication.Context);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Android System WebView is unavailable: {ex}");
            return false;
        }
    }
}
