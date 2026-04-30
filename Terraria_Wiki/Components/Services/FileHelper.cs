using System.IO.Compression;
using System.Text;
namespace Terraria_Wiki.Services // 记得改成你项目的命名空间
{
    public static class FileHelper
    {
        /// <summary>
        /// 导入文件（跨平台读取，自动处理移动端沙盒权限）
        /// </summary>
        /// <param name="title">弹窗标题</param>
        /// <param name="types">文件类型限制（传 null 则允许所有文件）</param>
        /// <returns>返回可读取的本地绝对路径。如果用户取消则返回 null。</returns>
        public static async Task<string> ImportFileAsync(string title = "请选择文件", FilePickerFileType types = null)
        {

            var options = new PickOptions
            {
                PickerTitle = title,
                FileTypes = types
            };

            var result = await FilePicker.Default.PickAsync(options);
            if (result == null) return null;



            // 桌面端：直接返回绝对路径
            return result.FullPath;

        }

        /// <summary>
        /// 导出文件（Windows 弹出文件夹选择，移动端/Mac 调用原生分享保存）
        /// </summary>
        /// <param name="sourceFilePath">要导出的源文件全路径（必须存在）</param>
        public static async Task ExportFileAppleAsync(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                System.Diagnostics.Debug.WriteLine("导出失败：源文件不存在。");
                return;
            }

            // iOS 端及 Mac 端：调用系统级的“分享/存储为”功能
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "导出文件",
                File = new ShareFile(sourceFilePath)
            });

        }


#if WINDOWS
        /// <summary>
        /// Windows 原生选择文件夹方法 (私有方法，仅限 Windows 编译)
        /// </summary>
        public static async Task<string> PickFolderWindowsAsync()
        {

            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            // 获取 Windows 原生窗口句柄
            var window = Application.Current?.Windows?[0]?.Handler?.PlatformView as MauiWinUIWindow;
            if (window == null) return null;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;

        }
