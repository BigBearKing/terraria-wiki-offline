using System.Text.Json;

namespace Terraria_Wiki.Services
{
    public class LocalizationService
    {
        private Dictionary<string, string> _translations = new();
        private string _currentLanguage = "zh-cn";

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
                _currentLanguage = NormalizeCode(savedLang);
            await LoadLanguage(_currentLanguage);
            NotifyStateChanged();
        }

        /// <summary>
        /// 切换语言并通知 UI 刷新
        /// </summary>
        public async Task SetLanguageAsync(string languageCode)
        {
            var normalized = NormalizeCode(languageCode);
            if (_currentLanguage == normalized) return;
            _currentLanguage = normalized;
            Preferences.Default.Set("AppLanguage", normalized);
            await LoadLanguage(normalized);
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
            ("zh-cn", "中文"),
            ("en-us", "English"),
        };

        /// <summary>
        /// 将语言代码规范化为文件名格式（zh-CN → zh-cn, 容错 zh → zh-cn）
        /// </summary>
        private static string NormalizeCode(string code)
        {
            var lower = (code ?? "en-US").ToLowerInvariant();
            // 如果已经是 zh-cn / en-us 完整格式，直接返回
            if (lower is "zh-cn" or "en-us")
                return lower;
            // 容错短代码
            if (lower.StartsWith("zh"))
                return "zh-cn";
            if (lower.StartsWith("en"))
                return "en-us";
            return lower;
        }

        private async Task LoadLanguage(string languageCode)
        {
            try
            {
                var normalized = NormalizeCode(languageCode);
                string filename = $"Languages/{normalized}.json";
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
            if (args == null || args.Length == 0)
                return translation;

            // 无任何占位符特征，直接返回避免 StringBuilder 开销
            if (translation.IndexOfAny(_placeholderChars) < 0)
                return translation;

            // {0} {1} 标准格式走最快路径
            if (translation.Contains("{0}"))
            {
                try { return string.Format(translation, args); }
                catch { return translation; }
            }

            // 单次遍历替换 @var 和 {var}
            return ReplaceNamedPlaceholders(translation, args);
        }

        private static readonly char[] _placeholderChars = { '{', '@', '}' };

        private static string ReplaceNamedPlaceholders(string template, object[] args)
        {
            var sb = new System.Text.StringBuilder(template.Length + args.Length * 8);
            int argIndex = 0;
            int i = 0;

            while (i < template.Length)
            {
                char c = template[i];

                if (c == '@' && i + 1 < template.Length && IsWordStart(template[i + 1]))
                {
                    int end = i + 1;
                    while (end < template.Length && IsWordPart(template[end]))
                        end++;
                    sb.Append(GetArg(args, ref argIndex));
                    i = end;
                }
                else if (c == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        var inner = template.Substring(i + 1, close - i - 1);
                        if (int.TryParse(inner, out _))
                        {
                            sb.Append(c);
                            i++;
                        }
                        else
                        {
                            sb.Append(GetArg(args, ref argIndex));
                            i = close + 1;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            return sb.ToString();
        }

        private static string GetArg(object[] args, ref int index)
        {
            if (index < args.Length)
                return args[index++]?.ToString() ?? string.Empty;
            return string.Empty;
        }

        private static bool IsWordStart(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
        private static bool IsWordPart(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.';

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
