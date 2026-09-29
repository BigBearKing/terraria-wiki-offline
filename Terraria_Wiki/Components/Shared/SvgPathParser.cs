using System.Globalization;
using Microsoft.Maui.Graphics;

namespace Terraria_Wiki.Components.Shared;

/// <summary>
/// 极简 SVG path 解析器：把 path 的 d 属性转成 MAUI 的 <see cref="PathF"/>。
///
/// 为什么要它：页内搜索自绘弹窗里的图标必须与 Icon.razor 里的图标一模一样，
/// 那些图标是填充/描边路径（不是几何图形），只能解析原始路径数据来复刻。
///
/// 支持 M/m L/l H/h V/v C/c S/s Q/q T/t A/a Z/z；圆弧按 SVG 规范转成三次贝塞尔。
/// </summary>
internal static class SvgPathParser
{
    /// <summary>
    /// 解析路径数据。
    /// </summary>
    /// <param name="d">path 的 d 属性。</param>
    /// <param name="scale">从 viewBox 单位到目标像素的缩放。</param>
    /// <param name="offsetX">附加平移（对应 path 的 transform="translate(x y)"）。</param>
    /// <param name="offsetY">附加平移。</param>
    /// <param name="flipY">是否沿 Y 翻转（对应 CSS rotate(180deg) 之类的等效变换）。</param>
    /// <param name="centerY">翻转时的轴（viewBox 中心）。</param>
    public static PathF Parse(
        string d,
        float scale = 1f,
        float offsetX = 0f,
        float offsetY = 0f,
        bool flipY = false,
        float centerY = 12f)
    {
        var path = new PathF();
        var tokens = Tokenize(d);
        var i = 0;

        // 当前点与上一控制点（用于 S/T 的平滑推导）
        float cx = 0f, cy = 0f;
        float lastCubicCtrlX = 0f, lastCubicCtrlY = 0f;
        float lastQuadCtrlX = 0f, lastQuadCtrlY = 0f;
        char prevCmd = '\0';
        char cmd = '\0';

        float Tx(float x) => (x + offsetX) * scale;
        float Ty(float y)
        {
            var yy = flipY ? 2f * centerY - y : y;
            return (yy + offsetY) * scale;
        }

        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.IsCommand)
            {
                cmd = token.Command;
                i++;
            }
            else if (cmd == '\0')
            {
                break; // 非法：数字出现在任何命令之前
            }
            else if (cmd == 'M')
            {
                cmd = 'L'; // SVG 规定 M 之后的多组坐标按 L 处理
            }
            else if (cmd == 'm')
            {
                cmd = 'l';
            }

            bool rel = char.IsLower(cmd);
            var upper = char.ToUpperInvariant(cmd);

