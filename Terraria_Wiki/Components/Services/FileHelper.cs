using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

#if WINDOWS
using Microsoft.Maui.Controls;
#endif

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

            // 移动端：因权限问题，先将文件拷贝到 App 缓存区，才能返回有效路径供底层读取
            if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS)
            {
                var targetPath = Path.Combine(FileSystem.CacheDirectory, result.FileName);
                await Task.Delay(500);

                await Task.Run(async () =>
                {

                    int bufferSize = 1024 * 1024; // 1MB 缓冲区

                    using (var stream = await result.OpenReadAsync())
                    // 不要用 File.Create，用 FileStream 自己定义缓冲区大小
                    using (var newFile = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize))
                    {
                        // CopyToAsync 也必须指定同样的缓冲区大小
                        await stream.CopyToAsync(newFile, bufferSize);
                    }
                });

                return targetPath;
            }

            // 桌面端：直接返回绝对路径
            return result.FullPath;

        }

        /// <summary>
        /// 导出文件（Windows 弹出文件夹选择，移动端/Mac 调用原生分享保存）
        /// </summary>
        /// <param name="sourceFilePath">要导出的源文件全路径（必须存在）</param>
        /// <param name="exportFileName">希望用户保存时的默认文件名</param>
        public static async Task ExportFileAsync(string sourceFilePath, string exportFileName)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                System.Diagnostics.Debug.WriteLine("导出失败：源文件不存在。");
                return;
            }

            // 移动端及 Mac 端：调用系统级的“分享/存储为”功能
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
        /// <param name="filePath">需要清理的文件路径</param>
        public static void CleanupTempFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            // 【安全锁】检查该路径是否真的在 App 的缓存目录中
            // 这样能绝对防止在 Windows 端误删用户的原文件
            string cacheDir = FileSystem.CacheDirectory;

            if (filePath.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    System.Diagnostics.Debug.WriteLine($"[安全清理] 已删除临时文件: {filePath}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[安全拦截] 该文件不在缓存区，跳过清理以保护源文件: {filePath}");
            }

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

        //判断文件有效性
        public static bool IsFileValid(string filePath)
        {
            try
            {

                //文件是否存在
                if (!File.Exists(filePath)) return false;
                //文件大小是否大于 0 字节
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0) return false;
                return true;
            }
            catch (Exception ex)
            {
                // 捕获权限异常或路径非法异常
                System.Diagnostics.Debug.WriteLine($"文件有效性检查失败: {ex.Message}");
                return false;
            }
        }

    }
}