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
            _log.Error(_loc.Get("GlobalException.Unhandled") + " [FATAL:IsTerminating=" + e.IsTerminating + "]", exception);
        else
            _log.Error(_loc.Get("GlobalException.UnhandledObject", e.ExceptionObject));
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // ★ AOT 排查强化：把整条 AggregateException 链和 InnerException 的完整堆栈都落盘，
        //   仅记 Message 无法定位到底是哪个类型触发了反射式序列化。
        _log.Error(_loc.Get("GlobalException.UnobservedTask") + " [DIAG]" + Describe(e.Exception), e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// 把 AggregateException 展开成可读的多层堆栈文本（含 InnerException 链）。
    /// </summary>
    internal static string Describe(Exception? ex)
    {
        if (ex is null) return " (null)";
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        while (ex is not null && depth < 10)
        {
            sb.Append("\n  --- [").Append(depth).Append("] ").Append(ex.GetType().FullName)
              .Append(": ").Append(ex.Message);
            if (ex is AggregateException agg)
            {
                int i = 0;
                foreach (var inner in agg.InnerExceptions)
                    sb.Append("\n      * inner[").Append(i++).Append("] ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
            }
            if (!string.IsNullOrEmpty(ex.StackTrace))
                sb.Append("\n      STACK: ").Append(ex.StackTrace);
            ex = ex.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
