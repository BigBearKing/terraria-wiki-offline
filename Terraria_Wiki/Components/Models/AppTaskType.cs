namespace Terraria_Wiki.Models;

public enum AppTaskType
{
    None = 0,
    CheckUpdate = 1,
    DownloadPages = 2,
    DownloadResources = 3,
    UpdateData = 4,
    DownloadAll = 13,
    UpdatePages = 14,
    UpdateAll = 15,
    DeleteResources = 6,
    RetryFailed = 7,
    DeleteData = 8,
    ExportData = 9,
    ImportData = 10,
    MigrateData = 11,
    LegacyUpgrade = 12
    ,ExportLog = 16
    ,DeleteLog = 17
    ,ClearFailedList = 18
}
