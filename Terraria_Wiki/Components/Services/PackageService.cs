using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Terraria_Wiki.Models;
#if ANDROID
using Terraria_Wiki.Platforms.Android;
#endif

namespace Terraria_Wiki.Services;

public sealed class PackageService
{
    private const string PackageHeader = "WIKIDATA";
    private readonly LogService _log;
    private readonly LocalizationService _loc;
    private readonly StoragePathService _storagePath;
    private readonly AppTaskRunner _taskRunner;

    public PackageService(
        LogService log,
        LocalizationService loc,
        StoragePathService storagePath,
        AppTaskRunner taskRunner)
    {
        _log = log;
        _loc = loc;
        _storagePath = storagePath;
        _taskRunner = taskRunner;
    }

    private string CreateImportDirectory() => Path.Combine(
        _storagePath.RootPath,
        "Temp",
        "import-" + Guid.NewGuid().ToString("N"));

    public async Task ExportDataAsync(int selectedWikiId)
    {
        var tasks = await App.ManagerDb!.GetItemsAsync<AppTask>();
        bool hasIncompletePausableTask = tasks.Any(task =>
            task.WikiId == selectedWikiId &&
            task.CanPause &&
            task.Status != AppTaskStatus.Completed);
        if (hasIncompletePausableTask)
        {
            App.AppStateManager.TriggerAlert(
                _loc.Get("Common.Notice"),
                _loc.Get("DataService.Log.ExportBlockedByTask"));
            return;
        }

        await _taskRunner.RunAsync(
            AppTaskType.ExportData,
            _ => ExportDataCoreAsync(selectedWikiId),
            access: AppTaskAccess.Exclusive);
    }

    public Task ImportDataAsync() =>
        _taskRunner.RunAsync(
            AppTaskType.ImportData,
            _ => ImportDataCoreAsync(),
            access: AppTaskAccess.Exclusive);

    private async Task ExportDataCoreAsync(int selectedWikiId)
    {
        _log.Info(_loc.Get("DataService.Log.ExportDataStart"));
        string? finalPkgPath = null;
        string tempDbPath = Path.Combine(FileSystem.CacheDirectory, "temp_export.db");
        var wikiBook = await App.ManagerDb!.GetItemAsync<WikiBook>(selectedWikiId)
            ?? throw new InvalidOperationException("要导出的 Wiki 不存在于当前应用配置中。");
        string dataDirectory = Path.Combine(_storagePath.RootPath, wikiBook.DataFolder);
        string originalDbPath = Path.Combine(dataDirectory, "data.db");

        if (!File.Exists(originalDbPath))
        {
            _log.Error(_loc.Get("DataService.Log.NoDatabaseFile"));
            return;
        }

        try
        {
            _log.Info(_loc.Get("DataService.Log.BackingUpDatabase"));
            var contentDb = selectedWikiId == App.AppStateManager!.ActiveWikiBookId
                ? App.ContentDb!
                : new ContentDbService(originalDbPath);
            var conn = contentDb.GetConnection();
            await Task.Run(() => conn.BackupAsync(tempDbPath));

            _log.Info(_loc.Get("DataService.Log.StartPackaging"));
            var info = new WikiPackageInfo
            {
                Id = wikiBook.Id,
                Title = wikiBook.Title,
                IsPageDownloaded = wikiBook.IsPageDownloaded,
                IsResourceDownloaded = wikiBook.IsResourceDownloaded,
                UpdateTime = wikiBook.UpdateTime,
                AppVersion = AppInfo.Current.VersionString,
                Files = []
            };
            string exportFileName = wikiBook.Title + ".pkg";
            if (App.AppStateManager?.IsWindows == true)
            {
#if WINDOWS
                string? exportPath = await FileHelper.PickFolderWindowsAsync();
                if (exportPath is null) return;
                finalPkgPath = Path.Combine(exportPath, exportFileName);
#endif
            }
            else if (App.AppStateManager?.IsMobile == true || App.AppStateManager?.IsMacCatalyst == true)
            {
                finalPkgPath = Path.Combine(FileSystem.CacheDirectory, exportFileName);
            }
            else
            {
                throw new NotSupportedException("不支持的平台");
            }

            await Task.Run(async () =>
            {
                var files = Directory.GetFiles(dataDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(file => !file.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) &&
                                   !file.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _log.Info(_loc.Get("DataService.Log.CalculatingMetadata"));
                using var md5 = MD5.Create();
                foreach (var file in files)
                {
                    string fileToRead = file == originalDbPath ? tempDbPath : file;
                    using var input = File.OpenRead(fileToRead);
                    info.Files.Add(new FileMeta
                    {
                        RelativePath = Path.GetRelativePath(dataDirectory, file),
                        Size = input.Length,
                        MD5 = Convert.ToHexStringLower(md5.ComputeHash(input))
                    });
                }

                _log.Info(_loc.Get("DataService.Log.GeneratingPackage"));
                using var output = new FileStream(finalPkgPath!, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new BinaryWriter(output);
                writer.Write(Encoding.UTF8.GetBytes(PackageHeader));
                byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info, AppJsonContext.Custom.WikiPackageInfo));
                writer.Write(json.Length);
                writer.Write(json);

                foreach (var file in files)
                {
                    using var input = File.OpenRead(file == originalDbPath ? tempDbPath : file);
                    await input.CopyToAsync(output);
                }
            });

            if (App.AppStateManager?.IsAndroid == true)
            {
#if ANDROID
                _log.Info(_loc.Get("DataService.Log.WaitingForSaveLocation"));
                var uri = await AndroidFileSaver.PickSaveLocationAsync(exportFileName, "application/octet-stream");
                if (uri is null)
                {
                    _log.Info(_loc.Get("DataService.Log.UserCancelledSave"));
                    return;
                }

                using var input = File.OpenRead(finalPkgPath);
                using var output = Android.App.Application.Context.ContentResolver.OpenOutputStream(uri);
                if (output is not null)
                {
                    await input.CopyToAsync(output);
                    await output.FlushAsync();
                }
#endif
            }
            else if (App.AppStateManager?.IsIOS == true || App.AppStateManager?.IsMacCatalyst == true)
            {
                await FileHelper.ExportFileAppleAsync(finalPkgPath!);
            }

            _log.Success(_loc.Get("DataService.Log.ExportSuccess", finalPkgPath));
            App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ExportSuccessShort"));
        }
        finally
        {
            if (File.Exists(tempDbPath))
                try { File.Delete(tempDbPath); } catch { }

            if (App.AppStateManager?.IsMobile == true || App.AppStateManager?.IsMacCatalyst == true)
                await FileHelper.ClearAppCacheAsync();
        }
    }

