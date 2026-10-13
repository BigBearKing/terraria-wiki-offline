using Microsoft.JSInterop;
using System.Runtime.InteropServices;
#if WINDOWS
using System.Runtime.InteropServices.Marshalling;
using MicrosoftuiWindowing = Microsoft.UI.Windowing;
#endif

namespace Terraria_Wiki;

/// <summary>
/// 窗口相关的原生操作。声明成 partial 是给源生成 COM 用的：
/// <c>[GeneratedComInterface]</c> 要求接口本身<b>和它的所有包含类型</b>都是 partial（SYSLIB1090）。
/// </summary>
public static partial class WindowHelper
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// GetWindowPlacement 的出参。关键字段是 <c>rcNormalPosition</c>：
    /// 窗口<b>未最大化时</b>的矩形（物理像素）——最大化时会话里 MAUI 的 Width/Height/X/Y 记的是
    /// "最大化后的矩形"，只有这里有真正的普通矩形，见 <see cref="TryGetNormalGeometry"/>。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    // ========== 公开方法 ==========

    /// <summary>
    /// 主窗口形态：三个系统按钮（最小化/最大化/关闭）压在 tab-bar（win-placeholder）那条上，
    /// 不显示系统标题栏底、不显示标题文字。
    /// </summary>
    public static void EnableResizableBorderless(Microsoft.UI.Xaml.Window nativeWindow)
    {
        if (nativeWindow is null)
            return;

        _nativeWindow = nativeWindow;
        ApplyBorderless(nativeWindow, isMaximizable: true, hasBorder: true, hasTitleBar: true);
    }

    /// <summary>
    /// 套用窗口外观（可重复调用，幂等）。每次都会<b>整份重建 presenter</b>，因为边框/标题栏开关
    /// 只在 SetPresenter 那一刻生效——挂在窗口上的 presenter 之后再改 SetBorderAndTitleBar，
    /// 实测完全没有效果（读回 presenter.HasTitleBar 不变，标题栏与三大按钮照旧）。
    ///
    ///   · 主窗口形态：(hasBorder: true, hasTitleBar: true) —— 三个系统按钮照常绘制在右上角，
    ///     压在自绘的 tab-bar 上；标题栏底与标题文字都不画（后者由 ExtendsContentIntoTitleBar 抑制）。
    ///   · 悬浮小条形态：(hasBorder: true, hasTitleBar: false) —— 按钮与标题栏都不画，
    ///     顶部只剩自绘的 32px 拖动条。
    ///   · hasBorder 两种形态都保持 true：实测 <c>hasBorder: false</c> 会额外在窗口顶部
    ///     留一条约 6dip 的浅色窄条；且 (false, true) 会让 WinUI 在 SetPresenter 时抛异常。
    ///   · WS_CAPTION 一律去掉（本方法末尾）：这是"没有系统标题栏底"的关键。实测保留它会让
    ///     系统把整条标题栏底画在 y≈4..130 全宽上，把 tab-bar 盖掉（与去掉的版本逐行 800+ 像素不同）；
    ///     去掉之后按钮位置不变（ReunionWindowingCaptionControls 仍是右上角 276x64），只是没有底色。
    ///
    /// ★ 注意：<b>不要在已挂到窗口上的 presenter 实例上改属性</b>（例如直接写
    ///   `presenter.IsMaximizable = false`）——WinUI 会因此重新套用一次边框状态，
    ///   把无边框顶掉、系统默认标题栏又冒出来。要改这些开关就整份重建 presenter 后重设，
    ///   再走一遍本方法把外观补回去。
    /// </summary>
    private static void ApplyBorderless(Microsoft.UI.Xaml.Window nativeWindow, bool isMaximizable, bool hasBorder, bool hasTitleBar)
    {
        var appWindow = nativeWindow.AppWindow;
        if (appWindow is null)
            return;

        // 内容延伸到标题栏区域，避免顶部露出系统标题栏背景
        nativeWindow.ExtendsContentIntoTitleBar = true;
        nativeWindow.Title = AppInfo.Name;

        // OverlappedPresenter.Create() 默认 (HasBorder=true, HasTitleBar=true)：
        // 不调用 SetBorderAndTitleBar 的话，系统标题栏与最小化/最大化/关闭按钮会在窗口顶部照旧画出来
        // （实测读回 HasTitleBar=True、右上角有 292 个按钮字形像素）。
        var presenter = MicrosoftuiWindowing.OverlappedPresenter.Create();
        presenter.IsResizable = true;
        presenter.IsMaximizable = isMaximizable;
        presenter.IsMinimizable = true;

        // 边框/标题栏开关：必须在 SetPresenter 之前落在 presenter 上（挂上去以后再改是无效的）
        presenter.SetBorderAndTitleBar(hasBorder, hasTitleBar);
        appWindow.SetPresenter(presenter);

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd != IntPtr.Zero)
        {
            // 一律去掉 WS_CAPTION：这是"没有系统标题栏底"的关键（保留它会把整条标题栏底
            // 画在 tab-bar 上，实测逐行 800+ 像素差异）。WS_THICKFRAME 交给 presenter 决定，这里不碰。
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
    /// 注册窗口销毁时保存状态（在 App.CreateWindow 中调用）。
    ///
    /// 只保存<b>主窗口形态</b>的几何：悬浮窗模式下（小条 420x560，或主窗口整个藏着）当前
    /// <c>w.X/Y/Width/Height</c> 是小条的尺寸，直接写进偏好会让下次启动的主窗口变成小条大小。
    /// 这种情况改存 <see cref="FloatingWindow.NormalGeometry"/>（进入悬浮窗之前的主窗口几何），
    /// 拿不到就干脆不存。
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
                    // 小条形态下先把小条几何记进偏好：用户完全可能把它拖到别处就直接退出应用，
                    // 那样下次启动展开小条时就该回到同一个位置（这一步必须在下面的几何分支之前）。
                    // 传 w：销毁中的窗口可能已经不在 Application.Current.Windows 里了。
                    RememberFloatingBarGeometry(w);

                    if (IsIconic(hwnd)) return;

                    // 悬浮期间窗口被强制还原过（见 SetTaskbarIconVisible），这时 IsZoomed 会骗人：
                    // 用进入小条前记下的标志兜底，否则"最大化 → 悬浮窗 → 直接退出应用"下次启动就不是最大化。
                    bool isMaximized = IsZoomed(hwnd) || _savedMaximized == true;
                    Preferences.Default.Set("IsMaximized", isMaximized);

                    if (!isMaximized)
                    {
                        // 悬浮窗开着 → 只记进入前的几何；没记到就跳过（绝不把悬浮窗尺寸存进去）
                        var geometry = FloatingWindow.IsOpen
                            ? FloatingWindow.NormalGeometry
                            : (w.Width, w.Height, w.X, w.Y);

                        if (geometry is not { } g) return;
                        if (g.X < -1000 || g.Y < -1000) return;

                        Preferences.Default.Set("WindowWidth", g.Width);
                        Preferences.Default.Set("WindowHeight", g.Height);
                        Preferences.Default.Set("WindowX", g.X);
                        Preferences.Default.Set("WindowY", g.Y);
                    }
                }
            }
        };
    }

    /// <summary>
    /// 设置窗口置顶（无需传 Window，自动获取当前窗口）。
    /// 置顶要求窗口可见时才稳：窗口处于隐藏态时 SetWindowPos(HWND_TOPMOST) 可能不生效，
    /// 所以调用点都安排在"小条已经显示出来"之后。
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

    /// <summary>
    /// 进入小条形态前主窗口是否处于最大化。<b>必须单独记</b>：进入小条时
    /// <see cref="SetTaskbarIconVisible"/> 会先 <c>SW_RESTORE</c> 把窗口还原（否则小条会带着最大化态），
    /// 之后 IsZoomed 恒为 false，退出时就再也判不出"原来是不是最大化"了。
    /// 只在进程内记，退出悬浮窗时消费掉（置回 null）。
    /// </summary>
    private static bool? _savedMaximized;

    /// <summary>小条形态的默认宽（dip）。</summary>
    private const double FloatingBarWidth = 420;

    /// <summary>小条形态的默认高（dip）：32px 条 + wiki 视图。</summary>
    private const double FloatingBarHeight = 560;

    /// <summary>小条形态距屏幕工作区右上角的留白（dip）。</summary>
    private const double FloatingBarMargin = 24;

    /// <summary>小条几何可用性下限（dip）：比这还小的记录一律当成脏数据丢掉。</summary>
    private const double FloatingBarMinSize = 200;

    /// <summary>小条至少要露出这么多（dip）在当前屏幕里，用户才够得着把它拖回来。</summary>
    private const double FloatingBarMinVisible = 80;

    // 小条几何的偏好键（dip）。写偏好的时机只有"收起成球 / 退出悬浮窗 / 退出应用"，
    // 都是低频动作，不会造成频繁落盘。
    private const string FloatingBarWidthKey = "FloatingBarWidth";
    private const string FloatingBarHeightKey = "FloatingBarHeight";
    private const string FloatingBarXKey = "FloatingBarX";
    private const string FloatingBarYKey = "FloatingBarY";

    /// <summary>
    /// 上一次小条形态的尺寸/位置（dip）。收起成球、再展开回来时要回到这里，
    /// 而不是每次都跳回屏幕右上角。进程内先记一份（同一次运行里最准），
    /// 同时写进偏好，这样<b>重启应用后小条也还在原处</b>。
    /// </summary>
    private static (double Width, double Height, double X, double Y)? _savedBarGeometry;

    /// <summary>
    /// 记住当前小条几何（收起成球 / 退出悬浮窗 / 退出应用时调用；只在确实是小条形态时记）。
    /// <paramref name="window"/> 给"窗口正在销毁"那条路用：那时窗口可能已经从
    /// <c>Application.Current.Windows</c> 里摘掉了，必须由调用方把它传进来。
    /// </summary>
    internal static void RememberFloatingBarGeometry(Microsoft.Maui.Controls.Window? window = null)
    {
        var mauiWindow = window ?? Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow is null || !FloatingWindow.IsBarActive)
            return;

        Try(nameof(RememberFloatingBarGeometry), "记住小条几何", () =>
        {
            var geometry = (mauiWindow.Width, mauiWindow.Height, mauiWindow.X, mauiWindow.Y);
            _savedBarGeometry = geometry;

            Preferences.Default.Set(FloatingBarWidthKey, geometry.Width);
            Preferences.Default.Set(FloatingBarHeightKey, geometry.Height);
            Preferences.Default.Set(FloatingBarXKey, geometry.X);
            Preferences.Default.Set(FloatingBarYKey, geometry.Y);
        });
    }

    /// <summary>
    /// 读回上次记住的小条几何（偏好，dip）。没记过返回 null；记的那份现在还能不能用，
    /// 由调用方拿 <see cref="IsFloatingBarGeometryUsable"/> 判定。
    /// </summary>
    private static (double Width, double Height, double X, double Y)? LoadRememberedFloatingBarGeometry()
    {
        if (!Preferences.Default.ContainsKey(FloatingBarWidthKey))
            return null;

        return (
            Preferences.Default.Get(FloatingBarWidthKey, FloatingBarWidth),
            Preferences.Default.Get(FloatingBarHeightKey, FloatingBarHeight),
            Preferences.Default.Get(FloatingBarXKey, 0.0),
            Preferences.Default.Get(FloatingBarYKey, 0.0));
    }

    /// <summary>
    /// 小条几何是否可用：尺寸不能小于下限，且必须有一部分落在当前屏幕里。
    /// 显示器换了、分辨率变小了、或者上次把窗口拖到屏幕外才收起，旧坐标都可能让这条小条
    /// 完全看不见也够不着——这种情况宁可退回默认的右上角。
    /// </summary>
    private static bool IsFloatingBarGeometryUsable((double Width, double Height, double X, double Y) geometry)
    {
        if (geometry.Width < FloatingBarMinSize || geometry.Height < FloatingBarMinSize)
            return false;

        var display = DeviceDisplay.Current.MainDisplayInfo;
        double density = display.Density > 0 ? display.Density : 1;
        double screenWidth = display.Width / density;
        double screenHeight = display.Height / density;

        return geometry.X + geometry.Width > FloatingBarMinVisible
            && geometry.X < screenWidth - FloatingBarMinVisible
            && geometry.Y + geometry.Height > FloatingBarMinVisible
            && geometry.Y < screenHeight - FloatingBarMinVisible;
    }

    /// <summary>
    /// 收起主窗口为悬浮"小条"形态：先记住当前尺寸/位置，再改成置顶小窗。
    /// 小条几何有记忆（<see cref="_savedBarGeometry"/>）：再次展开回到上次的位置/大小，
    /// 第一次才落到屏幕右上角默认位。尺寸/位置/置顶都在这里设；外观（无系统标题栏 + 禁最大化）
    /// 由 <see cref="ApplyBorderless"/> 整份重建 presenter 完成。
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

        // 注意顺序：必须在把小条几何写进主窗口之前、更必须在 SetTaskbarIconVisible 的
        // SW_RESTORE 之前，把"主窗口几何 + 最大化标志"记下来。
        Try(nameof(EnterFloatingBar), "记住原尺寸/位置/最大化状态", () =>
        {
            // ★ 只认第一次：收起成球再展开回来时也要走本方法，那时窗口已经是小条了
            //   （IsZoomed 恒 false、rcNormalPosition 也变成小条矩形），再记一次就会把
            //   小条几何当成"主窗口几何"，退出悬浮窗时把主窗口还原成小条大小。
            if (_savedGeometry is not null)
                return;

            var nativeWindow = mauiWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            _savedMaximized = nativeWindow is not null && IsMainWindowMaximized(nativeWindow);

            // 最大化时 MAUI 的 Width/Height/X/Y 记的是"最大化后的矩形"（≠ 普通矩形），
            // 用它还原会得到一个占满屏幕的普通窗口——点"还原"看着像没反应。
            // 所以这种情况改问 Win32 要真正的普通矩形；实在拿不到才退回 MAUI 的值。
            var fallback = (mauiWindow.Width, mauiWindow.Height, mauiWindow.X, mauiWindow.Y);
            _savedGeometry = _savedMaximized == true && nativeWindow is not null
                ? TryGetNormalGeometry(nativeWindow) ?? fallback
                : fallback;
        });

        // ★ 必须先退出最大化，再设尺寸/位置：最大化窗口会**忽略**程序设置的 Width/Height（Windows 行为），
        //   于是"小条"会顶着主窗口的尺寸显示；而且那次被忽略的设置还会把窗口的"普通矩形"弄脏，
        //   随后 SetTaskbarIconVisible 里的 SW_RESTORE 就还原到那个脏矩形上，越用越歪。
        //   旧代码靠 SW_RESTORE 兜底退最大化，但它排在"设置尺寸与位置"之后 —— 尺寸早就白设了。
        Try(nameof(EnterFloatingBar), "退出最大化（否则尺寸/位置设不下去）", () =>
        {
            if (mauiWindow.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                return;

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            if (hwnd != IntPtr.Zero && IsZoomed(hwnd))
                ShowWindow(hwnd, SW_RESTORE);
        });

        Try(nameof(EnterFloatingBar), "设置尺寸与位置", () =>
        {
            // 进程内的记忆优先（收起成球再展开走的就是这条），其次才是偏好里"上次退出时"的位置。
            // 两个来源都要过一遍可用性：过小或已经跑到屏幕外的旧坐标宁可不用，退回右上角默认位。
            var remembered = _savedBarGeometry ?? LoadRememberedFloatingBarGeometry();

            if (remembered is { } bar && IsFloatingBarGeometryUsable(bar))
            {
                // 回到上次的小条位置/大小
                mauiWindow.Width = bar.Width;
                mauiWindow.Height = bar.Height;
                mauiWindow.X = bar.X;
                mauiWindow.Y = bar.Y;
                return;                     // 已归位，不再挪到右上角
            }

            mauiWindow.Width = FloatingBarWidth;
            mauiWindow.Height = FloatingBarHeight;
            MoveToTopRight(mauiWindow, FloatingBarMargin, FloatingBarMargin);
        });

        // 小条不占任务栏：WS_EX_TOOLWINDOW 会把任务栏按钮与 Alt+Tab 条目一起去掉
        Try(nameof(EnterFloatingBar), "任务栏不显示图标", () => SetTaskbarIconVisible(false));

        // 小条不做最大化、不要系统标题栏：整份重建 presenter
        // （边框/标题栏开关只在 SetPresenter 那一刻生效，不能直接改已挂载的 presenter）
        Try(nameof(EnterFloatingBar), "重套外观（禁最大化、无标题栏）", () =>
        {
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                ApplyBorderless(nativeWindow, isMaximizable: false, hasBorder: true, hasTitleBar: false);
        });

        // 置顶必须放最后：重建 presenter 与上面的"隐藏→改样式→显示"都会把 topmost 顶掉
        // （实测中间态 SetWindowPos(HWND_TOPMOST) 之后 exStyle 里的 WS_EX_TOPMOST 会丢）。
        Try(nameof(EnterFloatingBar), "置顶（收尾，确保生效）", () => SetAlwaysOnTop(true));

        // 托盘图标不在这里加/撤：悬浮窗模式（球态 / 小条态）全程都有，
        // 由 FloatingWindow.Open 显示、本方法（退出悬浮窗模式）撤掉。
    }

    /// <summary>
    /// 还原主窗口（退出悬浮窗模式）：恢复进入前的尺寸/位置、最大化状态与可最大化能力。
    /// 顺手记住当前小条几何，下次再展开小条时回到同一位置/大小。
    /// </summary>
    public static void ExitFloatingBar()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow is null)
            return;

        // ★ 必须在任何"重建 presenter / 重新显示窗口"之前判定：
        //   进过小条的话窗口已被强制还原，IsZoomed 恒为 false，只能用进入前记下的 _savedMaximized；
        //   只开过球、从没进小条的那条路（最大化 → 球 → 托盘退出悬浮窗）没有这份记录，
        //   那时窗口还带着最大化态，用实时值兜底。
        var nativeWindow = mauiWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        bool wasMaximized = _savedMaximized ?? (nativeWindow is not null && IsMainWindowMaximized(nativeWindow));
        _savedMaximized = null;

        // 必须在把主窗口几何写回去之前记，否则记到的已经是主窗口尺寸
        RememberFloatingBarGeometry();

        if (_savedGeometry is { } saved)
        {
            Try(nameof(ExitFloatingBar), "还原尺寸/位置", () =>
            {
                mauiWindow.Width = saved.Width;
                mauiWindow.Height = saved.Height;
                mauiWindow.X = saved.X >= -1000 ? saved.X : 100;
                mauiWindow.Y = saved.Y >= -1000 ? saved.Y : 100;
            });
        }

        _savedGeometry = null;

        // 退回主窗口形态：撤掉托盘图标、重新占任务栏
        Try(nameof(ExitFloatingBar), "撤掉托盘图标", () => TrayIconWindow.Hide());
        Try(nameof(ExitFloatingBar), "恢复任务栏图标", () => SetTaskbarIconVisible(true));

        // 恢复主窗口形态：可最大化 + 三大按钮可见、无标题文字（同样要整份重建 presenter 才生效）
        Try(nameof(ExitFloatingBar), "恢复外观（可最大化、显示系统按钮）", () =>
        {
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
                ApplyBorderless(window, isMaximizable: true, hasBorder: true, hasTitleBar: true);
        });

        // ★ 还原最大化必须排在 ApplyBorderless 之后：整份重建 presenter 会把窗口打回"普通"状态，
        //   排在它前面做会被它抹掉。这里就是"最大化 → 悬浮窗 → 退出"能回到最大化的落点。
        if (wasMaximized)
        {
            Try(nameof(ExitFloatingBar), "还原最大化状态", () =>
            {
                if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
                    MaximizeMainWindow(window);
            });
        }

        // 还原时不再强行置顶：交给用户的"置顶"开关决定
        Try(nameof(ExitFloatingBar), "恢复置顶状态", () => SetAlwaysOnTop(App.AppStateManager?.IsPinned ?? false));
    }

    /// <summary>
    /// 主窗口当前是否最大化。<c>IsZoomed</c> 是权威判据；presenter 状态只是兜底 WinUI 侧的时机差
    /// （系统按钮走的就是 OverlappedPresenter.Maximize，正常两条都同步）。
    /// </summary>
    private static bool IsMainWindowMaximized(Microsoft.UI.Xaml.Window nativeWindow)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd != IntPtr.Zero && IsZoomed(hwnd))
            return true;

        return nativeWindow.AppWindow?.Presenter is MicrosoftuiWindowing.OverlappedPresenter presenter
            && presenter.State == MicrosoftuiWindowing.OverlappedPresenterState.Maximized;
    }

    /// <summary>
    /// 取窗口<b>未最大化时</b>的尺寸/位置（dip）。最大化状态下 MAUI 的 Width/Height/X/Y 是最大化矩形，
    /// 真正的普通矩形只在 Win32 的 <c>WINDOWPLACEMENT.rcNormalPosition</c> 里；拿回来按窗口自己的
    /// 光栅化比例换算成 dip，才能喂回 MAUI 的 Width/Height/X/Y。
    /// 拿不到（调用失败 / 比例异常 / 尺寸非正）返回 null，调用方退回 MAUI 的值。
    /// </summary>
    private static (double Width, double Height, double X, double Y)? TryGetNormalGeometry(Microsoft.UI.Xaml.Window nativeWindow)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero)
            return null;

        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return null;

        double scale = nativeWindow.Content?.XamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0)
            scale = 1.0;

        var rect = placement.rcNormalPosition;
        double width = (rect.Right - rect.Left) / scale;
        double height = (rect.Bottom - rect.Top) / scale;
        if (width <= 0 || height <= 0)
            return null;

        return (width, height, rect.Left / scale, rect.Top / scale);
    }

    /// <summary>
    /// 最大化主窗口（退出悬浮窗时按进入前的状态还原）。
    /// 优先走 presenter——与双击拖动区的 <see cref="ToggleMaximize"/> 同一条路，WinUI 内部状态会同步；
    /// 没生效再退回 <c>ShowWindow(SW_MAXIMIZE)</c>。已经是最大化态就直接返回，不重复调用。
    /// </summary>
    private static void MaximizeMainWindow(Microsoft.UI.Xaml.Window nativeWindow)
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero || IsZoomed(hwnd))
            return;

        if (nativeWindow.AppWindow?.Presenter is MicrosoftuiWindowing.OverlappedPresenter presenter
            && presenter.IsMaximizable)
        {
            presenter.Maximize();
            if (IsZoomed(hwnd))
                return;
        }

        ShowWindow(hwnd, SW_MAXIMIZE);
    }

    /// <summary>托盘图标左键双击用：把主窗口显示出来并叫到前台。</summary>
    public static void ActivateMainWindow()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow is null)
            return;

        if (!IsMainWindowVisible())
            SetMainWindowVisible(true);

        Try(nameof(ActivateMainWindow), "叫到前台", () =>
        {
            if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
                    SetForegroundWindow(hwnd);
                }
            }
        });
    }

    /// <summary>
    /// 是否在任务栏显示图标。
    ///
    /// 光改 WS_EX_TOOLWINDOW / WS_EX_APPWINDOW 不够：窗口显示过一次之后任务栏按钮已经建好，
    /// 只改样式它不会立刻收回。这里按三层来保证：
    ///   1) 扩样式加 WS_EX_TOOLWINDOW、去 WS_EX_APPWINDOW（同时移除 Alt+Tab 条目）；
    ///   2) 隐藏窗口 → 改样式 → 再显示：任务栏按"窗口再次显示"重建按钮；
    ///   3) ITaskbarList::DeleteTab / AddTab 明确增删按钮，兜住任务栏不理会样式变化的情况。
    /// 最大化状态下改样式前先还原，避免小条带着最大化态。
    /// </summary>
    private static void SetTaskbarIconVisible(bool visible)
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd == IntPtr.Zero)
            return;

        if (!visible && IsZoomed(hwnd))
            ShowWindow(hwnd, SW_RESTORE);   // ★ 这一步会抹掉最大化状态：进小条前必须先记下它（_savedMaximized）

        bool wasVisible = IsWindowVisible(hwnd);
        if (wasVisible)
            ShowWindow(hwnd, SW_HIDE);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle = visible
            ? (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW
            : (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        // 让扩展样式改动立即生效
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // 明确增删任务栏按钮（不依赖任务栏是否响应样式变化）
        Try(nameof(SetTaskbarIconVisible), "ITaskbarList 增删按钮", () =>
        {
            var taskbar = GetTaskbarList();
            if (taskbar is null)
                return;

            taskbar.HrInit();
            if (visible)
                taskbar.AddTab(hwnd);
            else
                taskbar.DeleteTab(hwnd);
        });

        if (wasVisible)
        {
            // 之前若是最小化状态，用 SW_SHOW 可能会把它“正常化”，所以按原状态还原
            ShowWindow(hwnd, IsIconic(hwnd) ? SW_RESTORE : SW_SHOW);
        }
    }

    // ===== 任务栏按钮的显式增删（ITaskbarList，AOT 安全版）=====
    //
    // ★ 绝对不要再用 [ComImport] + new TaskbarList() 那种写法：
    //   NativeAOT 不支持内置 COM 互操作（官方 IL3052：Built-in COM is not supported with full ahead
    //   of time compilation. Use COM wrappers instead.），AOT 下那次 new 会落到运行时类型加载器的
    //   兜底路径，先抛 InvalidProgramException，而且这条路极慢——实测每次"点球展开小条"都要卡
    //   8~9 秒才把主窗口显示出来（见 LogHistory 里 SetTaskbarIconVisible 报错的时间戳：
    //   13:12:54 进小条 → 13:13:03 才报错并继续；Debug/JIT 下同一个调用是毫秒级）。
    //   改成"CoCreateInstance + 源生成 COM 包装器"后，vtable 在编译期生成，AOT 下既不抛也不卡。

    /// <summary>ITaskbarList（shobjidl.h）的 vtable 顺序即声明顺序：HrInit / AddTab / DeleteTab / …</summary>
    [GeneratedComInterface]
    [Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
    internal partial interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    /// <summary>TaskbarList 的 CLSID（shobjidl.h）：进程内 COM 组件，用 CoCreateInstance 激活。</summary>
    private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11D0-958A-006097C9A090");

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    /// <summary>源生成 COM 的包装器工厂：vtable 编译期生成，运行时不做代码生成（AOT 安全）。</summary>
    private static readonly StrategyBasedComWrappers TaskbarComWrappers = new();

    /// <summary>进程内单例的 ITaskbarList（激活失败为 null）。</summary>
    private static ITaskbarList? _taskbarList;

    /// <summary>
    /// 取 ITaskbarList：激活一次后长期复用，省掉每次切换都重新激活，也绕开"包装器与
    /// CoCreateInstance 那份引用到底谁负责释放"的模糊地带（ITaskbarList 本身就是单例组件，
    /// 让这一份活到进程结束即可）。激活失败返回 null，调用方只记日志，不影响悬浮窗流程。
    /// </summary>
    private static ITaskbarList? GetTaskbarList()
    {
        if (_taskbarList is not null)
            return _taskbarList;

        var clsid = TaskbarListClsid;
        var iid = typeof(ITaskbarList).GUID;
        if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out IntPtr unknown) != 0
            || unknown == IntPtr.Zero)
        {
            return null;
        }

        // 这里刻意不释放 unknown：包装器可能已经加过引用，释放容易踩悬空指针；
        // 激活失败这条路最多留一份引用给这个单例组件（异常照旧由调用点的 Try 记日志）。
        _taskbarList = TaskbarComWrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None) as ITaskbarList;

        return _taskbarList;
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
    /// 让当前窗口进入拖动状态（由 JS 在标签栏 / 小条顶部按下后调用）。
    /// 实现与 tauri/tao 的 handle_os_dragging 一致：
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
    ///
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
