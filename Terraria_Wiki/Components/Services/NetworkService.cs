using LuYao.TlsClient;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Terraria_Wiki.Services;

public sealed record NetworkResponse(
    byte[] Data,
    HttpStatusCode StatusCode,
    string ContentType,
    DateTime? LastModified);

/// <summary>
/// 单次资源下载的续传状态：重试时保留已收到的字节，用 HTTP Range 只补剩余部分。
/// 只由同一个下载 worker 顺序使用，不需要加锁。
/// </summary>
public sealed class DownloadResumeState
{
    public MemoryStream Buffer { get; } = new();

    /// <summary>强 ETag（If-Range 用）；弱 ETag 不参与续传校验。</summary>
    public string? ETag { get; set; }

    public DateTime? LastModified { get; set; }

    public string? ContentType { get; set; }

    public long? TotalLength { get; set; }

    public void Reset()
    {
        Buffer.SetLength(0);
        Buffer.Position = 0;
        ETag = null;
        LastModified = null;
        ContentType = null;
        TotalLength = null;
    }
}

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

    /// <summary>
    /// Windows 下走 tls-client 原生库时所有请求共用一个会话：
    /// Cloudflare 的 __cf_bm / cf_clearance 之类 cookie 要跨请求保留，
    /// 否则每个请求都会被重新挑战。
    /// </summary>
    private static readonly string TlsSessionId = Guid.NewGuid().ToString();

    /// <summary>Firefox 135 的 UA（必须和 Firefox_135 指纹配套，别拿 Chrome UA 配 Firefox 指纹）。</summary>
    private const string FirefoxUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:135.0) Gecko/20100101 Firefox/135.0";

    /// <summary>等响应头的上限（含连接建立）。大文件不会卡在这里。</summary>
    private static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(30);

    /// <summary>响应体"空闲"上限：连续这么久没收到字节才判定连接死了，收到一块就重置。</summary>
    private static readonly TimeSpan BodyIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>文本响应（页面 / JSON）的总读取上限，页面体积小，用总时长更简单。</summary>
    private static readonly TimeSpan StringBodyTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpClient HttpClient = new()
    {
        // 固定总超时会误杀"文件大但一直在传"的下载：
        // 响应头交给 HeadersTimeout，响应体交给 BodyIdleTimeout（每收到一块就重置）。
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
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
        if (useTls && TlsClientSupported)
        {
            try
            {
                var response = await Task.Run(() => RequestViaTls(url, ifModifiedSince: null), cancellationToken);
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
        using var responseMessage = await SendWithHeadersTimeoutAsync(requestMessage, cancellationToken);
        responseMessage.EnsureSuccessStatusCode();

        using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyCts.CancelAfter(StringBodyTimeout);
        try
        {
            return await responseMessage.Content.ReadAsStringAsync(bodyCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateRetryableTimeout($"读取响应内容超时（{StringBodyTimeout.TotalSeconds:0}s）");
        }
    }

    public static async Task<byte[]> GetBytesAsync(
        string url,
        bool useTls = false,
        DateTime? ifModifiedSince = null,
        CancellationToken cancellationToken = default,
        DownloadResumeState? resume = null)
    {
        var response = await GetBytesResponseAsync(url, useTls, ifModifiedSince, cancellationToken, resume);
        return response.Data;
    }

    /// <summary>
    /// 下载二进制资源。传入同一个 <paramref name="resume"/> 可在重试时用 HTTP Range 续传：
    /// 已收到的字节保留在 <see cref="DownloadResumeState.Buffer"/> 里，只补剩余部分。
    /// 读取阶段按"空闲超时"判定死连接，不会因为文件大、传得久而误超时。
    /// </summary>
    public static async Task<NetworkResponse> GetBytesResponseAsync(
        string url,
        bool useTls = false,
        DateTime? ifModifiedSince = null,
        CancellationToken cancellationToken = default,
        DownloadResumeState? resume = null)
    {
        if (useTls && TlsClientSupported)
        {
            try
            {
                var response = await Task.Run(() => RequestViaTls(url, ifModifiedSince), cancellationToken);
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

        resume ??= new DownloadResumeState();

        // useTls 时带上浏览器头：系统 handler 的 TLS 指纹本来就是 Safari / Chrome 的，
        // 再缺这组头（UA / Sec-CH-UA / Accept-Language）反而容易被 WAF 认成爬虫。
        using var requestMessage = CreateRequest(url, useTls);
        long offset = resume.Buffer.Length;

        if (offset > 0)
        {
            requestMessage.Headers.Range = new RangeHeaderValue(offset, null);
            ApplyIfRange(requestMessage, resume);
            App.LogManager?.Info($"资源续传：{url}（已有 {offset} 字节）");
        }
        else if (ifModifiedSince.HasValue)
        {
            requestMessage.Headers.IfModifiedSince = new DateTimeOffset(
                DateTime.SpecifyKind(ifModifiedSince.Value, DateTimeKind.Utc));
        }

        using var responseMessage = await SendWithHeadersTimeoutAsync(requestMessage, cancellationToken);

        if (responseMessage.StatusCode == HttpStatusCode.NotModified)
        {
            if (offset > 0)
            {
                // 发了 Range 却拿到 304：服务器没按续传处理，丢掉残留重新下一遍
                resume.Reset();
                throw CreateRetryableTimeout("续传请求收到 304，重新下载");
            }

            return new NetworkResponse(
                [],
                HttpStatusCode.NotModified,
                responseMessage.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                responseMessage.Content.Headers.LastModified?.UtcDateTime);
        }

        if (responseMessage.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
        {
            // 文件变短 / 变了，旧偏移已经越界：丢掉残留，下一轮从 0 开始
            resume.Reset();
            throw CreateRetryableTimeout("续传位置已失效（416），重新下载");
        }

        responseMessage.EnsureSuccessStatusCode();

        bool append = false;
        if (responseMessage.StatusCode == HttpStatusCode.PartialContent)
        {
            var contentRange = responseMessage.Content.Headers.ContentRange;
            if (offset <= 0 || contentRange?.From is not long from || from != offset)
            {
                resume.Reset();
                throw CreateRetryableTimeout("服务器返回的 Range 起点不一致，重新下载");
            }

            append = true;
        }
        else if (offset > 0)
        {
            // 服务器忽略了 Range（200 全量）：只能丢掉残留，从头接收
            resume.Reset();
            append = false;
        }

        if (!append)
        {
            resume.Buffer.SetLength(0);
        }

        resume.Buffer.Position = resume.Buffer.Length;

        var etag = responseMessage.Headers.ETag;
        if (etag is { IsWeak: false })
        {
            var etagText = etag.ToString();
            if (append && !string.IsNullOrEmpty(resume.ETag) && resume.ETag != etagText)
            {
                resume.Reset();
                throw CreateRetryableTimeout("文件在续传期间发生变化，重新下载");
            }

            resume.ETag = etagText;
        }
        else if (etag is null)
        {
            resume.ETag = null;
        }

        if (responseMessage.Content.Headers.LastModified is { } lastModifiedHeader)
            resume.LastModified = lastModifiedHeader.UtcDateTime;

        if (responseMessage.Content.Headers.ContentType?.MediaType is { } mediaType)
            resume.ContentType = mediaType;

        long? totalLength = responseMessage.Content.Headers.ContentRange?.Length
                            ?? responseMessage.Content.Headers.ContentLength;
        if (totalLength is long total)
        {
            if (resume.TotalLength is long previousTotal && previousTotal != total)
            {
                resume.Reset();
                throw CreateRetryableTimeout("文件总长度发生变化，重新下载");
            }

            resume.TotalLength = total;
        }

        if (resume.TotalLength is long knownTotal && resume.Buffer.Length > knownTotal)
        {
            resume.Reset();
            throw CreateRetryableTimeout("已下载数据超过文件总长度，重新下载");
        }

        using (var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken))
        {
            await CopyBodyWithIdleTimeoutAsync(stream, resume.Buffer, cancellationToken);
        }

        if (resume.TotalLength is long expectedTotal && resume.Buffer.Length != expectedTotal)
        {
            throw CreateRetryableTimeout(
                $"资源下载不完整（{resume.Buffer.Length}/{expectedTotal} 字节），准备续传");
        }

        var dataBytes = resume.Buffer.ToArray();
        var contentTypeResult = resume.ContentType ?? "application/octet-stream";
        var lastModifiedResult = resume.LastModified;
        resume.Reset();   // 成功后释放累积的字节（10MB 级资源不长期占内存）

        return new NetworkResponse(dataBytes, responseMessage.StatusCode, contentTypeResult, lastModifiedResult);
    }

    /// <summary>
    /// 判断异常是不是"原生库没加载起来"。TlsClient 是静态字段，某些情况下首次访问会包一层
    /// TypeInitializationException，所以一并算进来。
    /// </summary>
    private static bool IsTlsNativeUnavailable(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException
            or MarshalDirectiveException;

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

    /// <summary>
    /// 走 tls-client 原生库的请求。
    ///
    /// 用 Firefox_135 指纹 + Firefox UA + 完整导航头：实测灾厄的 Cloudflare
    /// 对 Chrome_131 / Chrome_133 / Safari 指纹都弹"请稍候…"挑战（403），
    /// Firefox_135 能稳定拿到 200（API 与重定向列表均实测通过）。
    ///
    /// 所有请求共用一个 <see cref="TlsSessionId"/> 并打开 cookie jar，
    /// 让 __cf_bm / cf_clearance 之类 cookie 能跨请求保留。
    /// </summary>
    private static Response RequestViaTls(string url, DateTime? ifModifiedSince)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["User-Agent"] = FirefoxUserAgent,
            ["Accept"] = AcceptValue,
            ["Accept-Language"] = AcceptLanguageValue,
            ["Accept-Encoding"] = "gzip, deflate, br",
            ["Sec-Fetch-Dest"] = "document",
            ["Sec-Fetch-Mode"] = "navigate",
            ["Sec-Fetch-Site"] = "none",
            ["Sec-Fetch-User"] = "?1",
            ["Upgrade-Insecure-Requests"] = "1"
        };

        if (ifModifiedSince.HasValue)
        {
            headers["If-Modified-Since"] = new DateTimeOffset(
                DateTime.SpecifyKind(ifModifiedSince.Value, DateTimeKind.Utc)).ToString("R");
        }

        var input = new RequestInput
        {
            SessionId = TlsSessionId,
            FollowRedirects = true,
            TLSClientIdentifier = ClientIdentifiers.Firefox_135,
            TimeoutSeconds = 20,
            RequestUrl = EncodeUrl(url),
            RequestMethod = "GET",
            Headers = headers,
            HeaderOrder = headers.Keys.ToList(),
            WithDefaultCookieJar = true
        };

        return TlsClientNative.Request(input);
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

    /// <summary>只给"等响应头"设上限；连接建立也在这 30 秒里。</summary>
    private static async Task<HttpResponseMessage> SendWithHeadersTimeoutAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HeadersTimeout);
        try
        {
            return await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateRetryableTimeout($"等待响应头超时（{HeadersTimeout.TotalSeconds:0}s）");
        }
    }

    /// <summary>续传时带上 If-Range：文件变了服务器会回 200 全量，而不是拼出错位的数据。</summary>
    private static void ApplyIfRange(HttpRequestMessage request, DownloadResumeState resume)
    {
        if (!string.IsNullOrEmpty(resume.ETag) &&
            EntityTagHeaderValue.TryParse(resume.ETag, out var etag) &&
            etag is { IsWeak: false })
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(etag);
            return;
        }

        if (resume.LastModified is { } lastModified)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(
                new DateTimeOffset(DateTime.SpecifyKind(lastModified, DateTimeKind.Utc)));
        }
    }

    /// <summary>
    /// 边读边攒，每次 ReadAsync 单独给一个 30 秒空闲超时：
    /// 只要还在收数据就重置计时，10MB 传 10 分钟也不会被误杀；连续 30 秒没数据才抛可重试异常。
    /// </summary>
    private static async Task CopyBodyWithIdleTimeoutAsync(
        Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readCts.CancelAfter(BodyIdleTimeout);

                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw CreateRetryableTimeout(
                        $"响应体读取超时（{BodyIdleTimeout.TotalSeconds:0}s 没有收到数据），准备续传");
                }

                if (read == 0) break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 超时用 HttpRequestError.Unknown + IOException 包装：
    /// 既不会被 IsConnectivityFailure 误判成"断网"（那会把整个任务暂停），
    /// 又会让 BatchTaskScheduler 按普通失败重试。
    /// </summary>
    private static HttpRequestException CreateRetryableTimeout(string message)
        => new(HttpRequestError.Unknown, message, new IOException(message));

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
