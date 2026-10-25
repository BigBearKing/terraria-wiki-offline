using System.Diagnostics;
using System.Net;
using System.Text;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    /// <summary>
    /// iframe 内容源的「路由 + 取内容」部分。与承载方式无关：
    /// 三个平台的虚拟主机处理器（Windows 的 WebView2 事件、Apple 的 WKURLSchemeHandler、
    /// Android 的 ShouldInterceptRequest）都调用同一份实现，所以各平台行为必然一致。
    ///
    /// 本类**不含任何承载细节**（表头、状态码、CORS、If-None-Match、连接关闭），
    /// 这些留在各界面的拦截层。
    /// </summary>
    public class WikiContentProvider
    {
        private readonly ContentDbService _dbService;

        // ============================================================
        // 内存缓存
        //
        // 为什么必须有：实测一页 wiki 可以引用 1000+ 张图（灾厄 "2.1.0" 页 3790 个 <img>、
        // 泰拉中文"盔甲"页 1134 个），而**去重后的图片字节总共只有 0.2 MB** —— 全是小图标。
        // 每次请求都回 SQLite 读一遍 blob 是纯粹的浪费，而且会把 WebView 的请求队列压垮。
        // 缓存之后：一页的所有图标只查一次库，其余全是内存命中。
        //
        // 为什么要带上 wiki 名：路径本身不含 book，切 wiki 后必须不命中，否则会串数据。
        // 容量上限用"超了就整体清空"（与 AppService._pageCache 同款策略），实现简单且不会无界增长。
        // ============================================================
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WikiContentResponse> Cache = new();
        private static long _cacheBytes;
        private const long MaxCacheBytes = 128L * 1024 * 1024;
        /// <summary>单条超过这个大小就不缓存，避免一张大图把预算吃掉。</summary>
        private const int MaxCacheItemBytes = 4 * 1024 * 1024;

        private void TryCache(string key, WikiContentResponse response)
        {
            var size = response.Body.Length;
            if (size > MaxCacheItemBytes)
                return;

            if (Interlocked.Read(ref _cacheBytes) + size > MaxCacheBytes)
            {
                Cache.Clear();
                Interlocked.Exchange(ref _cacheBytes, 0);
            }

            if (Cache.TryAdd(key, response))
                Interlocked.Add(ref _cacheBytes, size);
        }

        /// <summary>
        /// 清空资源缓存。数据被整库替换/更新后必须调用，否则会继续返回旧图片。
        /// 调用点：与 <see cref="AppService.InvalidatePageCache"/> 同一批（整库替换、页面更新、迁移）。
        /// </summary>
        public static void InvalidateCache()
        {
            Cache.Clear();
            Interlocked.Exchange(ref _cacheBytes, 0);
        }

        public WikiContentProvider(ContentDbService dbService)
        {
            _dbService = dbService;
        }

        /// <summary>
        /// 剥掉 URL 里的 wiki 段 —— **仅当它确实等于当前 wiki 的 DataFolder**。
        ///
        /// 这样两种承载（HTTP / 虚拟主机）对"路径"的约定完全一致，可以互相回退：
        ///   <c>/&lt;book&gt;/images/a.png</c>（iframe 文档内相对路径的解析结果）→ <c>/images/a.png</c>
        ///   <c>/_common/iframe-bridge.js</c>、<c>/src/x.png</c>（根绝对路径）     → 原样保留
        ///
        /// 为什么不能无条件剥第一段：<c>/_common/**</c>、<c>/src/**</c> 是**根绝对路径**，
        /// 浏览器按根解析、不会补上 iframe 的路径前缀；无条件剥会把
        /// <c>/_common/iframe-bridge.js</c> 变成 <c>/iframe-bridge.js</c> → 全部 404。
        /// </summary>
        public static string StripBookSegment(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || absolutePath[0] != '/')
                return "/";

            var book = App.AppStateManager?.ActiveWikiBook?.DataFolder;
            if (string.IsNullOrEmpty(book))
                return absolutePath;

            var prefix = "/" + book;
            if (!absolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return absolutePath;

            var rest = absolutePath.Substring(prefix.Length);
            return rest.Length == 0 ? "/" : rest;
        }

        /// <summary>
        /// 按路径取内容。
        /// <paramref name="path"/> 必须是**已解码**的绝对路径（如 "/src/sword.png"、"/"），
        /// 解码由调用方负责：HTTP 侧原来用 WebUtility.UrlDecode，
        /// WebView 拦截侧拿到的 request.Uri 本身就是解码后的。
        /// 找不到返回 null（404）；取内容过程出错抛异常，由调用方决定如何记录与响应。
        /// </summary>
        public async Task<WikiContentResponse?> GetAsync(string path)
        {
            // 内存缓存：key 里带上当前 wiki，切换 wiki 后自然不命中，不会串数据。
            var cacheKey = (App.AppStateManager?.ActiveWikiBook?.DataFolder ?? "default") + "\n" + path;
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var response = await GetCoreAsync(path);

            if (response is not null)
                TryCache(cacheKey, response);

            return response;
        }

        private async Task<WikiContentResponse?> GetCoreAsync(string path)
        {
            // ==========================================
            // 路由策略 1: 数据库资源 (/src/...)
            // ==========================================
            if (path.StartsWith("/src/", StringComparison.OrdinalIgnoreCase))
            {
                // 提取文件名: "/src/sword.png" -> "sword.png"
                string fileName = path.Substring(5);
                var asset = await _dbService.GetItemAsync<WikiAsset>(fileName);

                // ★ 关键：库里存的是**解码后**的文件名，而页面 HTML 里引用的是**转义后**的
                //   （爬虫用 Uri.EscapeDataString 写 src，例如 Dark_Artist%27s_Robes.png）。
                //   旧 HTTP 承载是 WebUtility.UrlDecode 之后再查库，所以能命中；
                //   三个 WebView 拦截承载拿到的是转义路径，直接查库会全部 miss，
                //   于是返回 1×1 占位图 —— 表现为"某些图片没有"（带 ' ( ) 等字符的那些）。
                //   这里加一次解码兜底，两种形态都能命中。
                if (asset is null)
                {
                    var decoded = WebUtility.UrlDecode(fileName);
                    if (!string.IsNullOrEmpty(decoded) &&
                        !string.Equals(decoded, fileName, StringComparison.Ordinal))
                    {
                        asset = await _dbService.GetItemAsync<WikiAsset>(decoded);
                    }
                }

                if (asset != null)
                {
                    return new WikiContentResponse(asset.Data, asset.MimeType, ComputeETag(path, asset.Data), CacheControlFor(path));
                }

                // 数据库里没有这条资源：返回 1×1 透明占位图，避免页面布局塌陷。
                // ContentType 必须与占位图字节一致，否则浏览器按图片解码会失败。
                Debug.WriteLine($"[Asset Miss] {fileName}");

                bool wantSvg = GetMimeType(fileName) == "image/svg+xml";
                var contentType = wantSvg ? "image/svg+xml" : "image/png";
                var buffer = wantSvg
                    ? Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>")
                    : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

                return new WikiContentResponse(buffer, contentType, ComputeETag(path, buffer), CacheControlFor(path));
            }

            // ==========================================
            // 路由策略 2: 本地静态文件 (Resources/Raw/Web/...)
            // ==========================================
            // 如果请求根目录，默认返回 index.html
            if (path == "/") path = "/index.html";

            // 共享 bridge 位于 Blazor 宿主的 wwwroot，需要让 iframe 也能访问它。
            string assetPath;
            if (path.Equals("/_common/iframe-bridge.js", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "wwwroot/js/iframe-bridge.js";
            }
            else if (path.Equals("/_common/wiki-app-common.js", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Web/_common/wiki-app-common.js";
            }
            else if (path.Equals("/_common/handy-scroll.js", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Web/_common/handy-scroll.js";
            }
            else if (path.Equals("/_common/wiki-math.js", StringComparison.OrdinalIgnoreCase))
            {
                // 离线公式渲染脚本（MathJax 配置与渲染调度）
                assetPath = "Web/_common/wiki-math.js";
            }
            else if (path.StartsWith("/_common/mathjax/", StringComparison.OrdinalIgnoreCase))
            {
                // 共享的 MathJax 离线资源（tex-chtml-full.js + 字体）
                assetPath = "Web/_common/" + path.Substring("/_common/".Length);
            }
            else if (path.StartsWith("/_common/viewer/", StringComparison.OrdinalIgnoreCase))
            {
                // 共享的 Viewer.js 资源（各 wiki 共用一份）
                assetPath = "Web/_common/" + path.Substring("/_common/".Length);
            }
            else
            {
                // 拼接 Resources/Raw 下的路径
                // 假设你的 HTML 文件放在 Resources/Raw/Web 文件夹下
                assetPath = "Web/" + App.AppStateManager.ActiveWikiBook.DataFolder + path;
            }

            if (!await FileSystem.AppPackageFileExistsAsync(assetPath))
            {
                // Raw 资源里没找到
                Debug.WriteLine($"[File 404] {assetPath}");
                return null;
            }

            using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var fileBytes = ms.ToArray();

            var mime = GetMimeType(path);

            // 所有 wiki 页面都要改写脚本/样式 URL（不只是 index.html）：
            // iframe-bridge.js 等 /_common/ 资源以前按 immutable 缓存，不换 URL 的话
            // 页面内的旧脚本永远不会更新（页内搜索等新逻辑就失效）。
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                fileBytes = AddWikiResourceVersion(fileBytes);

            return new WikiContentResponse(fileBytes, mime, ComputeETag(path, fileBytes), CacheControlFor(path));
        }

        /// <summary>
        /// ETag 必须是纯 ASCII：中文、全角标点等字符在写入 HTTP 表头时会被按 Latin-1
        /// 截断成低字节，例如全角括号 "（"(U+FF08) 会变成控制字符 0x08，
        /// 使 WebHeaderCollection 抛 ArgumentException 并中断整个响应。
        /// 灾厄中文 wiki 大量使用中文文件名 + 全角括号消歧义后缀，正是踩中这一点。
        /// </summary>
        private static string ComputeETag(string path, byte[] buffer)
        {
            var wikiKey = Uri.EscapeDataString(App.AppStateManager.ActiveWikiBook?.DataFolder ?? "default");
            return $"\"{wikiKey}:{Uri.EscapeDataString(path)}:{buffer.Length:x}\"";
        }

        private static string CacheControlFor(string path) =>
            IsLongLivedStaticFile(path)
                ? "public, max-age=31536000, immutable"
                : "no-cache";

        // 简单的 MIME 类型映射辅助方法
        private static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }

        private static bool IsLongLivedStaticFile(string path)
        {
            // iframe-bridge.js 随 app 版本更新（页内搜索等逻辑在里面）。
            // 若也按 immutable 缓存一年，旧脚本会一直生效，新加的方法在 iframe 里根本不存在；
            // 这里让它走 no-cache + ETag 校验，内容变了就重新下载。
            if (path.Equals("/_common/iframe-bridge.js", StringComparison.OrdinalIgnoreCase))
                return false;

            return path.StartsWith("/_common/", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] AddWikiResourceVersion(byte[] html)
        {
            var bookKey = App.AppStateManager.ActiveWikiBook?.DataFolder ?? "default";
            var version = Uri.EscapeDataString(bookKey);
            var content = Encoding.UTF8.GetString(html);

            // ★ 改动 iframe-bridge.js（页内搜索等逻辑在里面）时把这里 +1。
            // 该脚本以前按 immutable 缓存一年，URL 不变的话旧脚本会一直生效，
            // 新方法在 iframe 里根本不存在；加上 v= 让新版本强制换 URL。
            const string bridgeVersion = "2";

            content = content.Replace("src=\"/_common/iframe-bridge.js\"", $"src=\"/_common/iframe-bridge.js?book={version}&v={bridgeVersion}\"")
                .Replace("src=\"/_common/wiki-app-common.js\"", $"src=\"/_common/wiki-app-common.js?book={version}\"")
                .Replace("src=\"/_common/wiki-math.js\"", $"src=\"/_common/wiki-math.js?book={version}\"")
                .Replace("src=\"/_common/handy-scroll.js\"", $"src=\"/_common/handy-scroll.js?book={version}\"")
                .Replace("src=\"/_common/viewer/viewer.min.js\"", $"src=\"/_common/viewer/viewer.min.js?book={version}\"")
                .Replace("href=\"/_common/viewer/viewer.min.css\"", $"href=\"/_common/viewer/viewer.min.css?book={version}\"")
                .Replace("src=\"app.js\"", $"src=\"app.js?book={version}\"")
                .Replace("href=\"vector.css\"", $"href=\"vector.css?book={version}\"")
                .Replace("href=\"common.css\"", $"href=\"common.css?book={version}\"")
                .Replace("href=\"Nunito.css\"", $"href=\"Nunito.css?book={version}\"")
                .Replace("href=\"layout.css\"", $"href=\"layout.css?book={version}\"")
                .Replace("href=\"theme/dark.css\"", $"href=\"theme/dark.css?book={version}\"")
                .Replace("href=\"theme/light.css\"", $"href=\"theme/light.css?book={version}\"");

            return Encoding.UTF8.GetBytes(content);
        }
    }

    /// <summary>
    /// <see cref="WikiContentProvider"/> 的返回结果。
    /// <see cref="ETag"/> / <see cref="CacheControl"/> 在内容侧算好（它们只跟内容与路径有关），
    /// 但「要不要回 304」由调用方按请求的 If-None-Match 决定 —— 那是 HTTP 语义。
    /// </summary>
    public sealed record WikiContentResponse(byte[] Body, string ContentType, string ETag, string CacheControl);
}
