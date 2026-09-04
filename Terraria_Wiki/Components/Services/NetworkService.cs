using LuYao.TlsClient;
using System.Net;

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

    private static readonly HttpClient TlsHttpClient = new(
        new TlsClientHttpMessageHandler(TlsClient))
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
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
            using var request = CreateRequest(url, useTls: true);
            using var response = await TlsHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
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
        using var request = CreateRequest(url, useTls);
        if (ifModifiedSince.HasValue)
        {
            request.Headers.IfModifiedSince = new DateTimeOffset(
                DateTime.SpecifyKind(ifModifiedSince.Value, DateTimeKind.Utc));
        }

        var client = useTls ? TlsHttpClient : HttpClient;
        using var responseMessage = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (responseMessage.StatusCode != HttpStatusCode.NotModified)
        {
            responseMessage.EnsureSuccessStatusCode();
        }

        var data = responseMessage.StatusCode == HttpStatusCode.NotModified
            ? []
            : await responseMessage.Content.ReadAsByteArrayAsync(cancellationToken);

        return new NetworkResponse(
            data,
            responseMessage.StatusCode,
            responseMessage.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            responseMessage.Content.Headers.LastModified?.UtcDateTime);
    }

    private static HttpRequestMessage CreateRequest(string url, bool useTls)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, EncodeUrl(url));
        if (useTls)
        {
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        }

        return request;
    }

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
