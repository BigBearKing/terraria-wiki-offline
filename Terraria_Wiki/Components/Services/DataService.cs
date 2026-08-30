using HtmlAgilityPack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    public class DataService
    {
        private string _baseDir;
        private string _currentDataDir;
        private string _resListPath;
        private string _tempResListPath;
        private string _pageListPath;
        private string _failedPageListPath;
        private string _tempFailedPageListPath;
        private string _failedResListPath;
        private string _tempFailedResListPath;
        private string _updatePageListPath;
        private string _updateResListPath;

        // 从 WikiBook 加载的 Wiki 源配置
        private string _baseApiUrl;
        private string _baseUrl;
        private string _redirectStartUrl;
        private int _mainNamespace;
        private List<int> _additionalNamespaces = [];
        private string _junkXPath;
        // ================= 事件与状态 =================
        private readonly LogService _log;
        private readonly LocalizationService _loc;
        private readonly StoragePathService _storagePath;
        private int _maxRetryAttempts;
        private int _pageConcurrency;
        private int _resConcurrency;
        private readonly SemaphoreSlim _downloadLock = new(1, 1);
        private CancellationTokenSource? _downloadCts;
        private DownloadTask? _activeDownloadTask;
        private int _completedPages;
        private int _completedResources;
        private int _totalPages;
        private int _totalResources;
        private readonly SemaphoreSlim _taskSaveLock = new(1, 1);

        public DataService(LogService logService, LocalizationService localizationService, StoragePathService storagePath)
        {
            _log = logService;
            _loc = localizationService;
            _storagePath = storagePath;
            
        }

        private string TempDir => Path.Combine(_storagePath.RootPath, "Temp");


        public async Task StartOrResumeDownloadAsync(bool includeResources)
        {
            await RunDownloadAsync(includeResources, DownloadTaskType.DownloadAll);
        }

        public async Task PauseDownloadAsync()
        {
            if (_activeDownloadTask is null) return;
            _activeDownloadTask.Status = DownloadTaskStatus.Paused;
            await SaveDownloadTaskAsync();
            _downloadCts?.Cancel();
        }

        public async Task DownloadDataAsync(bool includeResources, CancellationToken cancellationToken = default) =>
            await StartOrResumeDownloadAsync(includeResources);

        public async Task DownloadResourcesAsync(CancellationToken cancellationToken = default) =>
            await RunDownloadAsync(true, DownloadTaskType.DownloadResources);

        private async Task RunDownloadAsync(bool includeResources, DownloadTaskType taskType)
        {
            if (!await _downloadLock.WaitAsync(0)) return;
            try
            {
                InitializeSettings(cleanupTemporaryFiles: false);
                var wikiId = App.AppStateManager!.ActiveWikiBookId;
                var task = (await App.ManagerDb!.GetItemsAsync<DownloadTask>())
                    .Where(t => t.WikiId == wikiId && t.TaskType == taskType && t.Status != DownloadTaskStatus.Completed)
                    .OrderByDescending(t => t.UpdatedTime).FirstOrDefault();
                if (task is null)
                {
                    task = new DownloadTask
                    {
                        WikiId = wikiId, TaskType = taskType, IncludeResources = includeResources,
                        TaskDirectory = Path.Combine(TempDir, $"download_{wikiId}_{(int)taskType}"),
                        Phase = taskType == DownloadTaskType.DownloadResources ? DownloadTaskPhase.DownloadingResources : DownloadTaskPhase.FetchingLists,
                        CreatedTime = DateTime.Now, UpdatedTime = DateTime.Now
                    };
                    Directory.CreateDirectory(task.TaskDirectory);
                    await App.ManagerDb.SaveItemAsync(task);
                }
                _activeDownloadTask = task;
                var existingResourceList = _resListPath;
                ConfigureDownloadPaths(task);
                if (task.TaskType == DownloadTaskType.DownloadResources && !File.Exists(_resListPath) && File.Exists(existingResourceList))
                    File.Copy(existingResourceList, _resListPath, true);
                _downloadCts = new CancellationTokenSource();
                task.Status = DownloadTaskStatus.Running;
                App.AppStateManager.ProcessingTaskId = taskType == DownloadTaskType.DownloadResources ? 3 : 2;
                await SaveDownloadTaskAsync();
                await ExecuteDownloadAsync(task, _downloadCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (_activeDownloadTask != null && _activeDownloadTask.Status == DownloadTaskStatus.Running)
                    _activeDownloadTask.Status = DownloadTaskStatus.Interrupted;
                await SaveDownloadTaskAsync();
            }
            catch (Exception ex)
            {
                if (_activeDownloadTask != null)
                {
                    _activeDownloadTask.Status = DownloadTaskStatus.Failed;
                    _activeDownloadTask.LastError = ex.Message;
                    await SaveDownloadTaskAsync();
                }
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), ex);
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
                App.AppStateManager!.ProcessingTaskId = 0;
                _downloadLock.Release();
            }
        }

        private async Task ExecuteDownloadAsync(DownloadTask task, CancellationToken token)
        {
            var book = App.AppStateManager!.ActiveWikiBook;
            if (task.TaskType != DownloadTaskType.DownloadResources)
            {
                task.Phase = DownloadTaskPhase.FetchingLists;
                if (!File.Exists(_pageListPath) || new FileInfo(_pageListPath).Length == 0)
                {
                    await FetchWikiRedirectsListAsync(token);
                    await FetchWikiPagesListAsync(token);
                }
                _totalPages = task.TotalPages = CountLines(_pageListPath);
                task.Phase = DownloadTaskPhase.DownloadingPages;
                await SaveDownloadTaskAsync();
                _completedPages = task.CompletedPages = Math.Max(0, _totalPages - CountLines(_pageListPath));
                await DownloadPagesBatchAsync(_pageListPath, _resListPath, _failedPageListPath, _pageConcurrency, token);
                task.CompletedPages = _totalPages;
                if (task.IncludeResources)
                {
                    task.Phase = DownloadTaskPhase.DownloadingResources;
                    if (!File.Exists(_resListPath)) throw new InvalidOperationException("资源清单不存在。");
                    _totalResources = task.TotalResources = CountLines(_resListPath);
                    if (!File.Exists(_tempResListPath)) File.Copy(_resListPath, _tempResListPath, true);
                    _completedResources = task.CompletedResources = Math.Max(0, _totalResources - CountLines(_tempResListPath));
                    await SaveDownloadTaskAsync();
                    await DownloadResourcesBatchAsync(_resListPath, _failedResListPath, _resConcurrency, cancellationToken: token);
                    task.CompletedResources = _totalResources;
                    book.IsResourceDownloaded = true;
                }
            }
            else
            {
                if (!FileHelper.IsFileValid(_resListPath)) throw new InvalidOperationException("资源清单无效。");
                task.Phase = DownloadTaskPhase.DownloadingResources;
                _totalResources = task.TotalResources = CountLines(_resListPath);
                if (!File.Exists(_tempResListPath)) File.Copy(_resListPath, _tempResListPath, true);
                _completedResources = task.CompletedResources = Math.Max(0, _totalResources - CountLines(_tempResListPath));
                await DownloadResourcesBatchAsync(_resListPath, _failedResListPath, _resConcurrency, cancellationToken: token);
                task.CompletedResources = _totalResources;
                book.IsResourceDownloaded = true;
            }
            task.Phase = DownloadTaskPhase.PostProcessing;
            await SaveDownloadTaskAsync();
            book.IsPageDownloaded = task.TaskType != DownloadTaskType.DownloadResources || book.IsPageDownloaded;
            book.UpdateTime = DateTime.Now;
            await App.ManagerDb.SaveItemAsync(book);
            await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
            CleanupDownloadDirectory(task);
            task.Status = DownloadTaskStatus.Completed;
            task.Progress = 100;
            await SaveDownloadTaskAsync();
            App.AppStateManager.CurrentDownloadTask = task;
        }

        private void ConfigureDownloadPaths(DownloadTask task)
        {
            var dir = task.TaskDirectory;
            Directory.CreateDirectory(dir);
            _pageListPath = Path.Combine(dir, "pages.pending.txt");
            _resListPath = Path.Combine(dir, "resources.txt");
            _tempResListPath = Path.Combine(dir, "resources.pending.txt");
            _failedPageListPath = Path.Combine(dir, "failed_pages.txt");
            _failedResListPath = Path.Combine(dir, "failed_resources.txt");
        }

        private static int CountLines(string path) => File.Exists(path) ? File.ReadLines(path).Count() : 0;

        private async Task SaveDownloadTaskAsync()
        {
            if (_activeDownloadTask is null || App.ManagerDb is null) return;
            await _taskSaveLock.WaitAsync();
            try
            {
                _activeDownloadTask.Progress = CalculateDownloadProgress(_activeDownloadTask);
                _activeDownloadTask.UpdatedTime = DateTime.Now;
                await App.ManagerDb.SaveItemAsync(_activeDownloadTask);
                App.AppStateManager!.CurrentDownloadTask = _activeDownloadTask;
            }
            finally
            {
                _taskSaveLock.Release();
            }
        }

        private static double CalculateDownloadProgress(DownloadTask task)
        {
            if (task.TaskType == DownloadTaskType.DownloadResources)
                return task.TotalResources == 0 ? 0 : task.CompletedResources * 100d / task.TotalResources;
            var list = task.Phase == DownloadTaskPhase.FetchingLists ? 0 : 1;
            var pages = task.TotalPages == 0 ? 0 : task.CompletedPages / (double)task.TotalPages;
            if (!task.IncludeResources) return (list * 10 + pages * 45) / 55 * 100;
            var resources = task.TotalResources == 0 ? 0 : task.CompletedResources / (double)task.TotalResources;
            return list * 10 + pages * 45 + resources * 44 + (task.Phase == DownloadTaskPhase.PostProcessing || task.Status == DownloadTaskStatus.Completed ? 1 : 0);
        }

        private void CleanupDownloadDirectory(DownloadTask task)
        {
            if (Directory.Exists(task.TaskDirectory)) Directory.Delete(task.TaskDirectory, true);
        }

        //更新页面和资源
        public async Task UpdateDataAsync(bool includeResources, CancellationToken cancellationToken = default)
        {
            App.AppStateManager?.ProcessingTaskId = 4;
            if (includeResources)
            {
                _log.Info(_loc.Get("DataService.Log.UpdateAllPagesAndAssets"));
            }
            else
            {
                _log.Info(_loc.Get("DataService.Log.UpdatePagesOnly"));
            }
            try
            {
                InitializeSettings();
                //获取新的页面列表
                await FetchWikiRedirectsListAsync();
                await FetchWikiPagesListAsync();

                //检查是否有要更新的页面
                int updateCount = await CheckForPageUpdatesAsync(cancellationToken);
                _log.Success(_loc.Get("DataService.Log.UpdateListReady", updateCount));
                if (updateCount == 0)
                {
                    App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.NoPagesNeedUpdate"));
                    return;
                }

                if (includeResources)
                {
                    await DownloadPagesBatchAsync(_updatePageListPath, _updateResListPath, _failedPageListPath, _pageConcurrency, cancellationToken);
                    await DownloadResourcesBatchAsync(_updateResListPath, _failedResListPath, _resConcurrency, cancellationToken: cancellationToken);
                }
                else
                {
                    await DownloadPagesBatchAsync(_updatePageListPath, _updateResListPath, _failedPageListPath, _pageConcurrency, cancellationToken);
                }
                await FileHelper.AppendFileAsync(_updateResListPath, _resListPath);
                string tempFile = Path.Combine(_currentDataDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                FileHelper.RemoveDuplicatesOptimized(_resListPath, tempFile);
                File.Delete(_resListPath);
                File.Move(tempFile, _resListPath, true);
                var book = App.AppStateManager.ActiveWikiBook;
                book.UpdateTime = DateTime.Now;
                await App.ManagerDb.SaveItemAsync(book);
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanupTemporaryFiles();
                if (includeResources)
                {
                    _log.Success(_loc.Get("DataService.Log.AllPagesAndAssetsUpdated"));
                    App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.AllPagesAndAssetsUpdated"));
                }
                else
                {
                    _log.Success(_loc.Get("DataService.Log.PagesUpdateCompleted"));
                    App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.PagesUpdateCompleted"));
                }

            }
            catch (Exception e)
            {
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), e);
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ErrorWithMessage", e.Message));
            }
            finally
            {
                App.AppStateManager?.ProcessingTaskId = 0;

            }
        }


        //检查是否有要更新的页面
        private async Task<int> CheckForPageUpdatesAsync(CancellationToken cancellationToken = default)
        {
            using var writer = new BatchLineWriter(_updatePageListPath, 200);
            int totalCount = 0;
            if (File.Exists(_pageListPath))
            {
                totalCount = File.ReadLines(_pageListPath).Count(); // 加上这行计算总数
            }
            int currentCount = 0;
            int updateCount = 0;
            using var provider = new BatchLineProvider(_pageListPath);
            var scheduler = new BatchTaskScheduler<BatchLineItem>(_maxRetryAttempts);
            async Task ProcessPageLine(int workerId, BatchLineItem item, CancellationToken token)
            {
                string line = item.Line;
                var parts = line.Split('|');
                if (parts.Length < 2)
                {
                    item.Complete();
                    return;
                }
                var page = new PageInfo { Title = parts[0], LastModified = DateTime.Parse(parts[1]) };
                try
                {
                    if (await App.ContentDb.ItemExistsAsync<WikiPage>(page.Title))
                    {
                        var oldpage = await App.ContentDb.GetItemAsync<WikiPage>(page.Title);
                        if (oldpage.LastModified != page.LastModified)
                        {
                            writer.Add(line);
                            Interlocked.Increment(ref updateCount);
                        }
                    }
                    else
                    {
                        writer.Add(line);
                        Interlocked.Increment(ref updateCount);
                    }
                    item.Complete();
                }
                finally
                {
                }


            }
            await scheduler.RunAsync(
                provider.GetNextItemAsync,
                ProcessPageLine,
                async (_, item, ex, token) =>
                {
                    await AppendFailedUrlAsync(_failedPageListPath, item.Line);
                    item.Complete();
                },
                concurrency: 1,
                cancellationToken: cancellationToken);
            writer.Flush();
            return updateCount;
        }

        //清理数据库
        public async Task CleanupResourcesAsync()
        {
            App.AppStateManager?.ProcessingTaskId = 5;
            _log.Info(_loc.Get("DataService.Log.CleanUnusedAssetsStart"));
            try
            {
                await App.ContentDb.VacuumDatabaseAsync();
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                _log.Success(_loc.Get("DataService.Log.CleanUnusedAssetsCompleted"));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.CleanUnusedAssetsCompleted"));
            }
            catch (Exception ex)
            {
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), ex);
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ErrorWithMessage", ex.Message));
            }
            finally
            {

                App.AppStateManager?.ProcessingTaskId = 0;
            }

        }

        //删除图片资源
        public async Task DeleteResourcesAsync()
        {
            App.AppStateManager?.ProcessingTaskId = 6;
            _log.Info(_loc.Get("DataService.Log.DeleteAssetsStart"));
            try
            {
                await App.ContentDb.DeleteItemsAsync<WikiAsset>();
                var wikiBook = App.AppStateManager.ActiveWikiBook;
                wikiBook.IsResourceDownloaded = false;
                await App.ManagerDb.SaveItemAsync(wikiBook);
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                await AppService.WikiRefreshAsync();

                // 后台压缩数据库：先合并 WAL 回主库，再 VACUUM 回收磁盘空间
                // （SQLite 的 DELETE 只标记空闲页，文件大小不变，必须 VACUUM 才归还空间）
                // 注意：VACUUM 会重建整个库文件，期间避免并发读写，故放后台线程执行
                await Task.Run(async () =>
                {
                    var conn = App.ContentDb.GetConnection();
                    await conn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE)");
                    await App.ContentDb.VacuumDatabaseAsync();
                });

                _log.Success(_loc.Get("DataService.Log.DeleteAssetsCompleted"));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.DeleteAssetsCompleted"));

            }
            catch (Exception ex)

            {
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), ex);
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Error"), ex.Message);
            }
            finally
            {
                App.AppStateManager?.ProcessingTaskId = 0;
            }
        }

        //检查是否有失败列表
        public bool HasFailedItems()
        {
            if (App.AppStateManager.ProcessingTaskId != 0)
            {
                return false;
            }


            if (!(FileHelper.IsFileValid(_failedResListPath) || FileHelper.IsFileValid(_failedPageListPath)))
                return false;

            return true;

        }

        //重试失败列表
        public async Task RetryFailedItemsAsync()
        {
            App.AppStateManager?.ProcessingTaskId = 7;
            try
            {
                bool includeResources = true;
                var wikiBook = App.AppStateManager.ActiveWikiBook;
                if (!wikiBook.IsResourceDownloaded) includeResources = false;
                InitializeSettings();

                if (FileHelper.IsFileValid(_failedPageListPath))
                {
                    _log.Info(_loc.Get("DataService.Log.RetryFailedPages"));
                    await DownloadPagesBatchAsync(_failedPageListPath, _failedResListPath, _tempFailedPageListPath, 1);
                    await FileHelper.AppendFileAsync(_failedResListPath, _resListPath);
                    string tempFile = Path.Combine(_currentDataDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    FileHelper.RemoveDuplicatesOptimized(_resListPath, tempFile);
                    File.Delete(_resListPath);
                    File.Move(tempFile, _resListPath, true);
                    // 用本次仍失败的条目替换旧失败列表，成功的条目自动清除
                    ReplaceFailedList(_tempFailedPageListPath, _failedPageListPath);

                }

                if (FileHelper.IsFileValid(_failedResListPath) && includeResources)
                {
                    _log.Info(_loc.Get("DataService.Log.RetryFailedAssets"));
                    await DownloadResourcesBatchAsync(_failedResListPath, _tempFailedResListPath, 1, false);
                    // 用本次仍失败的条目替换旧失败列表，成功的条目自动清除
                    ReplaceFailedList(_tempFailedResListPath, _failedResListPath);
                }
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanupTemporaryFiles();
                _log.Success(_loc.Get("DataService.Log.RetryCompleted"));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.RetryCompleted"));
            }
            catch (Exception ex)
            {
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), ex);
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ErrorWithMessage", ex.Message));
            }
            finally
            {
                App.AppStateManager?.ProcessingTaskId = 0;
            }
        }

        public void ClearFailedList()
        {

            if (File.Exists(_failedPageListPath))
            {
                File.Delete(_failedPageListPath);
                _log.Info(_loc.Get("DataService.Log.FailedPagesCleared"));

            }
            if (File.Exists(_failedResListPath))
            {
                File.Delete(_failedResListPath);
                _log.Info(_loc.Get("DataService.Log.FailedAssetsCleared"));
            }
            App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.FailedListCleared"));

        }
        //删除文件夹
        public async Task DeleteDatabaseAsync()
        {
            App.AppStateManager?.ProcessingTaskId = 8;
            _log.Info(_loc.Get("DataService.Log.DeletingDatabase"));
            try
            {
                await App.ContentDb.CloseConnection();
                await Task.Run(() =>
                {
                    DeleteDataDirectory();
                });
                await App.ManagerDb.DeleteItemAsync<WikiBook>(App.AppStateManager.ActiveWikiBookId);
                await App.ManagerDb.Init(true);
                await App.ContentDb.ReconnectAsync();
                await App.ContentDb.Init(true, App.AppStateManager.ActiveWikiBook);
                App.AppStateManager.ResetWikiNavigation();
                await AppService.WikiRefreshAsync();
                _log.Success(_loc.Get("DataService.Log.DatabaseDeleted"));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.DatabaseDeleted"));
            }
            catch (Exception ex)
            {
                _log.Error(_loc.Get("DataService.Log.ErrorOccurred"), ex);
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Error"), ex.Message);
            }
            finally
            {
                App.AppStateManager?.ProcessingTaskId = 0;
            }

        }
        public void DeleteDataDirectory()
        {
            if (Directory.Exists(_currentDataDir))
                Directory.Delete(_currentDataDir, true);
        }


        //导出数据
        public async Task ExportDataAsync(int selectedWikiId)
        {
            App.AppStateManager?.ProcessingTaskId = 9;
            _log.Info(_loc.Get("DataService.Log.ExportDataStart"));
            InitializeSettings();
            string finalPkgPath = null;

            // --- 准备工作 ---
            string originalDbPath = App.ContentDb.DatabasePath;
            string tempDbPath = Path.Combine(FileSystem.CacheDirectory, "temp_export.db");
            string exportFileName ="default.pkg";

            if (!File.Exists(originalDbPath))
            {
                _log.Error(_loc.Get("DataService.Log.NoDatabaseFile"));
                App.AppStateManager?.ProcessingTaskId = 0;
                return;
            }

            try
            {
                // 1. 在线备份数据库 (SQLite API 本身支持异步，留在 UI 线程即可)
                _log.Info(_loc.Get("DataService.Log.BackingUpDatabase"));
                var conn = App.ContentDb.GetConnection();
                await Task.Run(async () =>
                {
                    await conn.BackupAsync(tempDbPath);
                });
                // 2. 准备基础数据
                _log.Info(_loc.Get("DataService.Log.StartPackaging"));
                var wikibook = await App.ManagerDb.GetItemAsync<WikiBook>(selectedWikiId);
                var info = new WikiPackageInfo
                {
                    Id = wikibook.Id,
                    Title = wikibook.Title,
                    IsPageDownloaded = wikibook.IsPageDownloaded,
                    IsResourceDownloaded = wikibook.IsResourceDownloaded,
                    UpdateTime = wikibook.UpdateTime,
                    AppVersion = AppInfo.Current.VersionString,
                    Files = new List<FileMeta>()
                };
                exportFileName = wikibook.Title + ".pkg";
                // 3. 获取导出路径（准备阶段）
                if (App.AppStateManager?.IsWindows == true)
                {
#if WINDOWS
                    string exportPath = await FileHelper.PickFolderWindowsAsync();
                    if (exportPath == null) return; // 用户取消了选择
                    finalPkgPath = Path.Combine(exportPath, exportFileName);
#endif
                }
                else if (App.AppStateManager?.IsMobile == true || App.AppStateManager?.IsMacCatalyst == true)
                {
                    // 【修改点 1】移动端 (包括安卓) 统一先输出到缓存目录
                    finalPkgPath = Path.Combine(FileSystem.CacheDirectory, exportFileName);
                }
                else
                {
                    throw new Exception("不支持的平台");
                }

                // ==========================================
                // 阶段 4：进入后台线程，执行脏活累活（算哈希、写大文件）
                // ==========================================
                await Task.Run(async () =>
                {
                    // 获取所有文件
                    var files = Directory.GetFiles(_currentDataDir, "*.*", SearchOption.AllDirectories).Where(f =>
                        !f.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                    ).ToList();

                    // 计算 MD5
                    _log.Info(_loc.Get("DataService.Log.CalculatingMetadata"));
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        foreach (var file in files)
                        {
                            string fileToRead = (file == originalDbPath) ? tempDbPath : file;
                            using var fs = File.OpenRead(fileToRead);
                            byte[] hashBytes = md5.ComputeHash(fs);

                            info.Files.Add(new FileMeta
                            {
                                RelativePath = Path.GetRelativePath(_currentDataDir, file),
                                Size = fs.Length,
                                MD5 = Convert.ToHexStringLower(hashBytes)
                            });
                        }
                    }

                    // 开始写入私有包 (写入到缓存目录或Windows的指定目录)
                    _log.Info(_loc.Get("DataService.Log.GeneratingPackage"));
                    using var fsOut = new FileStream(finalPkgPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var writer = new BinaryWriter(fsOut);

                    // 写入私有头标识和 JSON
                    writer.Write(Encoding.UTF8.GetBytes("WIKIDATA"));
                    string json = System.Text.Json.JsonSerializer.Serialize(info, AppJsonContext.Custom.WikiPackageInfo);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                    writer.Write(jsonBytes.Length);
                    writer.Write(jsonBytes);

                    // 流式写入所有二进制数据 (将大量磁盘 I/O 隔离在后台)
                    foreach (var file in files)
                    {
                        string fileToRead = (file == originalDbPath) ? tempDbPath : file;
                        using var fsIn = File.OpenRead(fileToRead);
                        await fsIn.CopyToAsync(fsOut);
                    }
                });
                // ==========================================
                // 后台任务结束，回到 UI 线程
                // ==========================================

                // 5. 移动端/Mac端：处理刚刚生成的缓存文件 (UI 操作，需在主线程)
                if (App.AppStateManager?.IsAndroid == true)
                {
#if ANDROID
                    // 【修改点 2】唤起 SAF 选择位置，将缓存包拷贝过去
                    _log.Info(_loc.Get("DataService.Log.WaitingForSaveLocation"));
                    // 注意：这里需要你保留之前写的 AndroidFileSaver 类
                    var uri = await AndroidFileSaver.PickSaveLocationAsync(exportFileName, "application/octet-stream");
                    if (uri != null)
                    {
                        // 使用流复制，避免包过大撑爆内存
                        using var fsIn = File.OpenRead(finalPkgPath);
                        var resolver = Android.App.Application.Context.ContentResolver;
                        using var streamOut = resolver.OpenOutputStream(uri);
                        if (streamOut != null)
                        {
                            await fsIn.CopyToAsync(streamOut);
                            await streamOut.FlushAsync();
                        }
                    }
                    else
                    {
                        _log.Info(_loc.Get("DataService.Log.UserCancelledSave"));
                        App.AppStateManager?.ProcessingTaskId = 0;
                        return; // 用户取消了，直接中断，不显示"导出成功"
                    }
#endif
                }
                else if (App.AppStateManager?.IsIOS == true || App.AppStateManager?.IsMacCatalyst == true)
                {
                    await FileHelper.ExportFileAppleAsync(finalPkgPath);
                }

                _log.Success(_loc.Get("DataService.Log.ExportSuccess", finalPkgPath));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ExportSuccessShort"));
            }
            catch (Exception ex)
            {
                _log.Error(_loc.Get("DataService.Log.ExportFailed", ex.Message));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Error"), _loc.Get("DataService.Log.ExportFailed", ex.Message));
            }
            finally
            {
                // 6. 清理临时文件
                if (File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch { /* 忽略清理失败 */ }
                }

                // 如果是移动端，临时生成的包分享/拷贝完后也要删掉防占用空间
                if (App.AppStateManager?.IsMobile == true || App.AppStateManager?.IsMacCatalyst == true)
                {
                    _ = FileHelper.ClearAppCacheAsync();
                }

                App.AppStateManager?.ProcessingTaskId = 0;
            }
        }

        //导入数据
        public async Task ImportDataAsync()
        {
            App.AppStateManager?.ProcessingTaskId = 10;
            _log.Info(_loc.Get("DataService.Log.ImportDataStart"));
            string filePath = null;
            InitializeSettings();
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".pkg" } },
        });
                if (Application.Current?.Windows[0].Page is MainPage mainPage)
                {
                    mainPage.ShowLoadingPopup("导入数据", "正在导入数据，请稍候...");
                }

                if (App.AppStateManager?.IsWindows == true)
                {
                    filePath = await FileHelper.ImportFileAsync(_loc.Get("DataService.Log.SelectImportPackage"), customFileType);
                }
                else
                {
                    filePath = await FileHelper.ImportFileAsync(_loc.Get("DataService.Log.SelectImportPackage"));
                }

                if (string.IsNullOrEmpty(filePath)) return;


                // ====== 第一步：读取头部和元数据（轻量操作） ======
                WikiPackageInfo meta = null;

                await Task.Run(() =>
                {
                    using var fsIn = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fsIn);

                    // 1. 校验私有头
                    _log.Info(_loc.Get("DataService.Log.ValidatingImportFormat"));
                    byte[] headerBytes = reader.ReadBytes(8);
                    if (Encoding.UTF8.GetString(headerBytes) != "WIKIDATA")
                    {
                        throw new Exception("非法的文件格式：无法识别该导入包！");
                    }

                    // 2. 读取元数据
                    int jsonLen = reader.ReadInt32();
                    string json = Encoding.UTF8.GetString(reader.ReadBytes(jsonLen));
                    Debug.Write(json);

                    meta = JsonSerializer.Deserialize<WikiPackageInfo>(json, AppJsonContext.Custom.WikiPackageInfo);
                });

                // ====== 版本检查：旧版包需要提示并执行迁移 ======
                bool needMigration = false;
                if (meta.AppVersion != null && Version.TryParse(meta.AppVersion, out var pkgVersion) && pkgVersion < new Version(0, 4))
                {
                    var currentPage = Application.Current?.Windows[0].Page;
                    if (currentPage != null)
                    {
                        bool confirm = await currentPage.DisplayAlertAsync(
                            _loc.Get("DataService.Log.ImportOldVersionTitle"),
                            _loc.Get("DataService.Log.ImportOldVersionDesc", meta.AppVersion, AppInfo.Current.VersionString),
                            _loc.Get("Common.OK"),
                            _loc.Get("Common.Cancel"));
                        if (!confirm)
                        {
                            App.AppStateManager?.ProcessingTaskId = 0;
                            return;
                        }
                    }
                    needMigration = true;
                }

                // ====== 第二步：提取并校验文件（耗时操作） ======
                await Task.Run(() =>
                {
                    using var fsIn = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fsIn);

                    // 跳过头部和元数据 JSON
                    reader.ReadBytes(8); // header
                    int jsonLen = reader.ReadInt32();
                    reader.ReadBytes(jsonLen); // skip JSON

                    if (!Directory.Exists(TempDir)) Directory.CreateDirectory(TempDir);

                    // 3. 逐个提取文件并实时校验 MD5
                    _log.Info(_loc.Get("DataService.Log.ExtractingAndVerifying"));
                    using var md5 = MD5.Create();
                    byte[] buffer = new byte[1024 * 1024];

                    foreach (var fileMeta in meta.Files)
                    {
                        string outPath = Path.Combine(TempDir, fileMeta.RelativePath);
                        string outDir = Path.GetDirectoryName(outPath);
                        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                        using var fsOut = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                        long remainingBytes = fileMeta.Size;
                        int bytesRead;
                        md5.Initialize();

                        while (remainingBytes > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remainingBytes);
                            bytesRead = fsIn.Read(buffer, 0, toRead);
                            if (bytesRead == 0) throw new Exception("文件意外结束，包可能已损坏！");

                            fsOut.Write(buffer, 0, bytesRead);
                            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                            remainingBytes -= bytesRead;
                        }

                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        string calculatedMd5 = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();

                        if (calculatedMd5 != fileMeta.MD5)
                        {
                            throw new Exception($"数据校验失败！文件已被篡改或损坏: {fileMeta.RelativePath}");
                        }
                    }
                });
                // 【关键】把耗时的本地文件夹删除和移动也放进后台线程
                _log.Info(_loc.Get("DataService.Log.ReplacingLocalFiles"));
                WikiBook wikiBook = await App.ManagerDb.GetItemAsync<WikiBook>(meta.Id);
                if (meta.Id == App.AppStateManager.ActiveWikiBookId)
                {
                    await App.ContentDb.CloseConnection();
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(_currentDataDir))
                        {
                            Directory.Delete(_currentDataDir, true);
                        }
                        Directory.Move(TempDir, _currentDataDir);

                        // 旧版包数据库文件重命名为 data.db
                        string oldDbPath = Path.Combine(_currentDataDir, "Terraria_Wiki.db");
                        string newDbPath = Path.Combine(_currentDataDir, "data.db");
                        if (File.Exists(oldDbPath))
                        {
                            File.Delete(newDbPath);
                            File.Move(oldDbPath, newDbPath);
                        }
                    });
                }
                else
                {
                    await Task.Run(() =>
                    {
                        string targetDir = Path.Combine(_baseDir, wikiBook.DataFolder);
                        if (Directory.Exists(targetDir))
                        {
                            Directory.Delete(targetDir, true);
                        }
                        Directory.Move(TempDir, targetDir);

                        // 旧版包数据库文件重命名为 data.db
                        string oldDbPath = Path.Combine(targetDir, "Terraria_Wiki.db");
                        string newDbPath = Path.Combine(targetDir, "data.db");
                        if (File.Exists(oldDbPath))
                        {
                            File.Delete(newDbPath);
                            File.Move(oldDbPath, newDbPath);
                        }
                    });
                }
                


                // ====== 核心修改结束 ======

                // 从这里开始，Task.Run 结束，代码又回到了 UI 线程
                // 以下是数据库操作（它们本身已经是真正的异步了，不需要包裹在 Task.Run 里）
                _log.Info(_loc.Get("DataService.Log.UpdatingDatabase"));


                
                wikiBook.IsPageDownloaded = meta.IsPageDownloaded;
                wikiBook.IsResourceDownloaded = meta.IsResourceDownloaded;
                wikiBook.UpdateTime = meta.UpdateTime;

                await App.ManagerDb.SaveItemAsync(wikiBook);
                if(meta.Id == App.AppStateManager.ActiveWikiBookId)
                {
                    await App.ContentDb.ReconnectAsync();
                    await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                    await AppService.WikiRefreshAsync();
                }

                // 旧版包迁移：为 <a> 标签添加 data-wiki 属性
                if (needMigration && meta.Id == App.AppStateManager.ActiveWikiBookId)
                {
                    var upgradeHandler = new LegacyUpgradeHandler(_storagePath);
                    await upgradeHandler.MigrateAnchorDataWikiAsync(App.AppStateManager.ActiveWikiBook);
                }

                _log.Success(_loc.Get("DataService.Log.ImportSuccess"));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.ImportSuccess"));
            }
            catch (Exception ex)
            {
                _log.Error(_loc.Get("DataService.Log.ImportFailed", ex.Message));
                App.AppStateManager?.TriggerAlert(_loc.Get("Common.Error"), _loc.Get("DataService.Log.ImportFailed", ex.Message));
            }
            finally
            {
                // ... (保持你原本的清理逻辑不变)
                if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
                if (App.AppStateManager?.IsMobile == true && !string.IsNullOrEmpty(filePath))
                {
                    _ = FileHelper.ClearAppCacheAsync();
                }
                if (Application.Current?.Windows[0].Page is MainPage mainPage)
                {
                    mainPage.HideLoadingPopup();
                }
                App.AppStateManager?.ProcessingTaskId = 0;
            }
        }

        // ================= 核心功能 1: 获取页面清单 =================
        private async Task<int> FetchWikiPagesListAsync(CancellationToken cancellationToken = default)
        {
            _log.Info(_loc.Get("DataService.Log.FetchingPageList"));
            var writer = new BatchLineWriter(_pageListPath, 200);
            int pagesCount = 0;
            bool firstBatch = true;

            // 将所有需要爬取的命名空间放入队列：主命名空间 + 额外命名空间
            var namespaceQueue = new Queue<int>();
            namespaceQueue.Enqueue(_mainNamespace);
            foreach (var ns in _additionalNamespaces)
                namespaceQueue.Enqueue(ns);

            while (namespaceQueue.Count > 0)
            {
                int currentNs = namespaceQueue.Dequeue();
                string? gapContinue = null;
                int retryCount = 0;
                string currentBaseUrl = $"{_baseApiUrl}?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace={currentNs}&gapfilterredir=nonredirects&gaplimit=max";

                while (true)
                {
                    string currentUrl = currentBaseUrl + (string.IsNullOrEmpty(gapContinue) ? "" : $"&gapcontinue={Uri.EscapeDataString(gapContinue)}");
                    _log.Info(_loc.Get("DataService.Log.PagesFetched", pagesCount));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string jsonResponse = await NetworkService.GetStringAsync(currentUrl, useTls: App.AppStateManager?.ActiveWikiBook?.Id == 2, cancellationToken: cancellationToken);
                        retryCount = 0;

                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var rawData = JsonSerializer.Deserialize(jsonResponse, AppJsonContext.Custom.RawResponse);

                        if (rawData?.Query?.Pages != null)
                        {
                            int batchCount = 0;
                            foreach (var page in rawData.Query.Pages.Values)
                            {
                                writer.Add($"{page.Title}|{page.Touched}");
                                pagesCount++;
                                batchCount++;
                            }

                            if (firstBatch)
                            {
                                firstBatch = false;
                                _log.Info(_loc.Get("DataService.Log.FirstBatchSize", batchCount));
                            }
                        }

                        if (string.IsNullOrEmpty(rawData?.Continue?.GapContinue))
                            break;

                        gapContinue = rawData?.Continue?.GapContinue;
                    }
                    catch (HttpRequestException e)
                    {
                        if (++retryCount > _maxRetryAttempts) throw;
                        _log.Error(_loc.Get("DataService.Log.RequestFailedRetrying", e.Message, retryCount, _maxRetryAttempts));
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            writer.Flush();
            _log.Success(_loc.Get("DataService.Log.FetchCompleted", pagesCount));

            return pagesCount;
        }

        private async Task FetchWikiRedirectsListAsync(CancellationToken cancellationToken = default)
        {
            string nextUrl = _redirectStartUrl;
            int pageCount = 1;
            int totalRedirects = 0;
            _log.Info(_loc.Get("DataService.Log.FetchingRedirects"));
            while (!string.IsNullOrEmpty(nextUrl))
            {
                int retry = 0;
                while (true)
                {
                    try
                    {
                        string fullUrl = _baseUrl + nextUrl;
                        cancellationToken.ThrowIfCancellationRequested();
                        string html = await NetworkService.GetStringAsync(fullUrl, useTls: App.AppStateManager?.ActiveWikiBook?.Id == 2, cancellationToken: cancellationToken);
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
                        var listItems = doc.DocumentNode.SelectNodes("//div[@class='mw-spcontent']//ol/li");

                        if (listItems == null)
                        {
                            _log.Error(_loc.Get("DataService.Log.NoDataOnPage"));
                            break;
                        }

                        var wikiRedirects = new List<WikiRedirect>();
                        foreach (var li in listItems)
                        {
                            var links = li.SelectNodes(".//a");

                            if (links != null && links.Count >= 2)
                            {
                                string fromTitle = HtmlEntity.DeEntitize(links[0].InnerText);
                                string toTitle = HtmlEntity.DeEntitize(links.Last().InnerText);
                                var wikiRedirect = new WikiRedirect { FromName = fromTitle, ToTarget = toTitle };
                                wikiRedirects.Add(wikiRedirect);
                                totalRedirects++;
                            }
                        }
                        await App.ContentDb.SaveItemsAsync(wikiRedirects);
                        _log.Info(_loc.Get("DataService.Log.PageParsed", pageCount));
                        var nextLinkNode = doc.DocumentNode.SelectSingleNode("//a[@class='mw-nextlink']");

                        if (nextLinkNode != null)
                        {
                            nextUrl = HtmlEntity.DeEntitize(nextLinkNode.GetAttributeValue("href", ""));
                            pageCount++;
                            await Task.Delay(500, cancellationToken);
                        }
                        else
                        {
                            _log.Success(_loc.Get("DataService.Log.RedirectsFetched", totalRedirects));
                            nextUrl = null;
                            break;
                        }

                    }
                    catch (Exception ex)
                    {
                        if (++retry > _maxRetryAttempts)
                        {
                            _log.Error(_loc.Get("DataService.Log.RedirectsFetchFailed", _maxRetryAttempts, ex.Message));
                            nextUrl = null;
                            throw;
                        }
                        _log.Error(_loc.Get("DataService.Log.RedirectsFetchError", retry, _maxRetryAttempts));
                        await Task.Delay(1000, cancellationToken);
                    }
                }

            }
        }
        // ================= 业务入口: 下载页面 =================
        private async Task DownloadPagesBatchAsync(string pageListPath, string resListPath, string failedPageListPath, int maxConcurrency, CancellationToken cancellationToken = default)
        {
            using var writer = new BatchLineWriter(resListPath, 200);
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(pageListPath))
            {
                totalCount = File.ReadLines(pageListPath).Count();
            }
            _log.Info(_loc.Get("DataService.Log.DownloadPagesStart", totalCount));
            // 定义如何处理单行数据


            using var provider = new BatchLineProvider(pageListPath);
            var scheduler = new BatchTaskScheduler<BatchLineItem>(_maxRetryAttempts);
            async Task ProcessPageLine(int workerId, BatchLineItem item, CancellationToken token)
            {
                string line = item.Line;
                bool processed = false;
                var parts = line.Split('|');
                if (parts.Length < 2)
                {
                    item.Complete();
                    return;
                }
                var page = new PageInfo { Title = parts[0], LastModified = DateTime.Parse(parts[1]) };
                try
                {
                    await DownloadAndSavePageToDbAsync(page, writer, token);
                    item.Complete();
                    processed = true;
                }
                finally
                {
                    int c = Interlocked.Increment(ref currentCount);
                    if (processed && _activeDownloadTask != null)
                    {
                        _activeDownloadTask.CompletedPages = Interlocked.Increment(ref _completedPages);
                        await SaveDownloadTaskAsync();
                    }
                    _log.Info(_loc.Get("DataService.Log.PageCompleted", workerId, c, totalCount, page.Title));
                }


            }

            await scheduler.RunAsync(
                provider.GetNextItemAsync,
                ProcessPageLine,
                async (_, item, ex, token) =>
                {
                    await AppendFailedUrlAsync(failedPageListPath, item.Line);
                    item.Complete();
                },
                maxConcurrency,
                onRetry: (workerId, item, retry, ex) =>
                    _log.Error(_loc.Get("DataService.Log.RetryingFailed", workerId, retry, _maxRetryAttempts, item.Line)),
                onNotFound: (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                },
                cancellationToken: cancellationToken);

            // 爬取完成后，清洗一下数据
            if (File.Exists(resListPath))
            {
                string tempFile = Path.Combine(_currentDataDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                FileHelper.RemoveDuplicatesOptimized(resListPath, tempFile);

                // 替换原文件
                File.Delete(resListPath);
                File.Move(tempFile, resListPath, true);
            }
            _log.Info(_loc.Get("DataService.Log.AllPagesCompleted"));

        }

        // ================= 业务入口: 下载资源 =================
        private async Task DownloadResourcesBatchAsync(string resListPath, string failedResListPath, int maxConcurrency, bool deleteFile = false, CancellationToken cancellationToken = default)
        {
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(resListPath))
            {
                totalCount = File.ReadLines(resListPath).Count();
            }
            _log.Info(_loc.Get("DataService.Log.DownloadAssetsStart", totalCount));
            using var provider = new BatchLineProvider(deleteFile ? resListPath : _tempResListPath);
            var scheduler = new BatchTaskScheduler<BatchLineItem>(_maxRetryAttempts);
            async Task ProcessResLine(int workerId, BatchLineItem item, CancellationToken token)
            {
                string url = item.Line;
                bool changeData = false;
                bool processed = false;
                string fileName = DataService.GetFileNameFromUrl(url);
                try
                {
                    changeData = await DownloadAndSaveResourceAsync(url, fileName, token);
                    item.Complete();
                    processed = true;
                }
                finally
                {
                    int c = Interlocked.Increment(ref currentCount);
                    if (processed && _activeDownloadTask != null)
                    {
                        _activeDownloadTask.CompletedResources = Interlocked.Increment(ref _completedResources);
                        await SaveDownloadTaskAsync();
                    }
                    if (changeData)
                    {
                        _log.Info(_loc.Get("DataService.Log.AssetCompleted", workerId, c, totalCount, fileName));
                    }
                    else
                    {
                        _log.Info(_loc.Get("DataService.Log.AssetSkipped", workerId, c, totalCount, fileName));
                    }
                }

            }
            if (!deleteFile)
            {
                if (!File.Exists(_tempResListPath))
                    File.Copy(resListPath, _tempResListPath, true);
                await scheduler.RunAsync(
                    provider.GetNextItemAsync,
                    ProcessResLine,
                    async (_, item, ex, token) =>
                    {
                        await AppendFailedUrlAsync(failedResListPath, item.Line);
                        item.Complete();
                    },
                    maxConcurrency,
                    onRetry: (workerId, item, retry, ex) =>
                        _log.Error(_loc.Get("DataService.Log.RetryingFailed", workerId, retry, _maxRetryAttempts, item.Line)),
                    onNotFound: (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                },
                    cancellationToken: cancellationToken);
            }
            else
            {
                await scheduler.RunAsync(
                    provider.GetNextItemAsync,
                    ProcessResLine,
                    async (_, item, ex, token) =>
                    {
                        await AppendFailedUrlAsync(failedResListPath, item.Line);
                        item.Complete();
                    },
                    maxConcurrency,
                    onRetry: (workerId, item, retry, ex) =>
                        _log.Error(_loc.Get("DataService.Log.RetryingFailed", workerId, retry, _maxRetryAttempts, item.Line)),
                    onNotFound: (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                },
                    cancellationToken: cancellationToken);
            }

            _log.Info(_loc.Get("DataService.Log.AssetsDownloadCompleted"));
        }


        // ================= 具体的处理逻辑 =================

        private async Task DownloadAndSavePageToDbAsync(PageInfo pageInfo, BatchLineWriter writer, CancellationToken cancellationToken = default)
        {
            // 如果本地已存在该页面且最后修改时间一致，则跳过下载
            if (await App.ContentDb.ItemExistsAsync<WikiPage>(pageInfo.Title))
            {
                var existingPage = await App.ContentDb.GetItemAsync<WikiPage>(pageInfo.Title);
                if (existingPage.LastModified == pageInfo.LastModified)
                {
                    return;
                }
            }

            var pageUrl = _baseApiUrl + $"?action=parse&page={pageInfo.Title}&prop=text&format=xml";

            string xml = await NetworkService.GetStringAsync(pageUrl, useTls: App.AppStateManager?.ActiveWikiBook?.Id == 2, cancellationToken: cancellationToken);

            var xmldoc = XDocument.Parse(xml);

            // 直接取 <text> 节点内容
            string html = xmldoc.Descendants("text").FirstOrDefault()?.Value;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var contentNode = doc.DocumentNode;

            if (contentNode == null) return;

            // 拆分为小函数，逻辑更清晰
            CleanJunkElements(contentNode);
            ProcessAnchorLinks(contentNode);
            ProcessAudioTags(contentNode);
            ProcessImages(contentNode, writer);

            var wikiPage = new WikiPage
            {
                Title = pageInfo.Title,
                Content = contentNode.OuterHtml,
                LastModified = pageInfo.LastModified
            };
            await App.ContentDb.SaveItemAsync(wikiPage);
            var plainContent = ExtractSearchableText(contentNode);
            await App.ContentDb.SaveSearchIndexAsync(pageInfo.Title, plainContent);

        }

        private void CleanJunkElements(HtmlNode node)
        {
            if (!string.IsNullOrEmpty(_junkXPath))
                node.SelectNodes(_junkXPath)?.ToList().ForEach(n => n.Remove());
        }

        private void ProcessAnchorLinks(HtmlNode node)
        {
            node.SelectNodes("//a[@href and @title]")?.ToList().ForEach(n =>
            {
                string href = n.Attributes["href"].Value;

                // 纯锚点（如 href="#历史"）是页内目录跳转，保持原样交给浏览器原生处理
                if (href.StartsWith('#'))
                {
                    return;
                }

                // 把 #锚点 合并进 title，作为站内跳转目标（含锚点定位）
                int hashIndex = href.IndexOf('#');
                if (hashIndex >= 0)
                {
                    n.SetAttributeValue("title", n.GetAttributeValue("title", "") + href.Substring(hashIndex));
                }

                // 前端 JS 通过 data-wiki 识别站内链接并触发应用内跳转
                n.SetAttributeValue("data-wiki", n.GetAttributeValue("title", ""));
                n.Attributes.Remove("href");
            });
        }

        private void ProcessAudioTags(HtmlNode node)
        {
            node.SelectNodes("//audio")?.ToList().ForEach(n =>
            {
                var sources = n.SelectNodes("./source");
                if (sources != null && sources.Count > 1)
                {
                    var keep = sources.FirstOrDefault(s => !s.GetAttributeValue("src", "").Contains("/transcoded/"))
                               ?? sources.Last();

                    foreach (var s in sources.ToArray()) // ToArray防止修改集合时报错
                    {
                        if (s != keep) s.Remove();
                    }
                }
            });
        }

        private void ProcessImages(HtmlNode node, BatchLineWriter writer)
        {
            // 移除图片链接
            node.SelectNodes("//a[@class='image' and @href]")?.ToList().ForEach(n => n.Attributes.Remove("href"));

            // 处理 src
            node.SelectNodes("//*[@src]")?.ToList().ForEach(n =>
            {
                // 清理属性
                foreach (var attr in new[] { "loading", "data-file-width", "data-file-height", "srcset" })
                    n.Attributes.Remove(attr);

                string src = n.Attributes["src"].Value;

                // 补全 URL
                if (!src.Contains("https://")) src = _baseUrl + src;

                // 还原缩略图
                src = Regex.Replace(src, @"/thumb/(.+)/[^/]+$", "/$1");
                src = DataService.GetUrlWithoutQuery(src);

                // 灰机 wiki 的缩略图域名与原图域名不同，需要切换
                if (App.AppStateManager?.ActiveWikiBookId == 2)
                    src = src.Replace("huiji-thumb", "huiji-public");

                // 写入文件
                writer.Add(src);
                string htmlSrc = Uri.EscapeDataString(DataService.GetFileNameFromUrl(src));
                // 替换为本地路径
                n.SetAttributeValue("src", "/src/" + htmlSrc);
            });
        }

        //处理搜索索引
        private static string ExtractSearchableText(HtmlNode contentNode)
        {


            var notNeedNodes = contentNode.SelectNodes("//div[contains(concat(' ', @class, ' '), ' message-box ') or contains(concat(' ', @class, ' '), ' infobox ') or contains(concat(' ', @role, ' '), ' navigation ')]");

            if (notNeedNodes != null)
            {
                foreach (var node in notNeedNodes)
                {
                    node.Remove();
                }
            }

            var targetNodes = contentNode.SelectNodes("//p | //h1 | //h2 | //h3 | //h4 | //h5 | //h6 | //li");
            string plainText = string.Empty;

            if (targetNodes != null)
            {
                plainText = string.Join(" ", targetNodes.Select(n => n.InnerText));
            }

            plainText = WebUtility.HtmlDecode(plainText);
            return Regex.Replace(plainText, @"\s+", " ").Trim();
        }

        private async Task<bool> DownloadAndSaveResourceAsync(string url, string fileName, CancellationToken cancellationToken = default)
        {
            // 1. 尝试从数据库获取已存在的资源记录
            WikiAsset existingAsset = null;
            if (await App.ContentDb.ItemExistsAsync<WikiAsset>(fileName))
            {
                existingAsset = await App.ContentDb.GetItemAsync<WikiAsset>(fileName);
            }

            // 2. 下载资源，并在已有记录时使用 Last-Modified 进行条件请求
            var response = await NetworkService.GetBytesResponseAsync(
                url,
                useTls: App.AppStateManager?.ActiveWikiBook?.Id == 2,
                ifModifiedSince: existingAsset?.LastModified,
                cancellationToken: cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return false;
            }

            byte[] data = response.Data;
            DateTime lastModifiedDate = response.LastModified ?? DateTime.UtcNow;
            await App.ContentDb.SaveItemAsync(new WikiAsset
            {
                FileName = fileName,
                Data = data,
                MimeType = response.ContentType,
                LastModified = lastModifiedDate
            });
            return true;
        }

        // ================= 辅助工具方法 =================

        //更新成员变量
        public void InitializeSettings(bool cleanupTemporaryFiles = true)
        {
            _maxRetryAttempts = Preferences.Default.Get("MaxRetryAttempts", 5);
            _pageConcurrency = Preferences.Default.Get("PageConcurrency", 2);
            if (Preferences.Default.Get("PageConcurrency", 2) > 3)
            {
                _pageConcurrency = 2;
                Preferences.Default.Set("PageConcurrency", 2);
            }
            _resConcurrency = Preferences.Default.Get("ResConcurrency", 10);

            // 从 WikiBook 加载 Wiki 源配置（含 DataFolder，必须在路径使用前加载）
            var book = App.AppStateManager.ActiveWikiBook;
            _baseApiUrl = book.ApiBaseUrl;
            _baseUrl = book.BaseUrl;
            _redirectStartUrl = book.RedirectListUrl;
            _mainNamespace = book.MainNamespace;
            _additionalNamespaces = string.IsNullOrEmpty(book.AdditionalNamespaces)
                ? []
                : book.AdditionalNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToList();
            _junkXPath = book.JunkXPath;

            // 根据 DataFolder 设置数据目录和所有文件路径
            _baseDir = _storagePath.RootPath;
            _currentDataDir= Path.Combine(_baseDir, book.DataFolder);
            _resListPath = Path.Combine(_currentDataDir, "res.txt");
            _tempResListPath = Path.Combine(_currentDataDir, "temp_res.txt");
            _pageListPath = Path.Combine(_currentDataDir, "pages.txt");
            _failedPageListPath = Path.Combine(_currentDataDir, "failed_pages.txt");
            _tempFailedPageListPath = Path.Combine(_currentDataDir, "temp_failed_pages.txt");
            _failedResListPath = Path.Combine(_currentDataDir, "failed_res.txt");
            _tempFailedResListPath = Path.Combine(_currentDataDir, "temp_failed_res.txt");
            _updatePageListPath = Path.Combine(_currentDataDir, "update_pages.txt");
            _updateResListPath = Path.Combine(_currentDataDir, "update_res.txt");

            if (!Directory.Exists(_currentDataDir)) Directory.CreateDirectory(_currentDataDir);
            if (cleanupTemporaryFiles)
                CleanupTemporaryFiles();
        }

        //清理临时文件
        private void CleanupTemporaryFiles()
        {
            _log.Info(_loc.Get("DataService.Log.CleaningTempFiles"));
            if (File.Exists(_pageListPath))
            {
                File.Delete(_pageListPath);
            }

            if (File.Exists(_tempResListPath))
            {
                File.Delete(_tempResListPath);
            }
            if (File.Exists(_tempFailedPageListPath))
            {
                File.Delete(_tempFailedPageListPath);
            }
            if (File.Exists(_tempFailedResListPath))
            {
                File.Delete(_tempFailedResListPath);
            }
            if (File.Exists(_updatePageListPath))
            {
                File.Delete(_updatePageListPath);
            }
            if (File.Exists(_updateResListPath))
            {
                File.Delete(_updateResListPath);
            }
        }

        //清理 URL 中的查询参数，获取干净的文件名
        private static string GetUrlWithoutQuery(string url)
        {
            int qIdx = url.IndexOf('?');
            return (qIdx > 0) ? url.Substring(0, qIdx) : url;
        }

        // 从 URL 中提取文件名，并进行 URL 解码
        public static string GetFileNameFromUrl(string url)
        {
            string cleanUrl = DataService.GetUrlWithoutQuery(url);
            string name = cleanUrl.Substring(cleanUrl.LastIndexOf('/') + 1);
            string decodedName = WebUtility.UrlDecode(name);
            return decodedName;
        }

        // 追加失败的 URL 到文件，使用异步方法并捕获异常以防止崩溃
        private static async Task AppendFailedUrlAsync(string path, string url)
        {
            await File.AppendAllLinesAsync(path, [url]);
        }

        // 用临时失败列表替换旧失败列表：如果临时文件有内容则替换，否则直接删除旧文件
        private static void ReplaceFailedList(string tempPath, string targetPath)
        {
            if (FileHelper.IsFileValid(tempPath))
            {
                File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }
            else
            {
                File.Delete(targetPath);
            }
        }


    }
}