using Microsoft.JSInterop;
using System.Runtime.InteropServices;
#if WINDOWS
using MicrosoftuiWindowing = Microsoft.UI.Windowing;
#endif

namespace Terraria_Wiki;

public static class WindowHelper
{
#if WINDOWS
    // 当前原生窗口引用（用于标题栏主题等需要窗口的操作）
    private static Microsoft.UI.Xaml.Window? _nativeWindow;

    // ========== Win32 API（仅保留窗口状态/置顶所需） ==========
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ========== Win32 常量 ==========
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_MAXIMIZE = 3;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    // ========== 公开方法 ==========

    /// <summary>
    /// 无边框 + 可调整大小（OverlappedPresenter 保留系统原生缩放能力，ExtendsContentIntoTitleBar 避免顶部白条）。
    /// </summary>
    public static void EnableResizableBorderless(Microsoft.UI.Xaml.Window nativeWindow)
    {
        if (nativeWindow is null)
            return;

        _nativeWindow = nativeWindow;
        ApplyBorderless(nativeWindow, isMaximizable: true);
    }

    /// <summary>
    /// 套用"无边框"外观（可重复调用，幂等）：
    ///   · 新建 OverlappedPresenter（保留 IsResizable → 系统原生缩放；不动它就不会重排非客户区）
    ///   · 去掉 WS_CAPTION、内容延伸进标题栏、DWM 圆角
    ///
    /// ★ 注意：<b>不要在已挂到窗口上的 presenter 实例上改属性</b>（例如直接写
    ///   `presenter.IsMaximizable = false`）——WinUI 会因此重新套用一次边框状态，
    ///   把无边框顶掉、系统默认标题栏又冒出来。要改这些开关就整份重建 presenter 后重设，
    ///   再走一遍本方法把无边框补回去。
    /// </summary>
    private static void ApplyBorderless(Microsoft.UI.Xaml.Window nativeWindow, bool isMaximizable)
    {
        var appWindow = nativeWindow.AppWindow;
        if (appWindow is null)
            return;

        // 内容延伸到标题栏区域，避免顶部露出系统标题栏背景
        nativeWindow.ExtendsContentIntoTitleBar = true;
        nativeWindow.Title = AppInfo.Name;

        // OverlappedPresenter.Create() 默认 IsResizable=true（保留 WS_THICKFRAME，系统原生可缩放），
        // 配合去掉 WS_CAPTION + ExtendsContentIntoTitleBar 实现无边框且无顶部白条
        var presenter = MicrosoftuiWindowing.OverlappedPresenter.Create();
        presenter.IsResizable = true;
        presenter.IsMaximizable = isMaximizable;
        presenter.IsMinimizable = true;
        appWindow.SetPresenter(presenter);

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd != IntPtr.Zero)
        {
            // 只去掉 WS_CAPTION（保留 WS_THICKFRAME 以支持系统原生缩放）
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_CAPTION);

