using System.Diagnostics;
using System.Net;

namespace Terraria_Wiki.Services
{
    /// <summary>
    /// iframe 内容源的 **HTTP 承载**：只负责监听 127.0.0.1、解析路径、写响应头与关闭连接。
    /// 「路由 + 取内容」在 <see cref="WikiContentProvider"/>，两者分开是为了让同一份路由
    /// 将来能同时服务于本监听器和 WebView 虚拟主机（https://wiki.local）的拦截处理器。
    /// </summary>
    public class LocalWebServer
    {
        private HttpListener _listener;
        private readonly WikiContentProvider _content;
        private readonly LogService _log;
        private readonly string _prefix;

        // 构造函数注入内容提供者与日志服务
        // port 可指定监听端口，默认 55000（与 MainLayout 里 iframe 的地址一致，行为不变）。
        // 之所以允许改：Windows 上 HTTP.SYS 对端口是独占的，App 运行时 55000 被占，
        // 开发用的浏览器预览服务器需要能在 App 同时开着的情况下换个端口起服务。
        public LocalWebServer(WikiContentProvider content, LogService logService, int port = 55000)
        {
            _content = content;
            _log = logService;
            _listener = new HttpListener();

            // 监听本地端口
            _prefix = $"http://127.0.0.1:{port}/";
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
                    // 以前这里只写 Debug.WriteLine，Release 包里等于静默失败：
                    // 服务器没起来，iframe 照样被渲染出来，用户看到的是一句
                    // "无法访问此页面 / ERR_CONNECTION_REFUSED"，却没有任何可查的痕迹。
                    Debug.WriteLine($"[Web Server Start Error] {ex.Message}");
                    _log.Error($"[Web Server Start Error] {_prefix}", ex);
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

            WikiContentResponse? content = null;
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

                // iframe 的地址里带 wiki 段（…/<book>/…），它不是内容路径的一部分；
                // 与虚拟主机承载共用同一个剥段规则，所以两种承载可以互相回退。
                content = await _content.GetAsync(WikiContentProvider.StripBookSegment(path));
                if (content is null)
                {
                    statusCode = 404; // 内容层没找到
                }
                else
                {
                    contentType = content.ContentType;
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                Debug.WriteLine($"[Server Error] {path} : {ex.Message}");
                _log.Error($"[Server Error] {path}", ex);
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

                if (content != null && statusCode == 200)
                {
                    response.Headers["ETag"] = content.ETag;

                    if (string.Equals(request.Headers["If-None-Match"], content.ETag, StringComparison.Ordinal))
                    {
                        response.StatusCode = 304;
                        response.ContentLength64 = 0;
                        return;
                    }

                    response.Headers["Cache-Control"] = content.CacheControl;
                }

                if (content != null)
                {
                    response.ContentLength64 = content.Body.Length;
                    await response.OutputStream.WriteAsync(content.Body, 0, content.Body.Length);
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
    }
}
