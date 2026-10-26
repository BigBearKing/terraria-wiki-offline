using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public class AppState : INotifyPropertyChanged
{
    public static IJSRuntime? JS;

    /// <summary>
    /// 统一属性变化通知（订阅方按属性名过滤或全量刷新）。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 业务事件（一次性命令通知，带参数），不属于"状态已变"语义，单独保留。
    /// </summary>
    public event Action<string, string>? OnShowAlert;

    public event Action? OnWikiBookSwitched;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }


    public static void Init(IJSRuntime jsRuntime) => JS = jsRuntime;
    public string AppName { get; set; } = AppInfo.Current.Name;

    /// <summary>
    /// 平台类型：应用启动时（DI 构造）只判断一次，避免各处反复调用 DeviceInfo。
    /// </summary>
    public DevicePlatform Platform { get; }

    public bool IsWindows => Platform == DevicePlatform.WinUI;
    public bool IsAndroid => Platform == DevicePlatform.Android;
    public bool IsIOS => Platform == DevicePlatform.iOS;
    public bool IsMacCatalyst => Platform == DevicePlatform.MacCatalyst;

    // ===== 尺寸与平台的术语约定（全项目统一，改动前先看这里）=====
    // 平台：IsMobile = 跑在 Android/iOS 上，与窗口大小无关。
    // 尺寸（阈值一律 768px，边界值算"紧凑"）：
    //   IsSmallScreen   宽 ≤768      窄
    //   IsShortScreen   高 ≤768      矮
    //   IsCompactScreen 窄或矮
    //   IsLargeScreen   又宽又高
    // 谁用哪一档看"该界面在哪一档才改变形态"：
    //   侧边栏抽屉 —— 只在窄（IsSmallScreen）浮层化，所以它的遮罩用 .narrow-only，不看高度
    //   顶部条     —— 分两层：TopStripAllowed（这个平台/尺寸允不允许）与
    //                 TopStripVisible（这一页到底在不在），见下面注释
    // 顶部条与标签归属只看 TopStripVisible：
    //   TopStripVisible == true  → 顶部条渲染，标签在它里面（主页时）
    //   TopStripVisible == false → 整条不渲染，标签列表在 MoreList 里
    // 标记/CSS 层：Mask 的 NarrowOnly 参数 / .narrow-only 遮罩，
    // 顶部栏 .narrow-menu-btn（只认宽度）与 .compact-more-btn（只在主页显示，见 TopBar.razor）。
    public bool IsMobile => Platform == DevicePlatform.Android || Platform == DevicePlatform.iOS;

    private string _dataRootPath = string.Empty;

    public string DataRootPath
    {
        get => _dataRootPath;
        private set => SetProperty(ref _dataRootPath, value);
    }

    public void SetDataRootPath(string path)
    {
        DataRootPath = path;
    }

    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _logPanelIsOpen = false;
    private bool _moreListOpen = false;
    private bool _findInPageOpen = false;
    private string _findInPageQuery = string.Empty;
    private bool _isDarkTheme;
    private bool _floatingSearchOpen = false;
    private bool _floatingBarActive = false;
    private readonly ConcurrentDictionary<int, ActiveTaskInfo> _activeTasks = new();
    private AppTask? _currentDownloadTask;

    private string _currentWikiPage;
    private List<TabModel> _tabs;
    private string _activeTabId;
    private int _activeWikiBookId = Preferences.Default.Get("ActiveWikiBookId", 1);
    private WikiBook? _activeWikiBook;
    private string _searchQuery = "";
    private string _currentLanguage = "zh-cn";
    private bool _isPinned = false;
    private bool _isSmallScreen = false;
    private bool _isShortScreen = false;
    private bool _isMacFullScreen = false;
    private int _wikiZoom = Preferences.Default.Get("WikiZoom", 100);

    /// <summary>
    /// 悬浮小条形态<b>专有</b>的缩放，与主窗口的 <see cref="WikiZoom"/> 互不影响，各存各的偏好。
    /// 惰性同步成"当前生效的缩放"：主窗口形态下读它拿到的就是 WikiZoom。
    /// </summary>
    private int _floatingZoom = Preferences.Default.Get("FloatingZoom", 100);
    private double _safeAreaTop = 0;
    private double _safeAreaBottom = 0;
    private double _safeAreaLeft = 0;
    private double _safeAreaRight = 0;
    public Dictionary<AppTaskType, TaskConfig> Tasks { get; } = new()
    {
        { AppTaskType.CheckUpdate, new TaskConfig { Id = AppTaskType.CheckUpdate, NameKey = "AppState.CheckUpdate", ProcessingTextKey = "AppState.CheckingUpdate" } },
        { AppTaskType.DownloadPages, new TaskConfig { Id = AppTaskType.DownloadPages, NameKey = "AppState.DownloadAllPages", ProcessingTextKey = "AppState.Downloading" } },
        { AppTaskType.DownloadResources, new TaskConfig { Id = AppTaskType.DownloadResources, NameKey = "AppState.DownloadAllAssets", ProcessingTextKey = "AppState.Downloading" } },
        { AppTaskType.DownloadAll, new TaskConfig { Id = AppTaskType.DownloadAll, NameKey = "AppState.DownloadAllContent", ProcessingTextKey = "AppState.Downloading" } },
        { AppTaskType.UpdateData, new TaskConfig { Id = AppTaskType.UpdateData, NameKey = "AppState.UpdateData", ProcessingTextKey = "AppState.Updating" } },
        { AppTaskType.UpdatePages, new TaskConfig { Id = AppTaskType.UpdatePages, NameKey = "AppState.UpdatePages", ProcessingTextKey = "AppState.Updating" } },
        { AppTaskType.UpdateAll, new TaskConfig { Id = AppTaskType.UpdateAll, NameKey = "AppState.UpdateAll", ProcessingTextKey = "AppState.Updating" } },
        { AppTaskType.DeleteResources, new TaskConfig { Id = AppTaskType.DeleteResources, NameKey = "AppState.DeleteAssets", ProcessingTextKey = "AppState.Deleting" } },
        { AppTaskType.RetryFailed, new TaskConfig { Id = AppTaskType.RetryFailed, NameKey = "AppState.RetryFailed", ProcessingTextKey = "AppState.Retrying" } },
        { AppTaskType.DeleteData, new TaskConfig { Id = AppTaskType.DeleteData, NameKey = "AppState.DeleteData", ProcessingTextKey = "AppState.Deleting" } },
        { AppTaskType.ExportData, new TaskConfig { Id = AppTaskType.ExportData, NameKey = "AppState.ExportData", ProcessingTextKey = "AppState.Exporting" } },
        { AppTaskType.ImportData, new TaskConfig { Id = AppTaskType.ImportData, NameKey = "AppState.ImportData", ProcessingTextKey = "AppState.Importing" } },
        { AppTaskType.MigrateData, new TaskConfig { Id = AppTaskType.MigrateData, NameKey = "AppState.MigrateData", ProcessingTextKey = "AppState.MigratingData" } },
        { AppTaskType.LegacyUpgrade, new TaskConfig { Id = AppTaskType.LegacyUpgrade, NameKey = "AppState.LegacyUpgrade", ProcessingTextKey = "AppState.LegacyUpgrading" } }
        ,{ AppTaskType.ExportLog, new TaskConfig { Id = AppTaskType.ExportLog, NameKey = "AppState.ExportLog", ProcessingTextKey = "AppState.Exporting" } }
        ,{ AppTaskType.DeleteLog, new TaskConfig { Id = AppTaskType.DeleteLog, NameKey = "AppState.DeleteLog", ProcessingTextKey = "AppState.Deleting" } }
        ,{ AppTaskType.ClearFailedList, new TaskConfig { Id = AppTaskType.ClearFailedList, NameKey = "AppState.ClearFailedList", ProcessingTextKey = "AppState.Cleaning" } }
    };

    public AppState()
    {
        Platform = DeviceInfo.Platform;
        var defaultTab = new TabModel();
        _tabs = new List<TabModel> { defaultTab };
        _activeTabId = defaultTab.Id;
    }

    public const int MaxTabs = 10;

    /// <summary>缩放上下限，与网页端 applyWikiZoom 的钳制范围一致。</summary>
    public const int MinWikiZoom = 50;
    public const int MaxWikiZoom = 200;

    /// <summary>功能栏 +/- 每次调整的步进。</summary>
    public const int WikiZoomStep = 10;

    public List<TabModel> Tabs
    {
        get => _tabs;
        set => SetProperty(ref _tabs, value);
    }

    public bool CanAddTab => _tabs.Count < MaxTabs;

    public string ActiveTabId
    {
        get => _activeTabId;
        set
        {
            if (SetProperty(ref _activeTabId, value))
            {
                var tab = GetActiveTab();
                if (tab != null)
                {
                    _currentWikiPage = tab.Title;
                    OnPropertyChanged(nameof(CurrentWikiPage));
                }
            }
        }
    }

    public List<PageViewInfo> TabHistory
    {
        get
        {
            var tab = GetActiveTab();
            return tab?.TabHistory ?? [];
        }
        set
        {
            var tab = GetActiveTab();
            if (tab != null)
            {
                tab.TabHistory = value ?? [];
                OnPropertyChanged(nameof(TabHistory));
            }
        }
    }

    public TabModel? ActiveTab => GetActiveTab();

    public TabModel? GetActiveTab()
    {
        return _tabs.FirstOrDefault(t => t.Id == _activeTabId);
    }

    /// <summary>
    /// 重置"wiki 导航"相关状态：标签页 / 当前 wiki 页面 / 搜索词 / 各种浮层。
    /// <paramref name="goHome"/> 决定是否顺带把 <see cref="CurrentPage"/> 拉回 home：
    ///   · 默认 true —— 数据迁移、首次下载完成、删除数据库这类"整库换掉"的场景适合回主页；
    ///   · 切换 Wiki 时传 false —— 用户是在数据页点的切换，不该被弹回主界面（页面自己会刷新）。
    /// </summary>
    public void ResetWikiNavigation(bool goHome = true)
    {
        var defaultTab = new TabModel();
        _tabs = new List<TabModel> { defaultTab };
        _activeTabId = defaultTab.Id;
        _currentWikiPage = string.Empty;
        if (goHome)
            _currentPage = "home";
        _searchQuery = string.Empty;
        _moreListOpen = false;
        _findInPageOpen = false;
        _findInPageQuery = string.Empty;
        _floatingSearchOpen = false;
        _floatingBarActive = false;

        OnPropertyChanged(nameof(Tabs));
        OnPropertyChanged(nameof(ActiveTabId));
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(CurrentWikiPage));
        if (goHome)
            OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(MoreListOpen));
        OnPropertyChanged(nameof(FloatingSearchOpen));
        OnPropertyChanged(nameof(FloatingBarActive));
        OnPropertyChanged(nameof(FindInPageOpen));
        OnPropertyChanged(nameof(FindInPageQuery));
    }

    public void NotifyWikiBookSwitched()
    {
        OnWikiBookSwitched?.Invoke();
    }

    public TabModel? AddTab()
    {
        if (_tabs.Count >= MaxTabs) return null;
        var tab = new TabModel();
        _tabs.Add(tab);
        OnPropertyChanged(nameof(Tabs));
        return tab;
    }

    public void CloseTab(string tabId)
    {
        if (_tabs.Count <= 1) return;
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        _tabs.RemoveAt(_tabs.IndexOf(tab));
        OnPropertyChanged(nameof(Tabs));
    }

    public string CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public bool SidebarIsExpanded
    {
        get => _sidebarIsExpanded;
        set => SetProperty(ref _sidebarIsExpanded, value);
    }

    public bool MoreListOpen
    {
        get => _moreListOpen;
        set => SetProperty(ref _moreListOpen, value);
    }

    /// <summary>
    /// 悬浮窗开关（仅 Windows）：开启后主窗口隐藏，桌面上只留一个图标球。
    /// 由 <see cref="FloatingWindow"/> 的真实窗口状态驱动。
    /// </summary>
    public bool FloatingSearchOpen
    {
        get => _floatingSearchOpen;
        set => SetProperty(ref _floatingSearchOpen, value);
    }

    /// <summary>
    /// 悬浮"小条"形态是否已展开（仅 Windows）：为 true 时主窗口缩小置顶，
    /// 里面只渲染 32px 条 + wiki 视图；为 false 时主窗口整个隐藏、桌面上只有图标球。
    /// </summary>
    public bool FloatingBarActive
    {
        get => _floatingBarActive;
        set => SetProperty(ref _floatingBarActive, value);
    }

    /// <summary>
    /// 主窗口形态的 Wiki 缩放百分比（50-200，步进 10），对应偏好键 "WikiZoom"。
    ///
    /// ★ 这里存的始终是<b>主窗口自己的值</b>，悬浮小条期间不会被小条的操作改写；
    ///   小条用的是 <see cref="FloatingZoom"/>（"FloatingZoom"），两者各存各的、互不影响。
    ///   "当前 iframe 实际用的缩放"另由 <see cref="_activeZoom"/> 记录，见 <see cref="PushZoomForCurrentMode"/>。
    /// </summary>
    public int WikiZoom
    {
        get => _wikiZoom;
        set
        {
            int clamped = Math.Clamp(value, MinWikiZoom, MaxWikiZoom);

            // 小条形态：改的应该是小条专有缩放，主窗口的值原样留着
            if (FloatingBarActive)
            {
                FloatingZoom = clamped;
                return;
            }

            if (SetProperty(ref _wikiZoom, clamped))
            {
                Preferences.Default.Set("WikiZoom", clamped);
                _activeZoom = clamped;
                PushZoomToIframe(clamped);
            }
        }
    }

    /// <summary>
    /// 悬浮小条形态专有的缩放百分比（50-200，步进 10），独立存偏好 "FloatingZoom"。
    /// 主窗口形态下读它等于 <see cref="WikiZoom"/>（"读到的就是当前生效值"），
    /// 进入小条时才会切到小条自己记着的值。
    /// </summary>
    public int FloatingZoom
    {
        get => FloatingBarActive ? Math.Clamp(_floatingZoom, MinWikiZoom, MaxWikiZoom) : _wikiZoom;
        set
        {
            int clamped = Math.Clamp(value, MinWikiZoom, MaxWikiZoom);
            _floatingZoom = clamped;
            Preferences.Default.Set("FloatingZoom", clamped);

            // 只在小条形态下改"当前生效值"：主窗口自己的 _wikiZoom 一个字节都不动
            if (FloatingBarActive)
            {
                if (SetProperty(ref _activeZoom, clamped, nameof(ActiveZoom)))
                    OnPropertyChanged(nameof(FloatingZoom));

                PushZoomToIframe(clamped);
                return;
            }

            OnPropertyChanged(nameof(FloatingZoom));
        }
    }

    /// <summary>
    /// 该 iframe 当前实际生效的缩放百分比。进入/退出小条时由
    /// <see cref="PushZoomForCurrentMode"/> 在"主窗口值"和"小条值"之间切换。
    /// </summary>
    private int _activeZoom = Preferences.Default.Get("WikiZoom", 100);

    /// <summary>当前 iframe 实际生效的缩放（只读，供调试/展示）。</summary>
    public int ActiveZoom => _activeZoom;

    /// <summary>
    /// 按"当前形态"重新推缩放给 iframe：
    /// 小条形态用 "FloatingZoom"，主窗口形态用 "WikiZoom"。
    /// 模式切换时必须调用——同一个 iframe 节点不重载，不推的话网页会停在另一个模式的值上
    /// （典型表现：退出悬浮窗后主窗口还显示着小条的缩放）。
    /// </summary>
    public void PushZoomForCurrentMode()
    {
        int zoom = FloatingBarActive
            ? Math.Clamp(_floatingZoom, MinWikiZoom, MaxWikiZoom)
            : _wikiZoom;

        if (SetProperty(ref _activeZoom, zoom, nameof(ActiveZoom)))
            OnPropertyChanged(nameof(FloatingZoom));

        PushZoomToIframe(zoom);
    }

    /// <summary>
    /// 把当前缩放推给 iframe。iframe 未就绪时不会有回复，所以限制等待时长后静默放弃；
    /// 此时 iframe 加载完成后会通过 ApplyZoomToIframeAsync 补齐，界面与网页不会不一致。
    /// </summary>
    public static void PushZoomToIframe(int zoom)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _ = IframeBridge.CallJsAsync("SetZoom", zoom.ToString(), cts.Token);
    }

    public bool FindInPageOpen
    {
        get => _findInPageOpen;
        set => SetProperty(ref _findInPageOpen, value);
    }

    /// <summary>
    /// 页内搜索的查询词。输入由 MAUI 原生输入框承担（叠在 WebView 之上），
    /// 因此这里作为原生侧与 Blazor 组件之间的共享状态。
    /// </summary>
    public string FindInPageQuery
    {
        get => _findInPageQuery;
        set => SetProperty(ref _findInPageQuery, value ?? string.Empty);
    }

    public bool LogPanelIsOpen
    {
        get => _logPanelIsOpen;
        set => SetProperty(ref _logPanelIsOpen, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }

    public IReadOnlyCollection<ActiveTaskInfo> ActiveTasks => _activeTasks.Values.ToArray();

    public bool HasActiveTasks => _activeTasks.Count != 0;

    public void AddActiveTask(ActiveTaskInfo task)
    {
        if (task.Task.Status != AppTaskStatus.Running)
            return;
        _activeTasks[task.Task.Id] = task;
        OnPropertyChanged(nameof(ActiveTasks));
        OnPropertyChanged(nameof(HasActiveTasks));
    }

    public void RemoveActiveTask(int taskId)
    {
        if (_activeTasks.TryRemove(taskId, out _))
        {
            OnPropertyChanged(nameof(ActiveTasks));
            OnPropertyChanged(nameof(HasActiveTasks));
        }
    }

    public ActiveTaskInfo? GetActiveTask(int taskId) => _activeTasks.GetValueOrDefault(taskId);

    public void NotifyActiveTasksChanged()
    {
        OnPropertyChanged(nameof(ActiveTasks));
        OnPropertyChanged(nameof(CurrentDownloadTask));
    }

    public IReadOnlyCollection<ActiveTaskInfo> GetActiveTasks(int? wikiId = null, AppTaskType? taskType = null)
        => _activeTasks.Values
            .Where(info => (!wikiId.HasValue || info.Task.WikiId == wikiId) &&
                          (!taskType.HasValue || info.Task.TaskType == taskType))
            .ToArray();

    public AppTask? GetCurrentDownloadTask(int wikiId)
        => GetActiveTasks(wikiId)
            .Select(info => info.Task)
            .FirstOrDefault(task => task.IsDownloadTask()) ??
           (_currentDownloadTask?.WikiId == wikiId && _currentDownloadTask.IsDownloadTask()
               ? _currentDownloadTask
               : null);

    public void SetCurrentDownloadTask(AppTask? task)
    {
        CurrentDownloadTask = task;
        NotifyActiveTasksChanged();
    }

    public AppTask? CurrentDownloadTask
    {
        get => _currentDownloadTask;
        set
        {
            if (value is not null &&
                (value.Status is AppTaskStatus.Completed ||
                 value.TaskType is AppTaskType.None or AppTaskType.CheckUpdate or
                     AppTaskType.DeleteResources or AppTaskType.DeleteData or AppTaskType.ExportData or
                     AppTaskType.ImportData or AppTaskType.MigrateData or AppTaskType.LegacyUpgrade or
                     AppTaskType.ExportLog or AppTaskType.DeleteLog or AppTaskType.ClearFailedList))
            {
                value = null;
            }
            SetProperty(ref _currentDownloadTask, value);
        }
    }

    public string CurrentWikiPage
    {
        get => _currentWikiPage;
        set
        {
            if (SetProperty(ref _currentWikiPage, value))
            {
                var tab = GetActiveTab();
                if (tab != null)
                {
                    tab.Title = value;
                }
            }
        }
    }

    public int ActiveWikiBookId
    {
        get => _activeWikiBookId;
        set
        {
            if (SetProperty(ref _activeWikiBookId, value))
            {
                Preferences.Default.Set("ActiveWikiBookId", value);
                _activeWikiBook = null; // 切换 wiki 时清缓存，下次访问时重新加载
                OnPropertyChanged(nameof(ActiveWikiBook));
            }
        }
    }

    public WikiBook? ActiveWikiBook
    {
        get => _activeWikiBook;
        set => SetProperty(ref _activeWikiBook, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetProperty(ref _currentLanguage, value);
    }

    public void TriggerAlert(string title, string message)
    {
        OnShowAlert?.Invoke(title, message);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    /// <summary>视口宽度 ≤768px（窄屏）。顶部条在此时让位给 MoreList。</summary>
    public bool IsSmallScreen
    {
        get => _isSmallScreen;
        set => SetProperty(ref _isSmallScreen, value);
    }

    /// <summary>视口高度 ≤768px（矮屏）。</summary>
    public bool IsShortScreen
    {
        get => _isShortScreen;
        set => SetProperty(ref _isShortScreen, value);
    }

    /// <summary>
    /// 顶部条的基础高度（不含顶部安全区）。与 CSS 的 --strip-base-height 同源，
    /// 两边改的时候必须一起改（CSS 侧负责渲染高度，这里供原生页内搜索面板算位置）。
    /// </summary>
    public const double TopStripHeight = 32;

    /// <summary>
    /// Mac Catalyst 是否处于系统全屏（绿灯进入的全屏，不是放大/最大化）。
    /// 只由 MainPage 写入，其他平台恒为 false。
    /// 判定方式见 MainPage：有标题栏（红绿灯）时 SafeAreaInsets.Top ≈ 41，系统全屏时归 0，
    /// 直接拿这个值比（实测可靠）。早期那套"宽高比与屏幕一致"的推断是错的 ——
    /// Catalyst 里窗口用 UIKit 点、屏幕用 macOS 点，两者比例对不齐，判定永远不成立。
    /// </summary>
    public bool IsMacFullScreen
    {
        get => _isMacFullScreen;
        set => SetProperty(ref _isMacFullScreen, value);
    }

    /// <summary>窄或矮：面板/遮罩一类"手机上才需要"的界面在这一档启用。</summary>
    public bool IsCompactScreen => IsSmallScreen || IsShortScreen;

    /// <summary>
    /// 平台与尺寸层面：顶部条这个形态允不允许存在（与"当前是哪一页"无关）。
    ///   Windows      —— 宽度决定：宽窗口允许（横条兼作无边框窗口的拖动/系统按钮区）；
    ///                   窄窗口不允许，顶部那 32px 改由 safe-area-top 保留；
    ///   Mac Catalyst —— 只有系统全屏允许（窗口态顶部要留给红绿灯，标签归 MoreList）；
    ///   其他平台      —— 又宽又高才允许（手机横竖屏都不允许，标签归 MoreList）。
    /// 这里只回答"能不能"，"这一页要不要"由 <see cref="TopStripVisible"/> 回答。
    /// </summary>
    private bool TopStripAllowed =>
        IsWindows ? !IsSmallScreen : IsMacCatalyst ? IsMacFullScreen : IsLargeScreen;

    /// <summary>
    /// 这一页上顶部条到底在不在 —— 渲染、顶部安全区归属、标签归属全都只认它：
    ///   Windows      —— 任何页面都在。它同时是无边框窗口的拖动条/系统按钮区，
    ///                   没有标签（非主页）也必须占着那 32px；
    ///   其他平台     —— 只有主页需要它（放标签）。其它页面整条不渲染，
    ///                   顶部安全区交回顶栏 —— 否则会留一条什么都不写的空白带
    ///                   （iPad 上就是这个问题：标签不渲染、横条还在）。
    /// ★ 它是 Blazor 直接渲染的类名（.main-area.has-top-strip），不再走 JS 同步，
    ///   所以换页 / 转屏 / 进出全屏和渲染在同一帧，不存在"两份真值晚一拍"。
    /// </summary>
    public bool TopStripVisible => TopStripAllowed && (CurrentPage == "home" || IsWindows);

    /// <summary>又宽又高（宽 &gt;768 且 高 &gt;768）：其他平台只有这一种情况才可能有上方横条。</summary>
    public bool IsLargeScreen => !IsCompactScreen;

    [JSInvokable]
    public static void OnScreenChanged(bool isSmall, bool isShort)
    {
        App.AppStateManager.IsSmallScreen = isSmall;
        App.AppStateManager.IsShortScreen = isShort;
    }

    public double SafeAreaTop
    {
        get => _safeAreaTop;
        set => SetSafeArea(ref _safeAreaTop, value, "setSafeAreaTop", nameof(SafeAreaTop));
    }
    public double SafeAreaBottom
    {
        get => _safeAreaBottom;
        set => SetSafeArea(ref _safeAreaBottom, value, "setSafeAreaBottom", nameof(SafeAreaBottom));
    }
    public double SafeAreaLeft
    {
        get => _safeAreaLeft;
        set => SetSafeArea(ref _safeAreaLeft, value, "setSafeAreaLeft", nameof(SafeAreaLeft));
    }
    public double SafeAreaRight
    {
        get => _safeAreaRight;
        set => SetSafeArea(ref _safeAreaRight, value, "setSafeAreaRight", nameof(SafeAreaRight));
    }

    private void SetSafeArea(ref double field, double value, string jsMethod, string propertyName)
    {
        if (SetProperty(ref field, value, propertyName))
            JS?.InvokeVoidAsync(jsMethod, value);
    }

}