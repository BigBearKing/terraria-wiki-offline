#if WINDOWS
using Microsoft.Web.WebView2.Core;
#elif IOS || MACCATALYST
using Foundation;
using WebKit;
#endif

namespace Terraria_Wiki.Services
{
    /// <summary>
    /// iframe 内容源的**虚拟主机承载**：把 <see cref="WikiContentProvider"/> 提供的内容
    /// 直接交给 WebView，不经过任何端口。三个平台各用一种机制，但共用同一个内容提供者。
    ///
    /// R4 之前还并存一个本地 HTTP 服务器（LocalWebServer + HttpListener），可作回退；
    /// 现在已删除 —— 虚拟主机是三平台唯一的内容承载（不占端口、无防火墙提示、无白屏）。
    ///
    /// | 平台 | 机制 | URL 形态 |
    /// |---|---|---|
    /// | Windows | WebView2 <c>AddWebResourceRequestedFilter</c> + <c>WebResourceRequested</c> | <c>https://wiki.local/&lt;book&gt;/</c> |
    /// | iOS / MacCatalyst | <c>WKURLSchemeHandler</c>（经 <c>BlazorWebViewInitializing</c> 注册） | <c>terrariawiki://wiki/&lt;book&gt;/</c> |
    /// | Android | 包装 <c>WebViewClient</c> + <c>ShouldInterceptRequest</c> | <c>https://wiki.local/&lt;book&gt;/</c> |
    ///
    /// ★ Windows 侧三个踩过的坑（真机日志换来的，改之前先读）：
    ///   ① <c>AddWebResourceRequestedFilter</c> 必须用**三参数**重载并传
    ///      <c>CoreWebView2WebResourceRequestSourceKinds.All</c>。两参数重载只覆盖"文档来源"，
    ///      iframe 里的 css/js/图片/字体不算文档来源 → 那些请求会真的做 DNS 解析，
    ///      全部 <c>ERR_NAME_NOT_RESOLVED</c>（主文档却能正常显示，极具迷惑性）。
    ///   ② <c>CreateWebResourceResponse</c> 在 WinRT 投影里要 <c>IRandomAccessStream</c>，
    ///      不是 <c>System.IO.Stream</c>（net462 那套投影才是 Stream），
    ///      所以必须 <c>new MemoryStream(bytes).AsRandomAccessStream()</c> 两步转。
    ///   ③ 请求头的 <c>GetHeader</c> 对**不存在**的表头抛 COMException（不是返回 null），
    ///      要用必须先 Contains。当前本类不读请求头。
    /// </summary>
    public sealed class WikiVirtualHost
    {
        /// <summary>
        /// 虚拟主机名，Windows 与 Android 共用（两端都是 https 形态，
        /// 只是拦截机制不同：WebView2 过滤器 / WebViewClient.ShouldInterceptRequest）。
        /// </summary>
        public const string Host = "wiki.local";

#if IOS || MACCATALYST
        /// <summary>Apple 平台注册的私有 scheme。WebKit 不允许为 https 注册 scheme handler，
        /// 且 MAUI 自己的 BlazorWebView 已占用了一个 scheme，所以必须另起一个。</summary>
        public const string AppleScheme = "terrariawiki";

        /// <summary>私有 scheme 里的固定 host 段。父目录必须带斜杠，否则上级相对路径会被截断。</summary>
        private const string AppleHostSegment = "wiki";
#endif

#if WINDOWS
        private static readonly object AttachedLock = new();
        private static readonly HashSet<CoreWebView2> Attached = [];
#elif IOS || MACCATALYST
        private static bool _appleSchemeReady;
#elif ANDROID
        private static bool _androidReady;
#endif

        /// <summary>iframe 内容基址变化（承载挂上/失败）时触发，供 MainLayout 重建 iframe 的 URL。</summary>
        public static event Action? ContentBaseChanged;

        private readonly WikiContentProvider _content;
        private readonly LogService _log;

        public WikiVirtualHost(WikiContentProvider content, LogService log)
        {
            _content = content;
            _log = log;
        }

