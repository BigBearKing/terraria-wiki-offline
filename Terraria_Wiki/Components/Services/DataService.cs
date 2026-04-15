using HtmlAgilityPack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Terraria_Wiki.Models;
using static SQLite.SQLite3;

namespace Terraria_Wiki.Services
{
    public class DataService
    {
        // ================= 配置与常量 =================
        private const string UserAgent = "TerrariaWikiScraper/1.0 (contact: bigbearkingus@gmail.com)";
        private const string JunkXPath = "//div[@id='marker-for-new-portlet-link']|//span[@class='mw-editsection']|//div[@role='navigation' and contains(@class, 'ranger-navbox')]|//comment()";
        private const string BaseApiUrl = "https://terraria.wiki.gg/zh/api.php";
        private const string BaseGuideApiUrl = "https://terraria.wiki.gg/zh/api.php?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace=10000&gapfilterredir=nonredirects&gaplimit=max";
        private const string BaseUrl = "https://terraria.wiki.gg";
        private const string RedirectStartUrl = "/zh/wiki/Special:ListRedirects?limit=5000";


        private static readonly string _baseDir = Path.Combine(FileSystem.AppDataDirectory, "Terraria_Wiki");
        private static readonly string _tempDir = Path.Combine(FileSystem.AppDataDirectory, "Temp");
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly string _resListPath = Path.Combine(_baseDir, "res.txt");
        private static readonly string _tempResListPath = Path.Combine(_baseDir, "temp_res.txt");
        private static readonly string _pageListPath = Path.Combine(_baseDir, "pages.txt");
        private static readonly string _failedPageListPath = Path.Combine(_baseDir, "failed_pages.txt");
        private static readonly string _tempFailedPageListPath = Path.Combine(_baseDir, "temp_failed_pages.txt");
        private static readonly string _failedResListPath = Path.Combine(_baseDir, "failed_res.txt");
        private static readonly string _tempFailedResListPath = Path.Combine(_baseDir, "temp_failed_res.txt");
        private static readonly string _updatePageListPath = Path.Combine(_baseDir, "update_pages.txt");
        private static readonly string _updateResListPath = Path.Combine(_baseDir, "update_res.txt");
        // ================= 事件与状态 =================
        private readonly LogService _log;
        private int _maxRetryAttempts;
        private int _pageConcurrency;
        private int _resConcurrency;

        public DataService(LogService logService)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _log = logService;
        }


        //主要功能
        public async Task DownloadDataAsync(bool isAll)
        {
            // 1. 锁定状态
            App.AppStateManager?.IsProcessing = true;
            if (isAll)
            {
                _log.Info("开始下载所有页面和资源");
            }
            else
            {
                _log.Info("开始仅下载页面数据");
            }

            try
            {
                await InitializeSettings();

                await GetWikiRedirectsListAsync();
                await GetWikiPagesListAsync();
                var book = await App.ManagerDb.GetItemAsync<WikiBook>(1);
                if (isAll)
                {
                    await StartDownloadPagesAsync(_pageListPath, _resListPath, _failedPageListPath, _pageConcurrency);
                    await StartDownloadResAsync(_resListPath, _failedResListPath, _resConcurrency);
                    book.IsResourceDownloaded = true;
                }
                else
                {
                    await StartDownloadPagesAsync(_pageListPath, _resListPath, _failedPageListPath, _pageConcurrency);
                }

                // 数据库更新操作

                book.UpdateTime = DateTime.Now;
                book.IsPageDownloaded = true;

                await App.ManagerDb.SaveItemAsync(book);
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanUpTempFile();
                await AppService.WikiRefreshAsync();
                if (isAll)
                {
                    _log.Success("所有页面和资源下载完成");
                    App.AppStateManager?.TriggerAlert("提示", "所有页面和资源下载完成");
                }
                else
                {
                    _log.Success("页面数据下载完成");
                    App.AppStateManager?.TriggerAlert("提示", "页面数据下载完成");
                }


            }
            catch (Exception ex)
            {
                _log.Error("发生错误", ex);
                App.AppStateManager?.TriggerAlert("错误", $"发生错误: {ex.Message}");


            }
            finally
            {
                App.AppStateManager?.IsProcessing = false;

            }
        }

