using SQLite;
namespace Terraria_Wiki.Models;

public class WikiBook
{
    [PrimaryKey]
    public int Id { get; set; }
    public string Title { get; set; } // 比如 "Terraria Wiki"

    public string Description { get; set; }

    // 核心字段：记录用户到底下没下载
    public bool IsPageDownloaded { get; set; }
    public bool IsResourceDownloaded { get; set; }
    public int PageCount { get; set; }
    public int ResourceCount { get; set; }
    public int RedirectCount { get; set; }
    // 存一下大小的字符串，展示给用户看，比如 "45.2 MB"
    public long DataSize { get; set; }

    public DateTime DownloadedTime { get; set; }


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
}

