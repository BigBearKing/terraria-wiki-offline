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

                if (page != null)
                {


                    WikiPageStringTime result = new WikiPageStringTime();
                    result.Title = page.Title;
                    result.Content = page.Content;
                    result.LastModified = page.LastModified.ToString("yyyy年MM月dd日 HH:mm");
                    App.AppStateManager.CurrentWikiPage = page.Title;
                    if (page.Title != "Terraria Wiki")
                        Task.Run(async () => await SaveToHistoryAsync(page.Title));


                    return IframeBridge.ObjToJson(result);
                }
                else
                {
                    return null;
                }

            };

            IframeBridge.Actions["GetRedirectedTitleAndAnchorAsync"] = async (input) =>
            {
                // 1. 如果没有锚点，先检查是否需要重定向，如果需要则替换 input
                if (input.IndexOf('#') == -1 && await App.ContentDb.ItemExistsAsync<WikiRedirect>(input))
                {
                    var redirect = await App.ContentDb.GetItemAsync<WikiRedirect>(input);
                    input = redirect.ToTarget; // 此时 input 变成了目标字符串（可能带#，也可能不带）
                }

                // 2. 统一处理分割逻辑 (Split只需写一次)
                // 限制只分割成2部分，确保只取第一个#之后的内容作为锚点
                var parts = input.Split(new[] { '#' }, 2);

                var result = new TitleWithAnchor
                {
                    Title = parts[0],
                    Anchor = parts.Length > 1 ? parts[1] : null
                };

                return IframeBridge.ObjToJson(result);
            };

            IframeBridge.Actions["SaveToTempHistory"] = async (args) =>
            {

                TempHistory tempHistory = IframeBridge.JsonToObj<TempHistory>(args);
                App.AppStateManager.TempHistory.Add(tempHistory);

                return null;
            };

        }



        private static async Task SaveToHistoryAsync(string title)
        {
            var history = new WikiHistory
            {
                WikiTitle = title,
                ReadAt = DateTime.Now,
                DateKey = DateTime.Now.ToString("yyyy-MM-dd")
            };
            await App.ContentDb.SaveHistoryAsync(history);

        }

        public static async Task WikiBackAsync()
        {
            var list = App.AppStateManager.TempHistory;
            var listcount = list.Count;
            if (listcount != 0)
            {
                await IframeBridge.CallJsAsync("BackToPage", IframeBridge.ObjToJson(list[listcount - 1]));
                list.RemoveAt(listcount - 1);
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("提示", "这已经是首页。", "确定");
            }

        }
        public static async Task WikiBackHomeAsync()
        {
            var list = App.AppStateManager.TempHistory;
            var listcount = list.Count;
            if (listcount != 0)
            {
                await IframeBridge.CallJsAsync("BackHome", "");
                list.Clear();
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("提示", "这已经是首页。", "确定");
            }

        }







        public static async Task RefreshWikiBookAsync(DatabaseService wikiBook, DatabaseService wikiContent)
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
