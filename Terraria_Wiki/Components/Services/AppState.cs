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
    public readonly Dictionary<int, TaskConfig> Tasks = new()
    {
        { 1, new TaskConfig { Id = 1, Name = "检查软件更新", ProcessingText = "正在检查更新" } },
        { 2, new TaskConfig { Id = 2, Name = "下载所有页面", ProcessingText = "正在下载" } },
        { 3, new TaskConfig { Id = 3, Name = "下载所有资源", ProcessingText = "正在下载" } },
        { 4, new TaskConfig { Id = 4, Name = "更新数据", ProcessingText = "正在更新" } },
        { 5, new TaskConfig { Id = 5, Name = "清理未用资源", ProcessingText = "正在清理" }  },
        { 6, new TaskConfig { Id = 6, Name = "删除图片资源", ProcessingText = "正在删除" }   },
        { 7, new TaskConfig { Id = 7, Name = "重试失败任务", ProcessingText = "正在重试" } },
        { 8, new TaskConfig { Id = 8, Name = "删除数据", ProcessingText = "正在删除" }  },
        { 9, new TaskConfig { Id = 9, Name = "导出数据", ProcessingText = "正在导出" }   },
        { 10, new TaskConfig { Id = 10, Name = "导入数据", ProcessingText = "正在导入" }  },
        { 11, new TaskConfig { Id = 11, Name = " ", ProcessingText = " " }  },
        { 12, new TaskConfig { Id = 12, Name = " ", ProcessingText = " " }  },
        { 13, new TaskConfig { Id = 13, Name = " ", ProcessingText = " " }  },
        { 14, new TaskConfig { Id = 14, Name = " ", ProcessingText = " " }  }
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
}