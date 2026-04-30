using System.Text;
using System.Threading.Channels;

namespace Terraria_Wiki.Services
{
    // 继承 IAsyncDisposable 确保应用退出时能优雅关闭文件
    public class LogService : IAsyncDisposable
    {
        private readonly string _activeLogPath;
        private readonly string _archiveFolderPath;

        // 内存索引
        private readonly List<long> _lineOffsets = new();

        // 优化1：使用读写锁 (读多写少时性能极佳，不再全量阻塞)
        private readonly ReaderWriterLockSlim _offsetLock = new();

        // 优化2：保持打开的文件流和写入器，拒绝每次追加时重新 Open/Close
        private FileStream _writeStream;
        private StreamWriter _streamWriter;

        // 优化3：引入线程安全的异步通道 (队列)，让 AppendLog 实现真正的“非阻塞”
        private readonly Channel<string> _logQueue;
        private readonly Task _processTask;
        private readonly CancellationTokenSource _cts = new();

        public event Action OnLogAdded;

        public LogService()
        {
            var basePath = FileSystem.AppDataDirectory;
            _archiveFolderPath = Path.Combine(basePath, "LogHistory");
            _activeLogPath = Path.Combine(basePath, "current_session.log");

            if (!Directory.Exists(_archiveFolderPath))
            {
                Directory.CreateDirectory(_archiveFolderPath);
            }

            InitializeSession();

            // 初始化无界异步队列
            _logQueue = Channel.CreateUnbounded<string>();
            // 启动后台专属消费线程，默默把队列里的日志写进硬盘
            _processTask = Task.Run(ProcessLogQueueAsync);
        }

