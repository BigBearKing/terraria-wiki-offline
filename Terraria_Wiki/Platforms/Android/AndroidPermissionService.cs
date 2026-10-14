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
            case AndroidPermissionType.Network:
                // INTERNET 是 normal 权限：正常情况下安装即授予，系统里既没有运行时弹窗也没有开关，
                // 因此这里无法"申请"。只有被 adb revoke / 厂商联网管家 / MDM 策略剥夺后才会不授予，
                // 此时唯一的解除方式是应用详情页里的系统开关 —— 所以直接跳过去。
                if (IsNetworkGranted())
                    break;
                System.Diagnostics.Debug.WriteLine(
                    "[Permission] INTERNET 未授予（normal 权限被 revoke / 被厂商联网管家或 MDM 策略剥夺），跳转应用详情页由用户手动开启。");
                OpenAppSettings();
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
