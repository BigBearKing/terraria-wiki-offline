using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Webkit;
#endif
#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif
#if IOS || MACCATALYST
using WebKit;
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
#if IOS || MACCATALYST
    private WKWebView? _appleWebView;
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
#if IOS || MACCATALYST
            if (platformWebView is WKWebView appleWebView)
                _appleWebView = appleWebView;
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
        if (_appleWebView is { } appleWebView)
        {
            var result = await FindAppleAsync(appleWebView, query, backwards: false);
            return Publish(result);
        }
#endif
        return Publish(FindInPageResult.Empty);
    }

    public async Task<FindInPageResult> NextAsync()
    {
        if (_query.Length == 0)
            return FindInPageResult.Empty;
#if ANDROID
        if (_androidWebView is { } androidWebView)
        {
            await MainThread.InvokeOnMainThreadAsync(() => androidWebView.FindNext(true));
            return _result;
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
        if (_appleWebView is { } appleWebView)
            return Publish(await FindAppleAsync(appleWebView, _query, backwards: false));
#endif
        return FindInPageResult.Empty;
    }

    public async Task<FindInPageResult> PreviousAsync()
    {
        if (_query.Length == 0)
            return FindInPageResult.Empty;
#if ANDROID
        if (_androidWebView is { } androidWebView)
        {
            await MainThread.InvokeOnMainThreadAsync(() => androidWebView.FindNext(false));
            return _result;
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
        if (_appleWebView is { } appleWebView)
            return Publish(await FindAppleAsync(appleWebView, _query, backwards: true));
#endif
        return FindInPageResult.Empty;
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

        // Android reports the active match ordinal as 1-based; the shared result contract is 0-based.
        var result = Publish(new FindInPageResult(numberOfMatches, numberOfMatches == 0 ? -1 : activeMatchOrdinal - 1));
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
    private static Task<FindInPageResult> FindAppleAsync(WKWebView webView, string query, bool backwards)
    {
        var completion = new TaskCompletionSource<FindInPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configuration = new WKFindConfiguration
        {
            Backwards = backwards,
            CaseSensitive = false,
            Wraps = true
        };
        webView.Find(query, configuration, result =>
        {
            completion.TrySetResult(new FindInPageResult(result.MatchFound ? 1 : 0, result.MatchFound ? 0 : -1));
        });
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
#endif
}

