#if WINDOWS
using Terraria_Wiki.Services;

namespace Terraria_Wiki;

/// <summary>
/// 悬浮窗业务壳（仅 Windows），两种形态之间切换，窗口本体不依赖 MAUI：
///
///   · <b>图标球</b>：由 <see cref="FloatingIconWindow"/> 提供的纯 Win32 分层小窗（桌面上那个白球），
///     此时主窗口整个隐藏。
///   · <b>小条</b>：把<b>主窗口本身</b>缩小成置顶小窗（32px 条 + wiki 视图），球消失。
///     用主窗口而不是另开窗口，是为了让 WebView2 / iframe 全程不重建、wiki 不重载。
///
/// 切换：点球 → 展开小条；<see cref="CollapseBar"/> → 回到球；<see cref="Close"/> → 完全退出。
/// </summary>
public static class FloatingWindow
{
    private static Microsoft.Maui.Controls.Window? _mainWindow;

    /// <summary>
    /// 悬浮模式是否开启。这是<b>业务层自己的标志</b>，不能靠"球的窗口在不在"去推断——
    /// 球在展开过程中会先被销毁，那一刻若还依赖它，展开逻辑会把自己挡在门外。
    /// </summary>
    private static bool _active;

    /// <summary>是否处于小条形态。</summary>
    private static bool _barActive;

    /// <summary>悬浮窗是否开着（球或小条任一形态）。</summary>
    public static bool IsOpen => _active;

    /// <summary>
    /// 进入悬浮窗之前<b>主窗口</b>的几何（dip）。悬浮期间窗口尺寸/位置是小条的，
    /// 退出时用它还原，退出销毁保存窗口状态时也用它——绝不把悬浮窗的几何写进偏好。
    /// </summary>
    internal static (double Width, double Height, double X, double Y)? NormalGeometry { get; private set; }


    /// <summary>是否处于小条形态。</summary>
    public static bool IsBarActive => _barActive;

    /// <summary>开启悬浮窗：显示图标球、隐藏主窗口。返回是否成功。</summary>
    public static bool Open()
    {
        if (IsOpen)
            return true;

        _mainWindow = Application.Current?.Windows.FirstOrDefault();

        _active = true;                  // 先立标志：即使后面某步失败，也还能走"退出"路径恢复
        _barActive = false;

        // 记下主窗口进入悬浮窗之前的几何（此时还没被改成小条尺寸）
        if (_mainWindow is { } mainWindow)
            NormalGeometry = (mainWindow.Width, mainWindow.Height, mainWindow.X, mainWindow.Y);

        var window = FloatingIconWindow.Show(AppInfo.Current.Name);
        if (window is null)
        {
            // 建窗失败就别藏主窗口，否则应用会“消失”
            _active = false;
            NormalGeometry = null;       // 没真正进入悬浮模式，别留着这份几何
            SyncState();
            return false;
        }

        window.Closed += OnIconClosed;   // 点球 → 展开小条

        // 先开小窗、再藏主窗：避免出现“一个可见窗口都没有”的瞬间
        HideMainWindow();
        SyncState();

        // 悬浮窗模式（球态 / 小条态）都在托盘显示图标，右键可退出悬浮窗模式
        TrayIconWindow.Show(AppInfo.Current.Name);
        return true;
    }

