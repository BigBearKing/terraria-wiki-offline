using Foundation;
using UIKit;
using System;
using ObjCRuntime;

namespace Terraria_Wiki; // 注意替换成你自己的命名空间

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // 1. 在最早的时机切断 MAUI 官方的键盘推挤逻辑
        Microsoft.Maui.Platform.KeyboardAutoManagerScroll.Disconnect();


        // 强行监听键盘弹出，把被推上去的根视图硬拽回来
        NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillShowNotification, OnKeyboardWillShow);

        return base.FinishedLaunching(application, launchOptions);
    }

    private void OnKeyboardWillShow(NSNotification notification)
    {
        // 延迟一点点执行，等 iOS 把页面推上去之后，我们再把它拉下来
        Device.BeginInvokeOnMainThread(() =>
        {
            var window = UIApplication.SharedApplication.Delegate?.GetWindow();
            if (window?.RootViewController?.View != null)
            {
                var view = window.RootViewController.View;

                // 如果发现整个视图被系统往上推了 (Y坐标变成了负数)
                if (view.Frame.Y < 0)
                {
                    view.Frame = new CoreGraphics.CGRect(view.Frame.X, 0, view.Frame.Width, view.Frame.Height);
                }
            }
        });
    }

}