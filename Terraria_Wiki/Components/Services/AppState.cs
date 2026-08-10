using System.ComponentModel;
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
    public bool IsMobile => Platform == DevicePlatform.Android || Platform == DevicePlatform.iOS;

    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _logPanelIsOpen = false;
    private bool _mobileTabPanelOpen = false;
    private bool _isDarkTheme;
    private int _processingTaskId = 0;

    private string _currentWikiPage;
    private List<TabModel> _tabs;
    private string _activeTabId;
    private int _activeWikiBookId = Preferences.Default.Get("ActiveWikiBookId", 1);
    private WikiBook? _activeWikiBook;
    private string _searchQuery = "";
    private string _currentLanguage = "zh-cn";
    private bool _isPinned = false;
    private bool _isSmallScreen = false;
    private double _safeAreaTop = 0;
    private double _safeAreaBottom = 0;
    private double _safeAreaLeft = 0;
    private double _safeAreaRight = 0;
    public readonly Dictionary<int, TaskConfig> Tasks = new()
    {
        { 1, new TaskConfig { Id = 1, NameKey = "AppState.CheckUpdate", ProcessingTextKey = "AppState.CheckingUpdate" } },
        { 2, new TaskConfig { Id = 2, NameKey = "AppState.DownloadAllPages", ProcessingTextKey = "AppState.Downloading" } },
        { 3, new TaskConfig { Id = 3, NameKey = "AppState.DownloadAllAssets", ProcessingTextKey = "AppState.Downloading" } },
        { 4, new TaskConfig { Id = 4, NameKey = "AppState.UpdateData", ProcessingTextKey = "AppState.Updating" } },
        { 5, new TaskConfig { Id = 5, NameKey = "AppState.CleanUnusedAssets", ProcessingTextKey = "AppState.Cleaning" }  },
        { 6, new TaskConfig { Id = 6, NameKey = "AppState.DeleteAssets", ProcessingTextKey = "AppState.Deleting" }   },
        { 7, new TaskConfig { Id = 7, NameKey = "AppState.RetryFailed", ProcessingTextKey = "AppState.Retrying" } },
        { 8, new TaskConfig { Id = 8, NameKey = "AppState.DeleteData", ProcessingTextKey = "AppState.Deleting" }  },
        { 9, new TaskConfig { Id = 9, NameKey = "AppState.ExportData", ProcessingTextKey = "AppState.Exporting" }   },
        { 10, new TaskConfig { Id = 10, NameKey = "AppState.ImportData", ProcessingTextKey = "AppState.Importing" }  },
        { 11, new TaskConfig { Id = 11, NameKey = "", ProcessingTextKey = "" }  },
        { 12, new TaskConfig { Id = 12, NameKey = "", ProcessingTextKey = "" }  },
        { 13, new TaskConfig { Id = 13, NameKey = "", ProcessingTextKey = "" }  },
        { 14, new TaskConfig { Id = 14, NameKey = "", ProcessingTextKey = "" }  }
    };

    public AppState()
    {
        Platform = DeviceInfo.Platform;
        var defaultTab = new TabModel();
        _tabs = new List<TabModel> { defaultTab };
        _activeTabId = defaultTab.Id;
    }

    public const int MaxTabs = 5;

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

    public bool MobileTabPanelOpen
    {
        get => _mobileTabPanelOpen;
        set => SetProperty(ref _mobileTabPanelOpen, value);
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

    public int ProcessingTaskId
    {
        get => _processingTaskId;
        set
        {
            if (SetProperty(ref _processingTaskId, value))
            {
                if (value != 0)
                {
                    _logPanelIsOpen = true;
                    OnPropertyChanged(nameof(LogPanelIsOpen));
                }
            }
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

    public bool IsSmallScreen
    {
        get => _isSmallScreen;
        set => SetProperty(ref _isSmallScreen, value);
    }

    [JSInvokable]
    public static void OnScreenChanged(bool isSmall)
    {
        App.AppStateManager.IsSmallScreen = isSmall;
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