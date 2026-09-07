using LuYao.TlsClient;
using System.Net;
using System.Text;

namespace Terraria_Wiki.Services;

public sealed record NetworkResponse(
    byte[] Data,
    HttpStatusCode StatusCode,
    string ContentType,
    DateTime? LastModified);

public static class NetworkService
{
    public static bool IsNetworkAvailable =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private static readonly TlsClient TlsClient = new()
    {
        TLSClientIdentifier = ClientIdentifiers.Chrome_131,
        FollowRedirect = true,
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(150)
    };

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private const string BrowserClientHints =
        "\"Not_A Brand\";v=\"99\", \"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\"";
    private const string CrawlerUserAgent =
        "TerrariaWikiScraper/1.0 (contact: bigbearkingus@gmail.com)";

    static NetworkService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(CrawlerUserAgent);
    }

    public static async Task<string> GetStringAsync(
        string url,
        bool useTls = false,
        CancellationToken cancellationToken = default)
    {
        if (useTls)
        {
            var request = TlsClient.CreateRequest();
            request.RequestUrl = EncodeUrl(url);
            request.RequestMethod = "GET";
            AddTlsBrowserHeaders(request);

            var response = await Task.Run(() => TlsClient.Request(request), cancellationToken);
            EnsureSuccessStatusCode(response.Status, url);
            return response.Body;
        }

        var encodedUrl = EncodeUrl(url);
        using var responseMessage = await HttpClient.GetAsync(
            encodedUrl,
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
        if (useTls)
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

        using var requestMessage = CreateRequest(url, useTls: false);
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

    private static HttpRequestMessage CreateRequest(string url, bool useTls)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, EncodeUrl(url));
        if (useTls)
            AddHttpBrowserHeaders(request);
        return request;
    }

    private static void AddTlsBrowserHeaders(dynamic request)
    {
        request.Headers["User-Agent"] = BrowserUserAgent;
        request.Headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
        request.Headers["Accept-Language"] = "zh-CN,zh;q=0.9";
        request.Headers["Sec-CH-UA"] = BrowserClientHints;
        request.Headers["Sec-CH-UA-Mobile"] = "?0";
        request.Headers["Sec-CH-UA-Platform"] = "\"Windows\"";
    }

    private static void AddHttpBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA", BrowserClientHints);
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
    }

    private static void EnsureSuccessStatusCode(int status, string url)
    {
        if (status < 200 || status >= 300)
            throw new HttpRequestException($"TLS request failed with status {status} for {url}", null, (HttpStatusCode)status);
    }

    private static string? GetResponseHeader(dynamic headers, string name)
    {
        if (headers is null)
            return null;

        dynamic values = null;
        return headers.TryGetValue(name, out values) && values.Count > 0
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
