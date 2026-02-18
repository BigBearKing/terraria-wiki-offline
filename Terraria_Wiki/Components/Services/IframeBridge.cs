using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public static class IframeBridge
{
    private static IJSRuntime? _js;

    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTasks = new();

    public static readonly Dictionary<string, Func<string, Task<string>>> Actions = new();


    public static void Init(IJSRuntime jsRuntime) => _js = jsRuntime;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        // 1. 允许中文直接显示，不转义
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 2. (可选) 自动转驼峰命名: C# UserId -> JS userId
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ObjToJson(object obj)
    {

        if (obj == null) return string.Empty;
        return JsonSerializer.Serialize(obj, Options);
    }
    public static T JsonToObj<T>(string json) where T : class
    {

        if (json == null||json=="") return null;
            return JsonSerializer.Deserialize<T>(json, Options);
    }
    // 2. C# 调用 Iframe：发送请求并等待结果
    public static async Task<string> CallJsAsync(string methodName, string argsJson)
    {
        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>();
        _pendingTasks[id] = tcs;

        // 调用宿主页面的 JS helper，让它转发给 iframe
        await _js!.InvokeVoidAsync("hostBridge.sendToIframe", new { type = "req", id, method = methodName, data = argsJson });

        return await tcs.Task; // 等待 Iframe 回复
    }

    // 3. 供 JS 调用的入口 (必须是 Public Static)
    [JSInvokable]
    public static async Task ReceiveMessage(string json)
    {

        var msg = JsonSerializer.Deserialize<JsMsg>(json, Options);

        if (msg.Type == "res") // A. 这是 JS 给 C# 的返回值
        {
            if (_pendingTasks.TryRemove(msg.Id, out var tcs))
                tcs.SetResult(msg.Data);
        }
        else if (msg.Type == "req") // B. 这是 JS 请求调用 C#
        {
            string result = "";
            if (Actions.TryGetValue(msg.Method, out var func))
                result = await func(msg.Data);

            // 发送返回值给 JS
            await _js!.InvokeVoidAsync("hostBridge.sendToIframe", new { type = "res", id = msg.Id, data = result });
        }
    }

}