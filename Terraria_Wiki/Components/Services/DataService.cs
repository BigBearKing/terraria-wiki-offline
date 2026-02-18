using HtmlAgilityPack;
using Microsoft.AspNetCore.Components.Forms;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    public class DataService
    {
        // ================= 配置与常量 =================
        private const string UserAgent = "TerrariaWikiScraper/1.0 (contact: bigbearkingus@gmail.com)";
        private const string JunkXPath = "//div[@class='marker-for-new-portlet-link']|//div[@class='mw-editsection']|//div[@role='navigation' and contains(@class, 'ranger-navbox')]|//comment()";
        private const string BaseApiUrl = "https://terraria.wiki.gg/zh/api.php";
        private const string BaseGuideApiUrl = "https://terraria.wiki.gg/zh/api.php?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace=10000&gapfilterredir=nonredirects&gaplimit=max";
        private const string BaseUrl = "https://terraria.wiki.gg";
        private const string RedirectStartUrl = "/zh/wiki/Special:ListRedirects?limit=5000";
        private static readonly string _baseDir = Path.Combine(FileSystem.AppDataDirectory, "Terraria_Wiki");
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly string _resListPath = Path.Combine(_baseDir, "res.txt");
        private static readonly string _tempResListPath = Path.Combine(_baseDir, "temp_res.txt");
        private static readonly string _pageListPath = Path.Combine(_baseDir, "pages.txt");
        // ================= 事件与状态 =================
        public event Action<string>? OnLog;

        static DataService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
        }

        //两个功能
        public async Task DownloadDataAsync(bool isAll)
        {
            App.AppStateManager.IsDownloading = true;
            await GetWikiRedirectsListAsync();
            await GetWikiPagesListAsync();
            if (isAll)
            {
                await StartDownloadPagesAsync(maxConcurrency: 2);
                await StartDownloadResAsync(maxConcurrency: 10);
            }
            else
            {
                await StartDownloadPagesAsync(maxConcurrency: 2);
            }
            var book = await App.ManagerDb.GetItemAsync<WikiBook>(1);
            book.DownloadedTime = DateTime.Now;
            book.IsPageDownloaded = true;
            book.IsResourceDownloaded = true;
            await App.ManagerDb.SaveItemAsync(book);
            await AppService.RefreshWikiBook(App.ManagerDb, App.ContentDb);
            CleanUpTempFile();
            App.AppStateManager.IsDownloading = false;

        }
        public async Task DownloadResAsync()
        {
            App.AppStateManager.IsDownloading = true;
            await StartDownloadResAsync(maxConcurrency: 10);
            var book = await App.ManagerDb.GetItemAsync<WikiBook>(1);

            book.IsResourceDownloaded = true;
            await App.ManagerDb.SaveItemAsync(book);
            await AppService.RefreshWikiBook(App.ManagerDb, App.ContentDb);
            CleanUpTempFile();
            App.AppStateManager.IsDownloading = false;
        }
        public async Task UpdateDataAsync(bool isAll)
        {

        }
        // ================= 核心功能 1: 获取页面清单 =================
        private async Task<int> GetWikiPagesListAsync()
        {
            OnLog?.Invoke("开始获取页面清单");
            string? gapContinue = null;
            int pagesCount = 0;
            int retryCount = 0;
            bool isGuideMode = false;
            string currentBaseUrl = BaseApiUrl + "?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace=0&gapfilterredir=nonredirects&gaplimit=max";

            while (true) // 逻辑未变，简化循环写法
            {
                string currentUrl = currentBaseUrl + (string.IsNullOrEmpty(gapContinue) ? "" : $"&gapcontinue={Uri.EscapeDataString(gapContinue)}");
                OnLog?.Invoke($"{pagesCount} 条已获取");

                try
                {
                    string jsonResponse = await _httpClient.GetStringAsync(currentUrl);
                    retryCount = 0; // 成功重置

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawData = JsonSerializer.Deserialize<RawResponse>(jsonResponse, options);

                    if (rawData?.Query?.Pages != null)
                    {
                        await using var sw = new StreamWriter(_pageListPath, append: true);
                        foreach (var page in rawData.Query.Pages.Values)
                        {
                            await sw.WriteLineAsync($"{page.Title}|{page.Touched}");
                            pagesCount++;
                        }
                    }

                    if (string.IsNullOrEmpty(rawData?.Continue?.GapContinue))
                    {
                        if (!isGuideMode)
                        {
                            isGuideMode = true;
                            gapContinue = null;
                            currentBaseUrl = BaseGuideApiUrl;
                            continue;
                        }
                        else
                        {

                            break;
                        }
                    }
                    gapContinue = rawData?.Continue?.GapContinue;
                }
                catch (HttpRequestException e)
                {
                    if (++retryCount > 5) throw;
                    OnLog?.Invoke($"请求失败: {e.Message} - 正在重试 ({retryCount}/5)...");
                    await Task.Delay(1000);
                }
            }
            OnLog?.Invoke($"获取完毕，共获取 {pagesCount} 个页面");

            return pagesCount;
        }

        private async Task GetWikiRedirectsListAsync()
        {
            string nextUrl = RedirectStartUrl;
            int pageCount = 1;
            OnLog?.Invoke("开始获取重定向列表");
            while (!string.IsNullOrEmpty(nextUrl))
            {
                int retry = 0;
                while (true)
                {
                    try
                    {
                        string fullUrl = BaseUrl + nextUrl;
                        OnLog?.Invoke($"[第 {pageCount} 页]正在下载: {fullUrl}");
                        string html = await _httpClient.GetStringAsync(fullUrl);
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
                        var listItems = doc.DocumentNode.SelectNodes("//div[@class='mw-spcontent']//ol/li");

                        if (listItems == null)
                        {
                            OnLog?.Invoke("警告：本页没有找到数据，可能已结束或结构改变");
                            break;
                        }

                        int countOnPage = 0;
                        var wikiRedirects = new List<WikiRedirect>();
                        foreach (var li in listItems)
                        {
                            var links = li.SelectNodes(".//a");

                            if (links != null && links.Count >= 2)
                            {
                                string fromTitle = HtmlEntity.DeEntitize(links[0].InnerText);
                                string toTitle = HtmlEntity.DeEntitize(links.Last().InnerText);
                                var wikiRedirect = new WikiRedirect { FromName = fromTitle, ToTarget = toTitle };
                                wikiRedirects.Add(wikiRedirect);
                                countOnPage++;
                            }
                        }
                        await App.ContentDb.SaveItemsAsync(wikiRedirects);
                        OnLog?.Invoke($"本页解析出 {countOnPage} 条重定向");
                        var nextLinkNode = doc.DocumentNode.SelectSingleNode("//a[@class='mw-nextlink']");

                        if (nextLinkNode != null)
                        {
                            nextUrl = HtmlEntity.DeEntitize(nextLinkNode.GetAttributeValue("href", ""));
                            pageCount++;
                            await Task.Delay(500);
                        }
                        else
                        {
                            OnLog?.Invoke("重定向列表获取成功");
                            nextUrl = null;
                            break;
                        }

                    }
                    catch (Exception ex)
                    {
                        if (++retry > 5)
                        {
                            OnLog?.Invoke($"重定向列表获取失败 (已重试5次): {ex.Message}");
                            nextUrl = null; // 停止整个大循环
                            break;
                        }
                        OnLog?.Invoke($"获取重定向列表出错，正在重试 ({retry}/5)...");
                        await Task.Delay(1000); // 间隔1秒
                    }
                }

            }

        }
        // ================= 核心功能 2: 批量任务调度器 =================

        private async Task RunBatchJobAsync(string inputPath, string failListName, int concurrency, Func<int, string, Task> itemProcessor, Action? preWork = null, Action? postWork = null)
        {

            string failedPath = Path.Combine(_baseDir, failListName);
            OnLog?.Invoke($"开始任务：最大并发 {concurrency}");

            // ================= 修改开始 =================
            // 使用 using 确保任务结束时执行 Dispose()，从而执行最后一次文件截断
            using var urlProvider = new BatchUrlProvider(inputPath, batchSize: 50);
            // ================= 修改结束 =================

            // 执行前置操作
            preWork?.Invoke();

            var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
            {
                await RunWorkerLoopAsync(i, urlProvider, failedPath, itemProcessor);
            }));


            await Task.WhenAll(tasks);
            postWork?.Invoke();


        }

        // 通用的 Worker 循环逻辑
        private async Task RunWorkerLoopAsync(int workerId, BatchUrlProvider provider, string failedPath, Func<int, string, Task> processAction)
        {
            while (true)
            {
                string? line = provider.GetNextLine();
                if (string.IsNullOrWhiteSpace(line)) break;

                try
                {
                    int retry = 0;
                    while (true)
                    {
                        try
                        {
                            await processAction(workerId, line);
                            break;
                        }
                        catch (Exception)
                        {
                            if (++retry > 5) throw;
                            OnLog?.Invoke($"[Worker {workerId}] 失败重试 ({retry}/5): {line}");
                            await Task.Delay(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Worker {workerId}] 错误: {ex.Message}");
                    await AppendFailedUrlAsync(failedPath, line);
                }
            }
        }

        // ================= 业务入口: 下载页面 =================
        private async Task StartDownloadPagesAsync(int maxConcurrency = 5)
        {
            var logger = new BatchLogWriter(_resListPath, 100);
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(_pageListPath))
            {
                totalCount = File.ReadLines(_pageListPath).Count();
            }
            OnLog?.Invoke($"开始下载所有页面，共 {totalCount} 个");
            // 定义如何处理单行数据


            async Task ProcessPageLine(int workerId, string line)
            {
                var parts = line.Split('|');
                if (parts.Length < 2) return;

                var page = new PageInfo { Title = parts[0], LastModified = DateTime.Parse(parts[1]) };

                await DownloadAndSavePageToDbAsync(page, logger);
                int c = Interlocked.Increment(ref currentCount);
                OnLog?.Invoke($"[Worker {workerId}] {c}/{totalCount} 完成页面: {page.Title}");
            }

            // 启动通用任务
            await RunBatchJobAsync(_pageListPath, "failed_page_urls.txt", maxConcurrency, ProcessPageLine,
                postWork: () => logger.Flush());
            OnLog?.Invoke("所有页面下载完毕");
            // 爬取完成后，清洗一下数据
            OnLog?.Invoke("正在处理Res数据");
            string tempFile = Path.Combine(_baseDir, "res_temp.txt");
            AppService.RemoveDuplicatesOptimized(_resListPath, tempFile);

            // 替换原文件
            File.Delete(_resListPath);
            File.Move(tempFile, _resListPath, true);
            OnLog?.Invoke("Res数据处理完毕");

        }

        // ================= 业务入口: 下载资源 =================
        private async Task StartDownloadResAsync(int maxConcurrency = 10)
        {
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(_resListPath))
            {
                totalCount = File.ReadLines(_resListPath).Count();
            }
            OnLog.Invoke($"开始下载资源文件，共 {totalCount} 个");
            async Task ProcessResLine(int workerId, string url)
            {

                string fileName = GetFileNameFromUrl(url);
                int c = Interlocked.Increment(ref currentCount);
                if (await App.ContentDb.ItemExistsAsync<WikiAsset>(fileName))
                {
                    OnLog?.Invoke($"[Worker {workerId}] 跳过资源: {fileName}  {c}/{totalCount}");

                }
                else
                {
                    await DownloadAndSaveResToDbAsync(url, fileName);
                    OnLog?.Invoke($"[Worker {workerId}] {c}/{totalCount} 完成资源: {fileName}");
                }


            }
            File.Copy(_resListPath, _tempResListPath, true);
            // 启动通用任务
            await RunBatchJobAsync(_tempResListPath, "failed_res_urls.txt", maxConcurrency, ProcessResLine);
            OnLog.Invoke("资源文件下载完毕");
        }

        // ================= 具体的处理逻辑 (重构过) =================

        private async Task DownloadAndSavePageToDbAsync(PageInfo pageInfo, BatchLogWriter logger)
        {
            var pageUrl = BaseApiUrl + $"?action=parse&page={pageInfo.Title}&prop=text&format=xml";

            string xml = await _httpClient.GetStringAsync(pageUrl);

            var xmldoc = XDocument.Parse(xml);

            // 直接取 <text> 节点内容
            string html = xmldoc.Descendants("text").FirstOrDefault()?.Value;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var contentNode = doc.DocumentNode;

            if (contentNode == null) return;

            // 拆分为小函数，逻辑更清晰
            CleanJunkElements(contentNode);
            ProcessAnchorLinks(contentNode);
            ProcessAudioTags(contentNode);
            ProcessImages(contentNode, logger);

            var wikiPage = new WikiPage
            {
                Title = pageInfo.Title,
                Content = contentNode.OuterHtml,
                LastModified = pageInfo.LastModified
            };
            await App.ContentDb.SaveItemAsync(wikiPage);
        }

        private void CleanJunkElements(HtmlNode node)
        {
            node.SelectNodes(JunkXPath)?.ToList().ForEach(n => n.Remove());
        }

        private void ProcessAnchorLinks(HtmlNode node)
        {
            node.SelectNodes("//a[@href and @title]")?.ToList().ForEach(n =>
            {
                string href = n.Attributes["href"].Value;
                int hashIndex = href.IndexOf('#');
                if (hashIndex >= 0)
                {
                    n.SetAttributeValue("anchor", href.Substring(hashIndex));
                }
                n.Attributes.Remove("href");
            });
        }

        private void ProcessAudioTags(HtmlNode node)
        {
            node.SelectNodes("//audio")?.ToList().ForEach(n =>
            {
                var sources = n.SelectNodes("./source");
                if (sources != null && sources.Count > 1)
                {
                    var keep = sources.FirstOrDefault(s => !s.GetAttributeValue("src", "").Contains("/transcoded/"))
                               ?? sources.Last();

                    foreach (var s in sources.ToArray()) // ToArray防止修改集合时报错
                    {
                        if (s != keep) s.Remove();
                    }
                }
            });
        }

        private void ProcessImages(HtmlNode node, BatchLogWriter logger)
        {
            // 移除图片链接
            node.SelectNodes("//a[@class='image' and @href]")?.ToList().ForEach(n => n.Attributes.Remove("href"));

            // 处理 src
            node.SelectNodes("//*[@src]")?.ToList().ForEach(n =>
            {
                // 清理属性
                foreach (var attr in new[] { "loading", "data-file-width", "data-file-height", "srcset" })
                    n.Attributes.Remove(attr);

                string src = n.Attributes["src"].Value;

                // 补全 URL
                if (!src.Contains("https://")) src = "https://terraria.wiki.gg" + src;

                // 还原缩略图
                src = Regex.Replace(src, @"/thumb/(.*?)/.*", "/$1");
                src = CleanUpUrl(src);
                // 记录日志
                logger.Add(src);
                string htmlSrc = Uri.EscapeDataString(GetFileNameFromUrl(src));
                // 替换为本地路径
                n.SetAttributeValue("src", "/src/" + htmlSrc);
            });
        }

        private async Task DownloadAndSaveResToDbAsync(string url, string fileName)
        {
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            byte[] data = await response.Content.ReadAsByteArrayAsync();

            await App.ContentDb.SaveItemAsync(new WikiAsset
            {
                FileName = fileName,
                Data = data,
                MimeType = mimeType
            });
        }

        // ================= 辅助工具方法 =================
        private void CleanUpTempFile()
        {
            OnLog?.Invoke("正在清理临时文件");
            if (File.Exists(_pageListPath))
            {
                File.Delete(_pageListPath);
            }

            if (File.Exists(_tempResListPath))
            {
                File.Delete(_tempResListPath);
            }
            OnLog?.Invoke("临时文件清理完毕");
        }
        private string CleanUpUrl(string url)
        {
            int qIdx = url.IndexOf('?');
            return (qIdx > 0) ? url.Substring(0, qIdx) : url;
        }
        private string GetFileNameFromUrl(string url)
        {
            string cleanUrl = CleanUpUrl(url);
            string name = cleanUrl.Substring(cleanUrl.LastIndexOf('/') + 1);
            string decodedName = WebUtility.UrlDecode(name);
            return decodedName;
        }

        private async Task AppendFailedUrlAsync(string path, string url)
        {
            try { await File.AppendAllLinesAsync(path, new[] { url }); } catch { }
        }
        public async Task TestLogStormAsync()
        {
            App.AppStateManager.IsDownloading = true;
            OnLog?.Invoke("🚀 === 开始多线程延迟测试：10个线程，每条延迟100-200ms ===");
            // 创建一个任务列表
            var tasks = new List<Task>();

            // 启动 10 个并发线程
            for (int i = 0; i < 10; i++)
            {
                int threadIndex = i; // 捕获局部变量以供 Task 使用

                // 每个线程是一个独立的 Task
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        // 1. 随机延迟 100 到 200 毫秒
                        // Random.Shared 是 .NET 6+ 线程安全的写法
                        int delay = Random.Shared.Next(400, 901);
                        await Task.Delay(delay);

                        // 2. 准备日志内容
                        // 获取当前底层受管线程ID，证明是不同线程在跑
                        int threadId = Environment.CurrentManagedThreadId;
                        string msg = $"[Thread-{threadId:00} / Worker-{threadIndex}] 正在处理任务... 进度 {j + 1}/100 (延迟 {delay}ms)";

                        // 3. 触发事件 (LogService 会捕获并加锁写入文件)
                        OnLog?.Invoke(msg);
                    }
                }));
            }

            // 等待所有 10 个线程全部完成
            await Task.WhenAll(tasks);

            OnLog?.Invoke("所有线程任务完成");
            App.AppStateManager.IsDownloading = false;
        }
    }

    // ================= 保持原逻辑的辅助类 (稍微整理格式) =================

    public class BatchLogWriter
    {
        private readonly string _filePath;
        private readonly int _batchSize;
        private readonly List<string> _buffer;
        private readonly object _lock = new();

        public BatchLogWriter(string filePath, int batchSize = 100)
        {
            _filePath = filePath;
            _batchSize = batchSize;
            _buffer = new List<string>(batchSize);
        }

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_lock)
            {
                _buffer.Add(line);
                if (_buffer.Count >= _batchSize) FlushInternal();
            }
        }

        public void Flush() { lock (_lock) FlushInternal(); }

        private void FlushInternal()
        {
            if (_buffer.Count == 0) return;
            File.AppendAllLines(_filePath, _buffer);
            _buffer.Clear();
        }
    }

    public class BatchUrlProvider : IDisposable
    {
        private readonly string _filePath;
        private readonly int _batchSize;
        private readonly ConcurrentQueue<string> _memoryQueue = new();
        private readonly object _fileLock = new();
        private bool _isFileExhausted = false;

        // 新增：记录上一次应该截断的位置
        private long _pendingTruncatePosition = -1;

        public BatchUrlProvider(string filePath, int batchSize = 50)
        {
            _filePath = filePath;
            _batchSize = batchSize;
        }

        public string? GetNextLine()
        {
            // 1. 尝试从内存队列取数据
            if (_memoryQueue.TryDequeue(out var url)) return url;

            lock (_fileLock)
            {
                // 双重检查，防止并发进入
                if (_memoryQueue.TryDequeue(out url)) return url;
                if (_isFileExhausted) return null;

                // 2. 关键修改：在读取新的一批数据之前，执行"上一批"的截断
                // 这意味着：如果程序在上一批处理中途崩溃，文件尚未截断，重启后数据还在
                if (_pendingTruncatePosition >= 0)
                {
                    TruncateFile(_filePath, _pendingTruncatePosition);
                    _pendingTruncatePosition = -1; // 重置
                }

                // 3. 读取新的一批数据（只读，不删）
                var (lines, newPosition) = PeekLastNLines(_filePath, _batchSize);

                if (lines.Count == 0)
                {
                    _isFileExhausted = true;
                    // 如果文件空了，且有待截断的操作，立即执行（清空文件）
                    if (_pendingTruncatePosition >= 0)
                    {
                        TruncateFile(_filePath, _pendingTruncatePosition);
                        _pendingTruncatePosition = -1;
                    }
                    return null;
                }

                // 4. 将数据加入队列，并记录"下一次"需要截断的位置
                foreach (var item in lines) _memoryQueue.Enqueue(item);
                _pendingTruncatePosition = newPosition;
            }

            return _memoryQueue.TryDequeue(out url) ? url : null;
        }

        // 实现 Dispose 以确保最后一批数据被截断
        public void Dispose()
        {
            lock (_fileLock)
            {
                if (_pendingTruncatePosition >= 0)
                {
                    try { TruncateFile(_filePath, _pendingTruncatePosition); } catch { }
                    _pendingTruncatePosition = -1;
                }
            }
            GC.SuppressFinalize(this);
        }

        // 将原 PopLastNLines 拆分为 PeekLastNLines（只读）和 TruncateFile（只删）

        private (List<string> lines, long newPosition) PeekLastNLines(string filePath, int count)
        {
            if (!File.Exists(filePath)) return (new List<string>(), 0);
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return (new List<string>(), 0);

            long pos = fs.Length - 1;
            int linesFound = 0;

            // 从后往前扫描换行符
            while (pos >= 0)
            {
                fs.Position = pos;
                if (fs.ReadByte() == '\n')
                {
                    if (++linesFound > count)
                    {
                        pos++; // 回到换行符之后（保留这个换行符给上一行）
                        break;
                    }
                }
                pos--;
            }

            if (pos < 0) pos = 0;

            // 读取这部分数据
            fs.Position = pos;
            byte[] buffer = new byte[fs.Length - pos];
            fs.Read(buffer, 0, buffer.Length);

            var resultLines = Encoding.UTF8.GetString(buffer).Trim()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            // 返回数据和应该截断的位置 (pos)
            return (resultLines, pos);
        }

        private void TruncateFile(string filePath, long length)
        {
            if (!File.Exists(filePath)) return;
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            fs.SetLength(length);
        }
    }
    public class LogService
    {
        // 当前正在写入的日志文件路径
        private readonly string _activeLogPath;
        // 历史归档文件夹路径
        private readonly string _archiveFolderPath;

        // 内存索引：只存当前 session 的行位置
        private readonly List<long> _lineOffsets = new();

        // 线程锁
        private readonly object _fileLock = new();

        // 事件：通知 UI 有新日志
        public event Action OnLogAdded;

        public LogService()
        {
            // 使用 AppDataDirectory，保证数据持久化（CacheDirectory 可能会被系统清理）
            var basePath = FileSystem.AppDataDirectory;
            _archiveFolderPath = Path.Combine(basePath, "LogHistory");
            _activeLogPath = Path.Combine(basePath, "current_session.log");

            // 确保归档目录存在
            if (!Directory.Exists(_archiveFolderPath))
            {
                Directory.CreateDirectory(_archiveFolderPath);
            }

            // ★★★ 核心步骤：启动时执行归档和初始化 ★★★
            InitializeSession();
        }

        private void InitializeSession()
        {
            lock (_fileLock)
            {
                // 1. 检查是否有上次遗留的活跃日志
                if (File.Exists(_activeLogPath))
                {
                    var fileInfo = new FileInfo(_activeLogPath);

                    // 只有文件有内容时才归档，空文件直接覆盖
                    if (fileInfo.Length > 0)
                    {
                        // 生成归档文件名：logs/history/log_2023-10-27_14-30-01.txt
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string archiveFileName = Path.Combine(_archiveFolderPath, $"log_{timestamp}.txt");

                        try
                        {
                            // 移动文件（相当于重命名），速度极快
                            File.Move(_activeLogPath, archiveFileName);
                        }
                        catch (Exception ex)
                        {
                            // 即使归档失败，也要保证当前程序能运行，这里可以做个简单的容错
                            System.Diagnostics.Debug.WriteLine($"归档失败: {ex.Message}");
                        }
                    }
                }

                // 2. 创建全新的空文件供本次使用
                File.WriteAllText(_activeLogPath, string.Empty);

                // 3. 重置内存索引
                _lineOffsets.Clear();
                _lineOffsets.Add(0); // 第一行起始位置是 0
            }
        }

        // --- 以下是写入和读取逻辑 (和之前类似，但只针对 _activeLogPath) ---

        public void AppendLog(string message)
        {
            var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(logLine);

            lock (_fileLock)
            {
                using (var fs = new FileStream(_activeLogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    _lineOffsets.Add(fs.Position); // 记录下一行的起始位置
                }
            }
            OnLogAdded?.Invoke();
        }

        public int GetTotalCount()
        {
            lock (_fileLock)
            {
                return Math.Max(0, _lineOffsets.Count - 1);
            }
        }

        public async ValueTask<IEnumerable<string>> GetLogsAsync(int startIndex, int count)
        {
            var result = new List<string>();
            int total = GetTotalCount();
            if (startIndex >= total) return result;

            int actualCount = Math.Min(count, total - startIndex);
            long startPosition, endPosition;

            lock (_fileLock)
            {
                startPosition = _lineOffsets[startIndex];
                endPosition = _lineOffsets[startIndex + actualCount];
            }

            using (var fs = new FileStream(_activeLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(startPosition, SeekOrigin.Begin);
                byte[] buffer = new byte[endPosition - startPosition];
                await fs.ReadAsync(buffer, 0, buffer.Length);

                var content = Encoding.UTF8.GetString(buffer);
                var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                for (int i = 0; i < actualCount; i++)
                {
                    if (i < lines.Length) result.Add(lines[i]);
                }
            }
            return result;
        }
    }


}