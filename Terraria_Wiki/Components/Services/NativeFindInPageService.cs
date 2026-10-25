using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Webkit;
#endif
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif

namespace Terraria_Wiki.Services;

public sealed record FindInPageResult(int Count, int Index)
{
    public static readonly FindInPageResult Empty = new(0, -1);
}

public interface INativeFindInPageService
{
    event Action<FindInPageResult>? ResultChanged;

    void Attach(object platformWebView);
    Task<FindInPageResult> SearchAsync(string query);
    Task<FindInPageResult> NextAsync();
    Task<FindInPageResult> PreviousAsync();
    Task ClearAsync();
}

public sealed class NativeFindInPageService : INativeFindInPageService
{
    private readonly object _sync = new();
    private string _query = string.Empty;
    private FindInPageResult _result = FindInPageResult.Empty;

#if ANDROID
    private Android.Webkit.WebView? _androidWebView;
    private AndroidFindListener? _androidListener;
    private TaskCompletionSource<FindInPageResult>? _androidSearchCompletion;
#endif
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.WebView2? _windowsWebView;
    /// <summary>已订阅事件的 CoreWebView2Find 实例（事件只需订阅一次）。</summary>
    private CoreWebView2Find? _windowsFindHooked;

    private static FindInPageResult CreateWindowsResult(CoreWebView2Find find)
    {
        var count = find.MatchCount;
        var index = count > 0 && find.ActiveMatchIndex > 0
            ? find.ActiveMatchIndex - 1
            : -1;

        return new FindInPageResult(count, index);
    }

    /// <summary>
    /// 订阅 CoreWebView2Find 的 MatchCountChanged / ActiveMatchIndexChanged。
    ///
    /// 这两个事件是"异步读取"问题的正解：之前我轮询到数值稳定才返回（约 100ms 延迟），
    /// 而事件驱动的做法既没有延迟，计数与索引也会各自在就绪的那一刻实时刷新。
    /// </summary>
    private void EnsureWindowsFindHooked(CoreWebView2Find find)
    {
        if (ReferenceEquals(_windowsFindHooked, find)) return;
        _windowsFindHooked = find;
        find.MatchCountChanged += (_, _) => Publish(CreateWindowsResult(find));
        find.ActiveMatchIndexChanged += (_, _) => Publish(CreateWindowsResult(find));
    }

    /// <summary>
    /// 等到事件驱动已至少上报一次（用于首次搜索：匹配计数要等 Chromium 数完）。
    /// 之后由事件负责刷新，这里只需给出当前快照。
    /// </summary>
    private async Task<FindInPageResult> AwaitWindowsResultAsync(CoreWebView2Find find)
    {
        var immediate = CreateWindowsResult(find);
        if (immediate.Count > 0) return immediate;

        // 计数尚未就绪：给它一小段时间，事件到达后 Publish 会带上结果
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromMilliseconds(400))
        {
            await Task.Delay(8);
            var now = CreateWindowsResult(find);
            if (now.Count > 0) return now;
        }

        return CreateWindowsResult(find);
    }
#endif

    public event Action<FindInPageResult>? ResultChanged;

    public void Attach(object platformWebView)
    {
        lock (_sync)
        {
#if ANDROID
            if (platformWebView is Android.Webkit.WebView androidWebView)
            {
                _androidWebView = androidWebView;
                _androidListener ??= new AndroidFindListener(this);
                _androidWebView.SetFindListener(_androidListener);
            }
#endif
#if WINDOWS
            if (platformWebView is Microsoft.UI.Xaml.Controls.WebView2 windowsWebView)
                _windowsWebView = windowsWebView;
#endif
        }
    }

    public async Task<FindInPageResult> SearchAsync(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            await ClearAsync();
            return FindInPageResult.Empty;
        }

        _query = query;