        private void InitializeSession()
        {
            if (File.Exists(_activeLogPath))
            {
                var fileInfo = new FileInfo(_activeLogPath);

                // 只有文件有内容时才归档
                if (FileHelper.IsFileValid(_activeLogPath))
                {
                    // ★ 核心改变：获取文件的最后修改时间，代表最后一条日志的落盘时间
                    DateTime lastLogTime = fileInfo.LastWriteTime;

                    // 使用这个时间生成归档文件名
                    string timestamp = lastLogTime.ToString("yyyy-MM-dd_HH-mm-ss");
                    string archiveFileName = Path.Combine(_archiveFolderPath, $"log_{timestamp}.txt");

                    try
                    {
                        File.Move(_activeLogPath, archiveFileName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"归档失败: {ex.Message}");
                    }
                }
            }

            _lineOffsets.Clear();
            _lineOffsets.Add(0);

            // 以 Write 权限打开，并且允许其他进程/流 以 Read 方式共享读取
            _writeStream = new FileStream(_activeLogPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _streamWriter = new StreamWriter(_writeStream, Encoding.UTF8) { AutoFlush = true };
        }

        public void Info(string message) => AppendLog($"[INFO] {message}");

        public void Success(string message) => AppendLog($"[SUCCESS] {message}");

        public void Error(string message, Exception ex = null)
        {
            // 如果有异常对象，把异常类型和简短消息也带上
            var errorDetail = ex != null ? $" ({ex.GetType().Name}: {ex.Message})" : "";
            AppendLog($"[ERROR] {message}{errorDetail}");
        }

        /// <summary>
        /// 极速追加日志（仅推入内存队列，瞬间返回，绝对不卡 UI）
        /// </summary>
        public void AppendLog(string message)
        {
            _logQueue.Writer.TryWrite(message);
        }

        /// <summary>
        /// 后台专属写入任务：吃苦耐劳，排队写入
        /// </summary>
        private async Task ProcessLogQueueAsync()
        {
            try
            {
                // 等待队列有数据，没有数据时就在这休眠，不耗 CPU
                await foreach (var message in _logQueue.Reader.ReadAllAsync(_cts.Token))
                {
                    var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";

                    // 异步写入文件
                    await _streamWriter.WriteLineAsync(logLine);

                    // 获取刚写完的末尾位置
                    long newOffset = _writeStream.Position;

                    // 加写锁，更新内存索引
                    _offsetLock.EnterWriteLock();
                    try
                    {
                        _lineOffsets.Add(newOffset);
                    }
                    finally
                    {
                        _offsetLock.ExitWriteLock();
                    }

                    // 抛出事件通知 UI 更新
                    OnLogAdded?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                // 收到程序退出的令牌，正常终止
            }
        }

        public int GetTotalCount()
        {
            // 加读锁，允许多个读操作并发，互不影响
            _offsetLock.EnterReadLock();
            try
            {
                return Math.Max(0, _lineOffsets.Count - 1);
            }
            finally
            {
                _offsetLock.ExitReadLock();
            }
        }

        public async ValueTask<IEnumerable<string>> GetLogsAsync(int startIndex, int count)
        {
            var result = new List<string>();
            int total = GetTotalCount();
            if (startIndex >= total) return result;

            int actualCount = Math.Min(count, total - startIndex);
            long startPosition, endPosition;

            _offsetLock.EnterReadLock();
            try
            {
                startPosition = _lineOffsets[startIndex];
                endPosition = _lineOffsets[startIndex + actualCount];
            }
            finally
            {
                _offsetLock.ExitReadLock();
            }

            // ★ 核心改变：FileShare.ReadWrite 允许在写入流未关闭的情况下强行读取
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

        /// <summary>
        /// 程序退出时的资源清理，防文件损坏
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            // 停止接收新日志
            _logQueue.Writer.TryComplete();
            _cts.Cancel();

            // 给后台任务最多 1 秒的时间把剩下的队列排空
            if (_processTask != null)
            {
                await Task.WhenAny(_processTask, Task.Delay(1000));
            }

            // 安全关闭文件流
            if (_streamWriter != null) await _streamWriter.DisposeAsync();
            if (_writeStream != null) await _writeStream.DisposeAsync();
            _offsetLock.Dispose();
        }


        //导出日志
        public async Task ExportLogsAsync()
        {
            // 直接复制当前日志文件到指定位置，简单高效
            try
            {
                string fileName = $"logs_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
                string tempZipPath = Path.Combine(FileSystem.CacheDirectory, fileName);

                FileHelper.CreateZipFromDirectory(_archiveFolderPath, tempZipPath);
                await FileHelper.AddFilesToZip(tempZipPath, new[] { _activeLogPath });

                if (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
                {
                    // Apple 端依然走系统分享或原有的导出逻辑
                    await FileHelper.ExportFileAppleAsync(tempZipPath);
                }
                else if (DeviceInfo.Platform == DevicePlatform.Android)
                {
#if ANDROID
                    // 安卓端走 SAF 机制
                    var uri = await AndroidFileSaver.PickSaveLocationAsync(fileName, "application/zip");
                    if (uri != null)
                    {
                        using var fsIn = File.OpenRead(tempZipPath);
                        var resolver = Android.App.Application.Context.ContentResolver;
                        using var streamOut = resolver.OpenOutputStream(uri);
                        if (streamOut != null)
                        {
                            await fsIn.CopyToAsync(streamOut);
                            await streamOut.FlushAsync();
                        }
                    }
                    else
                    {
                        // 用户取消了保存，直接退出，不显示成功提示
                        return;
                    }
#endif
                }
                else if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
#if WINDOWS
            string outputFolder = await FileHelper.PickFolderWindowsAsync();
            if (string.IsNullOrEmpty(outputFolder)) 
            {
                return; // 优化：用户在 Windows 取消了选择，直接退出
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }
            string destinationPath = Path.Combine(outputFolder, Path.GetFileName(tempZipPath));
            File.Copy(tempZipPath, destinationPath, true);
#else
                    throw new PlatformNotSupportedException("当前平台不支持导出日志功能");
#endif
                }
                else
                {
                    throw new PlatformNotSupportedException("当前平台不支持导出日志功能");
                }

                App.AppStateManager.TriggerAlert("导出日志成功", fileName);
            }
            catch (Exception ex)
            {
                App.AppStateManager.TriggerAlert("导出日志失败", $"{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"导出日志失败: {ex.Message}");
            }
            finally
            {
                // 无论成功还是失败，最后都清理缓存目录，防止临时 zip 占用空间
                await FileHelper.ClearAppCacheAsync();
            }
        }

        //删除日志
        public void DeleteLogs()
        {

            FileHelper.ClearDirectory(_archiveFolderPath);
            App.AppStateManager.TriggerAlert("提示", "日志已清除");
        }
    }
}