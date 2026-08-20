using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki
{
    public partial class App : Application
    {
        public static ManagerDbService? ManagerDb { get; private set; }
        public static ContentDbService? ContentDb { get; private set; }
        public static LocalWebServer? WebServer { get; private set; }
        public static DataService? DataManager { get; private set; }
        public static LogService? LogManager { get; private set; }
        public static AppState? AppStateManager { get; private set; }
        public static LocalizationService? Localization { get; private set; }
        public static StoragePathService? StoragePath { get; private set; }

        public App(LocalWebServer webServer, ManagerDbService managerDb,
                ContentDbService contentDb, DataService dataService, LogService logService, AppState appState, AppService appService,
                LocalizationService localizationService,
                StoragePathService storagePath,
                IServiceProvider services)
        {
            WebServer = webServer;
            ManagerDb = managerDb;
            ContentDb = contentDb;
            DataManager = dataService;
            LogManager = logService;
            AppStateManager = appState;
            Localization = localizationService;
            StoragePath = storagePath;

            ThemeService.InitTheme();
            _ = InitializeAsync();

            InitializeComponent();

            MainPage = services.GetRequiredService<MainPage>();
        }

        private async Task InitializeAsync()
        {
            AppStateManager!.IsNetworkAvailable =
                Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            await Localization!.InitializeAsync();
            await ManagerDb.Init();
            // 根据活跃 WikiBook 的 DataFolder 切换 ContentDb 到正确路径，并缓存到 AppState
            var activeBook = await ManagerDb.GetItemAsync<WikiBook>(AppStateManager.ActiveWikiBookId);
            AppStateManager.ActiveWikiBook = activeBook;

            // 执行旧版数据迁移（通过判断旧文件是否存在决定是否执行）
            var upgradeHandler = new LegacyUpgradeHandler();
            await upgradeHandler.RunAsync(activeBook);

            if (activeBook != null)
            {
                var contentDbPath = Path.Combine(StoragePath.RootPath, activeBook.DataFolder, "data.db");
                await ContentDb.SwitchDatabaseAsync(contentDbPath);
            }
            WebServer.Start();
            await ContentDb.Init(false, activeBook);
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
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

            // 应用即将进入后台 (失去焦点)
            window.Deactivated += (s, e) =>
            {
                WebServer.Stop();
            };

            // 应用回到前台 (恢复焦点)
            window.Resumed += async (s, e) =>
            {
                await WebServer.Start();
            };

            // 应用刚启动时也可以确保开启
            window.Created += async (s, e) =>
            {
                await WebServer.Start();
            };

            return window;
        }

#endif

    }
}