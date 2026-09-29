using Microsoft.Maui.Graphics;

namespace Terraria_Wiki.Components.Shared;

/// <summary>
/// 页内搜索弹窗的自绘实现。
///
/// 为什么自绘：原生 XAML 控件走 WinUI 合成层，窗口尺寸变化时重绘节奏由系统合成器驱动，
/// 与窗口重排不同步（表现为"被挤出窗口再弹回来"）。画布绘制由我们在 UI 线程主动
/// Invalidate 触发，可随窗口变化当帧刷新；hover / 按下 / 开关动画也全部在画布上完成，
/// 不依赖 MAUI 的变换属性。
///
/// 图标与配色 1:1 取自 Icon.razor 与 variables.css：
///   text-search / arrow(rotate ±90) / dismiss 三个图标直接解析 svg 的 d 属性绘制。
/// </summary>
public sealed class FindPanelDrawable : IDrawable
{
    // ===== 与网页样式表一一对应的几何常量（改这里前先看 FindInPage.razor.css） =====
    /// <summary>.find-in-page min-height: 52px</summary>
    public const float PanelHeight = 52f;
    /// <summary>.find-in-page border-radius: 12px</summary>
    public const float PanelRadius = 12f;
    /// <summary>.find-in-page padding: 0 8px 0 12px → 左内边距</summary>
    public const float PadLeft = 12f;
    /// <summary>.find-in-page padding: 0 8px 0 12px → 右内边距</summary>
    public const float PadRight = 8f;
    /// <summary>.find-in-page gap: 6px</summary>
    public const float Gap = 6f;

    /// <summary>.search-wrapper width / height</summary>
    public const float SearchWidthMax = 260f;
    public const float SearchWidthIdeal = 260f;
    /// <summary>
    /// 搜索框收缩下限。
    /// 取值由"面板必须能塞进最窄的窗口"反推：PadLeft(12) + floor + Gap(6) + 按钮块(112) + PadRight(8)
    /// 必须不高于最窄窗口减去外右距(10)。
    /// </summary>
    public const float SearchWidthFloor = 96f;
    public const float SearchHeight = 36f;

    /// <summary>.action-btn 36x36 / border-radius 8px</summary>
    public const float BtnSize = 36f;
    /// <summary>.search-actions gap: 2px</summary>
    public const float BtnGap = 2f;
    public const float BtnRadius = 8f;

    /// <summary>.search-count min-width: 42px</summary>
    public const float CountWidth = 42f;

    // ===== 面板与窗口边缘的距离（外部边距，对应 .find-in-page 的定位） =====
    /// <summary>right: calc(10px + var(--safe-area-right)) → 面板右缘距窗口右边 10px</summary>
    public const float PanelMarginRight = 10f;
    /// <summary>top: calc(var(--header-height) + 4px) → 面板顶距 header 底部 4px</summary>
    public const float PanelMarginTop = 4f;

    /// <summary>按钮块占用（三个按钮 + 内部间隔），不含右边距。</summary>
    private const float ButtonsBlock = BtnSize * 3 + BtnGap * 2;

    /// <summary>
    /// 放大镜图标。网页原版是 .find-icon 16x16、left: 12px；
    /// 这里按需求略放大，并让文字与图标之间留出更明显的间距。
    /// </summary>
    public const float IconSizeSearch = 18f;
    /// <summary>图标左缘距搜索框左缘（网页 left: 12px）</summary>
    public const float IconInsetSearch = 12f;
    /// <summary>文字左缘距搜索框左缘：图标右缘 12+18=30，再留 8 的间距</summary>
    public const float TextInsetSearch = 38f;

    /// <summary>按钮内图标尺寸（网页 .action-btn 里 Icon Size=18）</summary>
    public const float IconSizeAction = 18f;

    /// <summary>搜索框内文字字号（网页 .9rem=14.4，按需求调小）</summary>
    public const float FontSizeInput = 13f;
    /// <summary>.search-count font-size: 0.8rem</summary>
    public const float FontSizeCount = 12.8f;

    /// <summary>
    /// 面板理想宽度。
    /// 网页版 flex 展开式（有计数时）：
    ///   PadLeft(12) + search(260) + gap(6) + count(42) + gap(6) + buttons(36*3+2*2=112) + PadRight(8) = 446
    /// 计数为空时网页的 count 元素宽度塌缩为 0，面板变窄；这里沿用同一行为。
    /// </summary>
    public static float GetPanelWidthIdeal(bool hasCount, bool hasCountText = true)
        => PadLeft + SearchWidthMax + Gap
           + (hasCount ? CountWidth + Gap : 0f)
           + ButtonsBlock + PadRight;

