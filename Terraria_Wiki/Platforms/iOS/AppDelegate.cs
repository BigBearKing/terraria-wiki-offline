using Foundation;
using UIKit;

namespace Terraria_Wiki;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);

        // 强行关闭 iOS 自动推挤键盘的行为
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("KeyboardDisable", (handler, view) =>
        {
#if IOS
            // 如果你引用了 IQKeyboardManager 等第三方库，这里可以禁用。
            // MAUI 默认的一些滚动行为也会在这里被接管。
            // 确保 WebView 的 ScrollView 属性不自动适应 ContentInset
            if (handler.PlatformView is UITextField textField)
            {
                // 可选针对特定输入框的处理
            }
#endif
        });

        return result;
    }
}