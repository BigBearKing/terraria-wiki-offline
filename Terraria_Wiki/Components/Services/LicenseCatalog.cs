using Microsoft.Maui.Storage;

namespace Terraria_Wiki.Services;

/// <summary>
/// 随应用分发的 7 份法律文本的清单与读取。
/// 列表页（Licenses）和详情页（LicensesDetails）共用这一份，
/// 免得两边的文件名 / 中文名各写一套、对不上。
/// </summary>
public static class LicenseCatalog
{
    /// <summary>一份法律文本：<paramref name="NameKey"/> 是语言文件里的 Licenses.*，<paramref name="FileName"/> 是打进包里的文件名。</summary>
    public sealed record Document(string NameKey, string FileName);

    public static readonly Document[] Documents =
    [
        new("Licenses.AppLicense", "LICENSE"),
        new("Licenses.AppNotice", "NOTICE"),
        new("Licenses.ThirdParty", "THIRD_PARTY_NOTICES.md"),
        new("Licenses.ContentAttribution", "CONTENT_ATTRIBUTION.md"),
        new("Licenses.FontLicense", "OFL-1.1.txt"),
        new("Licenses.WebView2License", "WebView2-LICENSE.txt"),
        new("Licenses.WebView2Notice", "WebView2-NOTICE.txt"),
    ];

    /// <summary>按路由里的文件名找清单项（大小写不敏感）；没有这份就返回 null。</summary>
    public static Document? Find(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName)
            ? null
            : Documents.FirstOrDefault(d => string.Equals(d.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 读出许可正文：两个落点各试一次，都没读到返回 null（页面显示"未找到"）。
    /// 落点差异见 csproj 的 Legal notices 段：
    ///   · Android / iOS / MacCatalyst：7 份全在 assets/licenses/ 下；
    ///   · Windows：根目录 4 份 + licenses/ 3 份。
    /// </summary>
    public static async Task<string?> ReadAsync(string fileName)
    {
        foreach (var path in new[] { $"licenses/{fileName}", fileName })
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(path);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch (FileNotFoundException)
            {
                // 这份不在这个落点，试下一个
            }
            catch (DirectoryNotFoundException)
            {
                // 同上：Windows 上根目录那份没有 licenses/ 前缀
            }
            catch (Exception ex)
            {
                App.LogManager?.Error($"Read license '{path}' failed: {ex}");
            }
        }

        return null;
    }
}
