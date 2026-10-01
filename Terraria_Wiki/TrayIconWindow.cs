#if WINDOWS
using System.Runtime.InteropServices;

namespace Terraria_Wiki;

/// <summary>
/// 悬浮小条形态专用的托盘图标（纯 Win32，不依赖 MAUI / WinForms）：
///   · Shell_NotifyIcon 在通知区域显示应用图标，提示文字为应用名；
///   · 右键弹出一个菜单，唯一条目是"退出悬浮窗模式"（还原主窗口，撤掉图标球与托盘图标）；
///   · 左键双击把主窗口叫到前台；
///   · 图标句柄由 PngIconDecoder 解码 Utils\appicon.png 后自建（不走 System.Drawing，Release 可 AOT）。
///
/// 消息窗口用 HWND_MESSAGE 的 message-only 窗口承载，不进任务栏、不可见。
/// 右键/菜单选择都<b>只投递消息</b>（PostMessage）再在自己的消息里收起小条：
/// 收起过程会把本窗口销毁，直接在菜单回调里做会踩到"WndProc 还在栈上、实例已释放"。
/// </summary>
internal sealed class TrayIconWindow
{
    private const string WindowClassName = "TerrariaWikiTrayIcon";
    private const string IconRelativePath = @"Utils\appicon.png";

    /// <summary>托盘回调消息（Shell_NotifyIcon 用 uCallbackMessage 指定）。</summary>
    private const uint WM_TRAYICON = 0x0400 + 1;   // WM_APP + 1

    private static readonly object Sync = new();
    private static WndProcDelegate? _wndProc;      // 必须保活：被 GC 回收后消息回调会崩
    private static bool _classRegistered;
    private static TrayIconWindow? _instance;

    private nint _hwnd;
    private nint _hIcon;
    private bool _added;

    private TrayIconWindow(nint hwnd) => _hwnd = hwnd;

    /// <summary>当前是否已显示托盘图标。</summary>
    public static bool IsVisible
    {
        get { lock (Sync) return _instance is { IsAlive: true }; }
    }

    private bool IsAlive => _hwnd != 0 && NativeMethods.IsWindow(_hwnd);

    /// <summary>显示托盘图标（重复调用无副作用）。</summary>
    public static void Show(string tooltip)
    {
        lock (Sync)
        {
            if (_instance is { IsAlive: true })
                return;
        }

        var created = Create(tooltip);
        if (created is null)
            return;

        lock (Sync)
            _instance = created;
    }

    /// <summary>移除托盘图标并销毁消息窗口（重复调用无副作用）。</summary>
    public static void Hide()
    {
        TrayIconWindow? instance;
        lock (Sync)
        {
            instance = _instance;
            _instance = null;
        }

        instance?.DestroyCore();
    }

    private static TrayIconWindow? Create(string tooltip)
    {
        RegisterClassOnce();

        // 父窗口挂到主窗口上、自己不可见：这样是普通的 child 窗口，
        // 既能收 Shell_NotifyIcon 的回调消息，也能被 EnumChildWindows 找到（便于排查）。
        nint owner = GetMainWindowHandle();

        nint hwnd = NativeMethods.CreateWindowEx(0, WindowClassName, AppInfo.Current.Name,
            NativeMethods.WS_CHILD,
            0, 0, 0, 0, owner, 0, 0, 0);
        if (hwnd == 0)
            return null;

        
        var window = new TrayIconWindow(hwnd) { _hIcon = TryCreateIcon() };

        var data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = window._hIcon,
            szTip = tooltip ?? string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            App.LogManager?.Error($"Tray icon: Shell_NotifyIcon(NIM_ADD) failed, hIcon={window._hIcon}");
            NativeMethods.DestroyWindow(hwnd);
            return null;
        }

