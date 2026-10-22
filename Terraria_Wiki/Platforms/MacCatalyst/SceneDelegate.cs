using Foundation;

namespace Terraria_Wiki;

/// <summary>
/// Mac Catalyst 与 iOS 共用同一套 Scene 生命周期要求，MAUI 11 模板在 MacCatalyst 里也带这个类。
/// 与 Platforms/MacCatalyst/Info.plist 的 UIApplicationSceneManifest 配对使用。
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
