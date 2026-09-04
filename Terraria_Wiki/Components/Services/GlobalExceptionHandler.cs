namespace Terraria_Wiki.Services;

public sealed class GlobalExceptionHandler
{
    private readonly LogService _log;
    private readonly LocalizationService _loc;

    public GlobalExceptionHandler(LogService log, LocalizationService loc)
    {
        _log = log;
        _loc = loc;
    }

    public void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _log.Error(_loc.Get("GlobalException.Unhandled"), exception);
        else
            _log.Error(_loc.Get("GlobalException.UnhandledObject", e.ExceptionObject));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log.Error(_loc.Get("GlobalException.UnobservedTask"), e.Exception);
        e.SetObserved();
    }
}
