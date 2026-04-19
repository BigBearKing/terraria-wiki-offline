using Microsoft.JSInterop;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public class AppState
{
    private static IJSRuntime? _js;


    public event Action? OnChange;
    public event Action? OnCurrentPageChanged;
    public event Action? OnSearchQueryChanged;
    public event Action<string, string>? OnShowAlert;


    public static void Init(IJSRuntime jsRuntime) => _js = jsRuntime;
    public string AppName { get; set; } = AppInfo.Current.Name;

    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _logPanelIsOpen = false;
    private bool _isDarkTheme;
    private bool _isProcessing = false;
    private string _currentWikiPage;
    private string _searchQuery = "";
    private bool _isPinned = false;


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

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {

            _isProcessing = value;
            OnChange?.Invoke();

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

}