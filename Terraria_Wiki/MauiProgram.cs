using Microsoft.Extensions.Logging;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });
            var contentDbService = new ContentDbService(Path.Combine(FileSystem.AppDataDirectory, "Terraria_Wiki", "Terraria_Wiki.db"));
            var managerDbService = new ManagerDbService(Path.Combine(FileSystem.AppDataDirectory, "Manager.db"));
            builder.Services.AddSingleton(managerDbService);
            builder.Services.AddSingleton(contentDbService);
            builder.Services.AddSingleton(sp => new LocalWebServer(contentDbService));
            builder.Services.AddSingleton<AppState>();
            builder.Services.AddSingleton<LogService>();
            builder.Services.AddSingleton<DataService>();
            builder.Services.AddSingleton<AppService>();
            builder.Services.AddTransient<App>();
            builder.Services.AddMauiBlazorWebView();

#if IOS
            Microsoft.Maui.Platform.KeyboardAutoManagerScroll.Disconnect();
            builder.Services.AddSingleton<BurnInProtectionService>();
            Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("DisableKeyboardAutoScroll", (handler, view) =>
            {
                var webView = handler.PlatformView; // 底层的 WKWebView

                // 2. 禁止 iOS 系统自动给 WebView 增加内边距来躲避键盘
                webView.ScrollView.ContentInsetAdjustmentBehavior = UIKit.UIScrollViewContentInsetAdjustmentBehavior.Never;
            });
#endif

            builder.Services.AddTransient<MainPage>();
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
