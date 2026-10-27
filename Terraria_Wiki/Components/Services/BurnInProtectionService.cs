using System.ComponentModel;
using Microsoft.JSInterop;
using Terraria_Wiki.Models; // 引入 AppState 所在的命名空间

namespace Terraria_Wiki.Services;

#if IOS
/// <summary>
/// iOS 防烧屏 / 防息屏：有任务在跑时按住屏幕常亮，并在用户连续 180 秒没操作后
/// 把屏幕压暗、盖一层缓慢移动的文字（避免 OLED 长时间显示同一画面）。
///
/// ★ 三个必须遵守的点（以前这里踩过）：
///   1. KeepScreenOn、屏幕亮度、覆盖层都是 UIKit，只能在主线程动。任务起停的通知来自下载线程，
///      直接在那里改会抛跨线程异常，而异常会把后面的 JS 监听一起带掉 ——
///      表现就是"任务在跑，屏幕照样自动熄"。所以统一用 MainThread 包起来，且每一步各自 try。
///   2. <see cref="NotifyInteraction"/> 必须是 <b>static</b>：[JSInvokable] 配合
///      <c>DotNet.invokeMethodAsync(程序集, 方法名)</c> 只会解析静态方法。以前写成实例方法，
///      调用一直失败，JS 侧又 <c>.catch</c> 吞掉了异常 → "用户一操作就重新计时"从来没生效过。
///   3. 所有计时器的启停都留在主线程（IDispatcherTimer）。
/// </summary>
public class BurnInProtectionService
{
    /// <summary>JS 静态调用（NotifyInteraction）回落用的当前实例。本服务是 DI 单例。</summary>
    private static BurnInProtectionService? _current;

    private readonly IDispatcherTimer _idleTimer;
    private readonly AppState _appState;
    private bool _isActive;
    private const int IdleTimeoutSeconds = 180;

    /// <summary>上一次"是否有任务在跑"的结论。只有它翻转时才动计时器，见 HandleTaskStateChanged。</summary>
    private bool _busy;

    public event Action<bool>? OnStateChanged;
    public bool IsActive => _isActive;

    public BurnInProtectionService(IDispatcher dispatcher, AppState appState)
    {
        _current = this;
        _appState = appState;

        _idleTimer = dispatcher.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(IdleTimeoutSeconds);
        _idleTimer.Tick += (s, e) => Activate();

        // 自动监听活动任务集合变化
        _appState.PropertyChanged += OnAppStatePropertyChanged;

        // 构造时可能已经有任务在跑（任务先起、页面后建），先对齐一次状态
        HandleTaskStateChanged();
    }

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // AddActiveTask / RemoveActiveTask 两个都会通知，任一变化都重算
        if (e.PropertyName is nameof(AppState.ActiveTasks) or nameof(AppState.HasActiveTasks))
            HandleTaskStateChanged();
    }

    /// <summary>
    /// 任务起 / 停：切到主线程后按住或放开屏幕，并启停防烧屏倒计时与 iframe 内的交互监听。
    ///
    /// ★ 只在"有任务 ⇄ 无任务"真正翻转时动作。
    ///   ActiveTasks 这个属性在下载过程中会被反复通知（每完成一个页面/资源就 SaveAppTaskAsync →
    ///   NotifyActiveTasksChanged），如果每次通知都 ResetTimer()，180 秒永远攒不满，
    ///   表现就是"任务跑很久，屏幕一直亮着、从不变暗"。
    /// </summary>
    private void HandleTaskStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // 在主线程上现读，保证拿到的是最新状态（通知可能连发好几次）
            bool busy = _appState.HasActiveTasks;
            if (busy == _busy) return;      // 只是进度刷新，和计时无关
            _busy = busy;

            if (busy)
            {
                ResetTimer();
                SetKeepScreenOn(true);
                _ = AppState.JS?.InvokeVoidAsync("startBurnInMonitoring");
            }
            else
            {
                _idleTimer.Stop();
                Deactivate();
                SetKeepScreenOn(false);
                _ = AppState.JS?.InvokeVoidAsync("stopBurnInMonitoring");
            }
        });
    }

    /// <summary>单独包一层：即使 UIKit 这一步出问题，也不能连带后面的 JS 监听一起失败。</summary>
    private static void SetKeepScreenOn(bool on)
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = on;
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"KeepScreenOn({on}) failed: {ex}");
        }
    }

    /// <summary>
    /// JS（index.html 的 startBurnInMonitoring）在用户交互时回调：重置空闲计时。
    /// ★ 必须是 static，原因见类注释第 2 条。
    /// </summary>
    [JSInvokable]
    public static void NotifyInteraction() => _current?.OnUserInteraction();

    private void OnUserInteraction()
    {
        if (_isActive) return;

        // 交互事件本来就在主线程；这里再包一层是兜底，ResetTimer 会动 IDispatcherTimer
        MainThread.BeginInvokeOnMainThread(ResetTimer);
    }

    public void ResetTimer()
    {
        _idleTimer.Stop();

        // 💡 直接读取注入的 _appState
        if (_appState.HasActiveTasks)
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
