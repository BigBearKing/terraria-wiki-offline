using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidApplication = Android.App.Application;

namespace Terraria_Wiki.Services;

public static class AndroidPermissionService
{
    public static event Action? PermissionStateChanged;

    public static bool IsGranted(AndroidPermissionType permission) =>
        permission switch
        {
            AndroidPermissionType.Network => IsNetworkGranted(),
            AndroidPermissionType.Notifications => IsNotificationGranted(),
            AndroidPermissionType.Background => IsBackgroundGranted(),
            _ => false
        };

    public static async Task RequestAsync(AndroidPermissionType permission)
    {
        switch (permission)
        {
            case AndroidPermissionType.Notifications:
                await AndroidNotificationPermissionService.EnsureGrantedAsync();
                break;
            case AndroidPermissionType.Background:
                RequestBackgroundPermission();
                break;
        }

        PermissionStateChanged?.Invoke();
    }

    public static void NotifyResumed() => PermissionStateChanged?.Invoke();

    public static bool AreDownloadPermissionsGranted() =>
        IsGranted(AndroidPermissionType.Network) &&
        IsGranted(AndroidPermissionType.Notifications) &&
        IsGranted(AndroidPermissionType.Background);

    public static void OpenAppSettings()
    {
        var intent = new Intent(Settings.ActionApplicationDetailsSettings);
        intent.SetData(Android.Net.Uri.Parse($"package:{AndroidApplication.Context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        AndroidApplication.Context.StartActivity(intent);
    }

    private static bool IsNetworkGranted() =>
        AndroidApplication.Context.CheckSelfPermission(Android.Manifest.Permission.Internet) ==
        Android.Content.PM.Permission.Granted;

    private static bool IsNotificationGranted()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return true;

        return AndroidApplication.Context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) ==
               Android.Content.PM.Permission.Granted;
    }

    private static bool IsBackgroundGranted()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            return true;

        var powerManager = (PowerManager)AndroidApplication.Context.GetSystemService(Context.PowerService)!;
        return powerManager.IsIgnoringBatteryOptimizations(AndroidApplication.Context.PackageName);
    }

    private static void RequestBackgroundPermission()
    {
        if (IsBackgroundGranted())
            return;

        var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
        intent.SetData(Android.Net.Uri.Parse($"package:{AndroidApplication.Context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        AndroidApplication.Context.StartActivity(intent);
    }
}