    public const float PanelWidthIdeal = PadLeft + SearchWidthMax + Gap + CountWidth + Gap
                                         + ButtonsBlock + PadRight;

    /// <summary>面板最小宽度：搜索框收缩到下限、计数收起后仍要放得下。</summary>
    public const float PanelWidthMin = PadLeft + SearchWidthFloor + Gap + ButtonsBlock + PadRight;

    // ===== 配色（variables.css） =====
    private static readonly Color LightPanel = Color.FromArgb("#FFFFFF");
    private static readonly Color LightPill = Color.FromArgb("#F2F3F5");
    private static readonly Color LightText = Color.FromArgb("#111827");
    private static readonly Color LightMuted = Color.FromArgb("#6B7280");
    private static readonly Color LightHover = Color.FromArgb("#EDEFF2");
    private static readonly Color LightPress = Color.FromArgb("#E2E5EA");
    private static readonly Color LightAccent = Color.FromArgb("#0088FF");

    private static readonly Color DarkPanel = Color.FromArgb("#1A1B1E");
    private static readonly Color DarkPill = Color.FromArgb("#2A2D33");
    private static readonly Color DarkText = Color.FromArgb("#F9FAFB");
    private static readonly Color DarkMuted = Color.FromArgb("#9CA3AF");
    private static readonly Color DarkHover = Color.FromArgb("#33383F");
    private static readonly Color DarkPress = Color.FromArgb("#414751");
    private static readonly Color DarkAccent = Color.FromArgb("#0091FF");

    /// <summary>用于文本宽度测量，必须与绘制时 canvas.Font 一致。</summary>
    private static readonly IFont MeasureFont = Microsoft.Maui.Graphics.Font.Default;

    // ===== 运行时状态 =====
    public bool IsDarkTheme { get; set; }
    public bool IsFocused { get; set; }
    public string CountText { get; set; } = string.Empty;