    private async Task ImportDataCoreAsync()
    {
        _log.Info(_loc.Get("DataService.Log.ImportDataStart"));
#if ANDROID
        Android.Net.Uri? packageUri = null;
#else
        FileResult? fileResult = null;
#endif
        string? importDirectory = null;
        try
        {
            var fileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".pkg"]
            });
#if ANDROID
            packageUri = await AndroidFilePicker.PickPackageAsync();
            if (packageUri is null) return;
            Func<Task<Stream>> openStream = () => OpenAndroidStreamAsync(packageUri);
#else
            fileResult = await FileHelper.PickFileAsync(
                _loc.Get("DataService.Log.SelectImportPackage"),
                App.AppStateManager?.IsWindows == true ? fileType : null);
            if (fileResult is null) return;
            Func<Task<Stream>> openStream = fileResult.OpenReadAsync;
#endif

            if (Application.Current?.Windows[0].Page is MainPage mainPage)
                mainPage.ShowLoadingPopup("导入数据", "正在导入数据，请稍候...");

            WikiPackageInfo meta = await ReadMetadataAsync(openStream);
            bool needMigration = await ConfirmMigrationAsync(meta);
            if (meta.AppVersion is not null && Version.TryParse(meta.AppVersion, out var version) && version < new Version(0, 4) && !needMigration)
                return;

            importDirectory = CreateImportDirectory();
            await ExtractAndVerifyAsync(openStream, meta, importDirectory);
            var wikiBook = await App.ManagerDb!.GetItemAsync<WikiBook>(meta.Id)
                ?? throw new InvalidDataException("导入包中的 Wiki 不存在于当前应用配置中。");
            bool isActive = meta.Id == App.AppStateManager!.ActiveWikiBookId;
            string targetDirectory = Path.Combine(_storagePath.RootPath, wikiBook.DataFolder);

            _log.Info(_loc.Get("DataService.Log.ReplacingLocalFiles"));
            if (isActive) await App.ContentDb!.CloseConnection();
            await Task.Run(() => ReplaceDataDirectory(importDirectory, targetDirectory));
            importDirectory = null;

            _log.Info(_loc.Get("DataService.Log.UpdatingDatabase"));
            wikiBook.IsPageDownloaded = meta.IsPageDownloaded;
            wikiBook.IsResourceDownloaded = meta.IsResourceDownloaded;
            wikiBook.UpdateTime = meta.UpdateTime;
            await App.ManagerDb.SaveItemAsync(wikiBook);
            await ClearWikiTaskStateAsync(meta.Id);
            if (App.AppStateManager.CurrentDownloadTask?.WikiId == meta.Id)
                App.AppStateManager.SetCurrentDownloadTask(null);

            if (isActive)
            {
                await App.ContentDb!.ReconnectAsync();
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                await AppService.WikiRefreshAsync();
                if (needMigration)
                    await new LegacyUpgradeHandler(_storagePath).MigrateAnchorDataWikiAsync(App.AppStateManager.ActiveWikiBook);
            }

            _log.Success(_loc.Get("DataService.Log.ImportSuccess"));
            App.AppStateManager.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ImportSuccess"));
        }
        finally
        {
            if (importDirectory is not null && Directory.Exists(importDirectory))
                Directory.Delete(importDirectory, true);
            if (Application.Current?.Windows[0].Page is MainPage mainPage)
                mainPage.HideLoadingPopup();
        }
    }

    private async Task ClearWikiTaskStateAsync(int wikiId)
    {
        var tasks = await App.ManagerDb!.GetItemsAsync<AppTask>();
        foreach (var task in tasks.Where(task => task.WikiId == wikiId))
            await App.ManagerDb.DeleteItemAsync<AppTask>(task.Id);

        string taskDirectory = Path.Combine(_storagePath.RootPath, "Tasks", wikiId.ToString());
        if (Directory.Exists(taskDirectory))
            Directory.Delete(taskDirectory, true);
    }

