using Microsoft.JSInterop;
using Terraria_Wiki.Models; // 如果需要引用某些模型
using System.Threading.Tasks;

namespace Terraria_Wiki.Services
{
    public class ThemeService
    {
        private static IJSRuntime? _js;

        // 初始化注入 JS 运行时
        public static void Init(IJSRuntime jsRuntime)
        {
            _js = jsRuntime;
        }

        // --- 属性读取（仅负责读） ---

        public static string AppTheme => Preferences.Default.Get("AppTheme", "auto");

        public static string ContentTheme => Preferences.Default.Get("ContentTheme", "auto");

        // --- 状态修改（负责存入 Preferences、同步 localStorage 并更新 UI） ---

        public static async Task SetAppThemeAsync(string value)
        {
            // 1. 存入 C# 存储
            Preferences.Default.Set("AppTheme", value);

            // 2. 存入前端 localStorage
            if (_js != null)
            {
                await _js.InvokeVoidAsync("localStorage.setItem", "app-theme", value);
            }

        }

        public static async Task SetContentThemeAsync(string value)
        {
            // 1. 存入 C# 存储
            Preferences.Default.Set("ContentTheme", value);

            // 2. 存入前端 localStorage (修复了你原来代码中存错键名的 Bug)
            if (_js != null)
            {
                await _js.InvokeVoidAsync("localStorage.setItem", "content-theme", value);
            }

        }


        public static async Task InitThemeAsync()
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
            else // auto
            {
                if (_js != null)
                {
                    isDark = await _js.InvokeAsync<bool>("checkTheme");
                }
            }

            // 更新全局状态
            App.AppStateManager.IsDarkTheme = isDark;
        }

    }
}