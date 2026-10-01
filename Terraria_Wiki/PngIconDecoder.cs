#nullable enable
using System.IO.Compression;
using System.Text;

namespace Terraria_Wiki;

/// <summary>
/// 悬浮图标用的极简 PNG 解码 + 区域平均缩放（纯托管实现，零外部依赖）。
/// 支持 8 位 灰度 / 灰度+alpha / RGB / RGBA、非隔行——图标都是这一类。
/// 输出 BGRA，供 Win32 分层窗口的 UpdateLayeredWindow 使用。
/// 之所以不引 System.Drawing / WIC：Release 走 PublishAot，GDI+ 与 WinRT 都不合适。
/// </summary>
internal static class PngIconDecoder
{
    internal static bool TryDecodePng(byte[] data, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(signature))
            return false;
        int channels = 0;
        int bitDepth = 0;
        byte interlace = 0;
        var idat = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= data.Length)
        {
            int length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            if (length < 0 || offset + 12 + length > data.Length)
                return false;
            var type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
            int contentOffset = offset + 8;
            switch (type)
            {
                case "IHDR":
                    width = ReadBigEndian(data, contentOffset);
                    height = ReadBigEndian(data, contentOffset + 4);
                    bitDepth = data[contentOffset + 8];
                    int colorType = data[contentOffset + 9];
                    interlace = data[contentOffset + 12];
                    channels = colorType switch
                    {
                        0 => 1,      // 灰度
                        2 => 3,      // RGB
                        4 => 2,      // 灰度 + alpha
                        6 => 4,      // RGBA
                        _ => 0
                    };
                    break;
                case "IDAT":
                    idat.Write(data, contentOffset, length);
                    break;
                case "IEND":
                    offset = data.Length;
                    break;
            }
            offset += 12 + length;
        }
        if (channels == 0 || bitDepth != 8 || interlace != 0 || width <= 0 || height <= 0)
            return false;
        byte[] raw;
        try
        {
            raw = Inflate(idat.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inflate icon failed: {ex}");
            return false;
        }
        // 逐行反滤波（PNG 标准 5 种滤波器）
        int stride = width * channels;
        if (raw.Length < (long)(stride + 1) * height)
            return false;
        var image = new byte[stride * height];
        int filterBpp = channels;
        int position = 0;
        for (int y = 0; y < height; y++)
        {
            byte filter = raw[position++];
            int rowStart = y * stride;
            int previousStart = rowStart - stride;
            for (int x = 0; x < stride; x++)
            {
                byte value = raw[position + x];
                byte left = x >= filterBpp ? image[rowStart + x - filterBpp] : (byte)0;
                byte up = y > 0 ? image[previousStart + x] : (byte)0;
                byte upLeft = y > 0 && x >= filterBpp ? image[previousStart + x - filterBpp] : (byte)0;
                image[rowStart + x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) >> 1)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => value
                };
            }
            position += stride;
        }
        // 展开成 BGRA
        pixels = new byte[width * height * 4];
        for (int i = 0, source = 0, target = 0; i < width * height; i++, source += channels, target += 4)
        {
            byte red;
            byte green;
            byte blue;
            byte alpha;
            switch (channels)
            {
                case 1:
                    red = green = blue = image[source];
                    alpha = 255;
                    break;
                case 2:
                    red = green = blue = image[source];
                    alpha = image[source + 1];
                    break;
                case 3:
                    red = image[source];
                    green = image[source + 1];
                    blue = image[source + 2];
                    alpha = 255;
                    break;
                default:
                    red = image[source];
                    green = image[source + 1];
                    blue = image[source + 2];
                    alpha = image[source + 3];
                    break;
            }
            pixels[target] = blue;
            pixels[target + 1] = green;
            pixels[target + 2] = red;
            pixels[target + 3] = alpha;
        }
        return true;
    }
    private static byte Paeth(byte left, byte up, byte upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            return left;
        return distanceUp <= distanceUpLeft ? up : upLeft;
    }
    private static int ReadBigEndian(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    /// <summary>解 zlib 流（PNG 的 IDAT 就是 zlib 包装的 deflate）。</summary>
    private static byte[] Inflate(byte[] zlib)
    {
        if (zlib.Length < 6 || zlib[0] != 0x78)
            throw new InvalidDataException("Not a zlib stream.");
        using var input = new MemoryStream(zlib, 2, zlib.Length - 6, writable: false);
        using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
    /// <summary>
    /// 高质量区域平均缩放（box filter）：先在水平方向聚合、再垂直方向聚合，
    /// 颜色按 alpha 加权，避免透明像素把边缘拉黑。返回 BGRA。
    /// </summary>
    internal static byte[] ScaleToBgra(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var target = new byte[targetWidth * targetHeight * 4];
        for (int y = 0; y < targetHeight; y++)
        {
            int y0 = y * sourceHeight / targetHeight;
            int y1 = (y + 1) * sourceHeight / targetHeight;
            if (y1 <= y0) y1 = y0 + 1;
            for (int x = 0; x < targetWidth; x++)
            {
                int x0 = x * sourceWidth / targetWidth;
                int x1 = (x + 1) * sourceWidth / targetWidth;
                if (x1 <= x0) x1 = x0 + 1;
                long alphaSum = 0;
                long redSum = 0;
                long greenSum = 0;
                long blueSum = 0;
                int count = 0;
                for (int sy = y0; sy < y1 && sy < sourceHeight; sy++)
                {
                    int row = sy * sourceWidth * 4;
                    for (int sx = x0; sx < x1 && sx < sourceWidth; sx++)
                    {
                        int index = row + sx * 4;
                        int alpha = source[index + 3];
                        alphaSum += alpha;
                        redSum += source[index + 2] * alpha;
                        greenSum += source[index + 1] * alpha;
                        blueSum += source[index] * alpha;
                        count++;
                    }
                }
                if (count == 0)
                    continue;
                int targetAlpha = (int)(alphaSum / count);
                int targetIndex = (y * targetWidth + x) * 4;
                target[targetIndex + 3] = (byte)targetAlpha;
                if (alphaSum > 0)
                {
                    target[targetIndex] = (byte)(blueSum / alphaSum);
                    target[targetIndex + 1] = (byte)(greenSum / alphaSum);
                    target[targetIndex + 2] = (byte)(redSum / alphaSum);
                }
            }
        }
        return target;
    }
}