#if ANDROID
        if (_androidWebView is { } androidWebView)
        {
            _androidSearchCompletion = new TaskCompletionSource<FindInPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            await MainThread.InvokeOnMainThreadAsync(() => androidWebView.FindAllAsync(query));
            return await _androidSearchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
#endif
#if WINDOWS
        if (_windowsWebView?.CoreWebView2?.Find is { } find)
        {
            var options = _windowsWebView.CoreWebView2.Environment.CreateFindOptions();
            options.FindTerm = query;
            options.ShouldHighlightAllMatches = true;
            options.SuppressDefaultFindDialog = true;
            EnsureWindowsFindHooked(find);
            await find.StartAsync(options);
            var result = await AwaitWindowsResultAsync(find);
            return Publish(result);
        }
#endif
#if IOS || MACCATALYST
        // Apple 的 WKWebView.Find 只回 MatchFound（没有计数），也不保证把命中画出来；
        // 页内搜索的正文在 iframe 里，改由 iframe 内的脚本查找 + 自己高亮，返回 count|index。
        return Publish(await CallIframeFindAsync("FindInPage", query));
#else
        return Publish(FindInPageResult.Empty);
#endif
    }

    public async Task<FindInPageResult> NextAsync()
    {
        if (_query.Length == 0)
            return FindInPageResult.Empty;
#if ANDROID
        if (_androidWebView is { } androidWebView)
        {
            // FindNext 同样会异步触发 onFindResultReceived（isDoneCounting=true），
            // 因此必须等回调，不能用旧的 _result 直接返回 —— 否则计数不跟着走。
            _androidSearchCompletion = new TaskCompletionSource<FindInPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            await MainThread.InvokeOnMainThreadAsync(() => androidWebView.FindNext(true));
            return await _androidSearchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
#endif
#if WINDOWS
        if (_windowsWebView?.CoreWebView2?.Find is { } find)
        {
            EnsureWindowsFindHooked(find);
            find.FindNext();
            return Publish(CreateWindowsResult(find));
        }
#endif
#if IOS || MACCATALYST
        return Publish(await CallIframeFindAsync("FindInPageNext", string.Empty));
#else
        return FindInPageResult.Empty;
#endif
    }

    public async Task<FindInPageResult> PreviousAsync()
    {
        if (_query.Length == 0)
            return FindInPageResult.Empty;
#if ANDROID
        if (_androidWebView is { } androidWebView)
        {
            // 同上：等 onFindResultReceived 再返回，保证索引是最新的。
            _androidSearchCompletion = new TaskCompletionSource<FindInPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            await MainThread.InvokeOnMainThreadAsync(() => androidWebView.FindNext(false));
            return await _androidSearchCompletion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
#endif
#if WINDOWS
        if (_windowsWebView?.CoreWebView2?.Find is { } find)
        {
            EnsureWindowsFindHooked(find);
            find.FindPrevious();
            return Publish(CreateWindowsResult(find));
        }
#endif
#if IOS || MACCATALYST
        return Publish(await CallIframeFindAsync("FindInPagePrevious", string.Empty));
#else
        return FindInPageResult.Empty;
#endif
    }

    public async Task ClearAsync()
    {
        _query = string.Empty;
#if ANDROID
        if (_androidWebView is { } androidWebView)
            await MainThread.InvokeOnMainThreadAsync(androidWebView.ClearMatches);
#endif
#if WINDOWS
        _windowsWebView?.CoreWebView2?.Find?.Stop();
#endif
#if IOS || MACCATALYST
        // 清高亮是"关闭面板"的收尾动作，不等 iframe 回复，避免拖慢关闭动画。
        _ = CallIframeFindAsync("FindInPageClear", string.Empty);
#endif
        Publish(FindInPageResult.Empty);
    }

    private FindInPageResult Publish(FindInPageResult result)
    {
        _result = result;
        ResultChanged?.Invoke(result);
        return result;
    }

#if ANDROID
    private void OnAndroidResult(int activeMatchOrdinal, int numberOfMatches, bool isDoneCounting)
    {
        if (!isDoneCounting)
            return;

        // ★ Android 的 activeMatchOrdinal 是 **0-based**（官方文档原文：
        //   "the zero-based ordinal of the currently selected match"）。
        //   之前这里写成 -1，把第 1 个匹配算成了 -1，界面就显示成 "0/N"。
        //   共享契约同样是 0-based，所以直接透传，不做任何加减。
        var idx = numberOfMatches == 0
            ? -1
            : Math.Clamp(activeMatchOrdinal, 0, numberOfMatches - 1);
        var result = Publish(new FindInPageResult(numberOfMatches, idx));
        _androidSearchCompletion?.TrySetResult(result);
        _androidSearchCompletion = null;
    }

    private sealed class AndroidFindListener : Java.Lang.Object, Android.Webkit.WebView.IFindListener
    {
        private readonly NativeFindInPageService _owner;

        public AndroidFindListener(NativeFindInPageService owner) => _owner = owner;

        public void OnFindResultReceived(int activeMatchOrdinal, int numberOfMatches, bool isDoneCounting)
            => _owner.OnAndroidResult(activeMatchOrdinal, numberOfMatches, isDoneCounting);
    }
#endif

#if IOS || MACCATALYST
    /// <summary>
    /// 调用 iframe 内的查找脚本（<c>iframe-bridge.js</c> 的 <c>FindInPage*</c> 处理器）。
    /// WKWebView 自带的 Find 只回 MatchFound、也没有计数，命中高亮也不可靠，所以 Apple 平台
    /// 改为在 wiki 正文所在的 iframe 里自己查找 + 高亮，返回 "count|index"。
    /// iframe 未就绪 / 超时 → 空结果。
    /// </summary>
    private static async Task<FindInPageResult> CallIframeFindAsync(string method, string payload)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var raw = await IframeBridge.CallJsAsync(method, payload, timeout.Token);
            return ParseIframeFindReply(raw);
        }
        catch
        {
            return FindInPageResult.Empty;
        }
    }

    /// <summary>解析 iframe 返回的 "count|index"；任何不合法形态都按无结果处理。</summary>
    private static FindInPageResult ParseIframeFindReply(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return FindInPageResult.Empty;

        var parts = raw.Split('|');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var count) ||
            !int.TryParse(parts[1], out var index) ||
            count <= 0 || index < 0 || index >= count)
            return FindInPageResult.Empty;

        return new FindInPageResult(count, index);
    }
#endif
}