                window._added = true;
        return window;
    }

    private void DestroyCore()
    {
        nint hwnd = _hwnd;
        if (hwnd == 0)
            return;

        _hwnd = 0;

        if (_added)
        {
            var data = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (NativeMethods.IsWindow(hwnd))
            NativeMethods.DestroyWindow(hwnd);

        if (_hIcon != 0)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = 0;
        }
    }

    /// <summary>取主窗口句柄（托盘消息窗口挂到它下面）。</summary>
    private static nint GetMainWindowHandle()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

        return 0;
    }

    private static void RegisterClassOnce()
    {
        if (_classRegistered)
            return;

        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc = DispatchWndProc),
            hInstance = NativeMethods.GetModuleHandle(null),
            hCursor = NativeMethods.LoadCursor(0, NativeMethods.IDC_ARROW),
            lpszClassName = WindowClassName
        };

        NativeMethods.RegisterClassEx(ref wndClass);
        _classRegistered = true;
    }

    private static nint DispatchWndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            TrayIconWindow? instance;
            lock (Sync)
                instance = _instance is { IsAlive: true } current && current._hwnd == hwnd ? current : null;

            return instance is not null
                ? instance.WndProc(hwnd, message, wParam, lParam)
                : NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // 托管异常逃回原生消息循环会直接终止进程
            System.Diagnostics.Debug.WriteLine($"Tray icon WndProc failed (msg=0x{message:X4}): {ex}");
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_TRAYICON:
                // lParam 低 16 位是通知消息（WM_LBUTTONUP / WM_RBUTTONUP / WM_CONTEXTMENU…）。
                // 注意 Win11 的托盘在右键时发的是 WM_CONTEXTMENU，其 lParam 低位是屏幕坐标而不是消息码，
                // 所以先按"整值等于消息码"判断，再退回低位判断。
                if (lParam == NativeMethods.WM_CONTEXTMENU ||
                    lParam == NativeMethods.WM_RBUTTONUP ||
                    (lParam & 0xFFFF) == NativeMethods.WM_RBUTTONUP)
                {
                    Win32Menu.ShowAtCursor(hwnd, FloatingWindow.IsBarActive);
                    return 0;
                }

                // 左键双击：把主窗口叫到前台（不退出悬浮窗模式）
                if (lParam == NativeMethods.WM_LBUTTONDBLCLK ||
                    (lParam & 0xFFFF) == NativeMethods.WM_LBUTTONDBLCLK)
                {
                    WindowHelper.ActivateMainWindow();
                    return 0;
                }
                return 0;

            // 菜单命令回到自己的消息循环里执行（收起/退出都可能销毁本窗口）
            case NativeMethods.WM_MENU_COMMAND:
                Win32Menu.HandleCommand((uint)wParam);
                return 0;

            case NativeMethods.WM_DESTROY:
                _hwnd = 0;
                return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>托盘用的 HICON（走共享的 IconArt）。</summary>
    private static nint TryCreateIcon()
    {
        int side = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (side <= 0)
            side = 16;

        return IconArt.CreateHIcon(side);
    }
    /// <summary>把（非预乘）BGRA 像素做成带 alpha 的 HICON。</summary>
    private static nint CreateHIcon(byte[] bgra, int side)
    {
        nint screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
            return 0;

        nint colorBitmap = 0;
        nint maskBitmap = 0;

        try
        {
            // 与 FloatingIconWindow 同一套已验证写法：BITMAPINFO + BI_RGB，负高度=自上而下
            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = side,
                    biHeight = -side,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB
                }
            };

            colorBitmap = NativeMethods.CreateDIBSection(screenDc, ref info, NativeMethods.DIB_RGB_COLORS,
                out nint bits, 0, 0);
            if (colorBitmap == 0 || bits == 0)
            {
                App.LogManager?.Error("Tray icon: CreateDIBSection failed");
                return 0;
            }

            Marshal.Copy(bgra, 0, bits, Math.Min(bgra.Length, side * side * 4));

            // AND 掩码：给 CreateIconIndirect 用；32bpp 图标实际靠 alpha 通道，掩码内容无关紧要
            maskBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, side, side);
            if (maskBitmap == 0)
            {
                App.LogManager?.Error("Tray icon: CreateCompatibleBitmap failed");
                return 0;
            }

            var iconInfo = new NativeMethods.ICONINFO
            {
                fIcon = true,
                hbmColor = colorBitmap,
                hbmMask = maskBitmap
            };

            nint icon = NativeMethods.CreateIconIndirect(ref iconInfo);
            if (icon == 0)
                App.LogManager?.Error("Tray icon: CreateIconIndirect failed");
            return icon;
        }
        finally
        {
            if (maskBitmap != 0)
                NativeMethods.DeleteObject(maskBitmap);
            if (colorBitmap != 0)
                NativeMethods.DeleteObject(colorBitmap);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>本文件用到的 Win32 互操作。</summary>
    private static class NativeMethods
    {
        public const uint WM_COMMAND = 0x0111;
        public const uint WM_DESTROY = 0x0002;
        public const uint WM_CONTEXTMENU = 0x007B;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_LBUTTONDBLCLK = 0x0203;

        public const uint NIM_ADD = 0x00000000;
        public const uint NIM_DELETE = 0x00000002;
        public const uint NIF_MESSAGE = 0x00000001;
        public const uint NIF_ICON = 0x00000002;
        public const uint NIF_TIP = 0x00000004;

        public const uint MF_STRING = 0x00000000;
        public const uint TPM_RIGHTBUTTON = 0x0002;
        public const uint TPM_RETURNCMD = 0x0100;

        public const int SM_CXSMICON = 49;
        public const int IDC_ARROW = 32512;
        public const int DIB_RGB_COLORS = 0;
        public const uint BI_RGB = 0;

        public static readonly nint HWND_MESSAGE = new(-3);

        public const int WS_CHILD = 0x40000000;

        /// <summary>与 Win32Menu 约定的菜单命令消息。</summary>
        public const uint WM_MENU_COMMAND = 0x0400 + 0x10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public nint hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public nint hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public nint hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public nint hbmMask;
            public nint hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public nint lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public nint hInstance;
            public nint hIcon;
            public nint hCursor;
            public nint hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
            public nint hIconSm;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowEx(int exStyle, string className, string windowName,
            int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        public static extern nint CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyMenu(nint menu);

        [DllImport("user32.dll")]
        public static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern nint LoadCursor(nint instance, int cursorName);

        [DllImport("user32.dll")]
        public static extern nint GetDC(nint hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint hwnd, nint dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern nint CreateDIBSection(nint dc, ref BITMAPINFO header, uint usage,
            out nint bits, nint section, uint offset);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

        [DllImport("user32.dll")]
        public static extern nint CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(nint obj);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(nint icon);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(string? moduleName);
    }
}
#endif
