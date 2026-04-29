using System.Runtime.InteropServices;

namespace Terraria_Wiki;

public static class WindowHelper
{
#if WINDOWS
    // 1. 引入底层 Win32 API
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // Win32 常量定义
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1); // 置顶
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2); // 取消置顶
    private const uint SWP_NOSIZE = 0x0001; // 不改变尺寸
    private const uint SWP_NOMOVE = 0x0002; // 不改变位置


#endif

    // 3. 封装调用方法
    public static void SetAlwaysOnTop(Microsoft.Maui.Controls.Window mauiWindow, bool isAlwaysOnTop)
    {
#if WINDOWS
        // 2. 获取原生 WinUI 3 窗口
        if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            // 3. 核心突破点：直接通过 AppWindow.Id.Value 拿到真实的系统 HWND
            // 彻底避开了所有导致 AOT 崩溃的 WinRT/COM 接口反射！
            if (nativeWindow.AppWindow != null)
            {
                // AppWindow.Id.Value 本质上就是 HWND
                IntPtr hwnd = (IntPtr)nativeWindow.AppWindow.Id.Value;

                if (hwnd != IntPtr.Zero)
                {
                    IntPtr targetState = isAlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST;

                    // 4. 用底层的霸道方法去执行它
                    SetWindowPos(hwnd, targetState, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                }
            }
        }

#elif MACCATALYST
        // 获取 Mac Catalyst 的底层 UIWindow
        var nativeWindow = mauiWindow.Handler.PlatformView as UIKit.UIWindow;
        if (nativeWindow != null)
        {
            if (isAlwaysOnTop)
            {
                // 将窗口层级调高（超过普通弹窗层级），实现置顶
                nativeWindow.WindowLevel = UIKit.UIWindowLevel.Alert + 1;
            }
            else
            {
                // 恢复普通层级
                nativeWindow.WindowLevel = UIKit.UIWindowLevel.Normal;
            }
        }
#endif
    }

}