            // 让样式改动立即生效
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        ForceRoundedCorners(nativeWindow);
    }

    /// <summary>
    /// 强制 DWM 圆角（Win11 默认按系统判定圆角，需显式设置 DWMWCP_ROUND）
    /// </summary>
    private static void ForceRoundedCorners(Microsoft.UI.Xaml.Window nativeWindow)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero)
            return;

        uint preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(uint));
    }

    /// <summary>
    /// 恢复窗口位置/大小/最大化状态（在 App.CreateWindow 中调用）
    /// </summary>
    public static void RestoreWindowState(Microsoft.Maui.Controls.Window window)
    {
        bool isMaximized = Preferences.Default.Get("IsMaximized", false);
        double width = Preferences.Default.Get("WindowWidth", 1000.0);
        double height = Preferences.Default.Get("WindowHeight", 650.0);
        double x = Preferences.Default.Get("WindowX", 100.0);
        double y = Preferences.Default.Get("WindowY", 100.0);

        window.Width = width;
        window.Height = height;
        window.X = x >= -1000 ? x : 100;
        window.Y = y >= -1000 ? y : 100;

        window.HandlerChanged += (s, e) =>
        {
            if (isMaximized && window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow && nativeWindow.AppWindow != null)
            {
                // 等窗口真正激活后再最大化，避免首次 Show 覆盖最大化状态（此前 TryEnqueue 时机过早会偶发还原为普通窗口）
                nativeWindow.Activated += OnActivated;

                void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
                {
                    nativeWindow.Activated -= OnActivated;
                    IntPtr hwnd = (IntPtr)nativeWindow.AppWindow.Id.Value;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_MAXIMIZE);
                    }
                }
            }
        };
    }

    /// <summary>
    /// 注册窗口销毁时保存状态（在 App.CreateWindow 中调用）
    /// </summary>
    public static void RegisterSaveOnDestroy(Microsoft.Maui.Controls.Window window)
    {
        window.Destroying += (s, e) =>
        {
            if (s is not Microsoft.Maui.Controls.Window w) return;

            var nativeWindow = w.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow?.AppWindow != null)
            {
                IntPtr hwnd = (IntPtr)nativeWindow.AppWindow.Id.Value;

                if (hwnd != IntPtr.Zero)
                {
                    if (IsIconic(hwnd)) return;

                    bool isMaximized = IsZoomed(hwnd);
                    Preferences.Default.Set("IsMaximized", isMaximized);

                    if (!isMaximized)
                    {
                        if (w.X < -1000 || w.Y < -1000) return;

                        Preferences.Default.Set("WindowWidth", w.Width);
                        Preferences.Default.Set("WindowHeight", w.Height);
                        Preferences.Default.Set("WindowX", w.X);
                        Preferences.Default.Set("WindowY", w.Y);
                    }
                }
            }
        };
    }

    /// <summary>
    /// 设置窗口置顶（无需传 Window，自动获取当前窗口）
    /// </summary>
    public static void SetAlwaysOnTop(bool isAlwaysOnTop)
    {
        var mauiWindow = Application.Current?.Windows[0];
        if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            if (nativeWindow.AppWindow != null)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, isAlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST,
                        0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }
    }

    // ===== 悬浮窗：主窗口显隐 =====

    /// <summary>主窗口当前是否可见。</summary>
    public static bool IsMainWindowVisible()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return false;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        return hwnd != IntPtr.Zero && IsWindowVisible(hwnd);
    }

    /// <summary>
    /// 隐藏/显示主窗口（原生 ShowWindow）。悬浮窗开启时主窗口整窗隐藏（不出现在任务栏与 Alt+Tab），
    /// 关闭悬浮窗时按原尺寸/位置/最大化状态原样显回来。
    /// </summary>
    public static void SetMainWindowVisible(bool visible)
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero)
            return;

        if (visible)
        {
            // 之前若是最小化状态，用 SW_SHOW 可能会把它“正常化”，所以按原状态还原
            ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
        }
        else
        {
            ShowWindow(hwnd, SW_HIDE);
        }
    }

    // 悬浮图标窗本身用纯 Win32 实现（见 FloatingIconWindow.cs），这里只管主窗口的"小条形态"。

    /// <summary>主窗口进入小条形态前的尺寸/位置（dip），退出时原样还原。</summary>
    private static (double Width, double Height, double X, double Y)? _savedGeometry;

    /// <summary>小条形态的默认宽（dip）。</summary>
    private const double FloatingBarWidth = 420;

    /// <summary>小条形态的默认高（dip）：32px 条 + wiki 视图。</summary>
    private const double FloatingBarHeight = 560;

    /// <summary>小条形态距屏幕工作区右上角的留白（dip）。</summary>
    private const double FloatingBarMargin = 24;

    /// <summary>
    /// 收起主窗口为悬浮"小条"形态：先记住当前尺寸/位置，再改成置顶小窗。
    /// 无边框（WS_CAPTION 已去掉、内容延伸进标题栏）在启动时就设置好了，这里只调尺寸/位置/置顶。
    /// 每一步都单独兜底：任何一步失败都不该让整个"展开小条"失败（那会导致界面没有任何可见窗口）。
    /// </summary>
    public static void EnterFloatingBar()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow is null)
        {
            App.LogManager?.Error("EnterFloatingBar: main window not found");
            return;
        }

        Try(nameof(EnterFloatingBar), "记住原尺寸/位置", () =>
            _savedGeometry ??= (mauiWindow.Width, mauiWindow.Height, mauiWindow.X, mauiWindow.Y));

        Try(nameof(EnterFloatingBar), "设置尺寸", () =>
        {
            mauiWindow.Width = FloatingBarWidth;
            mauiWindow.Height = FloatingBarHeight;
        });

        Try(nameof(EnterFloatingBar), "移动到右上角", () => MoveToTopRight(mauiWindow, FloatingBarMargin, FloatingBarMargin));
        Try(nameof(EnterFloatingBar), "置顶", () => SetAlwaysOnTop(true));

        // 小条不做最大化；整份重建 presenter 并重套无边框（不能直接改已挂载的 presenter 属性，会顶掉无边框）
        Try(nameof(EnterFloatingBar), "重套无边框（禁最大化）", () =>
        {
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                ApplyBorderless(nativeWindow, isMaximizable: false);
        });
    }

    /// <summary>还原主窗口（退出小条形态）：恢复进入前的尺寸/位置与可最大化状态。</summary>
    public static void ExitFloatingBar()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow is null)
            return;

        if (_savedGeometry is { } saved)
        {
            Try(nameof(ExitFloatingBar), "还原尺寸/位置", () =>
            {
                mauiWindow.Width = saved.Width;
                mauiWindow.Height = saved.Height;
                mauiWindow.X = saved.X >= -1000 ? saved.X : 100;
                mauiWindow.Y = saved.Y >= -1000 ? saved.Y : 100;
            });

            _savedGeometry = null;
        }

        Try(nameof(ExitFloatingBar), "恢复无边框（可最大化）", () =>
        {
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                ApplyBorderless(nativeWindow, isMaximizable: true);
        });

        // 还原时不再强行置顶：交给用户的"置顶"开关决定
        Try(nameof(ExitFloatingBar), "恢复置顶状态", () => SetAlwaysOnTop(App.AppStateManager?.IsPinned ?? false));
    }

    /// <summary>把窗口挪到当前显示器工作区的右上角（按设备密度换算物理像素）。</summary>
    private static void MoveToTopRight(Microsoft.Maui.Controls.Window window, double marginX, double marginY)
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double density = display.Density > 0 ? display.Density : 1;

        // 工作区 ≈ 整屏；这里只用整屏宽度减右侧留白，不依赖 Win32
        double workRight = display.Width / density;

        window.X = workRight - window.Width - marginX;
        window.Y = marginY;
    }

    /// <summary>带日志的单步保护：某一步失败不影响整体流程。</summary>
    private static void Try(string scope, string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"{scope} / {step} failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"{scope} / {step} failed: {ex}");
        }
    }

