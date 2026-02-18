using System.Net;
using System.Text;
using System.Diagnostics;
using Terraria_Wiki.Services;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    public class LocalWebServer
    {
        private readonly HttpListener _listener;
        private readonly ContentDbService _dbService;
        private readonly string _prefix;

        // 构造函数注入数据库服务
        public LocalWebServer(ContentDbService dbService)
        {
            _dbService = dbService;
            _listener = new HttpListener();

            // 监听本地 55000 端口
            _prefix = "http://localhost:55000/";
            _listener.Prefixes.Add(_prefix);
        }

        public void Start()
        {
            if (!_listener.IsListening)
            {
                _listener.Start();
                Task.Run(ListenLoop);
                Debug.WriteLine($"[Web Server] Started at {_prefix}");
            }
        }

        public void Stop()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
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

            // 获取路径，例如 "/index.html" 或 "/db/sword.png"
            // UrlDecode 很重要，防止文件名中有空格被转义成 %20
            string rawPath = request.Url?.AbsolutePath ?? "/";
            string path = WebUtility.UrlDecode(rawPath);

            byte[]? buffer = null;
            string contentType = "text/plain";
            int statusCode = 200;

            try
            {
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
                        statusCode = 404; // 数据库里没找到
                        Debug.WriteLine($"[DB 404] {fileName}");
                    }
                }
                // ==========================================
                // 路由策略 2: 本地静态文件 (Resources/Raw/Web/...)
                // ==========================================
                else
                {
                    // 如果请求根目录，默认返回 index.html
                    if (path == "/") path = "/index.html";
#if DEBUG
                    // 1. 获取当前应用程序运行的目录 (通常是 bin/Debug/...)
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    DirectoryInfo projectRoot = new DirectoryInfo(baseDir);

                    // 2. 向上递归查找项目根目录
                    // 逻辑：不断获取父目录，直到找到一个目录下包含 "Resources" 文件夹
                    while (projectRoot != null && !Directory.Exists(Path.Combine(projectRoot.FullName, "Resources")))
                    {
                        projectRoot = projectRoot.Parent;
                    }

                    if (projectRoot != null)
                    {
                        // 3. 拼接本地物理路径：项目根目录 + Resources/Raw/Web + 请求的文件名
                        // TrimStart 移除路径开头的 / 或 \，防止 Path.Combine 将其视为根路径
                        string localPath = Path.Combine(projectRoot.FullName, "Resources", "Raw", "Web", path.TrimStart('/', '\\'));

                        if (File.Exists(localPath))
                        {
                            // 4. 读取物理文件（支持热重载：修改 html 后刷新 WebView 即可生效）
                            buffer = await File.ReadAllBytesAsync(localPath);
                            contentType = GetMimeType(path);
                            Debug.WriteLine($"[Local Debug] Loaded: {localPath}");
                        }
                        else
                        {
                            statusCode = 404;
                            Debug.WriteLine($"[Local Debug 404] File not found on disk: {localPath}");
                        }
                    }
                    else
                    {
                        statusCode = 500;
                        Debug.WriteLine("[Local Debug Error] Could not locate Project Root directory.");
                    }




#else
                    // 拼接 Resources/Raw 下的路径
                    // 假设你的 HTML 文件放在 Resources/Raw/Web 文件夹下
                    string assetPath = "Web" + path;

                    if (await FileSystem.AppPackageFileExistsAsync(assetPath))
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        buffer = ms.ToArray();

                        contentType = GetMimeType(path);
                    }
                    else
                    {
                        statusCode = 404; // Raw 资源里没找到
                        Debug.WriteLine($"[File 404] {assetPath}");
                    }




#endif


                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                Debug.WriteLine($"[Server Error] {ex.Message}");
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

                if (buffer != null)
                {
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }

                response.Close();
            }
            catch { /* 忽略响应发送失败（比如客户端断开连接） */ }
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
                ".json" => "application/json",
                _ => "application/octet-stream"
            };
        }
    }
}