using System.ComponentModel;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Terraria_Wiki.Services;

/// <summary>
/// 有任务在跑时阻止系统进入睡眠（Windows 上包含"新型待机" S0）。
///
/// 为什么需要：实测抓到过一次事故 —— 01:10:33 电脑进入新型待机，下载在待机里又跑了 77 分钟，
/// 之后待机把网卡断掉，最后一个请求就挂了 9 小时，直到 11:44 唤醒才被取消（当天的 Kernel-Power 506/507
/// 与 current_session.log 的 9 小时空档对得上）。用 <c>ES_SYSTEM_REQUIRED</c> 按住系统就不会有这一段：
/// <b>屏幕照常按电源计划熄灭</b>，但系统不会进睡眠/待机，下载能一口气跑完。
///
/// 挡不住的情况（这些只能改电源计划）：
///   · 合盖睡眠 —— 电源选项里"合上盖子时"要选"不采取任何操作"才不影响；
///   · 用户手动点"睡眠"或开始菜单的睡眠；
///   · 关键电池电量自动休眠。
///
/// 平台：Windows 用 SetThreadExecutionState；其它平台目前空实现
/// （Android 要挡住得申请 WAKE_LOCK，通常还要前台服务，等真要用到再说）。
/// </summary>
public static class SleepPreventionService
{
#if WINDOWS
    /// <summary>设置持续有效，直到下次调用改变它。</summary>
    private const uint ES_CONTINUOUS = 0x80000000;

    /// <summary>系统保持唤醒（不加 ES_DISPLAY_REQUIRED，屏幕该熄就熄）。</summary>
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
#endif

    private static AppState? _state;
    private static bool _preventing;

    /// <summary>挂到 <see cref="AppState"/> 上：任务开始/结束自动按住/放开系统。重复调用无副作用。</summary>
    public static void Attach(AppState state)
    {
        if (ReferenceEquals(_state, state))
            return;

        if (_state is not null)
            _state.PropertyChanged -= OnAppStateChanged;

        _state = state;
        _state.PropertyChanged += OnAppStateChanged;

        Apply(state.HasActiveTasks);
    }

    private static void OnAppStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // AddActiveTask / RemoveActiveTask 会主动通知 HasActiveTasks
        if (e.PropertyName is nameof(AppState.HasActiveTasks) or nameof(AppState.ActiveTasks))
            Apply(_state?.HasActiveTasks ?? false);
    }

    private static void Apply(bool busy)
    {
        if (busy == _preventing)
            return;

        _preventing = busy;

        // ★ 必须在同一个长命线程上调用：SetThreadExecutionState 的状态是**按线程**记的，
        //   调用它的线程一退出这份"按住"就没了。任务状态变化来自下载工作线程，所以统一切到主线程执行。
        // 这条路上不写常规日志（每次任务起停都刷一条没意义），只在真出错时记一条。
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
#if WINDOWS
                SetThreadExecutionState(ES_CONTINUOUS | (busy ? ES_SYSTEM_REQUIRED : 0));
#endif
            }
            catch (Exception ex)
            {
                App.LogManager?.Error($"SleepPrevention failed: {ex}");
            }
        });
    }
}
