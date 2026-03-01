using System.Net.Http.Json;
using Terraria_Wiki.Models; // 替换为你的命名空间

namespace Terraria_Wiki.Services
{
    public class UpdateService
    {
        private readonly HttpClient _httpClient;

        // 替换为你的 GitHub 用户名和仓库名
        private const string GitHubOwner = "BigBearKing";
        private const string GitHubRepo = "terraria-wiki-offline";

        public UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TerrariaWikiScraper/1.0 (contact: bigbearkingus@gmail.com)");
        }

        public async Task<UpdateCheckResult?> CheckForUpdatesAsync()
        {
            App.AppStateManager.IsDownloading = true;
            try
            {
                // 1. 构建 GitHub API URL 获取 Latest Release
                string apiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

                // 2. 发送 GET 请求并解析 JSON
                var releaseInfo = await _httpClient.GetFromJsonAsync<GitHubReleaseInfo>(apiUrl);

                if (releaseInfo == null || string.IsNullOrEmpty(releaseInfo.TagName))
                {
                    return null;
                }

                // 3. 获取本地当前版本号
                // MAUI 中可以使用 AppInfo.Current.VersionString，比如 "1.0.0"
                string currentVersionStr = AppInfo.Current.VersionString;

                // 4. 清理版本号字符串（GitHub 的 tag 通常带 'v'，如 "v1.0.1"）
                string latestVersionStr = releaseInfo.TagName.TrimStart('v', 'V');

                // 5. 使用 System.Version 进行安全的版本比对
                if (Version.TryParse(currentVersionStr, out Version currentVersion) &&
                    Version.TryParse(latestVersionStr, out Version latestVersion))
                {
                    bool isUpdateAvailable = latestVersion > currentVersion;

                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = isUpdateAvailable,
                        LatestVersion = latestVersionStr,
                        ReleaseNotes = releaseInfo.ReleaseNotes,
                        DownloadUrl = releaseInfo.ReleaseUrl // 引导用户去 GitHub 发布的网页下载
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                throw (ex);
            }
            finally
            {
                App.AppStateManager.IsDownloading = false;
            }
        }
    }
}