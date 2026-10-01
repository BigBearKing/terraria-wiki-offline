#if WINDOWS
using System.Runtime.InteropServices;

namespace Terraria_Wiki;

/// <summary>
/// 悬浮窗的右键菜单（纯 Win32 弹出菜单，球态与托盘图标共用一套文案/命令）。
///
/// 文案随状态变化：
///   · 球态（主窗口藏着）：展开小窗 / 退出悬浮窗模式
///   · 小条态：          收起为图标球 / 退出悬浮窗模式
///
/// ★ 调用方约定：<b>只在菜单弹出时才读状态、命令通过 PostMessage 投递回调用方自己的窗口</b>。
///   菜单回调里直接做"展开/收起/退出"很容易踩到自毁竞态（收起会销毁调用方窗口，
///   而它的 WndProc 还在栈上），所以这里只回传命令，由调用方在 WM_APP 消息里执行。
/// </summary>
internal static class Win32Menu
{
    /// <summary>展开小窗 / 收起为图标球。</summary>
    public const uint CommandToggle = 1;

    /// <summary>退出悬浮窗模式（还原主窗口，撤掉球与托盘图标）。</summary>
    public const uint CommandExitFloating = 2;

    /// <summary>
    /// 在鼠标位置弹出菜单。<paramref name="barActive"/> 为 true 时把第一项文案换成"收起为图标球"。
    /// </summary>
    public static void ShowAtCursor(nint owner, bool barActive)
    {
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
            return;

        try
        {
            string first = barActive ? "收起为图标球" : "展开小窗";
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (nuint)CommandToggle, first);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (nuint)CommandExitFloating, "退出悬浮窗模式");

            // 先把自己设成前台：否则菜单点空白处不消失（TrackPopupMenu 的标准用法）
            NativeMethods.SetForegroundWindow(owner);

            NativeMethods.GetCursorPos(out var pt);
            uint cmd = NativeMethods.TrackPopupMenu(menu, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                pt.x, pt.y, 0, owner, 0);

            // 菜单的"点空白关闭"依赖一条 WM_NULL
            NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, 0, 0);

            if (cmd != 0)
                NativeMethods.PostMessage(owner, NativeMethods.WM_MENU_COMMAND, (nint)cmd, 0);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    /// <summary>执行菜单命令，返回是否已处理。</summary>
    public static bool HandleCommand(uint command)
    {
        switch (command)
        {
            case CommandToggle:
                if (FloatingWindow.IsBarActive)
                    FloatingWindow.CollapseBar();
                else if (FloatingWindow.IsOpen)
                    FloatingWindow.ExpandBar();
                else
                    FloatingWindow.Open();
                return true;

            case CommandExitFloating:
                FloatingWindow.Close();
                WindowHelper.ActivateMainWindow();
                return true;
        }

        return false;
    }

    private static class NativeMethods
    {
        /// <summary>自定义消息：菜单命令回到窗口自己的消息循环里执行（避免菜单回调里做重活）。</summary>
        public const uint WM_MENU_COMMAND = 0x0400 + 0x10;   // WM_APP + 16

        public const uint WM_NULL = 0x0000;
        public const uint MF_STRING = 0x00000000;
        public const uint TPM_RIGHTBUTTON = 0x0002;
        public const uint TPM_RETURNCMD = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    }
}
#endif