#endif

        /// <summary>
        /// 清理导入时产生的临时缓存文件（移动端专属安全清理）
        /// </summary>
        public static async Task<long> ClearAppCacheAsync()
        {
            long freedSpace = 0;

            await Task.Run(() =>
            {
                try
                {
                    string cachePath = FileSystem.CacheDirectory;

                    if (!Directory.Exists(cachePath)) return;

                    // 1. 删除缓存根目录下的所有散落文件
                    string[] files = Directory.GetFiles(cachePath);
                    foreach (string file in files)
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(file);
                            long size = fi.Length;
                            fi.Delete();
                            freedSpace += size;
                        }
                        catch
                        {
                            // 忽略单个文件删除失败（可能正被系统占用或刚被创建）
                        }
                    }

                    // 2. 删除缓存目录下的所有子文件夹（递归删除）
                    string[] directories = Directory.GetDirectories(cachePath);
                    foreach (string dir in directories)
                    {
                        try
                        {

                            // true 表示连同里面的文件一起强制删除
                            Directory.Delete(dir, true);
                        }
                        catch
                        {
                            // 忽略单个文件夹删除失败
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[缓存清理] 完成！共释放空间: {freedSpace / 1024 / 1024} MB");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[缓存清理] 发生严重错误: {ex.Message}");
                }
            });

            return freedSpace;
        }

        //获取文件大小（字节），用于逻辑判断
        public static long GetSizeBytes(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return 0;
            }

            return new FileInfo(filePath).Length;
        }

        // 2. 获取格式化后的大小（字符串），用于 UI 显示
        public static string GetSizeString(long bytes)
        {

            return FormatBytes(bytes);
        }

        //这是一个静态辅助工具，负责把 long 转成 "MB", "KB"
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }

            //保留2位小数
            return string.Format("{0:n2} {1}", number, suffixes[counter]);
        }

        //文本去重
        public static int RemoveDuplicatesOptimized(string inputPath, string outputPath)
        {
            var uniqueSet = new HashSet<string>(150000);

            using (var reader = new StreamReader(inputPath))
            using (var writer = new StreamWriter(outputPath))
            {
                string? line;
                int duplicateCount = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    if (uniqueSet.Add(line))
                    {
                        writer.WriteLine(line);
                    }
                    else
                    {
                        duplicateCount++;
                    }
                }
                return duplicateCount;
            }
        }

        //追加文件
        public static async Task AppendFileAsync(string sourcePath, string destPath)
        {
            if (!File.Exists(sourcePath)) return;

            // 打开源文件（只读流）
            using var sourceStream = File.OpenRead(sourcePath);

            // 打开目标文件（追加模式，如果不存在会自动创建）
            using var destStream = new FileStream(destPath, FileMode.Append, FileAccess.Write, FileShare.None);


            // 将源流的内容异步复制并追加到目标流的尾部
            await sourceStream.CopyToAsync(destStream);
        }

        //判断文件是否为空
        public static bool IsFileValid(string filePath)
        {
            const long MinValidSize = 1024; // 1KB

            // 1. 拦截无效路径
            if (string.IsNullOrEmpty(filePath))
                return false;

            // 2. 获取文件信息（先检查 Exists 可避免后续抛出异常）
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
                return false;

            // 3. 大于 1KB 直接判定为有效
            if (fileInfo.Length > MinValidSize)
                return true;

            // 4. ≤ 1KB 时，流式读取检查真实内容
            try
            {
                // 保持 FileShare.ReadWrite，允许读取其他程序正在打开/写入的文件
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // 使用 StreamReader，它会自动检测并跳过文件最开头的 BOM 字节
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                int charCode;
                // 逐个字符读取，而不是把整个文件加载到内存变字符串
                while ((charCode = reader.Read()) != -1)
                {
                    char c = (char)charCode;

                    // 检查：如果是非空白字符，且不是空字符(\0)，且不是 BOM 残留(\uFEFF)
                    if (!char.IsWhiteSpace(c) && c != '\0' && c != '\uFEFF')
                    {
                        return true; // 只要发现哪怕一个有意义的字符，立刻退出并判定有效 (Early Exit)
                    }
                }

                // 读到文件末尾，全都是空白、\0 或 BOM，则视为无效
                return false;
            }
            catch (Exception)
            {
                // 捕获权限不足或 I/O 错误，保守处理视为无效
                return false;
            }
        }


        //清空文件夹
        public static void ClearDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            var directory = new DirectoryInfo(path);

            foreach (FileInfo file in directory.GetFiles("*.*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }

            foreach (DirectoryInfo dir in directory.GetDirectories("*.*", SearchOption.AllDirectories))
            {
                dir.Delete(true);
            }
        }

        //创建zip
        public static void CreateZipFromDirectory(string sourceDirectory, string zipFilePath, bool overwrite = true)
        {
            // 如果 ZIP 已存在且需要覆盖，先删除
            if (File.Exists(zipFilePath) && overwrite)
                File.Delete(zipFilePath);

            // 直接把整个目录（含子文件夹）压缩成 ZIP
            ZipFile.CreateFromDirectory(sourceDirectory, zipFilePath, CompressionLevel.Optimal, false);
        }

        //添加zip
        public static async Task AddFilesToZip(string zipFilePath, string[] filesToAdd, bool overwrite = true)
        {
            using var stream = new FileStream(zipFilePath,
                File.Exists(zipFilePath) ? FileMode.Open : FileMode.Create,
                FileAccess.ReadWrite);

            using var archive = new ZipArchive(stream,
                File.Exists(zipFilePath) ? ZipArchiveMode.Update : ZipArchiveMode.Create, true);

            foreach (string file in filesToAdd)
            {
                if (!File.Exists(file)) continue;

                string entryName = Path.GetFileName(file);

                var existing = archive.GetEntry(entryName);
                if (existing != null && overwrite)
                    existing.Delete();

                // ★ 关键：用 FileShare.ReadWrite 打开可能被占用的日志文件
                using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                await fileStream.CopyToAsync(entryStream);   // 改成异步更好
            }
        }


    }
}