        public async Task DownloadResAsync()
        {
            // 1. 锁定状态
            App.AppStateManager?.IsProcessing = true;
            _log.Info("开始下载所有资源");

            try
            {
                await InitializeSettings();

                // 2. 检查文件有效性
                if (!FileHelper.IsFileValid(_resListPath))
                {
                    _log.Error("资源列表文件无效");
                    App.AppStateManager?.TriggerAlert("错误", "资源列表文件无效");
                    // 直接 return 即可，下方的 finally 块会自动接管并重置状态
                    return;
                }

                // 3. 执行核心下载逻辑
                await StartDownloadResAsync(_resListPath, _failedResListPath, _resConcurrency);

                // 4. 更新数据库状态
                var book = await App.ManagerDb.GetItemAsync<WikiBook>(1);
                book.IsResourceDownloaded = true;
                await App.ManagerDb.SaveItemAsync(book);

                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanUpTempFile();
                await App.WebServer.Refresh();
                await AppService.WikiRefreshAsync();
                _log.Success("所有资源下载完成");
                App.AppStateManager?.TriggerAlert("提示", "所有资源下载完成");

            }
            catch (Exception ex)
            {
                _log.Error("发生错误", ex);
                App.AppStateManager?.TriggerAlert("错误", $"发生错误: {ex.Message}");

            }
            finally
            {
                App.AppStateManager?.IsProcessing = false;

            }
        }

