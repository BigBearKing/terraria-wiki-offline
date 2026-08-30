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

        public Task InitializationTask { get; }

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

            InitializeComponent();

            InitializeNativeStateBeforeBlazor();

            MainPage = services.GetRequiredService<MainPage>();

            InitializationTask = InitializeBeforeBlazorAsync();
        }

        private void InitializeNativeStateBeforeBlazor()
        {
            ThemeService.InitTheme();
        }

        private async Task InitializeBeforeBlazorAsync()
        {
            AppStateManager!.IsNetworkAvailable =
                Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
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
            await WebServer.Start();
            await ContentDb.Init(false, activeBook);
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
            await RestoreDownloadTaskStateAsync();
        }

        private static async Task RestoreDownloadTaskStateAsync()
        {
            var tasks = await ManagerDb!.GetItemsAsync<DownloadTask>();
            foreach (var task in tasks.Where(t => t.Status == DownloadTaskStatus.Running))
            {
                task.Status = DownloadTaskStatus.Interrupted;
                task.UpdatedTime = DateTime.Now;
                await ManagerDb.SaveItemAsync(task);
            }

            AppStateManager!.CurrentDownloadTask = tasks
                .Where(t => t.WikiId == AppStateManager.ActiveWikiBookId && t.Status != DownloadTaskStatus.Completed)
                .OrderByDescending(t => t.UpdatedTime)
                .FirstOrDefault();
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