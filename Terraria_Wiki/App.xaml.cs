using System.Diagnostics;
using Terraria_Wiki.Services;
namespace Terraria_Wiki
{
    public partial class App : Application
    {
        public static ManagerDbService? ManagerDb { get; private set; }
        public static ContentDbService? ContentDb { get; private set; }
        private readonly LocalWebServer _webServer;
        public static DataService? DataManager { get; private set; }
        public static LogService? LogManager { get; private set; }
        public static AppState? AppStateManager { get; private set; }
        public App(LocalWebServer webServer, ManagerDbService managerDb,   // 注入管理库
        ContentDbService contentDb, DataService dataService, LogService logService,AppState appState,AppService appService)
        {
            InitializeComponent();
            _webServer = webServer;
            ManagerDb = managerDb;
            ContentDb = contentDb;
            DataManager = dataService;
            LogManager = logService;
            AppStateManager = appState;


        }
        protected override async void OnStart()
        {
            base.OnStart();
            _webServer.Start();
            await ManagerDb.Init();
            await ContentDb.Init();
            await AppService.RefreshWikiBookAsync(ManagerDb, ContentDb);
            DataManager.OnLog += (msg) => LogManager.AppendLog(msg);
            Debug.WriteLine($"[App] 启动完成！数据库路径：{ManagerDb.DatabasePath}，{ContentDb.DatabasePath}");

        }
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "Terraria Wiki" };
        }

    }
}
