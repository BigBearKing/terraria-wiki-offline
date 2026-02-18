using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public class AppState
{
    // 1. 必须有的事件
    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    // ==========================================
    //  这里是核心技巧：把变量“包装”一下
    // ==========================================
    public string AppName { get; set; } = AppInfo.Current.Name;
    // 第一步：定义一个私有的“小金库”存数据
    private string _currentPage = "home";
    private bool _sidebarIsExpanded = false;
    private bool _isDarkTheme = false;
    private bool _isDownloading = false;
    private string _currentWikiPage = "Terraria Wiki";


    public AppState()
    {
        TempHistory = new List<TempHistory>();
    }



    // 第二步：定义公开的“柜台”
    public string CurrentPage
    {
        get => _currentPage;
        set
        {

            _currentPage = value;
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
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {

            _isDownloading = value;
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
}