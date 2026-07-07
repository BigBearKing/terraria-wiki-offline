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
            builder.Services.AddSingleton<LocalizationService>();
            builder.Services.AddTransient<App>();
            builder.Services.AddMauiBlazorWebView();

#if IOS
            builder.Services.AddSingleton<BurnInProtectionService>();
#endif
#if IOS
            // 1. 切断 MAUI 官方自带的推挤（保留你之前加的这句）
            Microsoft.Maui.Platform.KeyboardAutoManagerScroll.Disconnect();

            Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("KillWebKitScroll", (handler, view) =>
            {
                var webView = handler.PlatformView; // 底层的 WKWebView

                // 基础防御：禁止系统乱加边距和回弹
                webView.ScrollView.ContentInsetAdjustmentBehavior = UIKit.UIScrollViewContentInsetAdjustmentBehavior.Never;
                webView.ScrollView.Bounces = false;

                // 【终极物理锁死】：监听原生 UI 线程的滚动事件
                // WebKit 引擎一旦检测到输入框被挡住，会试图偷偷改变底层的 ContentOffset。
                // 我们在这里直接拦截：只要它敢改变偏移量，我们在画面渲染到屏幕前的瞬间，强行把它按回 0！
                webView.ScrollView.Scrolled += (sender, e) =>
                {
                    if (webView.ScrollView.ContentOffset.Y != 0 || webView.ScrollView.ContentOffset.X != 0)
                    {
                        // 瞬间归零，因为是在原生 UI 线程同步执行，肉眼绝对看不见任何抖动
                        webView.ScrollView.ContentOffset = new CoreGraphics.CGPoint(0, 0);
                    }
                };
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
