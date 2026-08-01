using System.Runtime.InteropServices;
#if WINDOWS
using MicrosoftuiWindowing = Microsoft.UI.Windowing;
#endif

namespace Terraria_Wiki;

public static class WindowHelper
{
#if WINDOWS
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    // ========== Win32 常量 ==========
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_MAXIMIZE = 3;

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

    // ========== 无边框拉伸（WM_NCHITTEST 子类化） ==========
    private const int WM_NCHITTEST = 0x0084;

    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    private static int _borderWidth = 16;
    private static SUBCLASSPROC? _subclassProc;

    // ========== 公开方法 ==========

    /// <summary>
    /// 无边框 + 可调整大小（WM_NCHITTEST 子类化，边缘命中带默认 4px）。
    /// </summary>
    public static void EnableResizableBorderless(Microsoft.UI.Xaml.Window nativeWindow, int borderWidth = 16)
    {
        if (nativeWindow is null)
            return;

        _borderWidth = borderWidth;

        var appWindow = nativeWindow.AppWindow;
        if (appWindow is null)
            return;

        // 内容延伸到标题栏区域，避免顶部露出系统标题栏背景
        nativeWindow.ExtendsContentIntoTitleBar = true;

        // IsResizable=false：不产生 WS_THICKFRAME，系统不会在顶部留白色非客户区（白条的根源）。
        // 窗口调整大小完全交给 WM_NCHITTEST 子类实现（SC_SIZE 原生缩放循环不依赖 WS_THICKFRAME）。
        var presenter = MicrosoftuiWindowing.OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (hwnd != IntPtr.Zero)
        {
            // 只去掉 WS_CAPTION（不再强制 WS_THICKFRAME，避免顶部白条）
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_CAPTION);

            // 隐藏 Win11 默认的 1px 窗口边框线
            //uint colorNone = DWMWA_COLOR_NONE;
            //DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorNone, sizeof(uint));

            // 让样式改动立即生效
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            // 挂 WM_NCHITTEST 子类，边缘命中返回拉伸码（委托用静态字段持有，防 GC）
            _subclassProc ??= SubclassProc;
            SetWindowSubclass(hwnd, _subclassProc, new UIntPtr(1001), IntPtr.Zero);
        }

        ForceRoundedCorners(nativeWindow);
    }

    /// <summary>
    /// WM_NCHITTEST 子类回调：仅边缘命中带返回拉伸码，其余区域放行给默认处理（保证内容可点击）。
    /// </summary>
    private static IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            GetWindowRect(hWnd, out RECT rect);

            bool top = y >= rect.Top && y - rect.Top <= _borderWidth;
            bool bottom = y <= rect.Bottom && rect.Bottom - y <= _borderWidth;
            bool left = x >= rect.Left && x - rect.Left <= _borderWidth;
            bool right = x <= rect.Right && rect.Right - x <= _borderWidth;

            // 四角优先
            if (top && left) return (IntPtr)HTTOPLEFT;
            if (top && right) return (IntPtr)HTTOPRIGHT;
            if (bottom && left) return (IntPtr)HTBOTTOMLEFT;
            if (bottom && right) return (IntPtr)HTBOTTOMRIGHT;
            if (top) return (IntPtr)HTTOP;
            if (bottom) return (IntPtr)HTBOTTOM;
            if (left) return (IntPtr)HTLEFT;
            if (right) return (IntPtr)HTRIGHT;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
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
#endif
}