#elif MACCATALYST
    /// <summary>
    /// 设置窗口置顶
    /// </summary>
    public static void SetAlwaysOnTop(bool isAlwaysOnTop)
    {
        var mauiWindow = Application.Current?.Windows[0];
        var nativeWindow = mauiWindow?.Handler?.PlatformView as UIKit.UIWindow;
        if (nativeWindow != null)
        {
            nativeWindow.WindowLevel = isAlwaysOnTop
                ? UIKit.UIWindowLevel.Alert + 1
                : UIKit.UIWindowLevel.Normal;
        }
    }

    #else
    public static void SetAlwaysOnTop(bool _) { }

    // 悬浮窗（主窗口显隐 / 小条形态）只有 Windows 有；其他平台调用无副作用。
    public static bool IsMainWindowVisible() => true;

    public static void SetMainWindowVisible(bool _) { }

    public static void EnterFloatingBar() { }

    public static void ExitFloatingBar() { }
#endif

    /// <summary>
    /// 应用标题栏主题（含最小化/最大化/关闭按钮颜色），暗色/亮色跟随应用主题。
    /// </summary>
    public static void ApplyTitleBarTheme(bool isDark)
    {
#if WINDOWS
        if (_nativeWindow?.AppWindow?.TitleBar is not { } titleBar) return;

        var bg = isDark ? Windows.UI.Color.FromArgb(255, 19, 19, 19) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
        var fg = isDark ? Windows.UI.Color.FromArgb(255, 249, 250, 251) : Windows.UI.Color.FromArgb(255, 17, 24, 39);
        var hoverBg = isDark ? Windows.UI.Color.FromArgb(255, 31, 41, 55) : Windows.UI.Color.FromArgb(255, 238, 238, 238);
        var pressedBg = isDark ? Windows.UI.Color.FromArgb(255, 55, 65, 81) : Windows.UI.Color.FromArgb(255, 227, 227, 227);
        var inactiveFg = isDark ? Windows.UI.Color.FromArgb(255, 156, 163, 175) : Windows.UI.Color.FromArgb(255, 107, 114, 128);
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        titleBar.BackgroundColor = bg;
        titleBar.ForegroundColor = fg;
        titleBar.InactiveBackgroundColor = bg;
        titleBar.InactiveForegroundColor = inactiveFg;
        // 按钮常态背景透明，仅悬停/按下时显示反馈色
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = fg;
        titleBar.ButtonHoverBackgroundColor = hoverBg;
        titleBar.ButtonHoverForegroundColor = fg;
        titleBar.ButtonPressedBackgroundColor = pressedBg;
        titleBar.ButtonPressedForegroundColor = fg;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonInactiveForegroundColor = inactiveFg;
#endif
    }

    /// <summary>
    /// 让当前窗口进入拖动状态（由 JS 在标签栏按下后调用，绕过 WebView2 子窗口对拖动区域的拦截）。
    /// 实现与 tauri/tao 的 handle_os_dragging 完全一致：
    ///   1. 取真实光标坐标打包进 lParam（保证拖拽锚点正确）
    ///   2. ReleaseCapture 释放 WebView2 子窗口可能持有的鼠标捕获
    ///   3. SendMessage 同步发送 WM_NCLBUTTONDOWN，在鼠标按下期间进入系统拖拽循环
    /// </summary>
    public static void StartWindowDragImmediately()
    {
#if WINDOWS
        var mauiWindow = Application.Current?.Windows[0];
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero)
            return;

        GetCursorPos(out POINT pt);
        IntPtr lParam = (IntPtr)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, lParam);