            switch (upper)
            {
                case 'M':
                {
                    if (!TryNum(tokens, ref i, out var x, out var y)) return path;
                    cx = rel ? cx + x : x;
                    cy = rel ? cy + y : y;
                    path.MoveTo(Tx(cx), Ty(cy));
                    prevCmd = cmd;
                    break;
                }
                case 'L':
                {
                    if (!TryNum(tokens, ref i, out var x, out var y)) return path;
                    cx = rel ? cx + x : x;
                    cy = rel ? cy + y : y;
                    path.LineTo(Tx(cx), Ty(cy));
                    prevCmd = cmd;
                    break;
                }
                case 'H':
                {
                    if (!TryNum(tokens, ref i, out var x)) return path;
                    cx = rel ? cx + x : x;
                    path.LineTo(Tx(cx), Ty(cy));
                    prevCmd = cmd;
                    break;
                }
                case 'V':
                {
                    if (!TryNum(tokens, ref i, out var y)) return path;
                    cy = rel ? cy + y : y;
                    path.LineTo(Tx(cx), Ty(cy));
                    prevCmd = cmd;
                    break;
                }
                case 'C':
                {
                    if (!TryNum(tokens, ref i, out var x1, out var y1, out var x2, out var y2, out var x, out var y))
                        return path;
                    float ax1 = rel ? cx + x1 : x1, ay1 = rel ? cy + y1 : y1;
                    float ax2 = rel ? cx + x2 : x2, ay2 = rel ? cy + y2 : y2;
                    float ax = rel ? cx + x : x, ay = rel ? cy + y : y;
                    path.CurveTo(Tx(ax1), Ty(ay1), Tx(ax2), Ty(ay2), Tx(ax), Ty(ay));
                    lastCubicCtrlX = ax2; lastCubicCtrlY = ay2;
                    cx = ax; cy = ay;
                    prevCmd = cmd;
                    break;
                }
                case 'S':
                {
                    if (!TryNum(tokens, ref i, out var x2, out var y2, out var x, out var y)) return path;
                    var isCubicPrev = prevCmd is 'C' or 'c' or 'S' or 's';
                    var rx1 = isCubicPrev ? 2 * cx - lastCubicCtrlX : cx;
                    var ry1 = isCubicPrev ? 2 * cy - lastCubicCtrlY : cy;
                    float ax2 = rel ? cx + x2 : x2, ay2 = rel ? cy + y2 : y2;
                    float ax = rel ? cx + x : x, ay = rel ? cy + y : y;
                    path.CurveTo(Tx(rx1), Ty(ry1), Tx(ax2), Ty(ay2), Tx(ax), Ty(ay));
                    lastCubicCtrlX = ax2; lastCubicCtrlY = ay2;
                    cx = ax; cy = ay;
                    prevCmd = cmd;
                    break;
                }
                case 'Q':
                {
                    if (!TryNum(tokens, ref i, out var x1, out var y1, out var x, out var y)) return path;
                    float ax1 = rel ? cx + x1 : x1, ay1 = rel ? cy + y1 : y1;
                    float ax = rel ? cx + x : x, ay = rel ? cy + y : y;
                    path.QuadTo(Tx(ax1), Ty(ay1), Tx(ax), Ty(ay));
                    lastQuadCtrlX = ax1; lastQuadCtrlY = ay1;
                    cx = ax; cy = ay;
                    prevCmd = cmd;
                    break;
                }
                case 'T':
                {
                    if (!TryNum(tokens, ref i, out var x, out var y)) return path;
                    var isQuadPrev = prevCmd is 'Q' or 'q' or 'T' or 't';
                    var rx1 = isQuadPrev ? 2 * cx - lastQuadCtrlX : cx;
                    var ry1 = isQuadPrev ? 2 * cy - lastQuadCtrlY : cy;
                    float ax = rel ? cx + x : x, ay = rel ? cy + y : y;
                    path.QuadTo(Tx(rx1), Ty(ry1), Tx(ax), Ty(ay));
                    lastQuadCtrlX = rx1; lastQuadCtrlY = ry1;
                    cx = ax; cy = ay;
                    prevCmd = cmd;
                    break;
                }
                case 'A':
                {
                    if (!TryNum(tokens, ref i, out var rx, out var ry, out var rot,
                            out var largeArc, out var sweep, out var x, out var y))
                        return path;
                    float ax = rel ? cx + x : x, ay = rel ? cy + y : y;
                    AppendArc(path, Tx(cx), Ty(cy), Tx(ax), Ty(ay),
                        MathF.Abs(rx) * scale, MathF.Abs(ry) * scale,
                        rot, largeArc != 0, sweep != 0);
                    cx = ax; cy = ay;
                    prevCmd = cmd;
                    break;
                }
                case 'Z':
                {
                    path.Close();
                    prevCmd = cmd;
                    break;
                }
                default:
                    return path;
            }
        }

        return path;
    }

    // ===================== 词法分析 =====================

    private readonly record struct Token(bool IsCommand, char Command, float Number);

    private static List<Token> Tokenize(string d)
    {
        var list = new List<Token>(d.Length);
        var i = 0;
        while (i < d.Length)
        {
            var c = d[i];
            if (char.IsWhiteSpace(c) || c == ',')
            {
                i++;
                continue;
            }

            if (char.IsLetter(c))
            {
                list.Add(new Token(true, c, 0f));
                i++;
                continue;
            }

            var start = i;
            if (c is '+' or '-') i++;

            // 按 SVG 数字文法扫描：小数点只能在尚未出现过时接受。
            // 这样 ".083-.094" 会正确切成 ".083" 与 "-.094"（dismiss 路径里就有这种连写），
            // 而 "1.046l-9" 里的 'l' 也不会被误当作数字的一部分。
            var seenDot = false;
            while (i < d.Length)
            {
                var ch = d[i];
                if (char.IsDigit(ch))
                {
                    i++;
                }
                else if (ch == '.' && !seenDot)
                {
                    seenDot = true;
                    i++;
                }
                else
                {
                    break;
                }
            }

            // 指数标记必须紧跟数字，否则它其实是下一个命令字母
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
            {
                var j = i + 1;
                if (j < d.Length && (d[j] == '+' || d[j] == '-')) j++;
                if (j < d.Length && char.IsDigit(d[j]))
                {
                    i = j;
                    while (i < d.Length && char.IsDigit(d[i])) i++;
                }
            }

            var span = d.AsSpan(start, i - start);
            var value = float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            list.Add(new Token(false, '\0', value));
        }
        return list;
    }

    private static bool TryNum(List<Token> tokens, ref int i, out float a)
    {
        a = 0f;
        if (i >= tokens.Count || tokens[i].IsCommand) return false;
        a = tokens[i].Number;
        i++;
        return true;
    }

    private static bool TryNum(List<Token> t, ref int i, out float a, out float b)
    {
        b = 0f;
        return TryNum(t, ref i, out a) && TryNum(t, ref i, out b);
    }

    private static bool TryNum(List<Token> t, ref int i, out float a, out float b, out float c)
    {
        c = 0f;
        return TryNum(t, ref i, out a, out b) && TryNum(t, ref i, out c);
    }

    private static bool TryNum(List<Token> t, ref int i, out float a, out float b, out float c, out float d)
    {
        d = 0f;
        return TryNum(t, ref i, out a, out b, out c) && TryNum(t, ref i, out d);
    }

    private static bool TryNum(List<Token> t, ref int i, out float a, out float b, out float c, out float d, out float e)
    {
        e = 0f;
        return TryNum(t, ref i, out a, out b, out c, out d) && TryNum(t, ref i, out e);
    }

    private static bool TryNum(List<Token> t, ref int i, out float a, out float b, out float c, out float d, out float e, out float f)
    {
        f = 0f;
        return TryNum(t, ref i, out a, out b, out c, out d, out e) && TryNum(t, ref i, out f);
    }

    private static bool TryNum(List<Token> t, ref int i,
        out float a, out float b, out float c, out float d, out float e, out float f, out float g)
    {
        g = 0f;
        return TryNum(t, ref i, out a, out b, out c, out d, out e, out f) && TryNum(t, ref i, out g);
    }

    // ===================== 圆弧 → 三次贝塞尔 =====================

    /// <summary>
    /// 按 SVG 规范（F.6.5 端点参数转中心参数）把椭圆弧转成若干三次贝塞尔段。
    /// </summary>
    private static void AppendArc(
        PathF path,
        float x1, float y1, float x2, float y2,
        float rx, float ry,
        float xAxisRotationDeg, bool largeArc, bool sweep)
    {
        if (rx <= 0f || ry <= 0f || (MathF.Abs(x1 - x2) < 1e-6f && MathF.Abs(y1 - y2) < 1e-6f))
        {
            // 半径退化为 0，或起终点重合：按直线处理。
            // 图标路径里存在 "a1 1 0 0 0 0 0" 这类退化圆弧，若当成真实圆弧描边会画出一条多余的线。
            path.LineTo(x2, y2);
            return;
        }

        var phi = xAxisRotationDeg * MathF.PI / 180f;
        var cosPhi = MathF.Cos(phi);
        var sinPhi = MathF.Sin(phi);

        var dx2 = (x1 - x2) / 2f;
        var dy2 = (y1 - y2) / 2f;
        var x1p = cosPhi * dx2 + sinPhi * dy2;
        var y1p = -sinPhi * dx2 + cosPhi * dy2;

        // 半径过小时按规范放大
        var lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lambda > 1f)
        {
            var s = MathF.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        var sign = largeArc == sweep ? -1f : 1f;
        var num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        var den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var coef = den <= 0f ? 0f : sign * MathF.Sqrt(MathF.Max(0f, num / den));

        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;

        var cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2f;
        var cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2f;

        var theta1 = MathF.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var theta2 = MathF.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var delta = theta2 - theta1;

        if (!sweep && delta > 0f) delta -= 2f * MathF.PI;
        else if (sweep && delta < 0f) delta += 2f * MathF.PI;

        // 每段不超过 90°，保证精度
        var segments = (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 2f));
        if (segments <= 0) segments = 1;

        var step = delta / segments;
        var t = 4f / 3f * MathF.Tan(step / 4f);

        var cosT1 = MathF.Cos(theta1);
        var sinT1 = MathF.Sin(theta1);

        for (var seg = 0; seg < segments; seg++)
        {
            var cosT2 = MathF.Cos(theta1 + step);
            var sinT2 = MathF.Sin(theta1 + step);

            // 起点（第一段用真实起点，避免累积误差）
            var p1x = seg == 0 ? x1 : cx + rx * cosPhi * cosT1 - ry * sinPhi * sinT1;
            var p1y = seg == 0 ? y1 : cy + rx * sinPhi * cosT1 + ry * cosPhi * sinT1;

            var p2x = cx + rx * cosPhi * cosT2 - ry * sinPhi * sinT2;
            var p2y = cy + rx * sinPhi * cosT2 + ry * cosPhi * sinT2;

            var d1x = -rx * cosPhi * sinT1 - ry * sinPhi * cosT1;
            var d1y = -rx * sinPhi * sinT1 + ry * cosPhi * cosT1;
            var d2x = -rx * cosPhi * sinT2 - ry * sinPhi * cosT2;
            var d2y = -rx * sinPhi * sinT2 + ry * cosPhi * cosT2;

            var c1x = p1x + t * d1x;
            var c1y = p1y + t * d1y;
            var c2x = p2x - t * d2x;
            var c2y = p2y - t * d2y;

            path.CurveTo(c1x, c1y, c2x, c2y, p2x, p2y);

            theta1 += step;
            cosT1 = MathF.Cos(theta1);
            sinT1 = MathF.Sin(theta1);
        }
    }
}
