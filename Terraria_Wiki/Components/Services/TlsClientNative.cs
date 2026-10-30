using System.Runtime.InteropServices;
using System.Text.Json;
using LuYao.TlsClient;

namespace Terraria_Wiki.Services;

/// <summary>
/// LuYao.TlsClient 的 P/Invoke 在 NativeAOT 下会抛
/// <see cref="MarshalDirectiveException"/>（Marshaling directives are invalid.）：
/// 它的 NativeMethods 用 UnmanagedType.CustomMarshaler 返回/接收 string，
/// 而 NativeAOT 不支持自定义封送器。
///
/// 这里直接调同一个原生库的 request / freeMemory，
/// 用 IntPtr + UTF-8 手动编组绕开 CustomMarshaler；
/// JSON 仍用包里的 TlsClientJsonContext（源生成，AOT 安全）。
/// </summary>
internal static class TlsClientNative
{
    [DllImport("tls-client", CallingConvention = CallingConvention.Cdecl, EntryPoint = "request")]
    private static extern IntPtr RequestNative(IntPtr requestParams);

    [DllImport("tls-client", CallingConvention = CallingConvention.Cdecl, EntryPoint = "freeMemory")]
    private static extern void FreeMemoryNative(IntPtr responseId);

    public static Response Request(RequestInput input)
    {
        string requestJson = JsonSerializer.Serialize(input, TlsClientJsonContext.Default.RequestInput);
        IntPtr requestPtr = Marshal.StringToCoTaskMemUTF8(requestJson);
        try
        {
            IntPtr responsePtr = RequestNative(requestPtr);
            if (responsePtr == IntPtr.Zero)
                throw new InvalidOperationException("tls-client 返回了空指针");

            string responseJson = Marshal.PtrToStringUTF8(responsePtr)
                ?? throw new InvalidOperationException("tls-client 返回了空响应");

            Response response = JsonSerializer.Deserialize(responseJson, TlsClientJsonContext.Default.Response)
                ?? throw new InvalidOperationException("tls-client 响应解析失败");

            // 与包里的 CStringMarshaler 行为一致：响应 JSON 带 id 时用它释放原生内存。
            if (!string.IsNullOrEmpty(response.Id))
            {
                IntPtr idPtr = Marshal.StringToCoTaskMemUTF8(response.Id);
                try
                {
                    FreeMemoryNative(idPtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(idPtr);
                }
            }

            return response;
        }
        finally
        {
            Marshal.FreeCoTaskMem(requestPtr);
        }
    }
}
