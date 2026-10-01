#if WINDOWS
using System.Runtime.InteropServices;

namespace Terraria_Wiki;

/// <summary>
/// 桌面上的分层图标窗（纯 Win32，不依赖 MAUI）：
///   · CreateWindowEx(WS_POPUP | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW)
///   · UpdateLayeredWindow 画"白色圆球 + 图标" → 窗口形状就是圆球，球外透明且点击穿透，不占任务栏
///   · 自己处理鼠标：按住拖动窗口，位移很小的松开算单击 → 触发 <see cref="Closed"/>
///   · 单击 / WM_CLOSE → 触发 <see cref="Closed"/>；右键不绑任何行为
/// 消息不自己泵：窗口建在调用线程上，由该线程已有的消息泵派发。
/// </summary>
internal sealed class FloatingIconWindow
{
    /// <summary>悬浮窗边长（dip），实际像素按屏幕 DPI 换算。</summary>
    public const int IconSizeDip = 40;

    /// <summary>窗口文件名（随程序输出到 Utils 目录）。</summary>
    private const string IconRelativePath = @"Utils\appicon.png";

    /// <summary>白色圆球半径相对窗口边长的比例（留一点透明边距，圆球外面完全透明）。</summary>
    private const double CircleRadiusRatio = 0.48;

    /// <summary>图标内容相对窗口边长的比例（白球内部再留一圈白边）。</summary>
    private const double IconContentRatio = 0.68;

    /// <summary>距屏幕工作区右上角的留白（dip）。</summary>
    private const int ScreenMarginDip = 24;

    private const string WindowClassName = "TerrariaWikiFloatingIcon";

    private static readonly object Sync = new();
    private static WndProcDelegate? _wndProc;      // 必须保活：被 GC 回收后消息回调会崩
    private static bool _classRegistered;

    private nint _hwnd;
    private bool _closeNotified;

    private byte[]? _pendingPixels;
    private int _pendingWidth;
    private int _pendingHeight;
    private int _pendingSide;

    private FloatingIconWindow(nint hwnd)
    {
        _hwnd = hwnd;
    }

    /// <summary>窗口已关闭（单击/右键/Esc/系统关闭都会走到）。</summary>
    public event Action? Closed;

    /// <summary>图标文件完整路径。</summary>
    public static string IconPath => IconArt.IconPath;

    /// <summary>当前是否有一个活着的图标窗。</summary>
    public static bool IsAnyOpen
    {
        get { lock (Sync) return _instance is { IsAlive: true }; }
    }

    private static FloatingIconWindow? _instance;

    /// <summary>当前活着的图标窗（没有则为 null）。</summary>
    public static FloatingIconWindow? Current
    {
        get { lock (Sync) return _instance is { IsAlive: true } ? _instance : null; }
    }

    public bool IsAlive => _hwnd != 0 && NativeMethods.IsWindow(_hwnd);

    /// <summary>最近一次合成好的预乘 BGRA 画面（自检用）。</summary>
    public static byte[]? LastComposedPixels { get; private set; }

    /// <summary>最近一次合成画面的边长（自检用）。</summary>
    public static int LastComposedSide { get; private set; }

