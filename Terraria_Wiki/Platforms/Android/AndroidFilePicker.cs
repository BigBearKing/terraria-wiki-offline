using Android.App;
using Android.Content;
using Terraria_Wiki.Services;

namespace Terraria_Wiki;

public static class AndroidFilePicker
{
    public const int RequestCode = 4322;
    public static TaskCompletionSource<Android.Net.Uri?>? CompletionSource { get; private set; }

    // QQ 群文件 →「用其他应用打开」递进来的包。由 MainActivity 写入，
    // PickPackageAsync 优先消费（消费后即清空），这样用户不用先存到本地再选。
    private static Android.Net.Uri? _pendingUri;

    public static async Task<Android.Net.Uri?> PickPackageAsync()
    {
        var pending = ConsumePendingUri();
        if (pending is not null)
            return pending;

        var completion = new TaskCompletionSource<Android.Net.Uri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CompletionSource = completion;

        // 任务体跑在线程池上（见 AppTaskRunner），而 StartActivityForResult 必须在主线程调用。
        await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            // ★ 不能写死 application/octet-stream：SAF 是按文档提供器报的 MIME 过滤的，
            //   而 .pkg 在不同 ROM / 不同来源（QQ 下载写进 MediaStore 的类型）上会被认成
            //   x-xar 之类，写死就会把文件置灰、选不了。这个过滤本身也不提供任何保护 ——
            //   真正的校验在 PackageService 里：读文件头 "WIKIDATA" + 逐文件 MD5。
            intent.SetType("*/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? throw new InvalidOperationException("Android 当前 Activity 不可用。");
            activity.StartActivityForResult(intent, RequestCode);
        });

        return await completion.Task;
    }

    /// <summary>
    /// 处理 ACTION_VIEW（QQ 群文件 →「用其他应用打开」）。必须在 base.OnCreate 之后调用，
    /// 那时 Application.Current / DI 容器才就绪。
    /// </summary>
    public static void HandleExternalViewIntent(Intent? intent, Activity activity)
    {
        if (intent?.Data is not { } uri) return;
        if (!string.Equals(intent.Action, Intent.ActionView, StringComparison.Ordinal)) return;

        // 消费掉 data，避免 Activity 因配置变更重建时再触发一次导入。
        intent.SetData(null);
        activity.Intent?.SetData(null);

        try
        {
            activity.ContentResolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        catch
        {
            // 普通 VIEW 给的是临时授权，不能持久化；本次导入期间仍然可读。
        }

        _pendingUri = uri;
        _ = ImportWhenReadyAsync();
    }

    private static Android.Net.Uri? ConsumePendingUri()
    {
        var uri = _pendingUri;
        _pendingUri = null;
        return uri;
    }

    private static async Task ImportWhenReadyAsync()
    {
        try
        {
            // 冷启动时 App 初始化（ManagerDb / ContentDb / 旧版迁移）还没跑完，必须先等它，
            // 否则导入会拿到未初始化的数据库。
            if (Microsoft.Maui.Controls.Application.Current is App app)
                await app.InitializationTask;

            // PackageService 里会取 Windows[0].Page 弹加载遮罩，窗口挂上之前不能进。
            for (int i = 0; i < 50; i++)
            {
                if ((Microsoft.Maui.Controls.Application.Current?.Windows.Count ?? 0) > 0) break;
                await Task.Delay(100);
            }

            var services = IPlatformApplication.Current?.Services;
            if (services?.GetService(typeof(PackageService)) is not PackageService service) return;

            await service.ImportDataAsync();
        }
        catch (Exception ex)
        {
            App.LogManager?.Error("处理外部打开的导入包失败", ex);
        }
    }
}
