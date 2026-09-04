using SQLite;
using System.Text.Json;
using Terraria_Wiki.Services;
namespace Terraria_Wiki.Models;

public class WikiBook
{
    [PrimaryKey]
    public int Id { get; set; }
    public string Title { get; set; } // 比如 "Terraria Wiki"

    public string Description { get; set; }
    public string Language { get; set; } = "";

    // 核心字段：记录用户到底下没下载
    public bool IsPageDownloaded { get; set; }
    public bool IsResourceDownloaded { get; set; }
    public int PageCount { get; set; }
    public int ResourceCount { get; set; }
    public int RedirectCount { get; set; }
    // 存一下大小的字符串，展示给用户看，比如 "45.2 MB"
    public long DataSize { get; set; }

    public DateTime UpdateTime { get; set; }

    // ================= Wiki 源配置 =================
    // 完整的 API 地址，例如 "https://terraria.wiki.gg/zh/api.php"
    public string ApiBaseUrl { get; set; }
    // 页面根域名，例如 "https://terraria.wiki.gg"
    public string BaseUrl { get; set; }
    // 重定向列表相对路径，例如 "/zh/wiki/Special:ListRedirects?limit=5000"
    public string RedirectListUrl { get; set; }
    // 主命名空间 ID（通常为 0）
    public int MainNamespace { get; set; }
    // 额外命名空间 ID，逗号分隔，例如 "10000,10002"（terraria.wiki.gg 指南为 10000）
    public string AdditionalNamespaces { get; set; }
    // XPath 表达式，用于清理不需要的 HTML 元素
    public string JunkXPath { get; set; }
    // 数据文件夹名（相对于 AppDataDirectory），例如 "Terraria_Wiki_zh"，存放 .db 及下载的资源
    public string DataFolder { get; set; }
    // 默认页面内容，下载数据前展示给用户
    public string DefaultPageContent { get; set; }
    // 默认页面标题（即 wiki 首页的 Page 主键），例如 "Terraria Wiki"
    public string DefaultPageTitle { get; set; }
}

public enum AppTaskStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Interrupted = 3,
    Completed = 4,
    Failed = 5
}

public enum AppTaskPhase
{
    FetchingLists = 0,
    DownloadingPages = 1,
    DownloadingResources = 2,
    PostProcessing = 3
}