        //更新页面和资源
        public async Task UpdateDataAsync(bool isAll)
        {
            App.AppStateManager?.IsProcessing = true;
            if (isAll)
            {
                _log.Info("开始更新所有页面和资源");
            }
            else
            {
                _log.Info("开始更新页面数据");
            }
            try
            {
                await InitializeSettings();
                //获取新的页面列表
                await GetWikiRedirectsListAsync();
                await GetWikiPagesListAsync();

                //检查是否有要更新的页面
                int updateCount = await CheckUpdatePage();
                _log.Success($"更新清单获取完毕，共 {updateCount} 个页面需要更新");
                if (updateCount == 0)
                {
                    App.AppStateManager?.TriggerAlert("提示", "没有页面需要更新");
                    return;
                }

                if (isAll)
                {
                    await StartDownloadPagesAsync(_updatePageListPath, _updateResListPath, _failedPageListPath, _pageConcurrency);
                    await StartDownloadResAsync(_updateResListPath, _failedResListPath, _resConcurrency);
                }
                else
                {
                    await StartDownloadPagesAsync(_updatePageListPath, _updateResListPath, _failedPageListPath, _pageConcurrency);
                }
                await FileHelper.AppendFileAsync(_updateResListPath, _resListPath);
                string tempFile = Path.Combine(_baseDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                FileHelper.RemoveDuplicatesOptimized(_resListPath, tempFile);
                File.Delete(_resListPath);
                File.Move(tempFile, _resListPath, true);
                var book = await App.ManagerDb.GetItemAsync<WikiBook>(1);
                book.UpdateTime = DateTime.Now;
                await App.ManagerDb.SaveItemAsync(book);
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanUpTempFile();
                if (isAll)
                {
                    _log.Success("所有页面和资源更新完成");
                    App.AppStateManager?.TriggerAlert("提示", "所有页面和资源更新完成");
                }
                else
                {
                    _log.Success("页面数据更新完成");
                    App.AppStateManager?.TriggerAlert("提示", "页面数据更新完成");
                }

            }
            catch (Exception e)
            {
                _log.Error("发生错误", e);
                App.AppStateManager?.TriggerAlert("错误", $"发生错误: {e.Message}");
            }
            finally
            {
                App.AppStateManager?.IsProcessing = false;

            }
        }


        //检查是否有要更新的页面
        private async Task<int> CheckUpdatePage()
        {
            var writer = new BatchLineWriter(_updatePageListPath, 200);
            int totalCount = 0;
            if (File.Exists(_pageListPath))
            {
                totalCount = File.ReadLines(_pageListPath).Count(); // 加上这行计算总数
            }
            int currentCount = 0;
            int updateCount = 0;
            async Task ProcessPageLine(int workerId, string line)
            {
                var parts = line.Split('|');
                if (parts.Length < 2) return;
                var page = new PageInfo { Title = parts[0], LastModified = DateTime.Parse(parts[1]) };
                try
                {
                    if (await App.ContentDb.ItemExistsAsync<WikiPage>(page.Title))
                    {
                        var oldpage = await App.ContentDb.GetItemAsync<WikiPage>(page.Title);
                        if (oldpage.LastModified != page.LastModified)
                        {
                            writer.Add(line);
                            Interlocked.Increment(ref updateCount);
                        }
                    }
                    else
                    {
                        writer.Add(line);
                        Interlocked.Increment(ref updateCount);
                    }

                }
                finally
                {
                    int c = Interlocked.Increment(ref currentCount);
                }


            }
            await RunBatchJobAsync(_pageListPath, _failedPageListPath, 1, ProcessPageLine, postWork: () => writer.Flush());
            return updateCount;
        }

        //清理数据库
        public async Task CleanUpResAsync()
        {
            App.AppStateManager?.IsProcessing = true;
            _log.Info("开始清理未用资源");
            try
            {
                await App.ContentDb.VacuumDatabaseAsync();
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                _log.Success("未用资源清理完成");
                App.AppStateManager?.TriggerAlert("提示", "未用资源清理完成");
            }
            catch (Exception ex)
            {
                _log.Error("发生错误", ex);
                App.AppStateManager?.TriggerAlert("错误", $"发生错误: {ex.Message}");
            }
            finally
            {

                App.AppStateManager?.IsProcessing = false;
            }

        }

        //检查是否有失败列表
        public bool CheckFailList()
        {
            if (App.AppStateManager.IsProcessing)
            {
                return false;
            }


            if (!(FileHelper.IsFileValid(_failedResListPath) || FileHelper.IsFileValid(_failedPageListPath)))
                return false;

            return true;

        }

        //重试失败列表
        public async Task RetryFailList()
        {
            App.AppStateManager.IsProcessing = true;
            try
            {
                bool isAll = true;
                var wikiBook = await App.ManagerDb.GetItemAsync<WikiBook>(1);
                if (!wikiBook.IsResourceDownloaded) isAll = false;
                await InitializeSettings();

                if (FileHelper.IsFileValid(_failedPageListPath))
                {
                    _log.Info("开始重试失败页面");
                    await StartDownloadPagesAsync(_failedPageListPath, _failedResListPath, _tempFailedPageListPath, 1);
                    await FileHelper.AppendFileAsync(_failedResListPath, _resListPath);
                    string tempFile = Path.Combine(_baseDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    FileHelper.RemoveDuplicatesOptimized(_resListPath, tempFile);
                    File.Delete(_resListPath);
                    File.Move(tempFile, _resListPath, true);
                    await FileHelper.AppendFileAsync(_tempFailedPageListPath, _failedPageListPath);

                }

                if (FileHelper.IsFileValid(_failedResListPath) && isAll == true)
                {
                    _log.Info("开始重试失败资源");
                    await StartDownloadResAsync(_failedResListPath, _tempFailedResListPath, 1, false);
                    await FileHelper.AppendFileAsync(_tempFailedResListPath, _failedResListPath);
                }
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                CleanUpTempFile();
                _log.Success("失败任务重试完毕");
                App.AppStateManager?.TriggerAlert("提示", "失败任务重试完毕");
            }
            catch (Exception ex)
            {
                _log.Error("发生错误", ex);
                App.AppStateManager?.TriggerAlert("错误", $"发生错误: {ex.Message}");
            }
            finally
            {
                App.AppStateManager.IsProcessing = false;
            }
        }

        public void ClearFailedList()
        {

            if (File.Exists(_failedPageListPath))
            {
                File.Delete(_failedPageListPath);
                _log.Info("已清理失败页面列表");

            }
            if (File.Exists(_failedResListPath))
            {
                File.Delete(_failedResListPath);
                _log.Info("已清理失败资源列表");
            }
            App.AppStateManager?.TriggerAlert("提示", "已清理失败列表");

        }
        //删除文件夹
        public async Task DeleteDatabase()
        {
            App.AppStateManager.IsProcessing = true;
            _log.Info("正在删除数据库文件");
            try
            {
                await App.ContentDb.CloseConnection();
                await Task.Run(() =>
                {
                    DeleteDataFolder();
                });
                await App.ManagerDb.DeleteItemAsync<WikiBook>(1);
                await App.ManagerDb.Init(true);
                await App.ContentDb.ReconnectAsync();
                await AppService.WikiRefreshAsync();
                await App.WebServer.Refresh();
                _log.Success("数据库文件删除成功");
                App.AppStateManager?.TriggerAlert("提示", "数据库文件删除成功");
            }
            catch (Exception ex)
            {
                _log.Error("删除数据库文件时发生错误", ex);
                App.AppStateManager?.TriggerAlert("错误", $"删除数据库文件时发生错误: {ex.Message}");
            }
            finally
            {
                App.AppStateManager.IsProcessing = false;
            }

        }
        public static void DeleteDataFolder()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, true);
        }


