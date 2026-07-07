using System.Text.Json;

namespace Terraria_Wiki.Services
{
    public class LocalizationService
    {
        private Dictionary<string, string> _translations = new();
        private string _currentLanguage = "zh-CN";

        public event Action? OnChange;

        public LocalizationService()
        {
        }

        /// <summary>
        /// 初始化并加载语言文件（从 Preferences 读取上次语言选择）
        /// </summary>
        public async Task InitializeAsync()
        {
            var savedLang = Preferences.Default.Get("AppLanguage", "");
            if (!string.IsNullOrEmpty(savedLang))
                _currentLanguage = savedLang;
            await LoadLanguage(_currentLanguage);
            NotifyStateChanged();
        }

        /// <summary>
        /// 切换语言并通知 UI 刷新
        /// </summary>
        public async Task SetLanguageAsync(string languageCode)
        {
            if (_currentLanguage == languageCode) return;
            _currentLanguage = languageCode;
            Preferences.Default.Set("AppLanguage", languageCode);
            await LoadLanguage(languageCode);
            NotifyStateChanged();
        }

        /// <summary>
        /// 获取当前语言代码
        /// </summary>
        public string CurrentLanguage => _currentLanguage;

        /// <summary>
        /// 获取支持的语言列表
        /// </summary>
        public static readonly (string Code, string Name)[] SupportedLanguages = new[]
        {
            ("zh-CN", "中文"),
            ("en-US", "English"),
        };

        private async Task LoadLanguage(string languageCode)
        {
            try
            {
                string filename = $"Languages/{languageCode.ToLower()}.json";
                using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var jsonDoc = JsonDocument.Parse(json);
                _translations.Clear();

                if (jsonDoc.RootElement.TryGetProperty("strings", out var stringsElement))
                {
                    foreach (var property in stringsElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            _translations[property.Name] = property.Value.GetString() ?? property.Name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load language {languageCode}: {ex.Message}");
                _translations.Clear();
            }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();

        /// <summary>
        /// 根据键名返回翻译值，如果不存在则返回键名本身
        /// </summary>
        public string Get(string key)
        {
            return _translations.TryGetValue(key, out var value) ? value : key;
        }

        /// <summary>
        /// 根据键名返回翻译值，支持格式化参数
        /// </summary>
        public string Get(string key, params object[] args)
        {
            var translation = Get(key);
            try
            {
                return string.Format(translation, args);
            }
            catch
            {
                return translation;
            }
        }

        /// <summary>
        /// 获取所有翻译键
        /// </summary>
        public IEnumerable<string> GetKeys() => _translations.Keys;

        /// <summary>
        /// 检查键是否存在
        /// </summary>
        public bool HasKey(string key) => _translations.ContainsKey(key);
    }
}
