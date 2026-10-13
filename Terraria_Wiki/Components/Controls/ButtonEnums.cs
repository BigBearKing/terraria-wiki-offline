namespace Terraria_Wiki.Components.Controls;

/// <summary>
/// <see cref="ActionButton"/> 的几何档位。四档都与迁移前页面里的实际取值一一对应，
/// 圆角已按「8 / 12 / 16 / 99」的统一刻度收敛。
/// </summary>
public enum ButtonSize
{
    /// <summary>6×12、圆角 8、0.9rem —— 设置页的 .setting-btn。</summary>
    Small,

    /// <summary>6×16、圆角 99、0.85rem/500 —— 数据页的语言筛选胶囊 .lang-btn。</summary>
    Pill,

    /// <summary>4×12、圆角 99、0.8rem + 1px 描边 —— 收藏卡里的"取消收藏" .remove-btn。</summary>
    PillOutline,

    /// <summary>12、圆角 12、1rem/500、可撑满 —— 数据详情页的语义色大按钮 .primary-btn。</summary>
    Large,

    /// <summary>无内边距无圆角、纯文字 —— 历史页头部的"清空历史" .clear-btn。</summary>
    Text
}

/// <summary>
/// <see cref="ActionButton"/> 的配色档位。悬停/按下的具体色由 (Size, Tone) 组合决定，
/// 见 ActionButton.razor.css —— 几何与配色分开，避免两两组合写成 20 条规则。
/// </summary>
public enum ButtonTone
{
    /// <summary>中性：设置页那种 --hover-bg 底、正文色字。</summary>
    Neutral,

    /// <summary>蓝实底 + 白字：语言筛选里"选中"的那颗胶囊。</summary>
    BlueSolid,

    /// <summary>灰描边、悬停转红：右上角的删除类小按钮。</summary>
    Danger,

    /// <summary>浅蓝底 + 蓝字（--blue-tertiary / --blue-primary）。</summary>
    Blue,

    /// <summary>浅绿底 + 绿字，表示成功/已安装。</summary>
    Green,

    /// <summary>浅红底 + 红字，表示删除/危险。</summary>
    Red,

    /// <summary>纯文字、蓝色、无悬停底色。</summary>
    Link
}

/// <summary>
/// <see cref="IconButton"/> 的尺寸档位。取值都是迁移前各处的实际尺寸（没有收敛尺寸，只收敛了圆角）。
/// </summary>
public enum IconButtonSize
{
    /// <summary>16×16 —— TabBar 的标签关闭键。</summary>
    Square16,

    /// <summary>24×24 —— MoreList 面板里的标签关闭键。</summary>
    Square24,

    /// <summary>26×26 —— TabBar 的"新建标签"。</summary>
    Square26,

    /// <summary>28×28 —— MoreList 面板里的"新建标签"。</summary>
    Square28,

    /// <summary>32×32 —— LogPanel 的关闭键。</summary>
    Square32,

    /// <summary>36×36 —— 顶栏与 MoreList 的方形图标键。</summary>
    Square36,

    /// <summary>42×42 —— 窄屏下 MoreList 的方形图标键。</summary>
    Square42,

    /// <summary>32×32 圆形 —— 页面标题头的返回箭头。</summary>
    Round32,

    /// <summary>44×32 直角 —— 悬浮小条上的那段（无缝紧排，唯一保留直角的一档）。</summary>
    Bar,

    /// <summary>高 36、左右各 8px 内边距、图标 + 文字 —— MoreList 宽屏那一排。</summary>
    Wide36
}

/// <summary><see cref="IconButton"/> 的配色档位。</summary>
public enum IconButtonTone
{
    /// <summary>--text-secondary 图标，悬停转正文色。</summary>
    Default,

    /// <summary>正文色图标，悬停不变色（标题头的返回箭头、标签栏的"新建标签"）。</summary>
    Primary,

    /// <summary>悬停转红（悬浮条的"退出悬浮窗"）。</summary>
    Danger
}

/// <summary>悬停与按下反馈。<see cref="None"/> 用于"出现底色反而脏"的按钮（顶栏 logo 菜单键）。</summary>
public enum ButtonFeedback
{
    /// <summary>默认：悬停 --hover-bg / 按下 --pressed-bg。</summary>
    Default,

    /// <summary>不出现底色变化（仍保留键盘焦点环）。</summary>
    None
}

/// <summary>
/// <see cref="RowButton"/> 的形态档位。原先 5 种行卡（.manage-card / .license-card / .wiki-card /
/// .list-item / .history-item）收敛成这两档：卡片 16px 内边距 + 16 圆角，行 12px 内边距 + 12 圆角。
/// </summary>
public enum RowVariant
{
    /// <summary>带底色（--bg-color-secondary）的卡片：数据页 / 许可列表。</summary>
    Card,

    /// <summary>无底色的列表行：所有页面 / 收藏 / 历史（悬停才出现底色）。</summary>
    Row
}

/// <summary><see cref="RowButton"/> 的主轴排布。</summary>
public enum RowJustify
{
    /// <summary>左对齐（默认）。</summary>
    Start,

    /// <summary>两端对齐：左边标题、右边时间那类行。</summary>
    SpaceBetween
}
