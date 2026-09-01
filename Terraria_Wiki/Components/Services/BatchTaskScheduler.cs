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
        Action<int, T, int, Exception>? onRetry = null,
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
        Action<int, T, int, Exception>? onRetry,
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
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (onNotFound is not null)
                            await onNotFound(workerId, task, ex);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (++retry > _maxRetryAttempts) throw;
                        onRetry?.Invoke(workerId, task, retry, ex);
                        await Task.Delay(1000, cancellationToken);
                    }
                }
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
