namespace Terraria_Wiki.Services;

/// <summary>
/// 「设置 → 数据 → 查看第三方网盘资源」页的正文来源。
///
/// 正文不随应用发布：它放在仓库的 docs/cloud-drives.txt 里，网盘链接失效或换分享时
/// 改那个文件就行，不用发新版本。取到后写一份本地缓存，之后断网也能看到上次的内容。
///
/// 两个源按顺序试：raw.githubusercontent 在国内常连不上，jsDelivr 的 GitHub 镜像是兜底。
/// </summary>
public static class CloudDriveCatalog
{
    private const string Owner = "BigBearKing";
    private const string Repo = "terraria-wiki-offline";
    private const string Branch = "main";
    private const string FilePath = "docs/cloud-drives.txt";

    private static readonly string[] Sources =
    [
        $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/{FilePath}",
        $"https://cdn.jsdelivr.net/gh/{Owner}/{Repo}@{Branch}/{FilePath}",
    ];

    /// <summary>单个源的超时：正文只有几 KB，10 秒还拿不到就换下一个，别让用户干等。</summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private const string CacheFileName = "cloud-drives.txt";
    private const string CacheTimeKey = "CloudDriveResourcesFetchedAt";

    private static string CachePath => Path.Combine(FileSystem.AppDataDirectory, CacheFileName);

    /// <summary>上次成功获取的本地时间；从没成功过返回 null。</summary>
    public static DateTime? CachedAt
    {
        get
        {
            var ticks = Preferences.Default.Get(CacheTimeKey, 0L);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Local) : null;
        }
    }

    /// <summary>读上次缓存的正文（可能没有）。</summary>
    public static async Task<string?> ReadCacheAsync()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            var text = await File.ReadAllTextAsync(CachePath);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"Read cloud drive cache failed: {ex}");
            return null;
        }
    }

    /// <summary>
    /// 依次尝试各个源取正文，成功后更新缓存。
    /// 全部失败返回 null —— 调用方决定是退回缓存还是显示"获取失败"。
    /// </summary>
    public static async Task<string?> FetchAsync(CancellationToken cancellationToken = default)
    {
        foreach (var url in Sources)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(FetchTimeout);

                var text = await NetworkService.GetStringAsync(url, cancellationToken: cts.Token);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                await WriteCacheAsync(text);
                return text;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 这个源超时了：换下一个（调用方没有取消）
            }
            catch (Exception ex)
            {
                App.LogManager?.Error($"Fetch cloud drive resources failed ({url}): {ex.Message}");
            }
        }

        return null;
    }

    private static async Task WriteCacheAsync(string text)
    {
        try
        {
            await File.WriteAllTextAsync(CachePath, text);
            Preferences.Default.Set(CacheTimeKey, DateTime.Now.Ticks);
        }
        catch (Exception ex)
        {
            // 缓存写不进去不算失败：本次仍然把内容显示出来
            App.LogManager?.Error($"Write cloud drive cache failed: {ex}");
        }
    }
}
