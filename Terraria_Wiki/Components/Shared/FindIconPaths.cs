using System.Numerics;
using Microsoft.Maui.Graphics;

namespace Terraria_Wiki.Components.Shared;

/// <summary>
/// 页内搜索自绘弹窗用到的图标路径。
///
/// 路径数据原样取自 Icon.razor（search / arrow / dismiss），
/// 通过 <see cref="SvgPathParser"/> 解析成 PathF，再按 Icon.razor 的用法做同样的变换：
///   - 图标按 Size/24 缩放（svg viewBox 为 0 0 24 24）；
///   - search 带 transform="translate(0.249 0.249)"；
///   - arrow 带 transform="translate(4.247 0)"，并配合 CSS rotate(90deg)/rotate(-90deg) 换成上下方向。
/// </summary>
internal static class FindIconPaths
{
    /// <summary>Icon.razor 里 search 的 path d（普通放大镜）。</summary>
    private const string SearchD =
        "M10 2.75a7.25 7.25 0 0 1 5.63 11.819l4.9 4.9a.75.75 0 0 1-.976 1.134l-.084-.073-4.901-4.9" +
        "A7.25 7.25 0 1 1 10 2.75Zm0 1.5a5.75 5.75 0 1 0 0 11.5 5.75 5.75 0 0 0 0-11.5Z";

    /// <summary>Icon.razor 里 arrow 的 path d。</summary>
    private const string ArrowD =
        "m4.296 12 8.492-8.727a.75.75 0 1 0-1.075-1.046l-9 9.25a.75.75 0 0 0 0 1.046l9 9.25a.75.75 0 1 0 " +
        "1.075-1.046L4.295 12Z";

    /// <summary>Icon.razor 里 dismiss 的 path d。</summary>
    private const string DismissD =
        "m4.21 4.387.083-.094a1 1 0 0 1 1.32-.083l.094.083L12 10.585l6.293-6.292a1 1 0 1 1 1.414 1.414" +
        "L13.415 12l6.292 6.293a1 1 0 0 1 .083 1.32l-.083.094a1 1 0 0 1-1.32.083l-.094-.083L12 13.415" +
        "l-6.293 6.292a1 1 0 0 1-1.414-1.414L10.585 12 4.293 5.707a1 1 0 0 1-.083-1.32l.083-.094-.083.094Z";

    private static readonly Dictionary<string, PathF> Cache = new();

    /// <summary>search 图标（viewBox 单位，含 translate(0.249 0.249)）。</summary>
    public static PathF Search() => Cached("search", () =>
        SvgPathParser.Parse(SearchD, 1f, offsetX: 0.249f, offsetY: 0.249f));

    /// <summary>关闭叉（viewBox 单位）。</summary>
    public static PathF Dismiss() => Cached("dismiss", () => SvgPathParser.Parse(DismissD, 1f));

    /// <summary>
    /// 上下箭头（viewBox 单位）。对应网页里 icon 上的 rotate(90deg)/(−90deg)：
    /// previous 朝上、next 朝下。旋转轴取 viewBox 中心 (12,12)，与 CSS transform-origin 默认值一致。
    /// </summary>
    public static PathF Arrow(bool up)
    {
        var key = up ? "arrow-up" : "arrow-down";
        return Cached(key, () =>
        {
            var path = SvgPathParser.Parse(ArrowD, 1f, offsetX: 4.247f);
            var angle = up ? MathF.PI / 2f : -MathF.PI / 2f;
            path.Transform(BuildRotation(angle, 12f, 12f));
            return path;
        });
    }

    /// <summary>构造绕 (cx,cy) 旋转 angle 弧度的矩阵（供 PathF.Transform 使用）。</summary>
    private static Matrix3x2 BuildRotation(float angle, float cx, float cy)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        // 结果 = 平移(+c) · 旋转 · 平移(−c)
        var m = Matrix3x2.CreateTranslation(-cx, -cy);
        m *= new Matrix3x2(cos, sin, -sin, cos, 0f, 0f);
        m *= Matrix3x2.CreateTranslation(cx, cy);
        return m;
    }

    private static PathF Cached(string key, Func<PathF> factory)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var path = factory();
        Cache[key] = path;
        return path;
    }
}
