using HtmlAgilityPack;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services;

/// <summary>
/// 旧版数据处理。所有从旧版本数据结构迁移到新版本的逻辑都放在这里。
/// 每个迁移步骤通过判断旧数据是否存在来决定是否执行。
/// </summary>
public class LegacyUpgradeHandler
{
    private readonly string _appDataDir;

    public LegacyUpgradeHandler(StoragePathService storagePath)
    {
        _appDataDir = storagePath.RootPath;
    }

    /// <summary>
    /// 执行所有需要的升级步骤。
    /// </summary>
    public async Task RunAsync(WikiBook activeBook)
    {
        App.LogManager?.Info(App.Localization?.Get("LegacyUpgrade.Started") ?? "开始执行旧版数据迁移。");
        try
        {
            await RenameDataFolderAsync(activeBook);
            MigrateFailedListsOnce(activeBook);
            App.LogManager?.Info(App.Localization?.Get("LegacyUpgrade.Completed") ?? "旧版数据迁移完成。");
        }
        catch (Exception ex)
        {
            App.LogManager?.Error(App.Localization?.Get("LegacyUpgrade.Failed") ?? "旧版数据迁移失败", ex);
            throw;
        }
        finally
        {
        }
    }

    /// <summary>
    /// 如果存在旧的硬编码文件夹 "Terraria_Wiki"，将其重命名为 WikiBook.DataFolder，
    /// 并将内部的 .db 文件重命名为 "data.db"。
    /// </summary>
    private Task RenameDataFolderAsync(WikiBook activeBook)
    {
        string oldDir = Path.Combine(_appDataDir, "Terraria_Wiki");
        string newDir = Path.Combine(_appDataDir, activeBook.DataFolder);

        if (!Directory.Exists(oldDir))
            return Task.CompletedTask;

        // 先重命名文件夹
        Directory.Move(oldDir, newDir);

        // 将旧 db 文件重命名为 data.db
        string oldDbPath = Path.Combine(newDir, "Terraria_Wiki.db");
        string newDbPath = Path.Combine(newDir, "data.db");
        if (File.Exists(oldDbPath) && !File.Exists(newDbPath))
            File.Move(oldDbPath, newDbPath);

        // 删除 WAL/SHM 临时文件，SQLite 会自动重建
        foreach (var tmp in new[] { "Terraria_Wiki.db-wal", "Terraria_Wiki.db-shm" })
        {
            string path = Path.Combine(newDir, tmp);
            try { File.Delete(path); } catch { }
        }

        return Task.CompletedTask;
    }

    private void MigrateFailedListsOnce(WikiBook activeBook)
    {
        var dataDir = Path.Combine(_appDataDir, activeBook.DataFolder);
        var taskDir = Path.Combine(_appDataDir, "Tasks", activeBook.Id.ToString(), "legacy-upgrade");
        Directory.CreateDirectory(taskDir);

        MoveIfExists(
            Path.Combine(_appDataDir, "Tasks", activeBook.Id.ToString(), "failed_pages.txt"),
            Path.Combine(taskDir, "failed_pages.txt"));
        MoveIfExists(
            Path.Combine(_appDataDir, "Tasks", activeBook.Id.ToString(), "failed_resources.txt"),
            Path.Combine(taskDir, "failed_resources.txt"));
        MoveIfExists(
            Path.Combine(taskDir, "failed_pages.pending.txt"),
            Path.Combine(taskDir, "failed_pages.pending.txt"));
        MoveIfExists(
            Path.Combine(taskDir, "failed_resources.pending.txt"),
            Path.Combine(taskDir, "failed_resources.pending.txt"));
    }

    private static void MoveIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath)) return;
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(sourcePath, targetPath);
    }

    /// <summary>
    /// 检测旧版数据：如果默认页面的 <a> 标签没有 data-wiki 属性，
    /// 则为所有页面的 <a> 标签添加 data-wiki，值取自原有的 title 属性。
    /// </summary>
    public async Task MigrateAnchorDataWikiAsync(WikiBook activeBook)
    {
        var defaultPage = await App.ContentDb.GetItemAsync<WikiPage>(activeBook.DefaultPageTitle);
        if (defaultPage == null) return;

        // 默认页没有 <a> 标签（如占位内容"请先下载数据"），说明还没下载过数据，无需迁移
        if (!defaultPage.Content.Contains("<a ", StringComparison.OrdinalIgnoreCase)) return;

        // 已有 <a> 标签且含 data-wiki，说明已是新版数据
        if (defaultPage.Content.Contains("data-wiki")) return;

        MainPage? mainPage = null;
        if (Application.Current?.Windows[0].Page is MainPage mp)
        {
            mainPage = mp;
            mainPage.ShowLoadingPopup("更新旧版数据库", "正在更新旧版数据库，请稍候...");
        }

        try
        {
            App.LogManager?.Info(App.Localization?.Get("LegacyUpgrade.AnchorMigrationStarted") ?? "开始迁移页面链接数据。");
            var titles = await App.ContentDb.GetAllPrimaryKeysAsync<WikiPage>();
            var doc = new HtmlDocument();
            int processed = 0;

            foreach (var title in titles)
            {
                var page = await App.ContentDb.GetItemAsync<WikiPage>(title);
                if (page == null) continue;

                doc.LoadHtml(page.Content);
                var anchors = doc.DocumentNode.SelectNodes("//a[@title]");
                if (anchors != null)
                {
                    foreach (var a in anchors)
                    {
                        string titleAttr = a.GetAttributeValue("title", "");
                        if (!string.IsNullOrEmpty(titleAttr))
                            a.SetAttributeValue("data-wiki", titleAttr);
                    }
                    page.Content = doc.DocumentNode.OuterHtml;
                    await App.ContentDb.SaveItemAsync(page);
                }

                processed++;
                if (processed % 50 == 0)
                    mainPage?.ShowLoadingPopup("更新旧版数据库", $"正在更新旧版数据库，请稍候... ({processed}/{titles.Count})");
            }

            mainPage?.ShowLoadingPopup("更新旧版数据库", $"更新完成，共处理 {processed} 个页面");
            await Task.Delay(1500);
            await AppService.WikiRefreshAsync();
        }
        finally
        {
            mainPage?.HideLoadingPopup();
        }
    }
}
