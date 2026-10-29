using LuYao.TlsClient;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Terraria_Wiki.Services;

public sealed record NetworkResponse(
    byte[] Data,
    HttpStatusCode StatusCode,
    string ContentType,
    DateTime? LastModified);

public static class NetworkService
{
    /// <summary>
    /// 能不能用 LuYao.TlsClient 的指纹客户端。
    /// 这个库的原生库只发布了 win-* / linux-* / osx-* 三套（runtimes/ 下就这些），
    /// 而 maccatalyst-arm64、ios-arm64、android-arm64 在 .NET 的 RID 图里**没有**
    /// osx-* / linux-* 回退，所以这三个平台的包里根本不会有 tls-client.dylib，
    /// P/Invoke 时必然抛 DllNotFoundException（就是日志里那串
    /// "Unable to load shared library 'tls-client'"）。
    /// 这几个平台改用系统 HttpClient：iOS / Mac Catalyst 走 NSUrlSession、
    /// Android 走 AndroidMessageHandler（两者 UseNativeHttpHandler 默认就是 true），
    /// 它们的 TLS 指纹本身就是 Safari / Chrome 的，再配合同样的浏览器头，
    /// 对按指纹拦爬虫的站点（灾厄用的 huijiwiki）一样有效。
    /// </summary>
    private static readonly bool TlsClientSupported =
        !OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst() && !OperatingSystem.IsAndroid();

