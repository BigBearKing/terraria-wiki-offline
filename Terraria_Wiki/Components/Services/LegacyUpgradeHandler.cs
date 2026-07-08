using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services;

/// <summary>
/// 旧版数据处理。所有从旧版本数据结构迁移到新版本的逻辑都放在这里。
/// 每个迁移步骤通过判断旧数据是否存在来决定是否执行。
/// </summary>
public class LegacyUpgradeHandler
{
    private readonly string _appDataDir;

    public LegacyUpgradeHandler()
    {
        _appDataDir = FileSystem.AppDataDirectory;
    }

    /// <summary>
    /// 执行所有需要的升级步骤。
    /// </summary>
    public async Task RunAsync(WikiBook activeBook)
    {
        await RenameDataFolderAsync(activeBook);

        // 未来升级：判断旧数据特征存在则执行
        // if (File.Exists(Path.Combine(_appDataDir, "old_config.json")))
        //     await MigrateOldConfigAsync();
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
}
