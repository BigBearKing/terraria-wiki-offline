using Microsoft.JSInterop;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public class AppState
{
    public static IJSRuntime? JS;


    public event Action? OnChange;
    public event Action? OnCurrentPageChanged;
    public event Action? OnSearchQueryChanged;
    public event Action<string, string>? OnShowAlert;


    public static void Init(IJSRuntime jsRuntime) => JS = jsRuntime;
    public string AppName { get; set; } = AppInfo.Current.Name;

    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _logPanelIsOpen = false;
    private bool _isDarkTheme;
    private int _processingTaskId = 0;

    private string _currentWikiPage;
    private string _searchQuery = "";
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
        TempHistory = new List<TempHistory>();

    }

    public string CurrentPage
    {
        get => _currentPage;
        set
        {

            _currentPage = value;
            OnCurrentPageChanged?.Invoke();
            OnChange?.Invoke();

        }
    }

    public bool SidebarIsExpanded
    {
        get => _sidebarIsExpanded;
        set
        {

            _sidebarIsExpanded = value;
            OnChange?.Invoke();

        }
    }

    public bool LogPanelIsOpen
    {
        get => _logPanelIsOpen;
        set
        {
            _logPanelIsOpen = value;
            OnChange?.Invoke();
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {

            _isDarkTheme = value;
            OnChange?.Invoke();
#if ANDROID

            // 获取当前 Activity 并转换为 MainActivity
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;

            // 确保在主线程执行 UI 相关操作
            activity?.RunOnUiThread(() =>
            {
                activity.ChangeStatusBarColor();
            });
#endif

        }
    }

    public int ProcessingTaskId
    {
        get => _processingTaskId;
        set
        {

            _processingTaskId = value;
            OnChange?.Invoke();
            if (value != 0)
            {
                LogPanelIsOpen = true;
            }
        }
    }

    public string CurrentWikiPage
    {
        get => _currentWikiPage;
        set
        {
            _currentWikiPage = value;
            OnChange?.Invoke();

        }
    }

    public List<TempHistory> TempHistory { get; set; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnSearchQueryChanged?.Invoke();
            }
        }
    }

    public void TriggerAlert(string title, string message)
    {
        OnShowAlert?.Invoke(title, message);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            _isPinned = value;
            OnChange?.Invoke();
        }
    }

    public bool IsSmallScreen
    {
        get => _isSmallScreen;
        set
        {
            _isSmallScreen = value;
            OnChange?.Invoke();
        }
    }

    [JSInvokable]
    public static void OnScreenChanged(bool isSmall)
    {
        App.AppStateManager.IsSmallScreen = isSmall;
    }

    public double SafeAreaTop
    {
        get => _safeAreaTop;
        set
        {
            _safeAreaTop = value;
            JS?.InvokeVoidAsync("setSafeAreaTop", value);
        }
    }
    public double SafeAreaBottom
    {
        get => _safeAreaBottom;
        set
        {
            _safeAreaBottom = value;
            JS?.InvokeVoidAsync("setSafeAreaBottom", value);
        }
    }
    public double SafeAreaLeft
    {
        get => _safeAreaLeft;
        set
        {
            _safeAreaLeft = value;
            JS?.InvokeVoidAsync("setSafeAreaLeft", value);
        }
    }
    public double SafeAreaRight
    {
        get => _safeAreaRight;
        set
        {
            _safeAreaRight = value;
            JS?.InvokeVoidAsync("setSafeAreaRight", value);
        }
    }

}