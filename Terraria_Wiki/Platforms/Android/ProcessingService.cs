using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Terraria_Wiki.Services; // 替换为你 AppState 所在的实际命名空间

namespace Terraria_Wiki.Platforms.Android;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync)]
public class ProcessingService : Service
{
    private const string ChannelId = "processing_channel";
    private const int NotificationId = 2002;

    public override IBinder OnBind(Intent intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        // 1. 从 MAUI 容器中获取你全局单例的 AppState
        var appState = IPlatformApplication.Current.Services.GetService<AppState>();

        // 假设你的 TaskId 是 int 类型。如果是 string，改成 string.IsNullOrEmpty(appState.TaskId) 等
        if (appState == null || appState.ProcessingTaskId == 0)
        {
            // 如果 TaskId 是 0，立刻销毁通知并停止服务
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        // 2. TaskId 不是 0，显示极其简单的通知栏（无进度条）
        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("正在处理任务")
            .SetContentText($"当前任务：{appState.Tasks[appState.ProcessingTaskId].Name}")
            .SetSmallIcon(Resource.Mipmap.appicon) // 确保你有这个图标
            .SetOngoing(true) // 禁止用户手动划掉
            .Build();

        // 3. 提升为前台服务保活
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }

    private void CreateNotificationChannel()
    {
        var notificationManager = (NotificationManager)GetSystemService(NotificationService);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "后台处理状态", NotificationImportance.Low);
            notificationManager.CreateNotificationChannel(channel);
        }
    }
}