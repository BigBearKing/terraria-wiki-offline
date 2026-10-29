using Android.Content;

namespace Terraria_Wiki;

public static class AndroidFilePicker
{
    public const int RequestCode = 4322;
    public static TaskCompletionSource<Android.Net.Uri?>? CompletionSource { get; private set; }

    public static async Task<Android.Net.Uri?> PickPackageAsync()
    {
        var completion = new TaskCompletionSource<Android.Net.Uri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CompletionSource = completion;

        // 任务体跑在线程池上（见 AppTaskRunner），而 StartActivityForResult 必须在主线程调用。
        await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/octet-stream");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? throw new InvalidOperationException("Android 当前 Activity 不可用。");
            activity.StartActivityForResult(intent, RequestCode);
        });

        return await completion.Task;
    }

    public static void Complete(Android.Net.Uri? uri)
    {
        CompletionSource?.TrySetResult(uri);
        CompletionSource = null;
    }
}
