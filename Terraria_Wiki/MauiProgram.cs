using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
#if WINDOWS
            // ★ WebView2 稳定性配置（必须在任何 WebView2 创建之前设置环境变量）。
            //
            // 症状：WebView2 的子进程（GPU / Storage Service / Network Service）反复崩溃，
            // 最终把整个浏览器进程拖死，表现为界面一直 loading。
            // 子进程真实退出码为 0x00000007 = ERROR_ARENA_TRASHED →
            // 根因是 Chromium 沙箱在本机环境无法创建子进程，与 GPU 加速本身无关。
            //
            // ★ 对照实验结论（不要改成 --disable-gpu）：
            //   (不加参数)                                  → GPU 崩溃 14 次，界面出不来
            //   --disable-gpu ...                           → GPU 崩溃 8 次，界面仍出不来（无效）
            //   --no-sandbox（GPU 保持开启）                → 0 次崩溃，界面正常显示 ✅
            // 即：只需 --no-sandbox，GPU 加速应当保持开启。
            ConfigureWebView2Stability();
#endif

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });
            var storagePathService = new StoragePathService();
            var contentDbService = new ContentDbService(Path.Combine(storagePathService.RootPath, "placeholder.db"));
            var managerDbService = new ManagerDbService(Path.Combine(storagePathService.RootPath, "Manager.db"));
            builder.Services.AddSingleton(storagePathService);
            builder.Services.AddSingleton(managerDbService);
            builder.Services.AddSingleton(contentDbService);
            builder.Services.AddSingleton(sp => new LocalWebServer(contentDbService));
            builder.Services.AddSingleton<AppState>();
            builder.Services.AddSingleton<LogService>();
            builder.Services.AddSingleton<AppTaskRunner>();
            builder.Services.AddSingleton<GlobalExceptionHandler>();
            builder.Services.AddSingleton<INativeFindInPageService, NativeFindInPageService>();
            builder.Services.AddSingleton<DataService>();
            builder.Services.AddSingleton<PackageService>();
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
#if WINDOWS
            builder.ConfigureLifecycleEvents((events) =>
            {
                events.AddWindows(wndLifeCycleBuilder =>
                {
                    wndLifeCycleBuilder.OnWindowCreated((window) =>
                    {
                        WindowHelper.EnableResizableBorderless(window);
                        // 启动时按当前主题设置标题栏（含最小化/最大化/关闭按钮）颜色
                        WindowHelper.ApplyTitleBarTheme(App.AppStateManager?.IsDarkTheme ?? false);
                    });
                });
            });

#endif
            var built = builder.Build();
            return built;
        }

#if WINDOWS
        /// <summary>
        /// 设置 WebView2 的浏览器启动参数，解决"一直 loading"。
        ///
        /// WebView2 会读取环境变量 <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> 作为
        /// 传给 msedgewebview2.exe 的额外命令行。必须在创建任何 WebView2 之前设置。
        ///
        /// 用环境变量而不是 <c>CoreWebView2Environment.CreateAsync</c>，是因为
        /// MAUI 的 BlazorWebView 内部自行创建环境，外部拿不到注入点；
        /// 环境变量是官方支持的、能覆盖到框架内部创建的途径。
        ///
        /// ★★ 根因（2026-10-02 对照实验确认）：
        ///   问题是 **Chromium 沙箱**在本机环境下无法正常创建子进程 ——
        ///   Storage Service 与 GpuProcess 反复以
        ///   `ExitCode=7 (ERROR_ARENA_TRASHED)` 崩溃（日志里各刷 7~14 次），
        ///   最终把浏览器进程一起拖死（BrowserProcessExited）→ CoreWebView2 变 null
        ///   → Blazor 的渲染回应收不到 → 界面永远 loading。
        ///
        ///   **解药是 `--no-sandbox`，不是 `--disable-gpu`。**
        ///   实测对照（每档跑满 18s，看 GpuProcessExited 次数 / appChildCount）：
        ///     ┌────────────────────────────────┬──────────┬───────────┬───────────────┐
        ///     │ 参数                            │ GPU 崩溃 │ 浏览器崩溃 │ appChildCount │
        ///     ├────────────────────────────────┼──────────┼───────────┼───────────────┤
        ///     │ (不加)                          │   14     │     2     │  空（失败）    │
        ///     │ --disable-gpu                   │    8     │     0     │  空（失败）    │
        ///     │ --disable-gpu --no-sandbox …    │    0     │     0     │  2（成功）     │
        ///     │ --no-sandbox（GPU 保持开启）     │    0     │     0     │  2（成功）★    │
        ///     └────────────────────────────────┴──────────┴───────────┴───────────────┘
        ///   即：**GPU 加速可以且应该保持开启**，`--disable-gpu` 对崩溃毫无帮助，
        ///   反而牺牲了渲染性能。只加 `--no-sandbox` 即最优。
        ///
        ///   ★ 注意：`--no-sandbox` 降低了浏览器进程的隔离级别。对一个纯离线
        ///   Wiki 阅读器（只加载本机 LocalWebServer 的 https://0.0.0.1 内容）风险可接受；
        ///   若将来要加载任意外部网页，应重新评估。
        /// </summary>
        private static void ConfigureWebView2Stability()
        {
            try
            {
                // 允许用环境变量覆盖，便于对照实验/排障：
                //   "none"      → 不加任何参数（基线，会崩）
                //   "nogpu"     → 关闭 GPU 加速（历史方案，实测无效）
                //   (未设置)/其他 → 默认：仅 --no-sandbox，**GPU 加速保持开启**
                var mode = Environment.GetEnvironmentVariable("TW_WEBVIEW_ARGS") ?? "default";

                string args = mode switch
                {
                    "none" => string.Empty,
                    "nogpu" => "--no-sandbox --disable-gpu --disable-gpu-compositing --disable-dev-shm-usage",
                    _ => "--no-sandbox",
                };

                var existing = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
                var merged = string.IsNullOrWhiteSpace(args)
                    ? (existing ?? string.Empty)
                    : (string.IsNullOrWhiteSpace(existing) ? args : existing + " " + args);
                if (!string.IsNullOrWhiteSpace(merged))
                    Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", merged);

                // 用户数据目录：默认**不覆盖**，让 WebView2 用 exe 同目录的
                // <exe>.WebView2（官方默认，实测最稳定）。
                //
                // ★ 2026-10-02 教训：曾无条件固定到
                // %LOCALAPPDATA%\BigBearKing\...\WebView2 —— 该目录在一轮轮崩溃后
                // 累积了 47MB Crashpad 垃圾与多个 0 字节 .tmp，反而加剧了 Storage Service
                // 崩溃。默认位置最省心；确需指定时用 TW_WEBVIEW_UD。
                var udMode = Environment.GetEnvironmentVariable("TW_WEBVIEW_UD");
                if (!string.IsNullOrWhiteSpace(udMode) && udMode != "default")
                {
                    Directory.CreateDirectory(udMode);
                    Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", udMode);
                }
            }
            catch
            {
                // 环境变量设置失败不应阻塞启动：退化为 WebView2 默认参数。
            }
        }
#endif
    }
}