        /// <summary>
        /// iframe 的内容基址。R4 之后本地 HTTP 服务器已删除，这里不再有回退分支 ——
        /// 三个平台一律返回各自的虚拟主机基址。
        ///
        /// 承载没挂上时的行为：iframe 的请求会失败（页面空白/报错），而不是悄悄退到另一条路。
        /// 这是有意的 —— 之前"静默回退"会把真正的失败藏起来（表现为"某些图片就是不出来"）。
        /// 挂载失败时 <see cref="TryAttach"/> / TryRegister* 会写日志，排查有据。
        /// </summary>
        public static string ContentBase
        {
            get
            {
#if IOS || MACCATALYST
                return $"{AppleScheme}://{AppleHostSegment}";
#else
                return $"https://{Host}";
#endif
            }
        }

        /// <summary>当前平台的虚拟主机承载是否已生效（仅用于日志/诊断）。</summary>
        public static bool IsAvailable
        {
            get
            {
#if WINDOWS
                return IsAttached;
#elif IOS || MACCATALYST
                return _appleSchemeReady;
#elif ANDROID
                return _androidReady;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 把请求的绝对路径规整成"内容层能认的路径"。
        ///
        /// 各承载的 URL 里都夹了额外段，必须剥掉，规则完全一致：
        ///   * Windows：<c>/&lt;book&gt;/images/a.png</c> → <c>/images/a.png</c>
        ///             <c>/_common/x.js</c>（根绝对路径，本来就没有 book 段）→ 原样
        ///   * Apple：额外多一段 host（<c>/wiki</c>）也要剥：
        ///             <c>/wiki/&lt;book&gt;/images/a.png</c> → <c>/images/a.png</c>
        ///             <c>/wiki/_common/x.js</c> → <c>/_common/x.js</c>
        ///
        /// 为什么不能无条件剥第一段：<c>/_common/**</c>、<c>/src/**</c> 是**根绝对路径**，
        /// 浏览器按根解析、不会补上文档的路径前缀；无条件剥会把
        /// <c>/_common/iframe-bridge.js</c> 变成 <c>/iframe-bridge.js</c> → 全部 404。
        /// </summary>
        public static string ToContentPath(string absolutePath)
        {
            var path = absolutePath;

#if IOS || MACCATALYST
            // 先剥私有 scheme 的 host 段
            var hostPrefix = "/" + AppleHostSegment;
            if (path.StartsWith(hostPrefix + "/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(hostPrefix.Length);
            else if (path.Equals(hostPrefix, StringComparison.OrdinalIgnoreCase))
                path = "/";
#endif

            return WikiContentProvider.StripBookSegment(path);
        }

#if WINDOWS
        /// <summary>当前 WebView2 上虚拟主机是否已生效。</summary>
        public static bool IsAttached
        {
            get { lock (AttachedLock) { return Attached.Count > 0; } }
        }

        /// <summary>
        /// 挂上拦截器。必须在任何 <c>https://wiki.local</c> 请求发生**之前**调用，
        /// 否则先到的请求会按"未知主机"失败。
        /// 同一 WebView2 只挂一次（重复挂会让每个请求被处理两次）。
        /// </summary>
        public bool TryAttach(CoreWebView2 core)
        {
            if (core is null)
                return false;

            lock (AttachedLock)
            {
                if (!Attached.Add(core))
                    return true; // 已挂过
            }

            try
            {
                core.AddWebResourceRequestedFilter($"https://{Host}/*",
                    CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                core.WebResourceRequested += OnWebResourceRequested;

                _log.Info($"[VHost] 已挂上虚拟主机 {Host}");
                ContentBaseChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                lock (AttachedLock) { Attached.Remove(core); }
                _log.Error($"[VHost] 挂载虚拟主机 {Host} 失败 —— R4 之后没有 HTTP 回退路径，iframe 内容将无法加载", ex);
                ContentBaseChanged?.Invoke();
                return false;
            }
        }

        private async void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var uri = args.Request.Uri;
            // 本处理器必须 await（取内容要读数据库），事件参数能否跨 await 使用由 GetDeferral 保证。
            var deferral = args.GetDeferral();
            try
            {
                if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
                    !string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase))
                {
                    return; // 不是我们的主机，放给 WebView2 自己处理
                }

                var contentPath = ToContentPath(parsed.AbsolutePath);

                // environment 在 await 之前取好：await 之后再碰 sender/args 才有风险。
                var environment = sender.Environment;
                var content = await _content.GetAsync(contentPath);

                if (content is null)
                {
                    // 只有映射出错时才会走到这里；不进日志面板（可能一次刷很多条），
                    // 留在调试输出里供排查，同时浏览器控制台会看到 404。
                    System.Diagnostics.Debug.WriteLine($"[VHost] 404 {contentPath}（原始 URL 路径 {parsed.AbsolutePath}）");
                    args.Response = environment.CreateWebResourceResponse(
                        null, 404, "Not Found",
                        "Content-Type: text/plain\r\nAccess-Control-Allow-Origin: *\r\n");
                    return;
                }

                // byte[] → Stream → WinRT IRandomAccessStream（两步都要，见类注释②）
                // 不回 304：WebView2 的内存响应不落磁盘缓存，条件请求基本不会发生。
                var stream = new MemoryStream(content.Body, writable: false).AsRandomAccessStream();
                args.Response = environment.CreateWebResourceResponse(
                    stream, 200, "OK",
                    $"Content-Type: {content.ContentType}\r\n" +
                    "Access-Control-Allow-Origin: *\r\n" +
                    $"ETag: {content.ETag}\r\n" +
                    $"Cache-Control: {content.CacheControl}\r\n");
            }
            catch (Exception ex)
            {
                _log.Error($"[VHost] 处理请求异常：{uri}", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }
#elif IOS || MACCATALYST
        /// <summary>
        /// 在 WKWebView **创建之前**把私有 scheme 的处理器注册到配置上。
        /// 调用点：MainPage 订阅 BlazorWebView 的 BlazorWebViewInitializing 事件
        /// （那是唯一能拿到 WKWebViewConfiguration 的时机，创建之后再注册无效）。
        /// </summary>
        public bool TryRegisterAppleScheme(WKWebViewConfiguration? configuration)
        {
            if (configuration is null)
                return false;

            if (_appleSchemeReady)
                return true;

            try
            {
                configuration.SetUrlSchemeHandler(new WikiSchemeHandler(_content, _log), AppleScheme);

                _appleSchemeReady = true;
                _log.Info($"[VHost] 已挂上私有协议 {AppleScheme}://（iframe 内容不再走端口）");
                ContentBaseChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _appleSchemeReady = false;
                _log.Error($"[VHost] 注册私有协议 {AppleScheme}:// 失败 —— R4 之后没有 HTTP 回退路径，iframe 内容将无法加载", ex);
                ContentBaseChanged?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// 私有 scheme 的请求处理器。内容来自与 HTTP 承载**同一个** <see cref="WikiContentProvider"/>，
        /// 路径规整规则也共用 <see cref="ToContentPath"/>，所以两条路返回的东西必然一致。
        /// </summary>
        private sealed class WikiSchemeHandler : NSObject, IWKUrlSchemeHandler
        {
            private readonly WikiContentProvider _content;
            private readonly LogService _log;

            public WikiSchemeHandler(WikiContentProvider content, LogService log)
            {
                _content = content;
                _log = log;
            }

            public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
            {
                // 关键：Url 可能为 null（非标准请求），必须兜住，否则 WebView 会拿到零响应。
                var url = urlSchemeTask?.Request?.Url;
                if (url is null)
                {
                    Fail(urlSchemeTask, "请求没有 URL");
                    return;
                }

                // WebKit 不保证在哪个线程回调，统一切回主线程处理，避免和 UI/数据库打交道时踩线程问题。
                _ = HandleAsync(urlSchemeTask!, url);
            }

            public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
            {
                // 请求被取消（例如页面已经跳走）。我们是一次性构造完整个响应，没有需要释放的中间态。
            }

            private async Task HandleAsync(IWKUrlSchemeTask task, NSUrl url)
            {
                try
                {
                    var contentPath = ToContentPath(url.Path ?? "/");
                    var content = await _content.GetAsync(contentPath);

                    NSData data;
                    int statusCode;
                    string contentType;
                    string? cacheControl;
                    string? etag;

                    if (content is null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VHost] 404 {contentPath}（原始 URL 路径 {url.Path}）");
                        statusCode = 404;
                        contentType = "text/plain";
                        cacheControl = null;
                        etag = null;
                        data = NSData.FromBytes(IntPtr.Zero, 0); // 空 body
                    }
                    else
                    {
                        statusCode = 200;
                        contentType = content.ContentType;
                        cacheControl = content.CacheControl;
                        etag = content.ETag;
                        data = NSData.FromArray(content.Body);
                    }

                    // 头部的写法在各版本绑定里差异较大，用字典最稳。
                    var headers = new NSMutableDictionary<NSString, NSString>
                    {
                        [new NSString("Content-Type")] = new NSString(contentType),
                        [new NSString("Access-Control-Allow-Origin")] = new NSString("*"),
                        [new NSString("Content-Length")] = new NSString(data.Length.ToString())
                    };
                    if (!string.IsNullOrEmpty(cacheControl))
                        headers[new NSString("Cache-Control")] = new NSString(cacheControl);
                    if (!string.IsNullOrEmpty(etag))
                        headers[new NSString("ETag")] = new NSString(etag);

                    using var response = new NSHttpUrlResponse(url, statusCode, "HTTP/1.1", headers);

                    task.DidReceiveResponse(response);
                    task.DidReceiveData(data);
                    task.DidFinish();
                }
                catch (Exception ex)
                {
                    _log.Error($"[VHost] 私有协议处理请求异常：{url}", ex);
                    Fail(task, ex.Message);
                }
            }

            private static void Fail(IWKUrlSchemeTask? task, string message)
            {
                try
                {
                    // 用 NSUrlErrorDomain —— WebKit 侧没有公开的"自定义 scheme"错误域
                    // （WKError 在本绑定里是枚举 WKErrorCode，没有错误域常量）。
                    // 失败详情只用于排查，所以描述走调试输出，NSError 只带域与码。
                    System.Diagnostics.Debug.WriteLine($"[VHost] 私有协议请求失败：{message}");
                    task?.DidFailWithError(NSError.FromDomain(NSError.NSUrlErrorDomain, 0, null));
                }
                catch
                {
                    // 请求可能已经被取消；这里再抛就没人接了。
                }
            }
        }
#elif ANDROID
        /// <summary>
        /// 给宿主的 <see cref="Android.Webkit.WebView"/> **套一层** WebViewClient：
        /// 命中 <c>https://wiki.local</c> 的请求交给 <see cref="WikiContentProvider"/>，其余一律转发给原 client。
        ///
        /// ★ 为什么必须"包装 + 转发"而不是直接 SetWebViewClient 一个全新实例：
        ///   MAUI 的 BlazorWebView 在 Android 上依赖它自己的 WebViewClient 做两件事 ——
        ///   ① 用 WebViewAssetLoader 伺服宿主页资源；② 初始化 Blazor 的 JS 通道。
        ///   整体替换会把 Blazor 打断（表现是宿主页白屏或 DotNet 对象不存在），
        ///   所以这里把原 client 保存下来，只在 wiki.local 上插手，其它回调全部转交。
        /// </summary>
        public bool TryRegisterAndroid(Android.Webkit.WebView? webView)
        {
            if (webView is null)
                return false;

            if (_androidReady)
                return true;

            try
            {
                var inner = webView.WebViewClient;
                webView.SetWebViewClient(new WikiWebViewClient(inner, _content, _log));

                _androidReady = true;
                _log.Info($"[VHost] 已挂上虚拟主机 {Host}（包装 WebViewClient，原 client 已转发）");
                ContentBaseChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _androidReady = false;
                _log.Error($"[VHost] 挂载虚拟主机 {Host} 失败 —— R4 之后没有 HTTP 回退路径，iframe 内容将无法加载", ex);
                ContentBaseChanged?.Invoke();
                return false;
            }
        }

        /// <summary>包装层：只拦 wiki.local，其余全部转交 MAUI 原来的 WebViewClient。</summary>
        private sealed class WikiWebViewClient : Android.Webkit.WebViewClient
        {
            private readonly Android.Webkit.WebViewClient? _inner;
            private readonly WikiContentProvider _content;
            private readonly LogService _log;

            // 统计（用于判断缓存/304 是否生效、有没有取内容失败）
            private int _served;
            private int _notModified;
            private int _miss;
            private int _failed;

            public WikiWebViewClient(
                Android.Webkit.WebViewClient? inner, WikiContentProvider content, LogService log)
            {
                _inner = inner;
                _content = content;
                _log = log;
            }

            public override Android.Webkit.WebResourceResponse? ShouldInterceptRequest(
                Android.Webkit.WebView? view, Android.Webkit.IWebResourceRequest? request)
            {
                var uri = request?.Url?.ToString();
                if (uri is null)
                    return _inner?.ShouldInterceptRequest(view, request);

                if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
                    !string.Equals(parsed.Host, Host, StringComparison.OrdinalIgnoreCase))
                {
                    return _inner?.ShouldInterceptRequest(view, request);
                }

                var contentPath = ToContentPath(parsed.AbsolutePath);
                var ifNoneMatch = GetHeader(request!, "If-None-Match");

                WikiContentResponse? content;
                try
                {
                    // 直接同步等待即可：不要自己再排一条单线程队列。
                    //   ① ShouldInterceptRequest 运行在 Chromium 的请求线程（不是 .NET 线程池），
                    //      所以阻塞它不会占满线程池、也不会饿死异步续体；
                    //   ② sqlite-net 的异步连接内部就是 SQLiteConnectionWithLock（自带锁、跑在线程池上），
                    //      并发调用本来就安全，不需要在外面再串行化。
                    //   ★ 之前用"专用单线程队列"处理，结果 1134 张图变成 1134 次串行排队，
                    //     页面长时间卡住、还被拖崩过渲染进程 —— 那个设计是错的，已移除。
                    content = _content.GetAsync(contentPath).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _failed++;
                    // ★ 不静默回落到 _inner：原 client 对 wiki.local 只能走网络，必然失败，
                    //   而且会把错误藏起来（表现为"有的图片就是不出来"）。这里明确回 500 并记日志。
                    _log.Error($"[VHost] 处理请求异常：{contentPath}（{uri}）", ex);
                    return ErrorResponse(uri, contentPath);
                }

                if (content is null)
                {
                    _miss++;
                    System.Diagnostics.Debug.WriteLine($"[VHost] 404 {contentPath}（原始 URL 路径 {parsed.AbsolutePath}）");
                    return new Android.Webkit.WebResourceResponse(
                        "text/plain", null, 404, "Not Found",
                        new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                        new MemoryStream(Array.Empty<byte>(), writable: false));
                }

                // ★ 必须带 Content-Length。
                //   旧 HTTP 承载是 response.ContentLength64 = buffer.Length，一定带这个头；
                //   我这套一开始漏了，WebView 就只能把每个响应当成"长度未知"整体缓冲，
                //   图片极多的页面（盔甲页 1134 张）渲染进程内存峰值会明显变高。
                var headers = new Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = "*",
                    ["ETag"] = content.ETag,
                    ["Cache-Control"] = content.CacheControl,
                    ["Content-Length"] = content.Body.Length.ToString()
                };

                // ★ 条件请求：命中就回 304（不带 body）。
                //   没有这一步时，`Cache-Control: no-cache` 的资源每次切页都要把整张图重新传一遍，
                //   上百张图加起来就是"切换页面有点慢"。
                if (!string.IsNullOrEmpty(ifNoneMatch) &&
                    string.Equals(ifNoneMatch, content.ETag, StringComparison.Ordinal))
                {
                    _notModified++;
                    headers["Content-Length"] = "0";
                    return new Android.Webkit.WebResourceResponse(
                        content.ContentType, null, 304, "Not Modified", headers,
                        new MemoryStream(Array.Empty<byte>(), writable: false));
                }

                _served++;
                LogStatsIfNeeded();
                // 末尾参数是 System.IO.Stream —— .NET for Android 的绑定会自己转成 Java 流，
                // 不要手动构造 Java.IO.ByteArrayInputStream（编译器会报 CS1503）。
                return new Android.Webkit.WebResourceResponse(
                    content.ContentType, null, 200, "OK", headers,
                    new MemoryStream(content.Body, writable: false));
            }

            // 统计不能挂在 OnPageFinished 上：那个回调只对**主文档**（Blazor 宿主页）触发，
            // iframe 里的 wiki 页面不会触发它 —— 之前那行"页面完成 200=0"就是这么来的，白记。
            // 改成按请求数周期性输出。
            private void LogStatsIfNeeded()
            {
                var total = _served + _notModified;
                if (total > 0 && total % 500 == 0)
                    _log.Info($"[VHost] 统计：200={_served} / 304={_notModified} / 404={_miss} / 失败={_failed}");
            }

            public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
                => _inner?.OnPageFinished(view, url);

            private Android.Webkit.WebResourceResponse ErrorResponse(string uri, string contentPath)
                => new("text/plain", null, 500, "Internal Server Error",
                       new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                       new MemoryStream(Array.Empty<byte>(), writable: false));

            private static string? GetHeader(Android.Webkit.IWebResourceRequest request, string name)
            {
                try
                {
                    var headers = request.RequestHeaders;
                    if (headers is null)
                        return null;

                    // 安卓侧表头名大小写不固定，两种都试
                    if (headers.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v))
                        return v;
                    if (headers.TryGetValue(name.ToLowerInvariant(), out v) && !string.IsNullOrEmpty(v))
                        return v;
                }
                catch
                {
                    // RequestHeaders 在个别机型上可能抛异常；拿不到就当作没有条件请求。
                }
                return null;
            }

            // ---- 其余回调一律转交，避免影响 MAUI 的 Blazor 行为 ----

            public override bool ShouldOverrideUrlLoading(
                Android.Webkit.WebView? view, Android.Webkit.IWebResourceRequest? request)
                => _inner?.ShouldOverrideUrlLoading(view, request) ?? false;

            public override void OnPageStarted(
                Android.Webkit.WebView? view, string? url, Android.Graphics.Bitmap? favicon)
                => _inner?.OnPageStarted(view, url, favicon);

            public override void OnReceivedError(
                Android.Webkit.WebView? view, Android.Webkit.IWebResourceRequest? request,
                Android.Webkit.WebResourceError? error)
                => _inner?.OnReceivedError(view, request, error);

            /// <summary>
            /// ★ 必须接管：渲染进程被 OOM 杀掉时，如果没有任何 WebViewClient 处理这个回调，
            ///   Chromium 会认为"没人接管"并**把整个应用一起杀掉**（实测日志：
            ///   "Render process kill (OOM or update) wasn't handed by all associated webviews,
            ///    killing application."）。在图片极多的页面（泰拉中文"盔甲"页 1134 张 <img>）
            ///   渲染进程很容易被系统回收，于是表现就是"切到某页卡一下然后闪退"。
            ///
            ///   返回 true = 由我们接管，App 不再被连带杀掉。代价是该 WebView 已经失效，
            ///   所以这里同时把事件报给外层，让宿主决定是否重建视图。
            /// </summary>
            public override bool OnRenderProcessGone(
                Android.Webkit.WebView? view, Android.Webkit.RenderProcessGoneDetail? detail)
            {
                var crashed = detail?.DidCrash() ?? false;
                _log.Error($"[VHost] 渲染进程已退出（crashed={crashed}）。" +
                           "页面内容过重（图片过多）时系统会回收渲染进程；已接管，避免整个 App 被连带杀掉。");

                // 让 MAUI 自己也有机会处理（它通常返回 false，所以我们兜底返回 true）。
                _inner?.OnRenderProcessGone(view, detail);
                return true;
            }
        }
#endif
    }
}
