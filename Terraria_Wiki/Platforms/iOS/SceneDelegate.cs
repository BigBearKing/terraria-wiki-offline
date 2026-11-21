using Foundation;
using UIKit;

namespace Terraria_Wiki;

/// <summary>
/// iOS 26/27 起 UIKit 要求 App 采用 Scene 生命周期；MAUI 11 的 iOS 模板同样带这个类，
/// 并与 Info.plist 里的 UIApplicationSceneManifest（UISceneDelegateClassName=SceneDelegate）配对。
/// 缺了它真机会在 scene 创建阶段抛异常、20 秒后被系统 watchdog 杀掉（表现为点图标闪退）。
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);
        HandleUrlContexts(connectionOptions?.UrlContexts);
    }

    [Export("scene:openURLContexts:")]
    public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        HandleUrlContexts(urlContexts);
    }

    private static void HandleUrlContexts(NSSet<UIOpenUrlContext>? urlContexts)
    {
        if (urlContexts is null || urlContexts.Count == 0)
            return;

        foreach (UIOpenUrlContext context in urlContexts)
        {
            IosFilePicker.HandleExternalOpenUrl(context.Url);
        }
    }
}
