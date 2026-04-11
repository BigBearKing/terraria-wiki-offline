using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Terraria_Wiki.Models; // 确保引入你的模型命名空间

namespace Terraria_Wiki.Services;

// 在这里列出所有需要被 JSON 序列化/反序列化的类
[JsonSerializable(typeof(JsMsg))]
[JsonSerializable(typeof(GitHubReleaseInfo))]
[JsonSerializable(typeof(WikiPageStringTime))]
[JsonSerializable(typeof(TitleWithAnchor))]
[JsonSerializable(typeof(TempHistory))]
[JsonSerializable(typeof(WikiPackageInfo))]
[JsonSerializable(typeof(RawResponse))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(JSCallResultType))]
[JsonSerializable(typeof(JSCallType))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(NavigationOptions))]
//重要：如果你还有其他模型类通过 IframeBridge 传递，必须在这里继续添加 [JsonSerializable(typeof(你的类名))]
public partial class AppJsonContext : JsonSerializerContext
{
    // 配置你原本在 IframeBridge 中使用的 Options
    public static readonly AppJsonContext Custom = new AppJsonContext(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}