    // 不支持的平台上干脆不构造这个实例（留着也没用，还容易在类型初始化阶段踩到原生库）。
    private static readonly TlsClient? TlsClient = TlsClientSupported
        ? new TlsClient
        {
            TLSClientIdentifier = ClientIdentifiers.Chrome_131,
            FollowRedirect = true,
            Timeout = TimeSpan.FromSeconds(15)
        }
        : null;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(150)
    };

    public static Task<bool> IsNetworkAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Connectivity.Current.NetworkAccess == NetworkAccess.Internet);

    /// <summary>
    /// 判断一次失败是不是"连不上"这类网络故障（而不是服务端返回了错误）。
    ///
    /// 用异常本身当判据：断网时 .NET 抛的是带
    /// <see cref="HttpRequestError.ConnectionError"/> / <see cref="HttpRequestError.NameResolutionError"/>
    /// 的 <see cref="HttpRequestException"/>，或者 <see cref="SocketException"/>、超时；
    /// 而 404 / 500 / 403 也是 HttpRequestException，但 HttpRequestError 是 Unknown —— 那种只该重试，不该暂停。
    /// 平台 handler（NSUrlSession / AndroidMessageHandler）偶尔不填 HttpRequestError，
    /// 所以调用方还会叠加一次 Connectivity 判断兜底。
    /// </summary>
    public static bool IsConnectivityFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException
                {
                    HttpRequestError: HttpRequestError.NameResolutionError
                        or HttpRequestError.ConnectionError
                        or HttpRequestError.SecureConnectionError
                        or HttpRequestError.ProxyTunnelError
                }:
                case SocketException:
                case TimeoutException:
                    return true;

                // 断网时请求常常是"卡到超时"才失败，这类也按网络问题处理
                case TaskCanceledException:
                    return true;
            }
        }

        return false;
    }

    // ---- 各平台的"自称"必须和该平台真正用的 TLS 栈对得上 ----
    //   Windows：走 LuYao.TlsClient，ClientHello 是 Chrome 131 桌面版 → 桌面 Chrome 的 UA + Client Hints
    //   iOS / Mac Catalyst：走 NSUrlSession，ClientHello 是 Safari 的 → Safari 的 UA，
    //                       且 Safari 根本不发 Sec-CH-UA*（发了反而露馅）
    //   Android：走 AndroidMessageHandler，ClientHello 是 Chrome for Android 的 →
    //            移动版 Chrome UA + 移动版 hints
    // 混着发（比如 Apple 的 TLS 配 Windows Chrome 的 UA）会被 WAF 当成爬虫直接拦掉。
    private const string AcceptValue = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
    private const string AcceptLanguageValue = "zh-CN,zh;q=0.9";

    private const string ChromeDesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const string ChromeClientHints =
        "\"Not_A Brand\";v=\"99\", \"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\"";

    private const string SafariMacUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.5 Safari/605.1.15";
    private const string SafariIphoneUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1";
    private const string SafariIpadUserAgent =
        "Mozilla/5.0 (iPad; CPU OS 18_5 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1";

    private const string ChromeAndroidUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";

    private const string CrawlerUserAgent =
        "TerrariaWikiScraper/1.0 (contact: bigbearkingus@gmail.com)";

    /// <summary>
    /// 一条"浏览器画像"：User-Agent + Client Hints。
    /// <paramref name="ClientHints"/> 为 null 表示这个浏览器不发 Client Hints（Safari）。
    /// </summary>
    private readonly record struct BrowserProfile(
        string UserAgent,
        string? ClientHints,
        string MobileHint,
        string PlatformHint);

    /// <summary>按当前平台挑一组和 TLS 指纹匹配的浏览器画像。</summary>
    private static BrowserProfile GetBrowserProfile()
    {
        if (OperatingSystem.IsIOS())
        {
            var userAgent = DeviceInfo.Current.Idiom == DeviceIdiom.Tablet
                ? SafariIpadUserAgent
                : SafariIphoneUserAgent;
            return new BrowserProfile(userAgent, null, "", "");
        }

        if (OperatingSystem.IsMacCatalyst())
            return new BrowserProfile(SafariMacUserAgent, null, "", "");

        if (OperatingSystem.IsAndroid())
            return new BrowserProfile(ChromeAndroidUserAgent, ChromeClientHints, "?1", "\"Android\"");

        // Windows（以及将来支持 tls-client 的桌面平台）：桌面 Chrome
        return new BrowserProfile(ChromeDesktopUserAgent, ChromeClientHints, "?0", "\"Windows\"");
    }

    public static async Task<string> GetStringAsync(
        string url,
        bool useTls = false,
        CancellationToken cancellationToken = default)
    {
        if (useTls && TlsClient is not null)
        {
            try
            {
                var request = TlsClient.CreateRequest();
                request.RequestUrl = EncodeUrl(url);
                request.RequestMethod = "GET";
                AddTlsBrowserHeaders(request);

                var response = await Task.Run(() => TlsClient.Request(request), cancellationToken);
                EnsureSuccessStatusCode(response.Status, url);
                return response.Body;
            }
            catch (Exception ex) when (IsTlsNativeUnavailable(ex))
            {
                // 原生库没带上（平台不支持 / 打包缺失）：退回系统 HttpClient，别让整个任务失败
                LogTlsFallbackOnce(ex);
            }
        }

        using var requestMessage = CreateRequest(url, useTls);
        using var responseMessage = await HttpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        responseMessage.EnsureSuccessStatusCode();
        return await responseMessage.Content.ReadAsStringAsync(cancellationToken);
    }

    public static async Task<byte[]> GetBytesAsync(
        string url,
        bool useTls = false,
        DateTime? ifModifiedSince = null,
        CancellationToken cancellationToken = default)
    {
        var response = await GetBytesResponseAsync(url, useTls, ifModifiedSince, cancellationToken);
        return response.Data;
    }

    public static async Task<NetworkResponse> GetBytesResponseAsync(
        string url,
        bool useTls = false,
        DateTime? ifModifiedSince = null,
        CancellationToken cancellationToken = default)
    {
        if (useTls && TlsClient is not null)
        {
            try
            {
                var request = TlsClient.CreateRequest();
                request.RequestUrl = EncodeUrl(url);
                request.RequestMethod = "GET";
                AddTlsBrowserHeaders(request);
                if (ifModifiedSince.HasValue)
                {
                    request.Headers["If-Modified-Since"] = new DateTimeOffset(
                        DateTime.SpecifyKind(ifModifiedSince.Value, DateTimeKind.Utc)).ToString("R");
                }

                var response = await Task.Run(() => TlsClient.Request(request), cancellationToken);
                if (response.Status != (int)HttpStatusCode.NotModified)
                    EnsureSuccessStatusCode(response.Status, url);

                var contentType = GetResponseHeader(response.Headers, "Content-Type") ?? "application/octet-stream";
                var lastModified = ParseLastModified(GetResponseHeader(response.Headers, "Last-Modified"));
                var data = response.Status == (int)HttpStatusCode.NotModified
                    ? []
                    : Encoding.UTF8.GetBytes(response.Body);

                return new NetworkResponse(data, (HttpStatusCode)response.Status, contentType, lastModified);
            }
            catch (Exception ex) when (IsTlsNativeUnavailable(ex))
            {
                // 同上：原生库不可用就退回系统 HttpClient
                LogTlsFallbackOnce(ex);
            }
        }

        // useTls 时带上浏览器头：系统 handler 的 TLS 指纹本来就是 Safari / Chrome 的，
        // 再缺这组头（UA / Sec-CH-UA / Accept-Language）反而容易被 WAF 认成爬虫。
        using var requestMessage = CreateRequest(url, useTls);
        if (ifModifiedSince.HasValue)
        {
            requestMessage.Headers.IfModifiedSince = new DateTimeOffset(
                DateTime.SpecifyKind(ifModifiedSince.Value, DateTimeKind.Utc));
        }

        using var responseMessage = await HttpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (responseMessage.StatusCode != HttpStatusCode.NotModified)
            responseMessage.EnsureSuccessStatusCode();

        var dataBytes = responseMessage.StatusCode == HttpStatusCode.NotModified
            ? []
            : await responseMessage.Content.ReadAsByteArrayAsync(cancellationToken);

        return new NetworkResponse(
            dataBytes,
            responseMessage.StatusCode,
            responseMessage.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            responseMessage.Content.Headers.LastModified?.UtcDateTime);
    }

    /// <summary>
    /// 判断异常是不是"原生库没加载起来"。TlsClient 是静态字段，某些情况下首次访问会包一层
    /// TypeInitializationException，所以一并算进来。
    /// </summary>
    private static bool IsTlsNativeUnavailable(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;

    private static int _tlsFallbackLogged;

    /// <summary>退回系统 HttpClient 只记一次日志，避免每个请求刷一条。</summary>
    private static void LogTlsFallbackOnce(Exception ex)
    {
        if (Interlocked.Exchange(ref _tlsFallbackLogged, 1) == 0)
            App.LogManager?.Error($"TLS 客户端不可用，已改用系统 HttpClient：{ex.Message}");
    }

    private static HttpRequestMessage CreateRequest(string url, bool useTls)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, EncodeUrl(url));
        if (useTls)
            AddHttpBrowserHeaders(request);
        else
            // 爬虫自己的 UA 只挂在请求上，不再塞进 HttpClient.DefaultRequestHeaders：
            // 否则带浏览器头的回退请求会同时叠两条 UA，服务器看到的是拼接后的怪串。
            request.Headers.UserAgent.ParseAdd(CrawlerUserAgent);
        return request;
    }

    // 形参改为静态类型 RequestInput：原来用 dynamic（DLR 运行时代码生成），
    // 在 NativeAOT 下必然抛异常，是 AOT 编译的硬性阻断项。
    private static void AddTlsBrowserHeaders(RequestInput request)
    {
        var profile = GetBrowserProfile();
        request.Headers["User-Agent"] = profile.UserAgent;
        request.Headers["Accept"] = AcceptValue;
        request.Headers["Accept-Language"] = AcceptLanguageValue;

        // Safari 不发 Client Hints，只有 Chrome 系才补这三条
        if (profile.ClientHints is null) return;
        request.Headers["Sec-CH-UA"] = profile.ClientHints;
        request.Headers["Sec-CH-UA-Mobile"] = profile.MobileHint;
        request.Headers["Sec-CH-UA-Platform"] = profile.PlatformHint;
    }

    private static void AddHttpBrowserHeaders(HttpRequestMessage request)
    {
        var profile = GetBrowserProfile();
        request.Headers.UserAgent.ParseAdd(profile.UserAgent);
        request.Headers.Accept.ParseAdd(AcceptValue);
        request.Headers.AcceptLanguage.ParseAdd(AcceptLanguageValue);

        if (profile.ClientHints is null) return;
        request.Headers.TryAddWithoutValidation("Sec-CH-UA", profile.ClientHints);
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", profile.MobileHint);
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", profile.PlatformHint);
    }

    private static void EnsureSuccessStatusCode(int status, string url)
    {
        if (status < 200 || status >= 300)
            throw new HttpRequestException($"TLS request failed with status {status} for {url}", null, (HttpStatusCode)status);
    }

    // 同上，Headers 的真实类型是 Dictionary<string, List<string>>，无需 dynamic。
    private static string? GetResponseHeader(Dictionary<string, List<string>>? headers, string name)
    {
        if (headers is null)
            return null;

        return headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    private static DateTime? ParseLastModified(string? value)
        => DateTime.TryParse(value, out var dateTime)
            ? dateTime.ToUniversalTime()
            : null;

    private static string EncodeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("输入的字符串不是合法的完整绝对 URL", nameof(url));
        }

        return uri.AbsoluteUri;
    }
}
