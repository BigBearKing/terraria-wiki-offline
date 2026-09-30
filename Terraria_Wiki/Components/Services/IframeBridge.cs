using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Text.Json;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public static class IframeBridge
{
    private static IJSRuntime? _js;

    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTasks = new();

    public static readonly Dictionary<string, Func<string, Task<string>>> Actions = new();
    public static readonly Dictionary<string, Func<string, Task<object?>>> StructuredActions = new();
    public static event Action? OnIframePageReady;


    public static void Init(IJSRuntime jsRuntime) => _js = jsRuntime;


    public static string ObjToJson<T>(T obj)
    {
        if (obj == null) return string.Empty;
        return JsonSerializer.Serialize(obj, typeof(T), AppJsonContext.Custom);
    }

    public static T? JsonToObj<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        return (T?)JsonSerializer.Deserialize(json, typeof(T), AppJsonContext.Custom);
    }

    // 2. C# 调用 Iframe：发送请求并等待结果
    // cancellationToken 用于给「发完就不管结果」的调用（如 SetZoom）加上等待上限：
    // iframe 尚未注册处理器时不会有回复，超时后必须把挂起的 TaskCompletionSource 摘掉，
    // 否则消息会一直压在 _pendingTasks 里直到 JS 互操作超时。
    public static async Task<string> CallJsAsync(string methodName, string argsJson, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>();
        _pendingTasks[id] = tcs;

        // 调用宿主页面的 JS helper，让它转发给 iframe
        await _js!.InvokeVoidAsync("hostBridge.sendToIframe", new { type = "req", id, method = methodName, data = argsJson });

        using var registration = cancellationToken.Register(() =>
        {
            if (_pendingTasks.TryRemove(id, out var pending))
                pending.TrySetResult(string.Empty);
        });

        // 等待超时后挂起项已被摘除，此处对已完成的 TCS 取值不会抛异常
        return await tcs.Task; // 等待 Iframe 回复
    }

    // 3. 供 JS 调用的入口 (必须是 Public Static)
    [JSInvokable]
    public static async Task<int> ReceiveMessage(string json)
    {

        var msg = (JsMsg?)JsonSerializer.Deserialize(json, typeof(JsMsg), AppJsonContext.Custom);

        if (msg == null) return 0;

        if (msg.Type == "res") // A. 这是 JS 给 C# 的返回值
        {
            if (_pendingTasks.TryRemove(msg.Id, out var tcs))
                tcs.SetResult(msg.Data);
        }
        else if (msg.Type == "req") // B. 这是 JS 请求调用 C#
        {
            if (StructuredActions.TryGetValue(msg.Method, out var structuredFunc))
            {
                var result = await structuredFunc(msg.Data);
                await _js!.InvokeVoidAsync("hostBridge.sendToIframe", new { type = "res", id = msg.Id, data = result });
            }
            else
            {
                string result = "";
                if (Actions.TryGetValue(msg.Method, out var func))
                    result = await func(msg.Data);

                // 发送返回值给 JS
                await _js!.InvokeVoidAsync("hostBridge.sendToIframe", new { type = "res", id = msg.Id, data = result });
            }
        }
        else if (msg.Type == "event" && msg.Method == "IframePageReady")
        {
            OnIframePageReady?.Invoke();
        }
        return 0;
    }

}