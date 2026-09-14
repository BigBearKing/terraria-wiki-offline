using System.Diagnostics;
using System.Net;
using System.Text;
using System.Collections.Concurrent;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    public class LocalWebServer
    {
        private HttpListener _listener;
        private readonly ContentDbService _dbService;
        private readonly string _prefix;
        private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType)> _staticFileCache = new();

        // 构造函数注入数据库服务
        public LocalWebServer(ContentDbService dbService)
        {
            _dbService = dbService;
            _listener = new HttpListener();

            // 监听本地 55000 端口
            _prefix = "http://127.0.0.1:55000/";
            _listener.Prefixes.Add(_prefix);
        }

        public async Task Start()
        {
            // 如果监听器为空或未在运行，则重新创建并启动
            if (_listener == null || !_listener.IsListening)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(_prefix);

                try
                {
                    _listener.Start();
                    Task.Run(ListenLoop);
                    Debug.WriteLine($"[Web Server] Started at {_prefix}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Web Server Start Error] {ex.Message}");
                    // 端口可能还没完全释放 (TIME_WAIT)，这里可以做个重试机制
                }
            }

            }

            public void Stop()
        {
            if (_listener != null && _listener.IsListening)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch { /* 忽略关闭时的异常 */ }
                finally
                {
                    _listener = null; // 设为空，保证下次 Resume 时能重新创建
                    Debug.WriteLine("[Web Server] Stopped.");
                }
            }
        }


        private async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    // 不等待处理，直接开启新任务处理请求，支持并发
                    _ = ProcessRequestAsync(context);
                }
                catch (HttpListenerException)
                {
                    // 服务器关闭时会抛出异常，忽略即可
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Web Server Error] {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            byte[]? buffer = null;
            string contentType = "text/plain";
            int statusCode = 200;
            string path = "/";

            try
            {
                // 获取路径，例如 "/index.html" 或 "/src/sword.png"。
                // UrlDecode 很重要，防止文件名中有空格被转义成 %20。
                // 注意：解析必须在 try 内，否则异常会逃逸成"未观察到的任务异常"，
                // 导致该请求拿到零响应（浏览器侧表现为 ERR_EMPTY_RESPONSE）。
                path = WebUtility.UrlDecode(request.Url?.AbsolutePath ?? "/");
                // ==========================================
                // 路由策略 1: 数据库资源 (/db/...)
                // ==========================================
                if (path.StartsWith("/src/", StringComparison.OrdinalIgnoreCase))
                {


                    // 提取文件名: "/src/sword.png" -> "sword.png"
                    string fileName = path.Substring(5);
                    var asset = await _dbService.GetItemAsync<WikiAsset>(fileName);

                    if (asset != null)
                    {
                        buffer = asset.Data;
                        contentType = asset.MimeType;
                    }
                    else
                    {
                        // 数据库里没有这条资源：返回 1×1 透明占位图，避免页面布局塌陷。
                        // ContentType 必须与占位图字节一致，否则浏览器按图片解码会失败。
                        Debug.WriteLine($"[Asset Miss] {fileName}");

                        bool wantSvg = GetMimeType(fileName) == "image/svg+xml";
                        contentType = wantSvg ? "image/svg+xml" : "image/png";
                        buffer = wantSvg
                            ? Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>")
                            : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
                    }
                }
                // ==========================================
                // 路由策略 2: 本地静态文件 (Resources/Raw/Web/...)
                // ==========================================
                else
                {
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

                    if (await FileSystem.AppPackageFileExistsAsync(assetPath))
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        buffer = ms.ToArray();

                        contentType = GetMimeType(path);

                        if (path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                            buffer = AddWikiResourceVersion(buffer);

                        if (IsCacheableStaticFile(path))
                            _staticFileCache.TryAdd(assetPath, (buffer, contentType));
                    }
                    else
                    {
                        statusCode = 404; // Raw 资源里没找到
                        Debug.WriteLine($"[File 404] {assetPath}");
                    }

                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                Debug.WriteLine($"[Server Error] {path} : {ex.Message}");
            }

            // ==========================================
            // 发送响应
            // ==========================================
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = contentType;

                // 解决跨域问题 (CORS)，防止 WebView 报错
                response.Headers.Add("Access-Control-Allow-Origin", "*");

                if (buffer != null && statusCode == 200)
                {
                    // ETag 必须是纯 ASCII：中文、全角标点等字符在写入表头时会被按 Latin-1
                    // 截断成低字节，例如全角括号 "（"(U+FF08) 会变成控制字符 0x08，
                    // 使 WebHeaderCollection 抛 ArgumentException 并中断整个响应。
                    // 灾厄中文 wiki 大量使用中文文件名 + 全角括号消歧义后缀，正是踩中这一点。
                    var wikiKey = Uri.EscapeDataString(App.AppStateManager.ActiveWikiBook?.DataFolder ?? "default");
                    var etag = $"\"{wikiKey}:{Uri.EscapeDataString(path)}:{buffer.Length:x}\"";
                    response.Headers["ETag"] = etag;

                    if (string.Equals(request.Headers["If-None-Match"], etag, StringComparison.Ordinal))
                    {
                        response.StatusCode = 304;
                        response.ContentLength64 = 0;
                        return;
                    }

                    response.Headers["Cache-Control"] = IsLongLivedStaticFile(path)
                        ? "public, max-age=31536000, immutable"
                        : "no-cache";
                }

                if (buffer != null)
                {
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                // 不要静默吞掉：客户端断开是常见情况，但写表头失败等错误必须留下线索。
                Debug.WriteLine($"[Response Error] {path} : {ex.Message}");
            }
            finally
            {
                // 无论成功、异常还是提前 return，都必须关闭响应。
                // 否则连接会被一直占用，同域连接（Chromium 为 6 条）耗尽后，
                // 后续图片请求只会排队而不会发出。
                try { response.Close(); } catch { }
            }
        }

        // 简单的 MIME 类型映射辅助方法
        private string GetMimeType(string path)
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

        private static bool IsCacheableStaticFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLongLivedStaticFile(string path)
        {
            return path.StartsWith("/_common/", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] AddWikiResourceVersion(byte[] html)
        {
            var bookKey = App.AppStateManager.ActiveWikiBook?.DataFolder ?? "default";
            var version = Uri.EscapeDataString(bookKey);
            var content = Encoding.UTF8.GetString(html);

            content = content.Replace("src=\"/_common/iframe-bridge.js\"", $"src=\"/_common/iframe-bridge.js?book={version}\"")
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
}