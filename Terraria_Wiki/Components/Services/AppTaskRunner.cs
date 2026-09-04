using Terraria_Wiki.Models;
using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;

namespace Terraria_Wiki.Services;

public sealed class AppTaskRunner
{
    private readonly AppState _appState;
    private readonly LogService _log;
    private readonly LocalizationService _loc;
    private readonly DatabaseService _managerDb;
    private readonly SemaphoreSlim _databaseTaskLock = new(1, 1);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _taskCancellation = new();

    public AppTaskRunner(AppState appState, LogService log, LocalizationService loc, ManagerDbService managerDb)
    {
        _appState = appState;
        _log = log;
        _loc = loc;
        _managerDb = managerDb;
    }

    public async Task<bool> RunAsync(
        AppTaskType taskType,
        Func<CancellationToken, Task> action,
        AppTaskOptions? options = null,
        CancellationToken cancellationToken = default,
        AppTaskAccess access = AppTaskAccess.Shared,
        int? wikiId = null,
        AppTask? existingTask = null,
        bool canPause = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        options ??= new AppTaskOptions();

        if (!await EnsureDownloadTaskRequirementsAsync(taskType))
            return false;

        var lockTaken = access == AppTaskAccess.Exclusive && await _databaseTaskLock.WaitAsync(0, cancellationToken);
        if (access == AppTaskAccess.Exclusive && !lockTaken)
        {
            if (options.ShowBusy)
                ShowAlert("Common.Notice", "AppTask.Busy");
            return false;
        }

        return await RunCoreAsync(taskType, action, options, cancellationToken, lockTaken, wikiId, existingTask, canPause);
    }

    public async Task<bool> RunExistingAsync(
        AppTask task,
        Func<CancellationToken, Task> action,
        AppTaskOptions? options = null,
        CancellationToken cancellationToken = default,
        AppTaskAccess access = AppTaskAccess.Exclusive)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(action);
        options ??= new AppTaskOptions();

        if (!await EnsureDownloadTaskRequirementsAsync(task.TaskType))
            return false;

        var lockTaken = access == AppTaskAccess.Exclusive && await _databaseTaskLock.WaitAsync(0, cancellationToken);
        if (access == AppTaskAccess.Exclusive && !lockTaken)
        {
            if (options.ShowBusy)
                ShowAlert("Common.Notice", "AppTask.Busy");
            return false;
        }

        return await RunCoreAsync(task.TaskType, action, options, cancellationToken, lockTaken, task.WikiId, task, task.CanPause);
    }

    public async Task<bool> PauseAsync(int taskId)
    {
        var activeTask = _appState.GetActiveTask(taskId);
        if (activeTask is null || !activeTask.CanPause)
            return false;

        activeTask.Task.Status = AppTaskStatus.Paused;
        await _managerDb.SaveItemAsync(activeTask.Task);
        if (_taskCancellation.TryGetValue(taskId, out var cancellation))
            cancellation.Cancel();
        return true;
    }

    private async Task<bool> RunCoreAsync(
        AppTaskType taskType,
        Func<CancellationToken, Task> action,
        AppTaskOptions options,
        CancellationToken cancellationToken,
        bool lockTaken,
        int? wikiId,
        AppTask? task,
        bool canPause)
    {
        Exception? error = null;
        var cancelled = false;
        CancellationTokenSource? taskCancellation = null;

        try
        {
            task ??= new AppTask
                {
                    WikiId = wikiId,
                    TaskType = taskType,
                    Status = AppTaskStatus.Running,
                    CreatedTime = DateTime.Now,
                    UpdatedTime = DateTime.Now
                };
            task.WikiId ??= wikiId;
            task.TaskType = taskType;
            task.CanPause = canPause || task.CanPause;
            task.Status = AppTaskStatus.Running;
            await _managerDb.SaveItemAsync(task);
            _appState.AddActiveTask(new ActiveTaskInfo { Task = task, StartedTime = task.CreatedTime, CanCancel = true });
            taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _taskCancellation[task.Id] = taskCancellation;
            await action(taskCancellation.Token);
            if (task.Status is not AppTaskStatus.Paused and not AppTaskStatus.Interrupted)
            {
                task.Status = AppTaskStatus.Completed;
                await _managerDb.SaveItemAsync(task);
            }
        }
        catch (OperationCanceledException) when (taskCancellation?.IsCancellationRequested == true)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            if (task is not null)
            {
                _taskCancellation.TryRemove(task.Id, out var cancellation);
                cancellation?.Dispose();
                _appState.RemoveActiveTask(task.Id);
            }
            if (lockTaken)
                _databaseTaskLock.Release();
        }

        if (cancelled)
        {
            _log.Info(_loc.Get("AppTask.Cancelled"));
            if (task is not null)
            {
                task.Status = task.Status == AppTaskStatus.Paused
                    ? AppTaskStatus.Paused
                    : AppTaskStatus.Interrupted;
                await SaveTaskStatusAsync(task);
            }
            if (options.OnCancelled is not null)
            {
                try
                {
                    await options.OnCancelled();
                }
                catch (Exception callbackError)
                {
                    _log.Error(_loc.Get("AppTask.Failed"), callbackError);
                }
            }
            return false;
        }

        if (error is not null)
        {
            _log.Error(_loc.Get("AppTask.Failed"), error);
            if (task is not null)
            {
                task.Status = AppTaskStatus.Failed;
                task.LastError = error.Message;
                await SaveTaskStatusAsync(task);
            }
            if (options.OnError is not null)
            {
                try
                {
                    await options.OnError(error);
                }
                catch (Exception callbackError)
                {
                    _log.Error(_loc.Get("AppTask.Failed"), callbackError);
                }
            }
            if (options.ShowError)
                ShowAlert("Common.Error", "AppTask.Error", error.Message);
            if (options.Rethrow)
                ExceptionDispatchInfo.Capture(error).Throw();
            return false;
        }

        if (options.ShowSuccess)
            ShowAlert("Common.Notice", "AppTask.Completed");
        return true;
    }

    private async Task SaveTaskStatusAsync(AppTask task)
    {
        try
        {
            await _managerDb.SaveItemAsync(task);
            if (task.IsDownloadTask())
                _appState.CurrentDownloadTask = task;
        }
        catch (Exception saveError)
        {
            _log.Error(_loc.Get("AppTask.Failed"), saveError);
        }
    }

    private void ShowAlert(string titleKey, string messageKey, params object[] args)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is null)
            return;

        _appState.TriggerAlert(_loc.Get(titleKey), _loc.Get(messageKey, args));
    }

    private async Task<bool> EnsureDownloadTaskRequirementsAsync(AppTaskType taskType)
    {
        if (taskType is not (AppTaskType.DownloadPages or AppTaskType.DownloadResources or
            AppTaskType.DownloadAll or AppTaskType.UpdatePages or AppTaskType.UpdateAll or
            AppTaskType.RetryFailed))
            return true;

#if ANDROID
        if (!await AndroidNotificationPermissionService.EnsureGrantedAsync())
        {
            ShowAlert("Common.Notice", "AppTask.NotificationPermissionRequired");
            return false;
        }
#endif

        if (NetworkService.IsNetworkAvailable)
            return true;

        ShowAlert("Common.Notice", "AppTask.NetworkUnavailable");
        return false;
    }
}