    /// <summary>展开为小条形态：把主窗口缩成置顶小窗显示出来，再撤掉球。</summary>
    public static bool ExpandBar()
    {
        try
        {
            return ExpandBarCore();
        }
        catch (Exception ex)
        {
            // 这条路上任何异常都不该打崩进程，也不能让界面落到"没有可见窗口"的状态
            App.LogManager?.Error($"Expand floating bar failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"Expand floating bar failed: {ex}");

            // 兜底：无论如何把主窗口显示出来，别让应用"消失"
            try
            {
                _barActive = false;
                WindowHelper.SetMainWindowVisible(true);
                SyncState();
            }
            catch (Exception recoveryEx)
            {
                System.Diagnostics.Debug.WriteLine($"Recover after expand failure also failed: {recoveryEx}");
            }

            return false;
        }
    }

    private static bool ExpandBarCore()
    {
        App.LogManager?.Info($"Floating bar: expand requested (active={_active})");

        // 用业务标志判断，不能用 IsOpen 的"窗口是否还在"语义：
        // 走到这里时球可能已经被销毁了。
        if (!_active)
            return false;

        _barActive = true;

        // ★ 顺序很重要：先把主窗口摆成小条并显示出来，再销毁球。
        //   反过来的话，中间任何一步失败都会变成"球没了、主窗口还藏着"= 应用像消失了一样。
        App.LogManager?.Info("Floating bar: enter floating bar");
        WindowHelper.EnterFloatingBar();          // 记住原尺寸/位置 → 改成小条

        SyncState();
        App.LogManager?.Info("Floating bar: show main window");
        WindowHelper.SetMainWindowVisible(true);  // 主窗口以小条形态显示

        App.LogManager?.Info("Floating bar: destroy icon ball");
        var icon = FloatingIconWindow.Current;
        if (icon is not null)
        {
            icon.Closed -= OnIconClosed;   // 这次是"收起球"，不是用户点球
            icon.Destroy();
        }

        App.LogManager?.Info($"Floating bar: expanded (visible={WindowHelper.IsMainWindowVisible()})");
        return true;
    }

    /// <summary>收起为图标球形态：隐藏主窗口，重新显示球。</summary>
    public static bool CollapseBar()
    {
        try
        {
            return CollapseBarCore();
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"Collapse floating bar failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"Collapse floating bar failed: {ex}");
            return false;
        }
    }

    private static bool CollapseBarCore()
    {
        // 先立判断再动窗口：否则"隐藏主窗口"已经做了、却因为不满足条件直接返回，
        // 会留下"窗口被藏、球也没出"的空档。
        if (!_active)
        {
            WindowHelper.SetMainWindowVisible(true);
            return false;
        }

        // 收起成球之前记住小条的位置/大小（必须在 _barActive 置 false 之前，
        // WindowHelper 那边要靠它判断"确实是小条形态"）
        WindowHelper.RememberFloatingBarGeometry();

        _barActive = false;
        HideMainWindow();

        if (!FloatingIconWindow.IsAnyOpen)
        {
            var window = FloatingIconWindow.Show(AppInfo.Current.Name);
            if (window is not null)
            {
                window.Closed -= OnIconClosed;
                window.Closed += OnIconClosed;
            }
        }

        SyncState();
        return true;
    }

    /// <summary>完全退出悬浮窗：关掉球、还原主窗口。</summary>
    public static bool Close()
    {
        try
        {
            if (!_active)
                return WindowHelper.IsMainWindowVisible();

            _active = false;
            _barActive = false;

            var icon = FloatingIconWindow.Current;
            if (icon is not null)
            {
                icon.Closed -= OnIconClosed;
                icon.Destroy();
            }

            RestoreMainWindow();
            NormalGeometry = null;       // 已还原主窗口，这份几何用完即弃
            SyncState();
            return WindowHelper.IsMainWindowVisible();
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"Close floating window failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"Close floating window failed: {ex}");
            return false;
        }
    }

    /// <summary>用户点了球（或系统关掉了球）：展开成小条。</summary>
    private static void OnIconClosed()
    {
        App.LogManager?.Info($"Floating bar: icon closed (active={_active}, barActive={_barActive})");

        if (_active && !_barActive)
            ExpandBar();
    }

    private static void HideMainWindow()
    {
        if (WindowHelper.IsMainWindowVisible())
            WindowHelper.SetMainWindowVisible(false);
    }

    /// <summary>还原主窗口原尺寸/位置并显示、叫到前台。</summary>
    private static void RestoreMainWindow()
    {
        WindowHelper.ExitFloatingBar();   // 恢复进入小条前的几何

        if (!WindowHelper.IsMainWindowVisible())
            WindowHelper.SetMainWindowVisible(true);

        if (_mainWindow is { } main)
            Application.Current?.ActivateWindow(main);
    }

    /// <summary>把真实窗口状态同步到 AppState，供 Blazor 侧决定渲染内容。</summary>
    private static void SyncState()
    {
        if (App.AppStateManager is not { } state)
            return;

        state.FloatingBarActive = _barActive;
        state.FloatingSearchOpen = _active;

        // 模式变了就推当前形态该用的缩放：小条用 "FloatingZoom"、主窗口用 "WikiZoom"，
        // 同一个 iframe 节点不重载，不推的话网页缩放会停在另一个模式的值上。
        state.PushZoomForCurrentMode();
    }
}
#endif