#if ANDROID
    private static Task<Stream> OpenAndroidStreamAsync(Android.Net.Uri uri)
    {
        var stream = Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        return Task.FromResult<Stream>(stream ?? throw new IOException("无法打开 Android 文档流。"));
    }
#endif

    private async Task<WikiPackageInfo> ReadMetadataAsync(Func<Task<Stream>> openStream)
    {
        await using var input = await openStream();
        return await Task.Run(() =>
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            if (Encoding.UTF8.GetString(reader.ReadBytes(PackageHeader.Length)) != PackageHeader)
                throw new InvalidDataException("非法的文件格式：无法识别该导入包！");
            int length = reader.ReadInt32();
            string json = Encoding.UTF8.GetString(reader.ReadBytes(length));
            Debug.Write(json);
            return JsonSerializer.Deserialize<WikiPackageInfo>(json, AppJsonContext.Custom.WikiPackageInfo)
                ?? throw new InvalidDataException("导入包元数据无效。");
        });
    }

    private async Task<bool> ConfirmMigrationAsync(WikiPackageInfo meta)
    {
        if (meta.AppVersion is null || !Version.TryParse(meta.AppVersion, out var version) || version >= new Version(0, 4))
            return false;
        var page = Application.Current?.Windows[0].Page;
        return page is null || await page.DisplayAlertAsync(
            _loc.Get("DataService.Log.ImportOldVersionTitle"),
            _loc.Get("DataService.Log.ImportOldVersionDesc", meta.AppVersion, AppInfo.Current.VersionString),
            _loc.Get("Common.OK"),
            _loc.Get("Common.Cancel"));
    }

    private async Task ExtractAndVerifyAsync(Func<Task<Stream>> openStream, WikiPackageInfo meta, string importDirectory)
    {
        await using var input = await openStream();
        await Task.Run(() =>
        {
            using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
            reader.ReadBytes(PackageHeader.Length);
            reader.ReadBytes(reader.ReadInt32());
            Directory.CreateDirectory(importDirectory);
            string importRoot = Path.GetFullPath(importDirectory) + Path.DirectorySeparatorChar;
            _log.Info(_loc.Get("DataService.Log.ExtractingAndVerifying"));
            using var md5 = MD5.Create();
            byte[] buffer = new byte[1024 * 1024];
            foreach (var file in meta.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath) || Path.IsPathRooted(file.RelativePath))
                    throw new InvalidDataException($"导入包包含无效文件路径: {file.RelativePath}");

                string outputPath = Path.GetFullPath(Path.Combine(importDirectory, file.RelativePath));
                if (!outputPath.StartsWith(importRoot, StringComparison.Ordinal))
                    throw new InvalidDataException($"导入包包含超出数据目录的文件路径: {file.RelativePath}");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                md5.Initialize();
                for (long remaining = file.Size; remaining > 0;)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new InvalidDataException("文件意外结束，包可能已损坏！");
                    output.Write(buffer, 0, read);
                    md5.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }
                md5.TransformFinalBlock([], 0, 0);
                if (!string.Equals(Convert.ToHexStringLower(md5.Hash!), file.MD5, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"数据校验失败！文件已被篡改或损坏: {file.RelativePath}");
            }
        });
    }

    private static void ReplaceDataDirectory(string importDirectory, string targetDirectory)
    {
        string backupDirectory = targetDirectory + ".import-backup-" + Guid.NewGuid().ToString("N");
        bool targetMoved = false;
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Move(targetDirectory, backupDirectory);
                targetMoved = true;
            }

            Directory.Move(importDirectory, targetDirectory);
            string oldDbPath = Path.Combine(targetDirectory, "Terraria_Wiki.db");
            if (File.Exists(oldDbPath))
            {
                string dbPath = Path.Combine(targetDirectory, "data.db");
                if (File.Exists(dbPath)) File.Delete(dbPath);
                File.Move(oldDbPath, dbPath);
            }

            if (targetMoved) Directory.Delete(backupDirectory, true);
        }
        catch
        {
            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
            if (targetMoved && Directory.Exists(backupDirectory))
                Directory.Move(backupDirectory, targetDirectory);
            throw;
        }
    }
}
