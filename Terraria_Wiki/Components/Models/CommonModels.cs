using System.Text.Json.Serialization;
namespace Terraria_Wiki.Models;

public class WikiPageSummary
{
    public string Title { get; set; }
    public DateTime LastModified { get; set; }
}
public class WikiPageStringTime
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string LastModified { get; set; }
}

class HistoryTimelineItem
{
    public WikiHistory Data { get; set; } = new();

    // 决定这个条目上方要不要显示日期标题
    public bool ShowDateHeader { get; set; }

    // 具体的标题文本（如 "今天", "10月5日"）
    public string DateLabel { get; set; } = "";
}

// DataService Models 定义
public class PageInfo
{
    public string Title { get; set; }
    public DateTime LastModified { get; set; }
}

public class RawResponse
{
    public RawContinue Continue { get; set; }
    public RawQuery Query { get; set; }
}
public class RawContinue
{
    [JsonPropertyName("gapcontinue")] public string GapContinue { get; set; }
}

public class RawQuery
{
    public Dictionary<string, RawPage> Pages { get; set; }
}

public class RawPage
{
    public string Title { get; set; }
    public string Touched { get; set; }
    public string FullUrl { get; set; }
}

// AppState Models 定义

public class TempHistory
{
    public string Title { get; set; }
    public float Position { get; set; }
}
public class TitleWithAnchor
{
    public string Title { get; set; }
    public string Anchor { get; set; }
}
public class JsMsg { public string Type { get; set; } public string Id { get; set; } public string Method { get; set; } public string Data { get; set; } }

public class GitHubReleaseInfo
{
    // GitHub API 返回的 JSON 字段映射
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string ReleaseUrl { get; set; } = string.Empty;
}

// 更新检查结果模型
public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}

//搜索结果
public class SearchResultItem
{
    public string Title { get; set; }
    public string RedirectTo { get; set; }
    public string Snippet { get; set; }
}
// 辅助类：用于接收 LIKE 查询出的完整文本
public class RawSearchResult
{
    public string Title { get; set; }
    public string PlainContent { get; set; }
}