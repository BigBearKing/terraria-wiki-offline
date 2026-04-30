using Android.Content;
using Application = Android.App.Application;

namespace Terraria_Wiki; // 替换成你的命名空间

public static class AndroidFileSaver
{
    // 用于将回调转换为异步等待
    public static TaskCompletionSource<Android.Net.Uri> tcs;

    /// <summary>
    /// 唤起 SAF 界面让用户选择保存位置
    /// </summary>
    public static Task<Android.Net.Uri> PickSaveLocationAsync(string fileName, string mimeType = "text/plain")
    {
        tcs = new TaskCompletionSource<Android.Net.Uri>();

        // 构造创建文件的 Intent (SAF 的核心)
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType); // 设置文件类型
        intent.PutExtra(Intent.ExtraTitle, fileName); // 设置默认文件名

        // 获取当前活动的 Activity 并启动 Intent
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        // 这里的 4321 是自定义的请求码，用于在回调中识别这次请求
        activity.StartActivityForResult(intent, 4321);

        return tcs.Task;
    }

    /// <summary>
    /// 将数据写入用户指定的 Uri
    /// </summary>
    public static void WriteDataToUri(Android.Net.Uri uri, byte[] data)
    {
        var resolver = Application.Context.ContentResolver;
        // 打开系统的输出流并写入数据
        using var stream = resolver.OpenOutputStream(uri);
        if (stream != null)
        {
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
    }
}