public class AppTask
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed]
    public int? WikiId { get; set; }
    public AppTaskType TaskType { get; set; }
    public AppTaskStatus Status { get; set; }
    public bool CanPause { get; set; }
    public string TaskData { get; set; } = "{}";
    public DateTime CreatedTime { get; set; }
    public DateTime UpdatedTime { get; set; }
    public string? LastError { get; set; }

    [Ignore]
    public AppTaskPhase Phase { get; set; }
    [Ignore]
    public bool IncludeResources { get; set; }
    [Ignore]
    public string TaskDirectory { get; set; } = string.Empty;
    [Ignore]
    public int TotalPages { get; set; }
    [Ignore]
    public int CompletedPages { get; set; }
    [Ignore]
    public int TotalResources { get; set; }
    [Ignore]
    public int CompletedResources { get; set; }
    [Ignore]
    public int ResumePageLine { get; set; }
    [Ignore]
    public int ResumeResourceLine { get; set; }
    [Ignore]
    public double Progress => CalculateProgress();

    public bool IsDownloadTask() => TaskType is AppTaskType.DownloadPages or AppTaskType.DownloadResources or
        AppTaskType.DownloadAll or AppTaskType.UpdatePages or AppTaskType.UpdateAll or AppTaskType.RetryFailed;

    public void LoadTaskData()
    {
        AppTaskData data;
        try
        {
            data = string.IsNullOrWhiteSpace(TaskData)
                ? new AppTaskData()
                : JsonSerializer.Deserialize<AppTaskData>(TaskData) ?? new AppTaskData();
        }
        catch (JsonException)
        {
            data = new AppTaskData();
        }
        Phase = data.Phase;
        IncludeResources = data.IncludeResources;
        TaskDirectory = data.TaskDirectory;
        TotalPages = data.TotalPages;
        CompletedPages = data.CompletedPages;
        TotalResources = data.TotalResources;
        CompletedResources = data.CompletedResources;
        ResumePageLine = data.ResumePageLine ?? CompletedPages;
        ResumeResourceLine = data.ResumeResourceLine ?? CompletedResources;
    }

    public void SaveTaskData()
    {
        TaskData = JsonSerializer.Serialize(new AppTaskData
        {
            Phase = Phase,
            IncludeResources = IncludeResources,
            TaskDirectory = TaskDirectory,
            TotalPages = TotalPages,
            CompletedPages = CompletedPages,
            TotalResources = TotalResources,
            CompletedResources = CompletedResources,
            ResumePageLine = ResumePageLine,
            ResumeResourceLine = ResumeResourceLine
        });
    }

    private double CalculateProgress()
    {
        if (TaskType == AppTaskType.DownloadResources)
            return TotalResources == 0 ? 0 : CompletedResources * 100d / TotalResources;

        var listProgress = Phase == AppTaskPhase.FetchingLists ? 0 : 1;
        var pageProgress = TotalPages == 0 ? 0 : CompletedPages / (double)TotalPages;
        if (!IncludeResources)
            return (listProgress * 10 + pageProgress * 45) / 55 * 100;

        var resourceProgress = TotalResources == 0 ? 0 : CompletedResources / (double)TotalResources;
        var postProcessProgress = Status == AppTaskStatus.Completed ? 1 : 0;
        return listProgress * 10 + pageProgress * 45 + resourceProgress * 44 + postProcessProgress;
    }
}

public sealed class AppTaskData
{
    public AppTaskPhase Phase { get; set; }
    public bool IncludeResources { get; set; }
    public string TaskDirectory { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public int CompletedPages { get; set; }
    public int TotalResources { get; set; }
    public int CompletedResources { get; set; }
    public int? ResumePageLine { get; set; }
    public int? ResumeResourceLine { get; set; }
}

public class WikiPage
{
    [PrimaryKey] // 标题作为主键，不再需要自增 ID
    public string Title { get; set; }

    public string Content { get; set; }

    public DateTime LastModified { get; set; }
}

public class WikiRedirect
{

    [PrimaryKey]
    public string FromName { get; set; }

    [Indexed]
    public string ToTarget { get; set; }
}

public class WikiHistory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // 这里存 Wiki 的标题，用来关联 WikiPage 表
    [Indexed]
    public string WikiTitle { get; set; }

    // 记录阅读时间，方便按时间排序
    public DateTime ReadAt { get; set; }

    [Indexed]
    public string DateKey { get; set; }
}

public class WikiFavorite
{
    [PrimaryKey] // 标题作为主键，确保一个条目只会在收藏夹里出现一次
    public string WikiTitle { get; set; }

    public DateTime FavoritedAt { get; set; } // 记录收藏的时间，方便排序
}

public class WikiAsset
{
    [PrimaryKey]
    public string FileName { get; set; } // 文件名作为主键 (如 "sword.png")

    public byte[] Data { get; set; }     // 图片的二进制数据

    public string MimeType { get; set; } // 文件类型 (如 "image/png")

    public DateTime? LastModified { get; set; } = DateTime.MinValue;
}

public class ManagerDbService : DatabaseService
{
    public ManagerDbService(string dbPath) : base(dbPath, DbMode.Manager)
    {
    }
}

// 2. 专门用于内容库的类型
public class ContentDbService : DatabaseService
{
    // 注意：这里默认连接 Terraria，后面可以通过方法切换
    public ContentDbService(string dbPath) : base(dbPath, DbMode.Content)
    {
    }
}