        //导出数据
        public async Task ExportData()
        {
            App.AppStateManager?.IsProcessing = true;
            _log.Info("开始导出数据");
            string exportPath = null;
            string finalPkgPath = null;

            // --- 准备工作 ---
            string originalDbPath = App.ContentDb.DatabasePath;
            string tempDbPath = Path.Combine(FileSystem.CacheDirectory, "temp_export.db");
            string exportFileName = Path.GetFileName(_baseDir) + ".pkg";

            if (!File.Exists(originalDbPath))
            {
                _log.Error("没有找到数据库文件，无法导出。");
                App.AppStateManager?.IsProcessing = false;
                return;
            }

            try
            {
                // 1. 在线备份数据库 (SQLite API 本身支持异步，留在 UI 线程即可)
                _log.Info("正在备份数据库文件");
                var conn = App.ContentDb.GetConnection();
                await Task.Run(async () =>
                {
                    await conn.BackupAsync(tempDbPath);
                });
                // 2. 准备基础数据
                _log.Info("开始打包数据");
                var wikibook = await App.ManagerDb.GetItemAsync<WikiBook>(1);
                var info = new WikiPackageInfo
                {
                    Id = 1,
                    Title = wikibook.Title,
                    IsPageDownloaded = wikibook.IsPageDownloaded,
                    IsResourceDownloaded = wikibook.IsResourceDownloaded,
                    UpdateTime = wikibook.UpdateTime,
                    AppVersion = AppInfo.Current.VersionString,
                    Files = new List<FileMeta>()
                };

                // 3. 【必须在 UI 线程】获取导出路径
                // 因为弹窗 UI 控件只能在主线程调用，绝对不能放进 Task.Run
                if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
#if WINDOWS
                    exportPath = await FileHelper.PickFolderWindowsAsync();
                    if (exportPath == null) return; // 用户取消了选择
                    finalPkgPath = Path.Combine(exportPath, exportFileName);
#endif
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
                {
                    finalPkgPath = Path.Combine(FileSystem.CacheDirectory, exportFileName);
                }
                else
                {
                    throw new Exception("不支持的平台");
                }

                // ==========================================
                // 阶段 4：进入后台线程，执行脏活累活（算哈希、写大文件）
                // ==========================================
                await Task.Run(async () =>
                {
                    // 获取所有文件
                    var files = Directory.GetFiles(_baseDir, "*.*", SearchOption.AllDirectories).Where(f =>
                        !f.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
                    ).ToList();

                    // 计算 MD5
                    _log.Info("正在计算文件元数据");
                    using (var md5 = MD5.Create())
                    {
                        foreach (var file in files)
                        {
                            string fileToRead = (file == originalDbPath) ? tempDbPath : file;
                            using var fs = File.OpenRead(fileToRead);
                            byte[] hashBytes = md5.ComputeHash(fs);

                            info.Files.Add(new FileMeta
                            {
                                RelativePath = Path.GetRelativePath(_baseDir, file),
                                Size = fs.Length,
                                MD5 = Convert.ToHexStringLower(hashBytes)
                            });
                        }
                    }

                    // 开始写入私有包
                    _log.Info("正在生成导出包");
                    using var fsOut = new FileStream(finalPkgPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var writer = new BinaryWriter(fsOut);

                    // 写入私有头标识和 JSON
                    writer.Write(Encoding.UTF8.GetBytes("WIKIDATA"));
                    string json = JsonSerializer.Serialize(info, AppJsonContext.Custom.WikiPackageInfo);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                    writer.Write(jsonBytes.Length);
                    writer.Write(jsonBytes);

                    // 流式写入所有二进制数据 (将大量磁盘 I/O 隔离在后台)
                    foreach (var file in files)
                    {
                        string fileToRead = (file == originalDbPath) ? tempDbPath : file;
                        using var fsIn = File.OpenRead(fileToRead);
                        await fsIn.CopyToAsync(fsOut);
                    }
                });
                // ==========================================
                // 后台任务结束，回到 UI 线程
                // ==========================================

                // 5. 移动端/Mac端：调用系统分享弹窗 (UI 操作，需在主线程)
                if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
                {
                    await FileHelper.ExportFileAsync(finalPkgPath, exportFileName);
                }

                _log.Success($"数据导出成功: {finalPkgPath}");
                App.AppStateManager?.TriggerAlert("提示", $"数据导出成功");
            }
            catch (Exception ex)
            {
                _log.Error($"数据导出失败: {ex.Message}");
                App.AppStateManager?.TriggerAlert("错误", $"数据导出失败: {ex.Message}");
            }
            finally
            {
                // 6. 清理临时文件
                if (File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch { /* 忽略清理失败 */ }
                }

                // 如果是移动端，临时生成的包分享完后也要删掉防占用空间
                if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
                {
                    _ = FileHelper.ClearAppCacheAsync();
                }

                App.AppStateManager?.IsProcessing = false;
            }
        }

        //导入数据
        public async Task ImportData()
        {
            App.AppStateManager?.IsProcessing = true;
            _log.Info("开始导入数据");
            string filePath = null;

            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".pkg" } },
        });
                if (Application.Current?.Windows[0].Page is MainPage mainPage)
                {
                    mainPage.ShowLoadingPopup("导入数据", "正在导入数据，请稍候...");
                }

                if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
                    filePath = await FileHelper.ImportFileAsync("请选择导入包", customFileType);
                }
                else
                {
                    filePath = await FileHelper.ImportFileAsync("请选择导入包");
                }
                
                if (string.IsNullOrEmpty(filePath)) return;


                // ====== 核心修改开始 ======
                // 声明一个变量，用来把后台线程解析出的 meta 数据传递给外面的 UI 线程
                WikiPackageInfo meta = null;

                // 使用 Task.Run 把所有耗时的同步 I/O 和 CPU 计算全部扔到后台线程池！
                await Task.Run(() =>
                {
                    using var fsIn = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var reader = new BinaryReader(fsIn);

                    // 1. 校验私有头
                    _log.Info("正在验证导入包格式");
                    byte[] headerBytes = reader.ReadBytes(8);
                    if (Encoding.UTF8.GetString(headerBytes) != "WIKIDATA")
                    {
                        throw new Exception("非法的文件格式：无法识别该导入包！");
                    }

                    // 2. 读取元数据
                    int jsonLen = reader.ReadInt32();
                    string json = Encoding.UTF8.GetString(reader.ReadBytes(jsonLen));
                    Debug.Write(json);
                    if(DeviceInfo.Platform == DevicePlatform.WinUI)
                    {
                        meta = JsonSerializer.Deserialize<WikiPackageInfo>(json, AppJsonContext.Custom.WikiPackageInfo);
                    }
                    else
                    {
                        meta = JsonSerializer.Deserialize<WikiPackageInfo>(json);
                    }

                    if (!Directory.Exists(_tempDir)) Directory.CreateDirectory(_tempDir);

                    // 3. 逐个提取文件并实时校验 MD5
                    _log.Info("正在提取文件并校验数据完整性");
                    using var md5 = MD5.Create();
                    byte[] buffer = new byte[1024 * 1024];

                    foreach (var fileMeta in meta.Files)
                    {
                        string outPath = Path.Combine(_tempDir, fileMeta.RelativePath);
                        string outDir = Path.GetDirectoryName(outPath);
                        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                        using var fsOut = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                        long remainingBytes = fileMeta.Size;
                        int bytesRead;
                        md5.Initialize();

                        while (remainingBytes > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remainingBytes);
                            bytesRead = fsIn.Read(buffer, 0, toRead);
                            if (bytesRead == 0) throw new Exception("文件意外结束，包可能已损坏！");

                            fsOut.Write(buffer, 0, bytesRead);
                            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                            remainingBytes -= bytesRead;
                        }

                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        string calculatedMd5 = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();

                        if (calculatedMd5 != fileMeta.MD5)
                        {
                            throw new Exception($"数据校验失败！文件已被篡改或损坏: {fileMeta.RelativePath}");
                        }
                    }
                });
                // 【关键】把耗时的本地文件夹删除和移动也放进后台线程
                _log.Info("正在替换本地文件");
                await App.ContentDb.CloseConnection();
                await Task.Run(() =>
                {
                    if (Directory.Exists(_baseDir))
                    {
                        Directory.Delete(_baseDir, true); // 现在文件没被锁了，删得干干净净！
                    }
                    Directory.Move(_tempDir, _baseDir);
                });

                // ====== 核心修改结束 ======

                // 从这里开始，Task.Run 结束，代码又回到了 UI 线程
                // 以下是数据库操作（它们本身已经是真正的异步了，不需要包裹在 Task.Run 里）
                _log.Info("正在更新数据库");


                WikiBook wikiBook = await App.ManagerDb.GetItemAsync<WikiBook>(meta.Id);
                wikiBook.IsPageDownloaded = meta.IsPageDownloaded;
                wikiBook.IsResourceDownloaded = meta.IsResourceDownloaded;
                wikiBook.UpdateTime = meta.UpdateTime;

                await App.ManagerDb.SaveItemAsync(wikiBook);
                await App.ContentDb.ReconnectAsync();
                await AppService.RefreshWikiBookAsync(App.ManagerDb, App.ContentDb);
                await AppService.WikiRefreshAsync();
                await App.WebServer.Refresh();

                _log.Success("数据导入成功");
                App.AppStateManager?.TriggerAlert("提示", "数据导入成功");
            }
            catch (Exception ex)
            {
                _log.Error($"数据导入失败: {ex.Message}");
                App.AppStateManager?.TriggerAlert("错误", $"数据导入失败: {ex.Message}");
            }
            finally
            {
                // ... (保持你原本的清理逻辑不变)
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
                if ((DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS) && !string.IsNullOrEmpty(filePath))
                {
                    _ = FileHelper.ClearAppCacheAsync();
                }
                if (Application.Current?.Windows[0].Page is MainPage mainPage)
                {
                    mainPage.HideLoadingPopup();
                }
                App.AppStateManager?.IsProcessing = false;
            }
        }

        // ================= 核心功能 1: 获取页面清单 =================
        private async Task<int> GetWikiPagesListAsync()
        {
            _log.Info("开始获取页面清单");
            var writer = new BatchLineWriter(_pageListPath, 200);
            string? gapContinue = null;
            int pagesCount = 0;
            int retryCount = 0;
            bool isGuideMode = false;
            string currentBaseUrl = BaseApiUrl + "?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace=0&gapfilterredir=nonredirects&gaplimit=max";

            while (true) // 逻辑未变，简化循环写法
            {
                string currentUrl = currentBaseUrl + (string.IsNullOrEmpty(gapContinue) ? "" : $"&gapcontinue={Uri.EscapeDataString(gapContinue)}");
                _log.Info($"{pagesCount} 条已获取");

                try
                {
                    string jsonResponse = await _httpClient.GetStringAsync(currentUrl);
                    retryCount = 0; // 成功重置

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawData = JsonSerializer.Deserialize(jsonResponse, AppJsonContext.Custom.RawResponse);

                    if (rawData?.Query?.Pages != null)
                    {
                        foreach (var page in rawData.Query.Pages.Values)
                        {
                            writer.Add($"{page.Title}|{page.Touched}");
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
                    if (++retryCount > _maxRetryAttempts) throw;
                    _log.AppendLog($"请求失败: {e.Message} - 正在重试 ({retryCount}/{_maxRetryAttempts})...");
                    await Task.Delay(1000);
                }
            }
            writer.Flush();
            _log.AppendLog($"获取完毕，共获取 {pagesCount} 个页面");

            return pagesCount;
        }

        private async Task GetWikiRedirectsListAsync()
        {
            string nextUrl = RedirectStartUrl;
            int pageCount = 1;
            int totalRedirects = 0;
            _log.AppendLog("开始获取重定向列表");
            while (!string.IsNullOrEmpty(nextUrl))
            {
                int retry = 0;
                while (true)
                {
                    try
                    {
                        string fullUrl = BaseUrl + nextUrl;
                        string html = await _httpClient.GetStringAsync(fullUrl);
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
                        var listItems = doc.DocumentNode.SelectNodes("//div[@class='mw-spcontent']//ol/li");

                        if (listItems == null)
                        {
                            _log.Error("错误：本页没有找到数据，可能已结束或结构改变");
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
                        _log.AppendLog($"第 {pageCount} 页解析出 {countOnPage} 条重定向");
                        var nextLinkNode = doc.DocumentNode.SelectSingleNode("//a[@class='mw-nextlink']");

                        if (nextLinkNode != null)
                        {
                            nextUrl = HtmlEntity.DeEntitize(nextLinkNode.GetAttributeValue("href", ""));
                            pageCount++;
                            await Task.Delay(500);
                        }
                        else
                        {
                            _log.Success("重定向列表获取成功");
                            nextUrl = null;
                            break;
                        }

                    }
                    catch (Exception ex)
                    {
                        if (++retry > _maxRetryAttempts)
                        {
                            _log.Error($"重定向列表获取失败 (已重试{_maxRetryAttempts}次): {ex.Message}");
                            nextUrl = null; // 停止整个大循环
                            throw; // 继续抛出异常到外层，触发整体失败逻辑
                        }
                        _log.Error($"获取重定向列表出错，正在重试 ({retry}/{_maxRetryAttempts})...");
                        await Task.Delay(1000); // 间隔1秒
                    }
                }

            }

        }
        // ================= 核心功能 2: 批量任务调度器 =================

        private async Task RunBatchJobAsync(string inputPath, string failedPath, int concurrency, Func<int, string, Task> itemProcessor, Action? preWork = null, Action? postWork = null)
        {

            _log.Info($"开始任务：最大并发 {concurrency}");

            // ================= 修改开始 =================
            // 使用 using 确保任务结束时执行 Dispose()，从而执行最后一次文件截断
            using var urlProvider = new BatchLineProvider(inputPath, batchSize: 50);
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
        private async Task RunWorkerLoopAsync(int workerId, BatchLineProvider provider, string failedPath, Func<int, string, Task> processAction)
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
                        catch (HttpRequestException httpEx) when (httpEx.StatusCode == HttpStatusCode.NotFound)
                        {
                            // 如果遇到 404 (NotFound)，直接抛出异常，不进入下面的常规 Exception 重试
                            _log.Info($"[Worker {workerId}] 资源不存在，放弃重试，不计入失败列表: {line}");
                            break;
                        }
                        catch (Exception)
                        {
                            if (++retry > _maxRetryAttempts) throw;
                            _log.Error($"[Worker {workerId}] 失败重试 ({retry}/{_maxRetryAttempts}): {line}");
                            await Task.Delay(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"[Worker {workerId}] 错误: {ex.Message}");
                    await DataService.AppendFailedUrlAsync(failedPath, line);
                }
            }
        }

        // ================= 业务入口: 下载页面 =================
        private async Task StartDownloadPagesAsync(string pageListPath, string resListPath, string failedPageListPath, int maxConcurrency)
        {
            var writer = new BatchLineWriter(resListPath, 200);
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(pageListPath))
            {
                totalCount = File.ReadLines(pageListPath).Count();
            }
            _log.Info($"开始下载页面，共 {totalCount} 个");
            // 定义如何处理单行数据


            async Task ProcessPageLine(int workerId, string line)
            {
                var parts = line.Split('|');
                if (parts.Length < 2) return;
                var page = new PageInfo { Title = parts[0], LastModified = DateTime.Parse(parts[1]) };
                try
                {
                    await DownloadAndSavePageToDbAsync(page, writer);
                }
                finally
                {
                    int c = Interlocked.Increment(ref currentCount);
                    _log.Info($"[Worker {workerId}] {c}/{totalCount} 完成页面: {page.Title}");
                }


            }

            // 启动通用任务
            await RunBatchJobAsync(pageListPath, failedPageListPath, maxConcurrency, ProcessPageLine,
                postWork: () => writer.Flush());
            _log.Info("所有页面下载完毕");
            // 爬取完成后，清洗一下数据
            _log.Info("正在处理重复数据");
            string tempFile = Path.Combine(_baseDir, $"temp_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            FileHelper.RemoveDuplicatesOptimized(resListPath, tempFile);

            // 替换原文件
            File.Delete(resListPath);
            File.Move(tempFile, resListPath, true);
            _log.Info("重复数据处理完毕");

        }

        // ================= 业务入口: 下载资源 =================
        private async Task StartDownloadResAsync(string resListPath, string failedResListPath, int maxConcurrency, bool deleteFile = false)
        {
            int totalCount = 0;
            int currentCount = 0;
            if (File.Exists(resListPath))
            {
                totalCount = File.ReadLines(resListPath).Count();
            }
            _log.Info($"开始下载资源文件，共 {totalCount} 个");
            async Task ProcessResLine(int workerId, string url)
            {

                string fileName = DataService.GetFileNameFromUrl(url);
                try
                {
                    await DownloadAndSaveResToDbAsync(url, fileName);
                }
                finally
                {
                    int c = Interlocked.Increment(ref currentCount);
                    _log.Info($"[Worker {workerId}] {c}/{totalCount} 完成资源: {fileName}");
                }

            }
            if (!deleteFile)
            {
                File.Copy(resListPath, _tempResListPath, true);
                // 启动通用任务
                await RunBatchJobAsync(_tempResListPath, failedResListPath, maxConcurrency, ProcessResLine);
            }
            else
            {
                await RunBatchJobAsync(resListPath, failedResListPath, maxConcurrency, ProcessResLine);
            }

            _log.Info("资源文件下载完毕");
        }


        // ================= 具体的处理逻辑 =================

        private async Task DownloadAndSavePageToDbAsync(PageInfo pageInfo, BatchLineWriter writer)
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
            ProcessImages(contentNode, writer);

            var wikiPage = new WikiPage
            {
                Title = pageInfo.Title,
                Content = contentNode.OuterHtml,
                LastModified = pageInfo.LastModified
            };
            await App.ContentDb.SaveItemAsync(wikiPage);
            var plainContent = ExtractSearchableText(contentNode);
            await App.ContentDb.SaveSearchIndexAsync(pageInfo.Title, plainContent);

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
                    n.SetAttributeValue("title", n.GetAttributeValue("title", "") + href.Substring(hashIndex));
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

        private void ProcessImages(HtmlNode node, BatchLineWriter writer)
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
                src = DataService.CleanUpUrl(src);
                // 写入文件
                writer.Add(src);
                string htmlSrc = Uri.EscapeDataString(DataService.GetFileNameFromUrl(src));
                // 替换为本地路径
                n.SetAttributeValue("src", "/src/" + htmlSrc);
            });
        }

        //处理搜索索引
        private static string ExtractSearchableText(HtmlNode contentNode)
        {


            var notNeedNodes = contentNode.SelectNodes("//div[contains(concat(' ', @class, ' '), ' message-box ') or contains(concat(' ', @class, ' '), ' infobox ') or contains(concat(' ', @role, ' '), ' navigation ')]");

            if (notNeedNodes != null)
            {
                foreach (var node in notNeedNodes)
                {
                    node.Remove();
                }
            }

            var targetNodes = contentNode.SelectNodes("//p | //h1 | //h2 | //h3 | //h4 | //h5 | //h6 | //li");
            string plainText = string.Empty;

            if (targetNodes != null)
            {
                plainText = string.Join(" ", targetNodes.Select(n => n.InnerText));
            }

            plainText = WebUtility.HtmlDecode(plainText);
            return Regex.Replace(plainText, @"\s+", " ").Trim();
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

        //更新成员变量
        private async Task InitializeSettings()
        {
            _maxRetryAttempts = Preferences.Default.Get("MaxRetryAttempts", 5);
            _pageConcurrency = Preferences.Default.Get("PageConcurrency", 2);
            if (Preferences.Default.Get("PageConcurrency", 2) > 3)
            {
                _pageConcurrency = 2;
                Preferences.Default.Set("PageConcurrency", 2);
            }
            _resConcurrency = Preferences.Default.Get("ResConcurrency", 10);
            if (!Directory.Exists(_baseDir)) Directory.CreateDirectory(_baseDir);
            CleanUpTempFile();

        }

        //清理临时文件
        private void CleanUpTempFile()
        {
            _log.Info("正在清理临时文件");
            if (File.Exists(_pageListPath))
            {
                File.Delete(_pageListPath);
            }

            if (File.Exists(_tempResListPath))
            {
                File.Delete(_tempResListPath);
            }
            if (File.Exists(_tempFailedPageListPath))
            {
                File.Delete(_tempFailedPageListPath);
            }
            if (File.Exists(_tempFailedResListPath))
            {
                File.Delete(_tempFailedResListPath);
            }
            if (File.Exists(_updatePageListPath))
            {
                File.Delete(_updatePageListPath);
            }
            if (File.Exists(_updateResListPath))
            {
                File.Delete(_updateResListPath);
            }
            _log.Info("临时文件清理完毕");
        }

        //清理 URL 中的查询参数，获取干净的文件名
        private static string CleanUpUrl(string url)
        {
            int qIdx = url.IndexOf('?');
            return (qIdx > 0) ? url.Substring(0, qIdx) : url;
        }

        // 从 URL 中提取文件名，并进行 URL 解码
        private static string GetFileNameFromUrl(string url)
        {
            string cleanUrl = DataService.CleanUpUrl(url);
            string name = cleanUrl.Substring(cleanUrl.LastIndexOf('/') + 1);
            string decodedName = WebUtility.UrlDecode(name);
            return decodedName;
        }

        // 追加失败的 URL 到文件，使用异步方法并捕获异常以防止崩溃
        private static async Task AppendFailedUrlAsync(string path, string url)
        {
            await File.AppendAllLinesAsync(path, [url]);
        }


    }

    // ================= 保持原逻辑的辅助类 (稍微整理格式) =================

    public class BatchLineWriter
    {
        private readonly string _filePath;
        private readonly int _batchSize;
        private readonly List<string> _buffer;
        private readonly object _lock = new();

        public BatchLineWriter(string filePath, int batchSize = 200)
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

    public class BatchLineProvider : IDisposable
    {
        private readonly string _filePath;
        private readonly int _batchSize;
        private readonly ConcurrentQueue<string> _memoryQueue = new();
        private readonly object _fileLock = new();
        private bool _isFileExhausted = false;

        // 新增：记录上一次应该截断的位置
        private long _pendingTruncatePosition = -1;

        public BatchLineProvider(string filePath, int batchSize = 50)
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


}