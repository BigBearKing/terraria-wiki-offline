using Microsoft.AspNetCore.Components.Web;

namespace Terraria_Wiki.Components.Controls;

/// <summary>
/// 可点容器的键盘激活约定。div / li 这类"假按钮"不会因为 Enter / Space 触发 click
/// （原生 &lt;button&gt; 才自带这个行为），所以各处在 @onkeydown 里显式调用这里。
///
/// 为什么单独放一个类：判定规则必须只有一份 —— 组件（RowButton）与外壳里几个"带自己形状的
/// 可点容器"（标签项、更多面板的标签项、搜索结果项、顶栏标题）都用它，避免各写一套 Enter/Space。
/// </summary>
public static class KeyboardActivation
{
    /// <summary>Enter / Space 视为激活键（空间键的旧名 Spacebar 一并认下）。</summary>
    public static bool IsActivateKey(KeyboardEventArgs args) =>
        args.Key is "Enter" or " " or "Spacebar";

    /// <summary>是激活键才执行动作，否则什么都不做。</summary>
    public static Task InvokeIfActivateKey(KeyboardEventArgs args, Func<Task> action) =>
        IsActivateKey(args) ? action() : Task.CompletedTask;
}