    /// <summary>
    /// 输入文字由画布绘制（原生 Entry 保持透明，只作为输入接收器）。
    /// 这三个属性任一变化都会让文本像素测量缓存失效，下一个绘制帧重算一次，之后复用。
    /// </summary>
    private string _inputText = string.Empty;
    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText == value) return;
            _inputText = value ?? string.Empty;
            _inputMeasured = false;
        }
    }

    private int _cursorPosition;
    public int CursorPosition
    {
        get => _cursorPosition;
        set
        {
            if (_cursorPosition == value) return;
            _cursorPosition = value;
            _inputMeasured = false;
        }
    }

    private int _selectionLength;
    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            if (_selectionLength == value) return;
            _selectionLength = value;
            _inputMeasured = false;
        }
    }

    public string Placeholder { get; set; } = string.Empty;
    public bool CaretVisible { get; set; } = true;

    /// <summary>
    /// 原生控件报告的真实选区起点（SelectionStart）。
    /// MAUI 的 Entry 只暴露 CursorPosition 与 SelectionLength，没有方向信息，
    /// 因此必须由宿主从平台控件上直接读取，不能靠这两个值推算。
    /// </summary>
    public int NativeSelectionStart { get; set; }

    // 文本像素测量缓存：GetStringSize 走平台文本测量，逐帧调用会导致拖选卡顿
    private bool _inputMeasured;
    private int _measuredBeforeLength = -1;
    private int _measuredSelectionLength;
    private float _measuredSelectionStartX;
    private float _measuredSelectionWidth;
    private float _measuredCaretX;

    public int HoveredButton { get; set; } = -1;
    /// <summary>按下的按钮索引。</summary>
    public int PressedButton { get; set; } = -1;

    /// <summary>开关动画进度：0 = 完全收起，1 = 完全展开。</summary>
    public float Progress { get; set; } = 1f;

    public float PanelWidth { get; private set; } = PanelWidthIdeal;

    private RectF _prevBtn, _nextBtn, _closeBtn;
    private RectF _searchPill;

    public bool ShowCount { get; private set; } = true;
    public float TextInset { get; private set; } = TextInsetSearch;

    private Color PanelBg => IsDarkTheme ? DarkPanel : LightPanel;
    private Color PillBg => IsDarkTheme ? DarkPill : LightPill;
    private Color TextColor => IsDarkTheme ? DarkText : LightText;
    private Color MutedColor => IsDarkTheme ? DarkMuted : LightMuted;
    private Color HoverColor => IsDarkTheme ? DarkHover : LightHover;
    private Color PressColor => IsDarkTheme ? DarkPress : LightPress;
    private Color AccentColor => IsDarkTheme ? DarkAccent : LightAccent;

    // ===== 布局 =====

    public readonly record struct PanelGeometry(
        float PanelWidth, float SearchX, float SearchY, float SearchWidth, float SearchHeight,
        float TextInset, bool ShowCount, bool ShowIcon);

    /// <summary>
    /// 计算面板宽度。
    /// 硬约束：**绝不超过可用宽度**，否则面板会伸出窗口（左缘变负），
    /// 表现为"左右边距不一样"。可用宽度 = WebView 宽 − 外右距。
    /// </summary>
    public static float ResolvePanelWidth(double availableWidth, float idealWidth = PanelWidthIdeal)
    {
        var w = (float)availableWidth;
        if (w <= 0) return idealWidth;

        var target = MathF.Min(idealWidth, w);
        // 正常情况不低于最小宽度；但若窗口比最小宽度还窄，只能继续缩，不能溢出
        return MathF.Min(MathF.Max(PanelWidthMin, target), w);
    }

    /// <summary>
    /// 按给定面板宽度解算布局。绘制与宿主摆放输入接收器都走这里，保证两者永远一致。
    ///
    /// 硬约束（曾经违反过，导致右侧被裁、视觉上左边距显大）：
    ///   左内边距 + 搜索框 + 间隔 + [计数 + 间隔] + 按钮块 + 右内边距 == 面板宽度
    ///
    /// 让位顺序：搜索框从 260 收到下限 120 → 仍不够才收起计数 → 再不够收起图标。
    /// 计数为空时按网页行为塌缩为 0（面板随之变窄）。
    /// </summary>
    public PanelGeometry LayoutFor(float panelWidth)
    {
        PanelWidth = panelWidth > 0 ? panelWidth : PanelWidthIdeal;

        var baseFixed = PadLeft + PadRight + ButtonsBlock + Gap;
        var spare = PanelWidth - baseFixed;
        if (spare < 0f) spare = 0f;

        var hasCountText = !string.IsNullOrEmpty(CountText);
        var countWithGap = CountWidth + Gap;

        bool showCount;
        bool showIcon;
        float searchW;

        if (!hasCountText)
        {
            searchW = spare;
            showCount = false;
        }
        else if (spare >= SearchWidthIdeal + countWithGap)
        {
            // 空间充足：搜索框拿满，计数保留完整宽度
            searchW = SearchWidthIdeal;
            showCount = true;
        }
        else if (spare >= SearchWidthFloor + countWithGap)
        {
            // 空间偏紧：优先压缩搜索框，计数仍然保留
            searchW = spare - countWithGap;
            showCount = true;
        }
        else
        {
            // 空间实在不够，才收起计数，把宽度让给搜索框
            searchW = spare;
            showCount = false;
        }

        searchW = MathF.Min(searchW, SearchWidthMax);
        showIcon = searchW >= IconInsetSearch + IconSizeSearch + 6f;
        ShowCount = showCount;
        TextInset = showIcon ? TextInsetSearch : 10f;

        return new PanelGeometry(
            PanelWidth, PadLeft, (PanelHeight - SearchHeight) / 2f,
            searchW, SearchHeight, TextInset, showCount, showIcon);
    }

    // ===== 绘制 =====

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var g = LayoutFor(dirtyRect.Width > 0 ? dirtyRect.Width : PanelWidthIdeal);

        var p = Math.Clamp(Progress, 0f, 1f);
        if (p < 1f)
        {
            // 开关动画：以右上角为轴做轻微缩放 + 淡入，与网页弹窗观感接近
            var scale = 0.90f + 0.10f * p;
            var cx = g.PanelWidth;
            const float cy = 0f;
            canvas.SaveState();
            canvas.Alpha = p;
            canvas.Translate(cx, cy);
            canvas.Scale(scale, scale);
            canvas.Translate(-cx, -cy);
        }

        canvas.FillColor = PanelBg;
        canvas.FillRoundedRectangle(0, 0, g.PanelWidth, PanelHeight, PanelRadius);

        _searchPill = new RectF(g.SearchX, g.SearchY, g.SearchWidth, g.SearchHeight);

        canvas.FillColor = IsFocused ? PanelBg : PillBg;
        canvas.FillRoundedRectangle(_searchPill.X, _searchPill.Y, _searchPill.Width, _searchPill.Height,
            SearchHeight / 2f);
        if (IsFocused)
        {
            canvas.StrokeColor = AccentColor;
            canvas.StrokeSize = 1.4f;
            canvas.DrawRoundedRectangle(_searchPill.X + 0.7f, _searchPill.Y + 0.7f,
                _searchPill.Width - 1.4f, _searchPill.Height - 1.4f, SearchHeight / 2f - 0.7f);
        }

        if (g.ShowIcon)
            DrawSearchIcon(canvas, _searchPill);

        DrawInputText(canvas, g);

        if (g.ShowCount && !string.IsNullOrEmpty(CountText))
            DrawCount(canvas, _searchPill.X + _searchPill.Width + Gap);

        // 三个控制按钮自右向左排布：右侧留 PadRight(8)，与网页的 padding-right 一致
        var right = g.PanelWidth - PadRight;
        _closeBtn = new RectF(right - BtnSize, (PanelHeight - BtnSize) / 2f, BtnSize, BtnSize);
        _nextBtn = new RectF(_closeBtn.X - BtnGap - BtnSize, _closeBtn.Y, BtnSize, BtnSize);
        _prevBtn = new RectF(_nextBtn.X - BtnGap - BtnSize, _closeBtn.Y, BtnSize, BtnSize);

        DrawActionButton(canvas, _prevBtn, 0, up: true);
        DrawActionButton(canvas, _nextBtn, 1, up: false);
        DrawActionButton(canvas, _closeBtn, 2, up: false, isClose: true);

        if (p < 1f) canvas.RestoreState();
    }

    /// <summary>
    /// 图标尺寸与 viewBox 的换算：Icon.razor 里 svg width=Size、viewBox="0 0 24 24"，
    /// 所以墨迹缩放 = Size / 24，把 viewBox 单位的路径缩放到实际图标尺寸。
    /// </summary>
    private static float IconScale(float iconSize) => iconSize / 24f;

    private void DrawSearchIcon(ICanvas canvas, RectF pill)
    {
        var scale = IconScale(IconSizeSearch);
        var boxX = pill.X + IconInsetSearch;
        var boxY = pill.Y + (pill.Height - IconSizeSearch) / 2f;

        canvas.SaveState();
        canvas.Translate(boxX, boxY);
        canvas.Scale(scale, scale);
        canvas.FillColor = IsFocused ? AccentColor : MutedColor;
        // 只填充、不描边：Icon.razor 的 stroke-width="0.2px" 在缩放后的坐标系里会被放大成可见的线
        canvas.FillPath(FindIconPaths.Search());
        canvas.RestoreState();
    }


    /// <summary>
    /// 画布绘制输入文字、占位符、选区高亮与光标。
    ///
    /// 性能要点：文字像素宽度用 GetStringSize 计算（走平台文本测量，较贵），
    /// 因此只在"选区起点/长度"变化时测一次并缓存；稳态下（含光标闪烁帧）零测量。
    /// </summary>
    private void DrawInputText(ICanvas canvas, PanelGeometry g)
    {
        var textLeft = g.SearchX + g.TextInset;
        var textRight = _searchPill.X + _searchPill.Width - 10f;
        var textWidth = textRight - textLeft;
        if (textWidth <= 4f) return;

        canvas.SaveState();
        canvas.ClipRectangle(textLeft, _searchPill.Y, textWidth, _searchPill.Height);

        canvas.FontSize = FontSizeInput;
        canvas.Font = MeasureFont;

        if (string.IsNullOrEmpty(InputText))
        {
            canvas.FontColor = MutedColor;
            canvas.DrawString(Placeholder ?? string.Empty,
                textLeft, _searchPill.Y, textWidth, _searchPill.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);

            // 空输入框同样要显示光标（这里原来直接 return，漏掉了光标绘制）
            if (IsFocused && CaretVisible)
            {
                canvas.FillColor = TextColor;
                canvas.FillRoundedRectangle(textLeft + 0.5f, _searchPill.Y + 8f,
                    1.4f, _searchPill.Height - 16f, 0.7f);
            }

            canvas.RestoreState();
            return;
        }

        // 选区起点用原生控件报告的真实值（SelectionStart）。
        // MAUI 的 Entry 不暴露它，而用 CursorPosition/SelectionLength 硬推
        // 在反向拖选时必然错一位（选中 "e" 会高亮成 "d"）。
        var selStart = Math.Clamp(NativeSelectionStart, 0, InputText.Length);
        var selLen = Math.Clamp(Math.Abs(SelectionLength), 0, InputText.Length - selStart);
        var caretIndex = Math.Clamp(CursorPosition, 0, InputText.Length);

        if (!_inputMeasured || _measuredBeforeLength != selStart || _measuredSelectionLength != selLen)
        {
            _measuredBeforeLength = selStart;
            _measuredSelectionLength = selLen;
            _measuredSelectionStartX = selStart == 0
                ? 0f
                : canvas.GetStringSize(InputText[..selStart], MeasureFont, FontSizeInput).Width;
            _measuredSelectionWidth = selLen == 0
                ? 0f
                : canvas.GetStringSize(InputText.Substring(selStart, selLen), MeasureFont, FontSizeInput).Width;
            _measuredCaretX = caretIndex == 0
                ? 0f
                : canvas.GetStringSize(InputText[..caretIndex], MeasureFont, FontSizeInput).Width;
            _inputMeasured = true;
        }

        if (selLen > 0)
        {
            canvas.FillColor = AccentColor;
            canvas.Alpha = 0.28f;
            canvas.FillRoundedRectangle(textLeft + _measuredSelectionStartX - 1f, _searchPill.Y + 6f,
                _measuredSelectionWidth + 2f, _searchPill.Height - 12f, 3f);
            canvas.Alpha = 1f;
        }

        canvas.FontColor = TextColor;
        canvas.DrawString(InputText,
            textLeft, _searchPill.Y, textWidth, _searchPill.Height,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        if (IsFocused && CaretVisible && SelectionLength == 0)
        {
            canvas.FillColor = TextColor;
            canvas.FillRoundedRectangle(textLeft + _measuredCaretX + 0.5f, _searchPill.Y + 8f,
                1.4f, _searchPill.Height - 16f, 0.7f);
        }

        canvas.RestoreState();
    }

    private void DrawCount(ICanvas canvas, float x)
    {
        canvas.Font = MeasureFont;
        canvas.FontColor = MutedColor;
        canvas.FontSize = FontSizeCount;
        canvas.DrawString(CountText, x, 0, CountWidth, PanelHeight,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    /// <summary>
    /// 动作按钮：hover / 按下有圆角底色反馈（对应网页 .action-btn 的 :hover / :active），
    /// 图标用 Icon.razor 的 arrow（按 CSS 的 rotate ±90 换向）与 dismiss 路径绘制。
    /// </summary>
    private void DrawActionButton(ICanvas canvas, RectF btn, int index, bool up, bool isClose = false)
    {
        if (PressedButton == index)
        {
            canvas.FillColor = PressColor;
            canvas.FillRoundedRectangle(btn.X, btn.Y, btn.Width, btn.Height, BtnRadius);
        }
        else if (HoveredButton == index)
        {
            canvas.FillColor = HoverColor;
            canvas.FillRoundedRectangle(btn.X, btn.Y, btn.Width, btn.Height, BtnRadius);
        }

        var scale = IconScale(IconSizeAction);
        var boxX = btn.X + (btn.Width - IconSizeAction) / 2f;
        var boxY = btn.Y + (btn.Height - IconSizeAction) / 2f;

        var path = isClose ? FindIconPaths.Dismiss() : FindIconPaths.Arrow(up);

        canvas.SaveState();
        canvas.Translate(boxX, boxY);
        canvas.Scale(scale, scale);
        canvas.FillColor = MutedColor;
        // 同样只填充不描边
        canvas.FillPath(path);
        canvas.RestoreState();
    }

    /// <summary>命中测试：0=上一个，1=下一个，2=关闭，-1=未命中。</summary>
    public int HitTestButton(PointF point)
    {
        if (Hit(_prevBtn, point)) return 0;
        if (Hit(_nextBtn, point)) return 1;
        if (Hit(_closeBtn, point)) return 2;
        return -1;
    }

    /// <summary>命中区域比视觉尺寸略大，方便点击（网页按钮也是 36px 的可点区域）。</summary>
    private static bool Hit(RectF r, PointF p)
    {
        const float slop = 4f;
        return p.X >= r.X - slop && p.X <= r.Right + slop && p.Y >= r.Y - slop && p.Y <= r.Bottom + slop;
    }
}

