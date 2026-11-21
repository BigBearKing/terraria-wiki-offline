using Foundation;
using UIKit;

namespace Terraria_Wiki
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
        {
            IosFilePicker.HandleExternalOpenUrl(url);
            return base.OpenUrl(application, url, options);
        }
    }
}
