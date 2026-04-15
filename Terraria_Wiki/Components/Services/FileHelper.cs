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