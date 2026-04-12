using Microsoft.JSInterop;
using Terraria_Wiki.Models; // 如果需要引用某些模型
using System.Threading.Tasks;

namespace Terraria_Wiki.Services
{
    public class ThemeService
    {

        // --- 属性读取（仅负责读） ---

        public static string AppTheme => Preferences.Default.Get("AppTheme", "auto");

        public static string ContentTheme => Preferences.Default.Get("ContentTheme", "auto");



        public static async Task SetAppThemeAsync(string value)
        {

            Preferences.Default.Set("AppTheme", value);


        }

        public static async Task SetContentThemeAsync(string value)
        {

            Preferences.Default.Set("ContentTheme", value);

        }


        public static void InitTheme()
        {
            bool isDark = false;
            var theme = AppTheme;

            if (theme == "dark")
            {
                isDark = true;
            }
            else if (theme == "light")
            {
                isDark = false;
            }
            else // auto 跟随系统
            {
                // 使用 MAUI 原生 API 获取系统当前主题，瞬间完成！
                isDark = Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
            }

            App.AppStateManager.IsDarkTheme = isDark;
        }
        public static async Task InitWebThemeAsync(IJSRuntime JS)
        {
            await JS.InvokeAsync<object>("initTheme", App.AppStateManager.IsDarkTheme ? "True" : "False");
        }
        public static string GetIframeThemeState()
        {
            if (ContentTheme == "auto")
            {
                // 注意这里，返回全小写的字符串方便前端处理
                return App.AppStateManager.IsDarkTheme ? "dark" : "light";
            }
            return "original";
        }

    }
}