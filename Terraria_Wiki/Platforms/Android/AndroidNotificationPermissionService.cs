using Android.OS;

namespace Terraria_Wiki.Services;

public static class AndroidNotificationPermissionService
{
    public static async Task<bool> EnsureGrantedAsync()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return true;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status == PermissionStatus.Granted)
            return true;

        status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }
}
