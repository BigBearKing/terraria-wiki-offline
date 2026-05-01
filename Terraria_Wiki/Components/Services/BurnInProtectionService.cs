using Microsoft.JSInterop;

namespace Terraria_Wiki.Services;
#if IOS
public class BurnInProtectionService
{
    private readonly IDispatcherTimer _idleTimer;
    private bool _isActive;
    private const int IdleTimeoutSeconds = 180;

    // 定义事件，当状态改变（进入/退出保护）时通知 UI
    public event Action<bool>? OnStateChanged;

    public bool IsActive => _isActive;

    public BurnInProtectionService()
    {
        // 创建 MAUI 计时器
        _idleTimer = Application.Current.Dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(IdleTimeoutSeconds);
        _idleTimer.Tick += (s, e) => Activate();
    }

    // 外部（如 DataService）通知任务开始，启动计时
    public void StartMonitoring()
    {
        ResetTimer();
    }

    // 外部通知任务结束，彻底关闭
    public void StopMonitoring()
    {
        _idleTimer.Stop();
        Deactivate();
    }

    // 用户交互时重置计时
    [JSInvokable]
    public void NotifyInteraction()
    {
        if (_isActive) return;
        ResetTimer();
    }

    public void ResetTimer()
    {
        _idleTimer.Stop();
        // 只有在有后台任务时才重新启动（这里可以结合你的 AppStateManager 判断）
        if (App.AppStateManager?.ProcessingTaskId != 0)
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