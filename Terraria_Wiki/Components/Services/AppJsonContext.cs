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
[JsonSerializable(typeof(PageViewInfo))]
[JsonSerializable(typeof(WikiPackageInfo))]
[JsonSerializable(typeof(List<FileMeta>))]
[JsonSerializable(typeof(FileMeta))]
[JsonSerializable(typeof(RawResponse))]
[JsonSerializable(typeof(JsonElement[]))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(JSCallResultType))]
[JsonSerializable(typeof(JSCallType))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
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

// 持久化（SQLite 落库 / 内置种子配置）专用 Context。
// 刻意使用默认命名（PascalCase）：原先这些地方走的是无 options 的反射式序列化，
// 保持默认命名才能让已落库的 TaskData 继续正确反序列化，不受 JS 桥的 camelCase 影响。
[JsonSerializable(typeof(AppTaskData))]
[JsonSerializable(typeof(List<WikiBook>))]
public partial class DbJsonContext : JsonSerializerContext
{
    public static readonly DbJsonContext Persistence = new DbJsonContext(new JsonSerializerOptions());
}