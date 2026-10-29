using System.Net;

namespace Terraria_Wiki.Services;

public sealed class BatchTaskScheduler<T>
{
    private readonly int _maxRetryAttempts;

    public BatchTaskScheduler(int maxRetryAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetryAttempts);
        _maxRetryAttempts = maxRetryAttempts;
    }

    public async Task RunAsync(
        Func<CancellationToken, Task<T?>> getNextTask,
        Func<int, T, CancellationToken, Task> processTask,
        Func<int, T, Exception, CancellationToken, Task> handleFailure,
        int concurrency,
        Func<int, T, int, Exception, CancellationToken, Task<bool>>? onRetry = null,
        Func<int, T, HttpRequestException, Task>? onNotFound = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getNextTask);
        ArgumentNullException.ThrowIfNull(processTask);
        ArgumentNullException.ThrowIfNull(handleFailure);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);

        var workers = Enumerable.Range(0, concurrency)
            .Select(workerId => RunWorkerAsync(
                workerId,
                getNextTask,
                processTask,
                handleFailure,
                onRetry,
                onNotFound,
                cancellationToken));

        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(
        int workerId,
        Func<CancellationToken, Task<T?>> getNextTask,
        Func<int, T, CancellationToken, Task> processTask,
        Func<int, T, Exception, CancellationToken, Task> handleFailure,
        Func<int, T, int, Exception, CancellationToken, Task<bool>>? onRetry,
        Func<int, T, HttpRequestException, Task>? onNotFound,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var task = await getNextTask(cancellationToken);
            if (task is null) return;

            try
            {
                int retry = 0;
                while (true)
                {
                    try
                    {
                        await processTask(workerId, task, cancellationToken);
                        break;
                    }
                    catch (DownloadStoppedException)
                    {
                        // 整个任务停下（目前只有断网）：当前条目不记失败，也不继续下一条。
                        throw;
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (onNotFound is not null)
                            await onNotFound(workerId, task, ex);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var nextRetry = retry + 1;
                        if (nextRetry > _maxRetryAttempts)
                            throw;

                        if (onRetry is not null &&
                            !await onRetry(workerId, task, nextRetry, ex, cancellationToken))
                            throw new OperationCanceledException(cancellationToken);

                        retry = nextRetry;
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }
            catch (DownloadStoppedException)
            {
                // ★ 必须在这里再放行一次：内层那个 catch 只是让"当前条目"不进失败列表，
                //   真正让它终止的是这里 —— 否则异常会掉进下面的 catch(Exception)，
                //   被当成普通失败记一条、然后 worker 继续跑下一条，
                //   整队条目会在几秒内被静默刷成失败，最后还报"全部完成"。
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await handleFailure(workerId, task, ex, cancellationToken);
            }
        }
    }
}
