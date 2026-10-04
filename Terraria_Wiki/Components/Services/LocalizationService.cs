using System.Globalization;
using System.Text.Json;

namespace Terraria_Wiki.Services
{
    public class LocalizationService
    {
        /// <summary>「跟随系统」选项的取值。</summary>
        public const string AutoLanguageCode = "auto";
        private const string ChineseLanguageCode = "zh-cn";
        private const string EnglishLanguageCode = "en-us";
        private const string LanguagePreferenceKey = "AppLanguage";

        private Dictionary<string, string> _translations = new();
        private string _currentLanguage = ChineseLanguageCode;
        private string _languagePreference = AutoLanguageCode;

        public event Action? OnChange;

        public LocalizationService()
        {
        }

        /// <summary>
        /// 初始化并加载语言文件（从 Preferences 读取上次语言选择，首次安装默认跟随系统）
        /// </summary>
        public async Task InitializeAsync()
        {
            var savedLang = Preferences.Default.Get(LanguagePreferenceKey, "");
            _languagePreference = string.IsNullOrEmpty(savedLang)
                ? AutoLanguageCode
                : NormalizePreference(savedLang);
            _currentLanguage = Resolve(_languagePreference);
            await LoadLanguage(_currentLanguage);
            App.AppStateManager!.CurrentLanguage = _currentLanguage;
            NotifyStateChanged();
        }

        /// <summary>
        /// 切换语言并通知 UI 刷新
        /// </summary>
        public async Task SetLanguageAsync(string languageCode)
        {
            var preference = NormalizePreference(languageCode);
            var resolved = Resolve(preference);
            if (_languagePreference == preference && _currentLanguage == resolved) return;
            _languagePreference = preference;
            _currentLanguage = resolved;
            Preferences.Default.Set(LanguagePreferenceKey, preference);
            await LoadLanguage(resolved);
            App.AppStateManager!.CurrentLanguage = _currentLanguage;
            NotifyStateChanged();
        }

        /// <summary>
        /// 当前实际生效的语言代码（zh-cn / en-us）
        /// </summary>
        public string CurrentLanguage => _currentLanguage;

        /// <summary>
        /// 用户选择的语言首选项（auto / zh-cn / en-us）
        /// </summary>
        public string LanguagePreference => _languagePreference;

        /// <summary>
        /// 获取支持的语言代码列表
        /// </summary>
        public static readonly string[] SupportedLanguageCodes =
        {
            AutoLanguageCode,
            ChineseLanguageCode,
            EnglishLanguageCode,
        };

        /// <summary>
        /// 获取语言在界面上的显示名（「跟随系统」一项要按当前语言本地化）
        /// </summary>
        public string GetLanguageName(string code) => code switch
        {
            AutoLanguageCode => Get("Settings.FollowSystem"),
            ChineseLanguageCode => "中文",
            EnglishLanguageCode => "English",
            _ => code
        };

        /// <summary>
        /// 把语言首选项解析成实际生效的语言代码
        /// </summary>
        public static string Resolve(string preference) =>
            IsAuto(preference) ? DetectSystemLanguage() : NormalizeCode(preference);

        private static bool IsAuto(string? code) =>
            string.Equals(code, AutoLanguageCode, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 探测系统语言：简体/繁体等任何中文 → 中文；其余一律英文
        /// </summary>
        public static string DetectSystemLanguage()
        {
            foreach (var culture in new[] { CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture })
            {
                var name = culture?.Name;
                if (string.IsNullOrEmpty(name))
                    continue; // 固定区域性（invariant）时继续看下一个
                return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    ? ChineseLanguageCode
                    : EnglishLanguageCode;
            }
            return EnglishLanguageCode;
        }

        private static string NormalizePreference(string? code)
        {
            if (IsAuto(code))
                return AutoLanguageCode;
            var normalized = NormalizeCode(code);
            return normalized is "zh-cn" or "en-us" ? normalized : EnglishLanguageCode;
        }

        /// <summary>
        /// 将语言代码规范化为文件名格式（zh-CN → zh-cn, 容错 zh → zh-cn）
        /// </summary>
        private static string NormalizeCode(string? code)
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

        /// <summary>
        /// 获取供 iframe/WebView 使用的翻译字典。
        /// </summary>
        public Dictionary<string, string> GetWebTranslations()
        {
            return _translations
                .Where(pair => pair.Key.StartsWith("Web.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}
