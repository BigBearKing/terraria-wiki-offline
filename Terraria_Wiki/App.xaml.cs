using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public partial class App : Application
    {
        public static ManagerDbService? ManagerDb { get; private set; }
        public static ContentDbService? ContentDb { get; private set; }
        public static DataService? DataManager { get; private set; }
        public static LogService? LogManager { get; private set; }
        public static AppState? AppStateManager { get; private set; }
        public static LocalizationService? Localization { get; private set; }
        public static StoragePathService? StoragePath { get; private set; }
        /// <summary>
        /// iframe 内容源的虚拟主机承载。三平台机制不同但共用一份内容提供者：
        /// Windows = WebView2 过滤器，iOS/MacCatalyst = WKURLSchemeHandler，Android = WebViewClient 包装。
        /// </summary>
        public static WikiVirtualHost? VirtualHost { get; private set; }

        public Task InitializationTask { get; }

        public App(ManagerDbService managerDb,
                ContentDbService contentDb, DataService dataService, LogService logService, AppState appState, AppService appService,
                LocalizationService localizationService,
                StoragePathService storagePath,
                WikiVirtualHost virtualHost,
                IServiceProvider services)
        {
            ManagerDb = managerDb;
            ContentDb = contentDb;
            DataManager = dataService;
            LogManager = logService;
            AppStateManager = appState;
            Localization = localizationService;
            StoragePath = storagePath;
            VirtualHost = virtualHost;
            services.GetRequiredService<GlobalExceptionHandler>().Register();

            // 有任务在跑时按住系统，别让电脑睡过去/进新型待机把下载掐断（详见 SleepPreventionService）
            SleepPreventionService.Attach(appState);

            InitializeComponent();

            InitializeNativeStateBeforeBlazor();

#if WINDOWS
            if (!IsWindowsWebViewAvailable())
            {
                MainPage = new WebViewUnavailablePage(localizationService);
                InitializationTask = InitializeUnavailablePageAsync();
                return;
            }
#endif
#if ANDROID
            if (!AndroidSystemWebViewService.IsAvailable)
            {
                MainPage = new WebViewUnavailablePage(localizationService);
                InitializationTask = InitializeUnavailablePageAsync();
                return;
            }
#endif

            MainPage = services.GetRequiredService<MainPage>();

            InitializationTask = InitializeBeforeBlazorAsync();
        }

        private async Task InitializeUnavailablePageAsync()
        {
            await Localization!.InitializeAsync();
        }

        private void InitializeNativeStateBeforeBlazor()
        {
            ThemeService.InitTheme();
        }

#if WINDOWS
        private static bool IsWindowsWebViewAvailable()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(
                    Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows WebView2 is unavailable: {ex}");
                return false;
            }
        }
#endif

        private async Task InitializeBeforeBlazorAsync()
        {
            AppStateManager.SetDataRootPath(StoragePath!.RootPath);

            await Localization!.InitializeAsync();
            await ManagerDb.Init();
            // 根据活跃 WikiBook 的 DataFolder 切换 ContentDb 到正确路径，并缓存到 AppState
            var activeBook = await ManagerDb.GetItemAsync<WikiBook>(AppStateManager.ActiveWikiBookId);
            AppStateManager.ActiveWikiBook = activeBook;

            // 执行旧版数据迁移（通过判断旧文件是否存在决定是否执行）
            var upgradeHandler = new LegacyUpgradeHandler(StoragePath!);
            await upgradeHandler.RunAsync(activeBook);

            if (activeBook != null)
            {
                var contentDbPath = Path.Combine(StoragePath.RootPath, activeBook.DataFolder, "data.db");
                await ContentDb.SwitchDatabaseAsync(contentDbPath);
            }
            await ContentDb.Init(false, activeBook);
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
            await RestoreDownloadTaskStateAsync();
            _ = RunAutomaticPageUpdateAsync();
        }

        private static async Task RunAutomaticPageUpdateAsync()
        {
            if (!Preferences.Default.Get("AutoUpdatePages", false) ||
                !await NetworkService.IsNetworkAvailableAsync() ||
                AppStateManager?.ActiveWikiBook?.IsPageDownloaded != true)
            {
                return;
            }

            await DataManager!.UpdateDataAsync(includeResources: false);
        }

        private static async Task RestoreDownloadTaskStateAsync()
        {
            var tasks = await ManagerDb!.GetItemsAsync<AppTask>();
            foreach (var task in tasks.Where(t => t.Status != AppTaskStatus.Completed && t.Status != AppTaskStatus.Failed))
            {
                task.Status = AppTaskStatus.Interrupted;
                task.UpdatedTime = DateTime.Now;
                await ManagerDb.SaveItemAsync(task);
            }

            await AppService.RestoreDownloadTaskStateAsync(AppStateManager.ActiveWikiBookId);
        }

#if WINDOWS
        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window window = base.CreateWindow(activationState);

#if RELEASE
            window.MinimumWidth = 400;
            window.MinimumHeight = 300;
#endif
            // 恢复上次的窗口位置/大小/最大化状态，并在销毁时保存
            WindowHelper.RestoreWindowState(window);
            WindowHelper.RegisterSaveOnDestroy(window);
            return window;
        }
#endif
#if ANDROID || IOS
        protected override Window CreateWindow(IActivationState? activationState)
        {
            Window window = base.CreateWindow(activationState);

            // R4 之后 iframe 的内容源是 WebView 虚拟主机（不占端口、不监听 socket），
            // 所以前后台切换不需要再停/启任何服务器。这里只保留与安全区相关的工作 ——
            // 手机旋转/从后台回来时安全区会变，必须重算，否则内容会被刘海或手势条压住。
            window.Resumed += async (s, e) =>
            {
                if (MainPage is MainPage mainPage)
                    await mainPage.RefreshSafeAreaAsync();
            };

            return window;
        }

#endif

    }
}