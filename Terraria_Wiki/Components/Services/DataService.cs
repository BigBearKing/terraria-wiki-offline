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

        private string TaskRecordsDir => Path.Combine(_storagePath.RootPath, "Tasks");

        private string TempDir => Path.Combine(_storagePath.RootPath, "Temp");

        private string WikiTaskRecordsDir(int wikiId) => Path.Combine(TaskRecordsDir, wikiId.ToString());

        private string PublicFailedPageListPath => Path.Combine(
            WikiTaskRecordsDir(App.AppStateManager!.ActiveWikiBookId), "failed_pages.txt");

        private string PublicFailedResourceListPath => Path.Combine(
            WikiTaskRecordsDir(App.AppStateManager!.ActiveWikiBookId), "failed_resources.txt");

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
        private readonly AppTaskRunner _taskRunner;
        private int _maxRetryAttempts;
        private int _pageConcurrency;
        private int _resConcurrency;
        private readonly SemaphoreSlim _downloadLock = new(1, 1);
        private CancellationTokenSource? _downloadCts;
        private AppTask? _activeDownloadTask;
        private int _completedPages;
        private int _completedResources;
        private int _totalPages;
        private int _totalResources;
        private readonly SemaphoreSlim _taskSaveLock = new(1, 1);
        private readonly SemaphoreSlim _resourceProgressLock = new(1, 1);

        public DataService(LogService logService, LocalizationService localizationService, StoragePathService storagePath, AppTaskRunner taskRunner)
        {
            _log = logService;
            _loc = localizationService;
            _storagePath = storagePath;
            _taskRunner = taskRunner;
            
        }

        public async Task StartOrResumeDownloadAsync(bool includeResources)
        {
            await RunDownloadAsync(
                includeResources,
                includeResources ? AppTaskType.DownloadAll : AppTaskType.DownloadPages);
        }

        public async Task PauseDownloadAsync()
        {
            if (_activeDownloadTask is null) return;
            if (await _taskRunner.PauseAsync(_activeDownloadTask.Id))
                return;
            _activeDownloadTask.Status = AppTaskStatus.Paused;
            await SaveAppTaskAsync();
            _downloadCts?.Cancel();
        }

        public async Task DownloadDataAsync(bool includeResources, CancellationToken cancellationToken = default) =>
            await StartOrResumeDownloadAsync(includeResources);

        public async Task DownloadResourcesAsync(CancellationToken cancellationToken = default) =>
            await RunDownloadAsync(true, AppTaskType.DownloadResources);

        private async Task<AppTask> GetOrCreateTaskAsync(
            int wikiId,
            AppTaskType taskType,
            bool includeResources,
            AppTaskPhase initialPhase)
        {
            var task = (await App.ManagerDb!.GetItemsAsync<AppTask>())
                .Where(t => t.WikiId == wikiId && t.TaskType == taskType && t.Status != AppTaskStatus.Completed)
                .OrderByDescending(t => t.UpdatedTime)
                .FirstOrDefault();

            if (task is null)
            {
                task = new AppTask
                {
                    WikiId = wikiId,
                    TaskType = taskType,
                    IncludeResources = includeResources,
                    Phase = initialPhase,
                    CreatedTime = DateTime.Now,
                    UpdatedTime = DateTime.Now
                };
                await App.ManagerDb.SaveItemAsync(task);
            }

            task.IncludeResources = includeResources;
            if (string.IsNullOrWhiteSpace(task.TaskDirectory) ||
                !task.TaskDirectory.StartsWith(WikiTaskRecordsDir(wikiId), StringComparison.OrdinalIgnoreCase))
            {
                task.TaskDirectory = Path.Combine(WikiTaskRecordsDir(wikiId), task.Id.ToString());
                await App.ManagerDb.SaveItemAsync(task);
            }

            Directory.CreateDirectory(task.TaskDirectory);
            Directory.CreateDirectory(WikiTaskRecordsDir(wikiId));
            return task;
        }

        private async Task<bool> RunManagedTaskAsync(
            AppTask task,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default,
            bool showError = false)
        {
            if (task.Status == AppTaskStatus.Failed)
                ResetFailedTask(task);

            task.CanPause = true;
            if (!await EnsureNetworkAvailableAsync(task))
                return false;

            return await _taskRunner.RunExistingAsync(
                task,
                async token =>
                {
                    _activeDownloadTask = task;
                    await action(token);
                },
                new AppTaskOptions { ShowError = showError },
                cancellationToken,
                AppTaskAccess.Exclusive);
        }

        private void ResetFailedTask(AppTask task)
        {
            task.Status = AppTaskStatus.Pending;
            task.LastError = null;
            task.CompletedPages = 0;
            task.CompletedResources = 0;
            task.TotalPages = 0;
            task.TotalResources = 0;
            task.Phase = task.TaskType == AppTaskType.DownloadResources
                ? AppTaskPhase.DownloadingResources
                : AppTaskPhase.FetchingLists;

            if (task.TaskType == AppTaskType.RetryFailed)
                return;

            if (Directory.Exists(task.TaskDirectory))
                Directory.Delete(task.TaskDirectory, true);
            Directory.CreateDirectory(task.TaskDirectory);
        }

        private async Task RunDownloadAsync(bool includeResources, AppTaskType taskType)
        {
            if (!await _downloadLock.WaitAsync(0)) return;
            try
            {
                InitializeSettings();
                var wikiId = App.AppStateManager!.ActiveWikiBookId;
                var existingResourceList = _resListPath;
                var task = await GetOrCreateTaskAsync(
                    wikiId,
                    taskType,
                    includeResources,
                    taskType == AppTaskType.DownloadResources
                        ? AppTaskPhase.DownloadingResources
                        : AppTaskPhase.FetchingLists);
                await RunManagedTaskAsync(
                    task,
                    async token =>
                    {
                        ConfigureDownloadPaths(task);
                        ConfigurePublicFailedPaths(wikiId);
                        if (task.TaskType == AppTaskType.DownloadResources && !File.Exists(_resListPath) && File.Exists(existingResourceList))
                            File.Copy(existingResourceList, _resListPath, true);
                        await SaveAppTaskAsync();
                        await ExecuteDownloadAsync(task, token);
                    },
                    showError: true);
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
                if (_activeDownloadTask is not null)
                    App.AppStateManager?.RemoveActiveTask(_activeDownloadTask.Id);
                _activeDownloadTask = null;
                _downloadLock.Release();
            }
        }

        private async Task<bool> EnsureNetworkAvailableAsync(AppTask task)
        {
            if (NetworkService.IsNetworkAvailable)
                return true;

            task.Status = AppTaskStatus.Paused;
            task.LastError = _loc.Get("DataService.NetworkUnavailable");
            task.UpdatedTime = DateTime.Now;
            await App.ManagerDb!.SaveItemAsync(task);
            App.AppStateManager!.CurrentDownloadTask = task;
            App.AppStateManager?.TriggerAlert(
                _loc.Get("Common.Notice"),
                _loc.Get("DataService.NetworkUnavailable"));
            return false;
        }

        private async Task<bool> CheckNetworkBeforeRetryAsync(
            int workerId, BatchLineItem item, int retry, Exception ex, CancellationToken token)
        {
            _log.Error(_loc.Get("DataService.Log.RetryingFailed", workerId, retry, _maxRetryAttempts, item.Line));
            if (NetworkService.IsNetworkAvailable)
                return true;

            if (_activeDownloadTask is not null)
                await _taskRunner.PauseAsync(_activeDownloadTask.Id);
            App.AppStateManager?.TriggerAlert(
                _loc.Get("Common.Notice"),
                _loc.Get("DataService.NetworkUnavailable"));
            token.ThrowIfCancellationRequested();
            return false;
        }

        private static void MergeAndDeduplicateResourceLists(string targetPath, string additionalPath)
        {
            var tempPath = targetPath + ".merge.tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            using (var writer = new BatchLineWriter(tempPath, 500))
            {
                if (File.Exists(targetPath))
                    foreach (var line in File.ReadLines(targetPath)) writer.Add(line);
                if (File.Exists(additionalPath))
                    foreach (var line in File.ReadLines(additionalPath)) writer.Add(line);
            }

            File.Move(tempPath, targetPath, true);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        private async Task ExecuteDownloadAsync(AppTask task, CancellationToken token)
        {
            var book = App.AppStateManager!.ActiveWikiBook;
            if (task.TaskType != AppTaskType.DownloadResources)
            {
                if (task.Phase == AppTaskPhase.FetchingLists)
                {
                    if (!File.Exists(_pageListPath) || new FileInfo(_pageListPath).Length == 0)
                    {
                        await FetchWikiRedirectsListAsync(token);
                        await FetchWikiPagesListAsync(token);
                    }
                    task.TotalPages = CountLines(_pageListPath);
                    task.Phase = AppTaskPhase.DownloadingPages;
                    await SaveAppTaskAsync();
                }

                _totalPages = task.TotalPages;
                _completedPages = task.CompletedPages;
                await DownloadPagesBatchAsync(_pageListPath, _resListPath, _failedPageListPath, _pageConcurrency, token);
                if (task.IncludeResources)
                {
                    task.Phase = AppTaskPhase.DownloadingResources;
                    if (!File.Exists(_resListPath)) throw new InvalidOperationException("资源清单不存在。");
                    if (task.TotalResources == 0)
                        task.TotalResources = CountLines(_resListPath);
                    if (!File.Exists(_tempResListPath)) File.Copy(_resListPath, _tempResListPath, true);
                    _totalResources = task.TotalResources;
                    _completedResources = task.CompletedResources;
                    await SaveAppTaskAsync();
                    await DownloadResourcesBatchAsync(_resListPath, _failedResListPath, _resConcurrency, cancellationToken: token);
                    book.IsResourceDownloaded = true;
                }
            }
            else
            {
                if (!FileHelper.IsFileValid(_resListPath)) throw new InvalidOperationException("资源清单无效。");
                task.Phase = AppTaskPhase.DownloadingResources;
                if (task.TotalResources == 0)
                    task.TotalResources = CountLines(_resListPath);
                _totalResources = task.TotalResources;
                if (!File.Exists(_tempResListPath)) File.Copy(_resListPath, _tempResListPath, true);
                _completedResources = task.CompletedResources;
                await DownloadResourcesBatchAsync(_resListPath, _failedResListPath, _resConcurrency, cancellationToken: token);
                book.IsResourceDownloaded = true;
            }
            task.Phase = AppTaskPhase.PostProcessing;
            await SaveAppTaskAsync();
            book.IsPageDownloaded = task.TaskType != AppTaskType.DownloadResources || book.IsPageDownloaded;
            book.UpdateTime = DateTime.Now;
            await App.ManagerDb.SaveItemAsync(book);
            await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
            CleanupDownloadDirectory(task);
            task.Status = AppTaskStatus.Completed;
            await SaveAppTaskAsync();
            App.AppStateManager.CurrentDownloadTask = task;
        }

        private void ConfigureDownloadPaths(AppTask task)
        {
            var dir = task.TaskDirectory;
            Directory.CreateDirectory(dir);
            _pageListPath = Path.Combine(dir, "pages.pending.txt");
            _resListPath = Path.Combine(dir, "resources.txt");
            _tempResListPath = Path.Combine(dir, "resources.pending.txt");
            ConfigurePublicFailedPaths(task.WikiId!.Value);
        }

        private static int CountLines(string path) => File.Exists(path) ? File.ReadLines(path).Count() : 0;

        private void ConfigureRetryPaths(string taskDirectory)
        {
            Directory.CreateDirectory(taskDirectory);
            _tempFailedPageListPath = Path.Combine(taskDirectory, "failed_pages.pending.txt");
            _tempFailedResListPath = Path.Combine(taskDirectory, "failed_resources.pending.txt");
        }

        private void ConfigurePublicFailedPaths(int wikiId)
        {
            Directory.CreateDirectory(WikiTaskRecordsDir(wikiId));
            _failedPageListPath = Path.Combine(WikiTaskRecordsDir(wikiId), "failed_pages.txt");
            _failedResListPath = Path.Combine(WikiTaskRecordsDir(wikiId), "failed_resources.txt");
        }

        private async Task SaveAppTaskAsync()
        {
            if (_activeDownloadTask is null || App.ManagerDb is null) return;
            await _taskSaveLock.WaitAsync();
            try
            {
                _activeDownloadTask.UpdatedTime = DateTime.Now;
                _activeDownloadTask.SaveTaskData();
                await App.ManagerDb.SaveItemAsync(_activeDownloadTask);
                App.AppStateManager!.CurrentDownloadTask = _activeDownloadTask;
                App.AppStateManager.NotifyActiveTasksChanged();
            }
            finally
            {
                _taskSaveLock.Release();
            }
        }

        private void CleanupDownloadDirectory(AppTask task)
        {
            if (task.Status != AppTaskStatus.Completed) return;
            if (Directory.Exists(task.TaskDirectory)) Directory.Delete(task.TaskDirectory, true);
        }

        //更新页面和资源
        public async Task UpdateDataAsync(bool includeResources, CancellationToken cancellationToken = default)
        {
            var taskType = includeResources ? AppTaskType.UpdateAll : AppTaskType.UpdatePages;
            if (!await _downloadLock.WaitAsync(0)) return;

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
                InitializeSettings(cleanupTemporaryFiles: false);
                var wikiId = App.AppStateManager!.ActiveWikiBookId;
                var task = await GetOrCreateTaskAsync(
                    wikiId,
                    taskType,
                    includeResources,
                    AppTaskPhase.FetchingLists);
                await RunManagedTaskAsync(
                    task,
                    async token =>
                    {
                        ConfigureUpdatePaths(task);
                        ConfigureRetryPaths(task.TaskDirectory);
                        ConfigurePublicFailedPaths(wikiId);
                        await SaveAppTaskAsync();
                        await ExecuteUpdateAsync(task, token);
                    },
                    cancellationToken);
            }
            finally
            {
                if (_activeDownloadTask is not null)
                    App.AppStateManager?.RemoveActiveTask(_activeDownloadTask.Id);
                _activeDownloadTask = null;
                _downloadLock.Release();
            }
        }

        private void ConfigureUpdatePaths(AppTask task)
        {
            Directory.CreateDirectory(task.TaskDirectory);
            _pageListPath = Path.Combine(task.TaskDirectory, "pages.txt");
            _tempResListPath = Path.Combine(task.TaskDirectory, "resources.pending.txt");
            _updatePageListPath = Path.Combine(task.TaskDirectory, "update_pages.txt");
            _updateResListPath = Path.Combine(task.TaskDirectory, "update_resources.txt");
            ConfigurePublicFailedPaths(task.WikiId!.Value);
        }

        private async Task ExecuteUpdateAsync(AppTask task, CancellationToken cancellationToken)
        {
            var resourceListPath = _resListPath;
            if (task.Phase == AppTaskPhase.FetchingLists)
            {
                await FetchWikiRedirectsListAsync(cancellationToken);
                await FetchWikiPagesListAsync(cancellationToken);

                var updateCount = await CheckForPageUpdatesAsync(cancellationToken);
                _log.Success(_loc.Get("DataService.Log.UpdateListReady", updateCount));
                if (updateCount == 0)
                {
                    task.Phase = AppTaskPhase.PostProcessing;
                    task.Status = AppTaskStatus.Completed;
                    await SaveAppTaskAsync();
                    CleanupDownloadDirectory(task);
                    App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.NoPagesNeedUpdate"));
                    return;
                }

                _totalPages = task.TotalPages = updateCount;
                _completedPages = task.CompletedPages = 0;
                task.Phase = AppTaskPhase.DownloadingPages;
                await SaveAppTaskAsync();
            }

            if (task.Phase == AppTaskPhase.DownloadingPages)
            {
                _totalPages = task.TotalPages;
                _completedPages = task.CompletedPages;
                await DownloadPagesBatchAsync(_updatePageListPath, _updateResListPath, _failedPageListPath, _pageConcurrency, cancellationToken);
                if (task.IncludeResources)
                {
                    task.Phase = AppTaskPhase.DownloadingResources;
                    if (task.TotalResources == 0)
                        task.TotalResources = CountLines(_updateResListPath);
                    _totalResources = task.TotalResources;
                    _completedResources = task.CompletedResources;
                    await SaveAppTaskAsync();
                }
            }

            if (task.Phase == AppTaskPhase.DownloadingResources)
            {
                if (task.TotalResources == 0)
                    task.TotalResources = CountLines(_updateResListPath);
                _totalResources = task.TotalResources;
                _completedResources = task.CompletedResources;
                await DownloadResourcesBatchAsync(_updateResListPath, _failedResListPath, _resConcurrency, cancellationToken: cancellationToken);
            }

            task.Phase = AppTaskPhase.PostProcessing;
            await SaveAppTaskAsync();
            await FileHelper.AppendFileAsync(_updateResListPath, resourceListPath);
                string tempFile = Path.Combine(_currentDataDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            FileHelper.RemoveDuplicatesOptimized(resourceListPath, tempFile);
            File.Delete(resourceListPath);
            File.Move(tempFile, resourceListPath, true);
            var book = App.AppStateManager.ActiveWikiBook;
            book.UpdateTime = DateTime.Now;
            await App.ManagerDb.SaveItemAsync(book);
            await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
            CleanupTemporaryFiles();
            task.Status = AppTaskStatus.Completed;
            await SaveAppTaskAsync();
            CleanupDownloadDirectory(task);

            if (task.IncludeResources)
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


        //检查是否有要更新的页面
        private async Task<int> CheckForPageUpdatesAsync(CancellationToken cancellationToken = default)
        {
            using var writer = new BatchLineWriter(_updatePageListPath, 200);
            int totalCount = 0;
            if (File.Exists(_pageListPath))
            {
                totalCount = File.ReadLines(_pageListPath).Count(); // 加上这行计算总数
            }
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

        //删除图片资源
        public async Task DeleteResourcesAsync()
            => await _taskRunner.RunAsync(AppTaskType.DeleteResources, _ => DeleteResourcesCoreAsync(), access: AppTaskAccess.Exclusive);

        private async Task DeleteResourcesCoreAsync()
        {
            _log.Info(_loc.Get("DataService.Log.DeleteAssetsStart"));
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

        //检查是否有失败列表
        public bool HasFailedItems()
        {
            if (App.AppStateManager.HasActiveTasks)
            {
                return false;
            }
            ConfigurePublicFailedPaths(App.AppStateManager!.ActiveWikiBookId);
            return FileHelper.IsFileValid(_failedResListPath) || FileHelper.IsFileValid(_failedPageListPath);

        }

        //重试失败列表
        public async Task RetryFailedItemsAsync(CancellationToken cancellationToken = default)
        {
            if (!await _downloadLock.WaitAsync(0)) return;
            try
            {
                bool includeResources = true;
                var wikiBook = App.AppStateManager.ActiveWikiBook;
                if (!wikiBook.IsResourceDownloaded) includeResources = false;
                InitializeSettings(cleanupTemporaryFiles: false);
                var task = await GetOrCreateTaskAsync(
                    wikiBook.Id,
                    AppTaskType.RetryFailed,
                    includeResources,
                    AppTaskPhase.DownloadingPages);
                await RunManagedTaskAsync(
                    task,
                    token => ExecuteRetryFailedAsync(task, includeResources, token),
                    cancellationToken);
            }
            finally
            {
                if (_activeDownloadTask is not null)
                    App.AppStateManager?.RemoveActiveTask(_activeDownloadTask.Id);
                _activeDownloadTask = null;
                _downloadLock.Release();
            }
        }

        private async Task ExecuteRetryFailedAsync(AppTask task, bool includeResources, CancellationToken token)
        {
            ConfigureRetryPaths(task.TaskDirectory);
            ConfigurePublicFailedPaths(task.WikiId!.Value);
            _activeDownloadTask = task;
            bool isNewRetry = task.CompletedPages == 0 && task.CompletedResources == 0 &&
                              task.TotalPages == 0 && task.TotalResources == 0;
            task.Status = AppTaskStatus.Running;
            task.IncludeResources = includeResources;
            if (isNewRetry)
            {
                task.Phase = AppTaskPhase.DownloadingPages;
                task.TotalPages = FileHelper.IsFileValid(_failedPageListPath) ? CountLines(_failedPageListPath) : 0;
                task.CompletedPages = 0;
                task.TotalResources = 0;
                task.CompletedResources = 0;
            }
            _totalPages = task.TotalPages;
            _completedPages = task.CompletedPages;
            _totalResources = task.TotalResources;
            _completedResources = task.CompletedResources;
            await SaveAppTaskAsync();

            if (task.Phase == AppTaskPhase.DownloadingPages && FileHelper.IsFileValid(_failedPageListPath))
            {
                _log.Info(_loc.Get("DataService.Log.RetryFailedPages"));
                await DownloadPagesBatchAsync(_failedPageListPath, _failedResListPath, $@"{_tempFailedPageListPath}", _pageConcurrency, token);
                MergeAndDeduplicateResourceLists(_resListPath, _failedResListPath);
                ReplaceFailedList(_tempFailedPageListPath, _failedPageListPath);
            }

            if (task.Phase == AppTaskPhase.DownloadingPages && includeResources)
            {
                task.Phase = AppTaskPhase.DownloadingResources;
                if (task.TotalResources == 0)
                    task.TotalResources = FileHelper.IsFileValid(_failedResListPath) ? CountLines(_failedResListPath) : 0;
                _totalResources = task.TotalResources;
                _completedResources = task.CompletedResources;
                await SaveAppTaskAsync();
            }

            if (task.Phase == AppTaskPhase.DownloadingResources && FileHelper.IsFileValid(_failedResListPath) && includeResources)
            {
                _log.Info(_loc.Get("DataService.Log.RetryFailedAssets"));
                await DownloadResourcesBatchAsync(_failedResListPath, _tempFailedResListPath, _resConcurrency, true, token);
                ReplaceFailedList(_tempFailedResListPath, _failedResListPath);
            }
            else if (includeResources)
            {
                task.TotalResources = task.CompletedResources;
            }

            bool hasFailedItems = FileHelper.IsFileValid(_failedPageListPath) || FileHelper.IsFileValid(_failedResListPath);
            task.Status = hasFailedItems ? AppTaskStatus.Paused : AppTaskStatus.Completed;
            if (!hasFailedItems)
                task.Phase = AppTaskPhase.PostProcessing;
            await SaveAppTaskAsync();
            await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
            _log.Success(_loc.Get("DataService.Log.RetryCompleted"));
            App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.RetryCompleted"));
        }

        public async Task ClearFailedListAsync()
        {
            await _taskRunner.RunAsync(AppTaskType.ClearFailedList, _ =>
            {
                ClearFailedListCore();
                return Task.CompletedTask;
            });
        }

        private void ClearFailedListCore()
        {
            InitializeSettings(cleanupTemporaryFiles: false);
            ConfigurePublicFailedPaths(App.AppStateManager!.ActiveWikiBookId);
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
            if (File.Exists(_tempFailedPageListPath)) File.Delete(_tempFailedPageListPath);
            if (File.Exists(_tempFailedResListPath)) File.Delete(_tempFailedResListPath);
            _log.Info(_loc.Get("DataService.Log.FailedListCleared"));
        }
        //删除文件夹
        public async Task DeleteDatabaseAsync(int wikiId)
            => await _taskRunner.RunAsync(
                AppTaskType.DeleteData,
                _ => DeleteDatabaseCoreAsync(wikiId),
                access: AppTaskAccess.Exclusive,
                wikiId: wikiId);

        private async Task DeleteDatabaseCoreAsync(int wikiId)
        {
            _log.Info(_loc.Get("DataService.Log.DeletingDatabase"));
            var wikiBook = await App.ManagerDb!.GetItemAsync<WikiBook>(wikiId);
            if (wikiBook is null)
                throw new InvalidOperationException($"WikiBook {wikiId} 不存在。");

            var downloadTasks = (await App.ManagerDb.GetItemsAsync<AppTask>())
                .Where(task => task.WikiId == wikiId)
                .ToList();
            var targetDataDirectory = Path.Combine(_storagePath.RootPath, wikiBook.DataFolder);
            var isActiveWiki = App.AppStateManager!.ActiveWikiBookId == wikiId;

            if (isActiveWiki)
                await App.ContentDb.CloseConnection();

            await Task.Run(() =>
            {
                foreach (var appTask in downloadTasks)
                {
                    if (!string.IsNullOrWhiteSpace(appTask.TaskDirectory) && Directory.Exists(appTask.TaskDirectory))
                        Directory.Delete(appTask.TaskDirectory, true);
                }

                if (Directory.Exists(targetDataDirectory))
                    Directory.Delete(targetDataDirectory, true);
            });

            foreach (var appTask in downloadTasks)
                await App.ManagerDb.DeleteItemAsync<AppTask>(appTask.Id);

            if (App.AppStateManager.CurrentDownloadTask?.WikiId == wikiId)
                App.AppStateManager.CurrentDownloadTask = null;

            await App.ManagerDb.DeleteItemAsync<WikiBook>(wikiId);

            if (isActiveWiki)
            {
                await App.ManagerDb.Init(true);
                await App.ContentDb.ReconnectAsync();
                await App.ContentDb.Init(true, App.AppStateManager.ActiveWikiBook);
                App.AppStateManager.ResetWikiNavigation();
                await AppService.WikiRefreshAsync();
            }

            _log.Success(_loc.Get("DataService.Log.DatabaseDeleted"));
            App.AppStateManager?.TriggerAlert(_loc.Get("Common.Notice"), _loc.Get("DataService.Log.DatabaseDeleted"));
        }
        public void DeleteDataDirectory()
        {
            if (Directory.Exists(_currentDataDir))
                Directory.Delete(_currentDataDir, true);
        }

        // ================= 核心功能 1: 获取页面清单 =================
        private async Task<int> FetchWikiPagesListAsync(CancellationToken cancellationToken = default)
        {
            _log.Info(_loc.Get("DataService.Log.FetchingPageList"));
            await UpdateListProgressAsync(0.5, 0);
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

                            await UpdateListProgressAsync(
                                0.5 + 0.49 * (1 - 1d / (pagesCount + 1)),
                                pagesCount);
                        }

                        if (string.IsNullOrEmpty(rawData?.Continue?.GapContinue))
                            break;

                        gapContinue = rawData?.Continue?.GapContinue;
                    }
                    catch (Exception e) when (!cancellationToken.IsCancellationRequested &&
                                               (e is HttpRequestException or TaskCanceledException))
                    {
                        if (++retryCount > _maxRetryAttempts) throw;
                        _log.Error(_loc.Get("DataService.Log.RequestFailedRetrying", e.Message, retryCount, _maxRetryAttempts));
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            writer.Flush();
            await UpdateListProgressAsync(1, pagesCount);
            _log.Success(_loc.Get("DataService.Log.FetchCompleted", pagesCount));

            return pagesCount;
        }

        private async Task FetchWikiRedirectsListAsync(CancellationToken cancellationToken = default)
        {
            string nextUrl = _redirectStartUrl;
            int pageCount = 1;
            int totalRedirects = 0;
            await UpdateListProgressAsync(0, 0);
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
                        await UpdateListProgressAsync(
                            0.49 * (1 - 1d / (pageCount + 1)),
                            totalRedirects);
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
                            await UpdateListProgressAsync(0.5, totalRedirects);
                            _log.Success(_loc.Get("DataService.Log.RedirectsFetched", totalRedirects));
                            nextUrl = null;
                            break;
                        }

                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
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

        private async Task UpdateListProgressAsync(double progress, int itemsFetched)
        {
            if (_activeDownloadTask is null)
                return;

            _activeDownloadTask.ListItemsFetched = itemsFetched;
            await SaveAppTaskAsync();
        }

        // ================= 业务入口: 下载页面 =================
        private async Task DownloadPagesBatchAsync(string pageListPath, string resListPath, string failedPageListPath, int maxConcurrency, CancellationToken cancellationToken = default)
        {
            using var writer = new BatchLineWriter(resListPath, 200);
            using var failedWriter = new BatchLineWriter(failedPageListPath, 200);
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(pageListPath))
            {
                totalCount = File.ReadLines(pageListPath).Count();
            }
            _log.Info(_loc.Get("DataService.Log.DownloadPagesStart", totalCount));
            using var provider = new BatchLineProvider(
                pageListPath,
                startLine: _activeDownloadTask?.ResumePageLine ?? _completedPages,
                initialCompletedCount: _activeDownloadTask?.ResumePageLine ?? _completedPages);
            async Task MarkPageCompletedAsync()
            {
                if (_activeDownloadTask != null)
                {
                    _activeDownloadTask.CompletedPages = _completedPages = provider.CompletedItemCount;
                    _activeDownloadTask.ResumePageLine = provider.CompletedLineCount;
                    await SaveAppTaskAsync();
                }
            }
            // 定义如何处理单行数据


            var scheduler = new BatchTaskScheduler<BatchLineItem>(_maxRetryAttempts);
            async Task ProcessPageLine(int workerId, BatchLineItem item, CancellationToken token)
            {
                string line = item.Line;
                bool processed = false;
                var parts = line.Split('|');
                if (parts.Length < 2)
                {
                    item.Complete();
                    await MarkPageCompletedAsync();
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
                    if (processed)
                    {
                        await MarkPageCompletedAsync();
                    }
                    _log.Info(_loc.Get("DataService.Log.PageCompleted", workerId, c, totalCount, page.Title));
                }


            }

            await scheduler.RunAsync(
                provider.GetNextItemAsync,
                ProcessPageLine,
                async (_, item, ex, token) =>
                {
                    failedWriter.Add(item.Line);
                    item.Complete();
                    await MarkPageCompletedAsync();
                },
                maxConcurrency,
                    onRetry: CheckNetworkBeforeRetryAsync,
                onNotFound: async (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                    await MarkPageCompletedAsync();
                },
                cancellationToken: cancellationToken);
            failedWriter.Flush();
            File.Delete(pageListPath);

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
            var inputListPath = deleteFile ? resListPath : _tempResListPath;
            if (!deleteFile && !File.Exists(inputListPath))
                File.Copy(resListPath, inputListPath, true);
            using var provider = new BatchLineProvider(
                inputListPath,
                startLine: _activeDownloadTask?.ResumeResourceLine ?? _completedResources,
                initialCompletedCount: _activeDownloadTask?.CompletedResources ?? _completedResources,
                completedLines: _activeDownloadTask?.CompletedResourceLines);
            async Task MarkResourceCompletedAsync()
            {
                if (_activeDownloadTask != null)
                {
                    await _resourceProgressLock.WaitAsync();
                    try
                    {
                        _activeDownloadTask.CompletedResources = _completedResources = provider.CompletedItemCount;
                        _activeDownloadTask.ResumeResourceLine = provider.CompletedLineCount;
                        _activeDownloadTask.CompletedResourceLines = provider.GetCompletedLineNumbers().ToHashSet();
                        await SaveAppTaskAsync();
                    }
                    finally
                    {
                        _resourceProgressLock.Release();
                    }
                }
            }
            using var failedWriter = new BatchLineWriter(failedResListPath, 200);
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
                    if (processed)
                    {
                        await MarkResourceCompletedAsync();
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
                await scheduler.RunAsync(
                    provider.GetNextItemAsync,
                    ProcessResLine,
                    async (_, item, ex, token) =>
                    {
                        failedWriter.Add(item.Line);
                        item.Complete();
                        await MarkResourceCompletedAsync();
                    },
                    maxConcurrency,
                    onRetry: CheckNetworkBeforeRetryAsync,
                    onNotFound: async (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                        await MarkResourceCompletedAsync();
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
                        failedWriter.Add(item.Line);
                        item.Complete();
                        await MarkResourceCompletedAsync();
                    },
                    maxConcurrency,
                    onRetry: CheckNetworkBeforeRetryAsync,
                    onNotFound: async (workerId, item, ex) =>
                {
                    _log.Info(_loc.Get("DataService.Log.ResourceNotFound", workerId, item.Line));
                    item.Complete();
                        await MarkResourceCompletedAsync();
                },
                    cancellationToken: cancellationToken);
            }

            failedWriter.Flush();
            File.Delete(inputListPath);

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
            foreach (var mergeFile in Directory.Exists(_currentDataDir)
                ? Directory.EnumerateFiles(_currentDataDir, "*.merge.tmp", SearchOption.TopDirectoryOnly)
                : [])
            {
                try { File.Delete(mergeFile); } catch { }
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