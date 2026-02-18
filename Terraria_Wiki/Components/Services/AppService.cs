using System.Text.Encodings.Web;
using System.Text.Json;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services
{
    public class AppService
    {

        public AppService()
        {
            IframeBridge.Actions["PageRedirectAsync"] = async (title) =>
            {
                WikiPage page;
                if (await App.ContentDb.ItemExistsAsync<WikiPage>(title))
                {
                    page = await App.ContentDb.GetItemAsync<WikiPage>(title);
                }
                else if (await App.ContentDb.ItemExistsAsync<WikiRedirect>(title))
                {
                    var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(title);
                    page = await App.ContentDb.GetItemAsync<WikiPage>(redirect.ToTarget);
                }
                else
                {
                    await Application.Current.MainPage.DisplayAlert("提示", "页面不存在。", "确定");
                    return null;

                }
                WikiPageStringTime result = new WikiPageStringTime();
                if (page != null)
                {
                    result.Title = page.Title;
                    result.Content = page.Content;
                    result.LastModified = GetFormattedDate(page.LastModified, 2);
                    App.AppStateManager.CurrentWikiPage = page.Title;
                    Task.Run(async () => await SaveToHistory(page.Title));
                    return IframeBridge.ObjToJson(result);
                }
                else
                {
                    return null;
                }

            };
        }



        public static void SaveToTempHistory(string title,int position)
        {
            var tempHistory = new TempHistory { Title = title, Position = position };
            App.AppStateManager.TempHistory.Add(tempHistory);
        }

        public static async Task SaveToHistory(string title)
        {
            var history = new WikiHistory
            {
                WikiTitle = title,
                ReadAt = DateTime.Now,
                DateKey = DateTime.Now.ToString("yyyy-MM-dd")
            };
            await App.ContentDb.SaveHistoryAsync(history);

        }








        public static async Task RefreshWikiBook(DatabaseService wikiBook, DatabaseService wikiContent)
        {
            var book = await wikiBook.GetItemAsync<WikiBook>(1);
            book.PageCount = await wikiContent.GetCountAsync<WikiPage>();
            book.RedirectCount = await wikiContent.GetCountAsync<WikiRedirect>();
            book.ResourceCount = await wikiContent.GetCountAsync<WikiAsset>();
            book.DataSize = GetSizeBytes(wikiContent.DatabasePath);
            await wikiBook.SaveItemAsync(book);
        }
        // 1. 获取文件大小（字节），用于逻辑判断
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
        public static string GetFormattedDate(DateTime time, int type = 0)
        {
            string formattedDate;

            if (type == 0)
            {
                formattedDate = time.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (type == 1) {
            
                formattedDate = time.ToString("yy-MM-dd HH:mm");
            }
            else { 
                formattedDate = time.ToString("yyyy年MM月dd日 HH:mm");
            }

            return formattedDate;
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


    }
}
