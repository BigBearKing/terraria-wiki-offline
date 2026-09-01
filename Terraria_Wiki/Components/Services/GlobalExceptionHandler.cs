namespace Terraria_Wiki.Services;

public sealed class GlobalExceptionHandler
{
    private readonly LogService _log;

    public GlobalExceptionHandler(LogService log)
    {
        _log = log;
    }

    public void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _log.Error("未处理的全局异常", exception);
        else
            _log.Error($"未处理的全局异常: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.Error("未观察到的任务异常", e.Exception);
        e.SetObserved();
    }
}
