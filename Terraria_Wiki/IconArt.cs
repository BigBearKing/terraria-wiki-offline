#if WINDOWS
using System.Runtime.InteropServices;

namespace Terraria_Wiki;

/// <summary>
/// 把 Resources\AppIcon\appicon.png（随输出复制到 Utils 目录）转成 Win32 需要的形式。
/// 悬浮球、右键菜单项图标、托盘图标共用这一套，避免三处各写一遍 GDI 互操作。
/// </summary>
internal static class IconArt
{
    private const string IconRelativePath = @"Utils\appicon.png";

    /// <summary>图标文件完整路径。</summary>
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, IconRelativePath);

    /// <summary>
    /// 造一个边长 <paramref name="side"/> 的 HICON（带 alpha）。失败返回 0。
    /// 返回的句柄由调用方负责 DestroyIcon。
    /// </summary>
    public static nint CreateHIcon(int side)
    {
        if (side <= 0)
            return 0;

        try
        {
            if (!File.Exists(IconPath))
            {
                App.LogManager?.Error($"IconArt: png not found at {IconPath}");
                return 0;
            }

            byte[] png = File.ReadAllBytes(IconPath);
            if (!PngIconDecoder.TryDecodePng(png, out var rgba, out int width, out int height))
            {
                App.LogManager?.Error("IconArt: png decode failed");
                return 0;
            }

            byte[] pixels = PngIconDecoder.ScaleToBgra(rgba, width, height, side, side);
            if (pixels.Length < side * side * 4)
                return 0;

            return CreateHIcon(pixels, side);
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"IconArt: build HICON failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"IconArt build HICON failed: {ex}");
            return 0;
        }
    }

    /// <summary>把（非预乘）BGRA 像素做成带 alpha 的 HICON。</summary>
    public static nint CreateHIcon(byte[] bgra, int side)
    {
        nint screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0)
            return 0;

        nint colorBitmap = 0;
        nint maskBitmap = 0;

        try
        {
            // BITMAPINFO + BI_RGB，负高度 = 自上而下（与分层窗口那套一致）
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
                App.LogManager?.Error("IconArt: CreateDIBSection failed");
                return 0;
            }

            Marshal.Copy(bgra, 0, bits, Math.Min(bgra.Length, side * side * 4));

            // AND 掩码：32bpp 图标实际靠 alpha 通道，掩码内容无关紧要
            maskBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, side, side);
            if (maskBitmap == 0)
            {
                App.LogManager?.Error("IconArt: CreateCompatibleBitmap failed");
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
                App.LogManager?.Error("IconArt: CreateIconIndirect failed");
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

    private static class NativeMethods
    {
        public const int DIB_RGB_COLORS = 0;
        public const uint BI_RGB = 0;

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
    }
}
#endif
