using Microsoft.JSInterop;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public class AppState
{
    private static IJSRuntime? _js;
    

    public event Action? OnChange;
    public event Action? OnCurrentPageChanged;
    public event Action? OnSearchQueryChanged;
    private void NotifyStateChanged() => OnChange?.Invoke();
    public static void Init(IJSRuntime jsRuntime) => _js = jsRuntime;
    public string AppName { get; set; } = AppInfo.Current.Name;

    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _isDarkTheme;
    private bool _isProcessing = false;
    private string _currentWikiPage;
    private string _searchQuery = "";
    

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
            NotifyStateChanged();

        }
    }

    public bool SidebarIsExpanded
    {
        get => _sidebarIsExpanded;
        set
        {

            _sidebarIsExpanded = value;
            NotifyStateChanged();

        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {

            _isDarkTheme = value;
            NotifyStateChanged();

        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {

            _isProcessing = value;
            NotifyStateChanged();

        }
    }

    public string CurrentWikiPage
    {
        get => _currentWikiPage;
        set
        {
            _currentWikiPage = value;
            NotifyStateChanged();

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
}