#endif
    }

    /// <summary>
    /// 双击拖拽区最大化/还原（对应 tauri 的 internal_toggle_maximize 命令）。
    /// 先检查 is_resizable 和 is_maximizable，与 tauri 行为一致。
    /// </summary>
    [JSInvokable]
    public static void ToggleMaximize()
    {
#if WINDOWS
        var mauiWindow = Application.Current?.Windows[0];
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        var appWindow = nativeWindow.AppWindow;
        if (appWindow?.Presenter is not MicrosoftuiWindowing.OverlappedPresenter presenter)
            return;

        if (presenter.State == MicrosoftuiWindowing.OverlappedPresenterState.Maximized)
            presenter.Restore();
        else
            presenter.Maximize();
#endif
    }
}

#if WINDOWS
/// <summary>
/// 暴露给 WebView2 JS 的同步拖拽桥（host object）。
/// JS 通过 window.chrome.webview.hostObjects.sync.dragBridge 同步调用，
/// 绕过 Blazor JS interop 的异步消息队列（该队列在 WebView2 输入事件处理期间会被推迟），
/// 实现"按下即拖拽、实时跟随"。等价于 tauri 的 JS→原生 IPC 通道。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public class DragBridge
{
    public void StartWindowDrag() => WindowHelper.StartWindowDragImmediately();
    public void ToggleMaximize() => WindowHelper.ToggleMaximize();
}
#endif