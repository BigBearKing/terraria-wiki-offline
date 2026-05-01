using Microsoft.JSInterop;
using Terraria_Wiki.Models; // 引入 AppState 所在的命名空间

namespace Terraria_Wiki.Services;

#if IOS
public class BurnInProtectionService
{
    private readonly IDispatcherTimer _idleTimer;
    private readonly AppState _appState;
    private bool _isActive;
    private const int IdleTimeoutSeconds = 180;

    public event Action<bool>? OnStateChanged;
    public bool IsActive => _isActive;

    // 💡 重点：把 AppState 和 IDispatcher 都通过容器注入进来
    public BurnInProtectionService(IDispatcher dispatcher, AppState appState)
    {
        _appState = appState;

        _idleTimer = dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(IdleTimeoutSeconds);
        _idleTimer.Tick += (s, e) => Activate();

        // 💡 自动监听！当任务状态改变时，执行 HandleTaskStateChanged
        _appState.OnChange += HandleTaskStateChanged;
    }

    // 💡 事件处理：根据 TaskId 自动决定启动还是停止
    private void HandleTaskStateChanged()
    {
        int taskId = _appState.ProcessingTaskId;
        if (taskId != 0)
        {
            // 任务开始了，自动启动防烧屏倒计时
            ResetTimer();
            AppState.JS?.InvokeVoidAsync("startBurnInMonitoring");
        }
        else
        {
            // 任务结束了，彻底关闭防烧屏机制
            _idleTimer.Stop();
            Deactivate();
            AppState.JS?.InvokeVoidAsync("stopBurnInMonitoring");
        }
    }

    // JS 回调：用户交互时重置计时
    [JSInvokable]
    public void NotifyInteraction()
    {
        if (_isActive) return;
        ResetTimer();
    }

    public void ResetTimer()
    {
        _idleTimer.Stop();

        // 💡 直接读取注入的 _appState
        if (_appState.ProcessingTaskId != 0)
        {
            _idleTimer.Start();
        }
    }

    private void Activate()
    {
        _idleTimer.Stop();
        _isActive = true;
        OnStateChanged?.Invoke(true);
    }

    public void Deactivate()
    {
        _isActive = false;
        OnStateChanged?.Invoke(false);
    }
}
#endif