    /// <summary>窗口当前的屏幕矩形（物理像素）。</summary>
    public (int X, int Y, int Width, int Height) CurrentRect
    {
        get
        {
            if (_hwnd == 0 || !NativeMethods.GetWindowRect(_hwnd, out var rect))
                return (0, 0, 0, 0);

            return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    /// <summary>创建并显示图标窗。失败返回 null（调用方据此不要隐藏主窗口）。</summary>
    public static FloatingIconWindow? Show(string windowTitle)
    {
        lock (Sync)
        {
            if (_instance is { IsAlive: true })
                return _instance;
        }

        var created = Create(windowTitle);
        if (created is null)
            return null;

        lock (Sync)
            _instance = created;

        return created;
    }

    /// <summary>关闭窗口并通知一次 <see cref="Closed"/>（幂等）。</summary>
    public void Destroy()
    {
        nint hwnd = _hwnd;
        if (hwnd == 0)
            return;

        _hwnd = 0;

        lock (Sync)
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        NotifyClosed();

        if (NativeMethods.IsWindow(hwnd))
            NativeMethods.DestroyWindow(hwnd);
    }

    private static FloatingIconWindow? Create(string windowTitle)
    {
        RegisterClassOnce();

        if (!PngIconDecoder.TryDecodePng(ReadIconBytes(), out var rgba, out var sourceWidth, out var sourceHeight))
            return null;

        uint dpi = NativeMethods.GetDpiForWindow(NativeMethods.GetDesktopWindow());
        if (dpi == 0) dpi = 96;

        int side = (int)Math.Round(IconSizeDip * dpi / 96.0);

        // WS_POPUP 无菜单无边框：客户区就是整窗，尺寸直接用图标边长。
        // 窗口过程在注册窗口类时就挂上（见 RegisterClassOnce），不用 SetWindowLongPtr 二次替换。
        nint hwnd = NativeMethods.CreateWindowEx(
            WindowExStyle, WindowClassName, windowTitle, WindowStyle,
            0, 0, side, side, 0, 0, 0, 0);

        if (hwnd == 0)
            return null;

        var window = new FloatingIconWindow(hwnd)
        {
            _pendingPixels = rgba,
            _pendingWidth = sourceWidth,
            _pendingHeight = sourceHeight,
            _pendingSide = side
        };

        int margin = (int)Math.Round(ScreenMarginDip * dpi / 96.0);
        int x = margin;
        int y = margin;
        if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, out var workArea, 0))
        {
            x = workArea.Right - side - margin;
            y = workArea.Top + margin;
        }

        // 显示前后各画一次：分层表面在窗口真正可见之前，UpdateLayeredWindow 的内容不会被合成（实测）
        window.Paint();

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, side, side,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        window.Paint();

        return window;
    }

    private static byte[] ReadIconBytes()
    {
        var path = IconPath;
        if (!File.Exists(path))
        {
            System.Diagnostics.Debug.WriteLine($"Floating icon not found: {path}");
            return [];
        }

        return File.ReadAllBytes(path);
    }

    private static void RegisterClassOnce()
    {
        if (_classRegistered)
            return;

        // 窗口过程直接注册成托管委托：不再用 SetWindowLongPtr 二次替换。
        // 委托保活在同一静态字段里（GC 回收它等于窗口回调悬空）。
        // hCursor 必须显式给标准箭头：给 NULL 的话系统会退回默认光标（在圆球上会看到上下双箭头）。
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

    /// <summary>把"白色圆球 + 图标"按 per-pixel alpha 画到分层窗口上（显示前后各调用一次）。</summary>
    private void Paint()
    {
        if (_pendingPixels is not { } rgba || _pendingSide <= 0)
            return;

        int side = _pendingSide;
        var composed = ComposeOnWhiteBall(rgba, _pendingWidth, _pendingHeight, side);
        if (composed.Length < side * side * 4)
            return;

        LastComposedPixels = composed;
        LastComposedSide = side;

        nint screenDc = NativeMethods.GetDC(0);
        nint memoryDc = 0;
        nint bitmap = 0;
        nint oldBitmap = 0;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = side,
                    biHeight = -side,          // 负高度 = 自上而下的行序
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB
                }
            };

            nint dibBits;
            bitmap = NativeMethods.CreateDIBSection(memoryDc, ref info, NativeMethods.DIB_RGB_COLORS,
                out dibBits, 0, 0);
            if (bitmap == 0 || dibBits == 0)
                return;

            Marshal.Copy(composed, 0, dibBits, composed.Length);

            oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            var size = new NativeMethods.SIZE { cx = side, cy = side };
            var sourcePoint = new NativeMethods.POINT { x = 0, y = 0 };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA   // 像素为预乘 BGRA
            };

            NativeMethods.UpdateLayeredWindow(_hwnd, screenDc, 0, ref size, memoryDc, ref sourcePoint,
                0, ref blend, NativeMethods.ULW_ALPHA);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paint floating icon failed: {ex}");
        }
        finally
        {
            if (memoryDc != 0)
            {
                if (oldBitmap != 0)
                    NativeMethods.SelectObject(memoryDc, oldBitmap);

                if (bitmap != 0)
                    NativeMethods.DeleteObject(bitmap);

                NativeMethods.DeleteDC(memoryDc);
            }

            if (screenDc != 0)
                NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// 合成最终画面：白色圆球（外缘 1px 抗锯齿，球外 alpha=0 完全透明）+ 居中缩放的图标，
    /// 输出<b>预乘</b> BGRA —— UpdateLayeredWindow(AC_SRC_ALPHA) 要求的正是预乘格式，
    /// 否则抗锯齿边缘会出错、边缘可能露出矩形底。
    /// </summary>
    private static byte[] ComposeOnWhiteBall(byte[] iconRgba, int iconWidth, int iconHeight, int side)
    {
        var result = new byte[side * side * 4];

        double radius = CircleRadiusRatio * side;
        double center = (side - 1) / 2.0;
        double innerRadius = radius - 1.0;          // 抗锯齿过渡起点
        double outerRadius = radius + 1.0;          // 过渡终点

        int contentSize = Math.Max(1, (int)Math.Round(IconContentRatio * side));
        var icon = PngIconDecoder.ScaleToBgra(iconRgba, iconWidth, iconHeight, contentSize, contentSize);
        int iconOffset = (side - contentSize) / 2;

        for (int y = 0; y < side; y++)
        {
            int row = y * side * 4;

            for (int x = 0; x < side; x++)
            {
                double distance = Math.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
                double coverage = distance <= innerRadius
                    ? 1.0
                    : distance >= outerRadius
                        ? 0.0
                        : (outerRadius - distance) / (outerRadius - innerRadius);

                int index = row + (x * 4);
                if (coverage <= 0)
                    continue;   // 球外保持全透明

                // 白球底色，再按图标 alpha 叠上图标（直通 alpha 空间）
                double red = 255;
                double green = 255;
                double blue = 255;

                int iconX = x - iconOffset;
                int iconY = y - iconOffset;
                if (iconX >= 0 && iconX < contentSize && iconY >= 0 && iconY < contentSize)
                {
                    int iconIndex = ((iconY * contentSize) + iconX) * 4;
                    double alpha = icon[iconIndex + 3] / 255.0;
                    if (alpha > 0)
                    {
                        blue = (icon[iconIndex] * alpha) + (blue * (1 - alpha));
                        green = (icon[iconIndex + 1] * alpha) + (green * (1 - alpha));
                        red = (icon[iconIndex + 2] * alpha) + (red * (1 - alpha));
                    }
                }

                int alpha255 = (int)Math.Round(255 * coverage);

                // 预乘
                result[index] = (byte)Math.Clamp((int)Math.Round(blue * coverage), 0, 255);
                result[index + 1] = (byte)Math.Clamp((int)Math.Round(green * coverage), 0, 255);
                result[index + 2] = (byte)Math.Clamp((int)Math.Round(red * coverage), 0, 255);
                result[index + 3] = (byte)alpha255;
            }
        }

        return result;
    }

    // ===================== 窗口消息 =====================

    /// <summary>按下时的鼠标位置（屏幕坐标），用于区分"单击"与"拖动"。</summary>
    private int _pressScreenX;
    private int _pressScreenY;

    /// <summary>按下瞬间的鼠标位置，松手时用来判定是否为单击。</summary>
    private int _downScreenX;
    private int _downScreenY;

    /// <summary>已累计但还没搬进窗口的位移（松手时补上，避免快速拖动"掉队"）。</summary>
    private int _pendingMoveX;
    private int _pendingMoveY;

    private bool _dragging;

    /// <summary>拖动判定阈值（物理像素）：位移不超过它就算单击。</summary>
    private const int DragThresholdPx = 4;

    /// <summary>
    /// 注册给窗口类的静态入口：按 HWND 找到实例再转发。
    /// 用静态方法而不是实例方法，避免窗口类里长期持有某个实例的委托。
    /// 整个回调都包了兜底：托管异常一旦逃回原生消息循环，进程会直接被终止（表现为闪退）。
    /// </summary>
    private static nint DispatchWndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            FloatingIconWindow? instance;
            lock (Sync)
                instance = _instance is { IsAlive: true } current && current._hwnd == hwnd ? current : null;

            return instance is not null
                ? instance.WndProc(hwnd, message, wParam, lParam)
                : NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Floating icon WndProc failed (msg=0x{message:X4}): {ex}");
            return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    /// <summary>
    /// 自己实现"按住拖动 + 单击展开"。
    ///
    /// 注意<b>不能</b>在 WM_NCHITTEST 里返回 HTCAPTION：那会让 Windows 接管成"拖动标题栏"
    /// 的模态循环，第一次单击会被当成"开始拖动"而被系统吃掉，于是变成要点两下才生效。
    ///
    /// 判定"是不是单击"只看<b>按下点与松手点的总位移</b>，不依赖中途收到多少条
    /// WM_MOUSEMOVE —— 只要中间那条消息少了一条/坐标系有偏差，拖动就会被误判成单击。
    /// 中途的 WM_MOUSEMOVE 只负责"把窗口搬过去"，顺带把基准点前移，等价于累计位移。
    /// </summary>
    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                NativeMethods.GetCursorPos(out var pressPoint);
                _pressScreenX = pressPoint.x;      // 用于拖动时算增量
                _pressScreenY = pressPoint.y;
                _downScreenX = pressPoint.x;       // 按住不动
                _downScreenY = pressPoint.y;       // 松手时用来判定是不是单击
                _pendingMoveX = 0;
                _pendingMoveY = 0;
                _dragging = false;
                NativeMethods.SetCapture(hwnd);
                return 0;

            case NativeMethods.WM_MOUSEMOVE:
                // wParam 没带左键就不要动：避免别的窗口发来的 move 把球搬走
                if ((wParam & NativeMethods.MK_LBUTTON) == 0)
                    return 0;

                NativeMethods.GetCursorPos(out var movePoint);
                int deltaX = movePoint.x - _pressScreenX;
                int deltaY = movePoint.y - _pressScreenY;

                if (!_dragging
                    && (Math.Abs(deltaX) > DragThresholdPx || Math.Abs(deltaY) > DragThresholdPx))
                {
                    _dragging = true;
                }

                if (_dragging && NativeMethods.GetWindowRect(hwnd, out var current))
                {
                    NativeMethods.SetWindowPos(hwnd, 0,
                        current.Left + deltaX, current.Top + deltaY, 0, 0,
                        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

                    // 窗口已跟着鼠标走，基准点同步前移；残留位移清零
                    _pressScreenX = movePoint.x;
                    _pressScreenY = movePoint.y;
                    _pendingMoveX = 0;
                    _pendingMoveY = 0;
                }
                else if (_dragging)
                {
                    // SetWindowPos 失败时先攒着，松手时补
                    _pendingMoveX += deltaX;
                    _pendingMoveY += deltaY;
                }

                return 0;

            case NativeMethods.WM_LBUTTONUP:
                NativeMethods.ReleaseCapture();

                // 松手时再算一次总位移：即使中途一条 WM_MOUSEMOVE 都没收到，也能识别出拖动
                NativeMethods.GetCursorPos(out var releasePoint);
                int totalX = releasePoint.x - _downScreenX;
                int totalY = releasePoint.y - _downScreenY;
                bool moved = _dragging
                    || Math.Abs(totalX) > DragThresholdPx
                    || Math.Abs(totalY) > DragThresholdPx;

                _dragging = false;

                if (moved)
                {
                    // ★ 只补"还没搬走的那一小段"（_pendingMove*），绝不把总位移再加一遍——
                    //   移动消息里那条路径已经搬过了，再加总位移会让球飞出去。
                    int remainingX = _pendingMoveX;
                    int remainingY = _pendingMoveY;
                    _pendingMoveX = 0;
                    _pendingMoveY = 0;

                    if ((remainingX != 0 || remainingY != 0)
                        && NativeMethods.GetWindowRect(hwnd, out var finalRect))
                    {
                        NativeMethods.SetWindowPos(hwnd, 0,
                            finalRect.Left + remainingX, finalRect.Top + remainingY, 0, 0,
                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    }
                }
                else
                {
                    Destroy();      // 只有真的没动过才算单击：展开小条（调用方在 Closed 里处理）
                }

                return 0;

            // 右键：弹出菜单（展开小窗 / 退出悬浮窗模式）
            case NativeMethods.WM_RBUTTONUP:
                NativeMethods.ReleaseCapture();
                Win32Menu.ShowAtCursor(hwnd, FloatingWindow.IsBarActive);
                return 0;

            // 菜单命令回到自己的消息循环里执行：收起/退出都可能销毁本窗口，
            // 在菜单回调里直接做会踩到"WndProc 还在栈上、实例已释放"。
            case NativeMethods.WM_MENU_COMMAND:
                Win32Menu.HandleCommand((uint)wParam);
                return 0;

            case NativeMethods.WM_SETCURSOR:
                // 鼠标在窗口上时保持标准箭头（避免窗口类光标被系统换成别的形状）
                if ((lParam & 0xFFFF) == NativeMethods.HTCLIENT)
                {
                    NativeMethods.SetCursor(NativeMethods.LoadCursor(0, NativeMethods.IDC_ARROW));
                    return 1;
                }
                break;

            case NativeMethods.WM_CAPTURECHANGED:
                _dragging = false;
                return 0;

            case NativeMethods.WM_CLOSE:
                // 直接返回 0，不交给 DefWindowProc：避免它再发一次 WM_DESTROY
                Destroy();
                return 0;

            case NativeMethods.WM_DESTROY:
                // 系统直接销毁（或 Destroy 走到这里）时收敛状态
                _hwnd = 0;
                NotifyClosed();
                return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>保证 <see cref="Closed"/> 只触发一次。</summary>
    private void NotifyClosed()
    {
        if (_closeNotified)
            return;

        _closeNotified = true;
        try
        {
            Closed?.Invoke();
        }
        catch (Exception ex)
        {
            // 订阅方（展开小条、还原主窗口）出错不应该打崩进程
            System.Diagnostics.Debug.WriteLine($"Floating icon Closed handler failed: {ex}");
        }
    }

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    private const int WindowStyle = NativeMethods.WS_POPUP;

    private const int WindowExStyle =
        NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW;

    /// <summary>本文件用到的 Win32 互操作。</summary>
    private static class NativeMethods
    {
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SPI_GETWORKAREA = 0x0030;
        public const uint BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const uint ULW_ALPHA = 0x00000002;
        public const int MK_LBUTTON = 0x0001;

        public const uint WM_DESTROY = 0x0002;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_SETCURSOR = 0x0020;
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_CAPTURECHANGED = 0x0215;

        /// <summary>与 Win32Menu 约定的菜单命令消息。</summary>
        public const uint WM_MENU_COMMAND = 0x0400 + 0x10;

        /// <summary>标准箭头光标（MAKEINTRESOURCE(IDC_ARROW)）。</summary>
        public const int IDC_ARROW = 32512;

        /// <summary>WM_SETCURSOR 的命中码：客户区。</summary>
        public const int HTCLIENT = 1;

        public static readonly nint HWND_TOPMOST = new(-1);

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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetCapture(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint LoadCursor(nint hInstance, int lpCursorName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetCursor(nint hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern nint GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern nint GetDC(nint hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(nint hWnd, nint hDC);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(nint hWnd, nint hdcDst, nint pptDst, ref SIZE psize,
            nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(string? lpModuleName);

        [DllImport("gdi32.dll")]
        public static extern nint CreateCompatibleDC(nint hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(nint hdc);

        [DllImport("gdi32.dll")]
        public static extern nint SelectObject(nint hdc, nint hObject);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(nint hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage,
            out nint ppvBits, nint hSection, uint offset);
    }
}
#endif
