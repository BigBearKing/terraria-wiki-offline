using Microsoft.Maui.ApplicationModel;

namespace Terraria_Wiki.Services;

/// <summary>
/// 系统提示框（<c>DisplayAlert</c> 那种）的唯一出口。
///
/// 为什么要有这个类：提示框以前是 MainLayout 在自己 <c>OnInitialized</c> 里订阅
/// <see cref="AppState.OnShowAlert"/> 来显示的。iOS 从后台恢复时 WKWebView 有时会重载页面，
/// Blazor 会再挂载一次 MainLayout；只要旧实例没被 Dispose，同一个提示框就会被两个处理器
/// 各显示一次 —— 用户看到的就是"弹窗出现两次"（第二个往往在第一个关掉后立刻再弹一次）。
/// 订阅挪到 DI 单例后，界面重建多少次都只有一份处理器。
///
/// 另外两道保险，都是针对"同一提示出现两次"这个症状：
///   · 同一条内容 2 秒内重复触发只显示一次（两处代码都触发、或两个界面实例都触发）；
///   · 串行显示（<see cref="SemaphoreSlim"/>），不并发 present —— iOS 上并发 present
///     两个 UIAlertController 会排队，表现同样是"弹了两次"。
/// </summary>
public sealed class AlertPresenter : IDisposable
{
    private readonly AppState _appState;
    private readonly LocalizationService _loc;

    /// <summary>保证同一时间只有一个提示框在显示；等用户点掉才放行下一条。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>上一条提示的内容与时间，用于丢弃"重复触发"的同一条提示。</summary>
    private readonly object _dedupeLock = new();
    private string? _lastKey;
    private DateTime _lastAt;

    public AlertPresenter(AppState appState, LocalizationService loc)
    {
        _appState = appState;
        _loc = loc;

        // 单例构造即订阅：全进程只有这一份
        _appState.OnShowAlert += OnShowAlert;
    }

    private void OnShowAlert(string title, string message)
    {
        string key = title + "\n" + message;

        lock (_dedupeLock)
        {
            var now = DateTime.UtcNow;
            if (key == _lastKey && now - _lastAt < TimeSpan.FromSeconds(2))
                return;                     // 同一条提示，2 秒内重复触发：丢掉

            _lastKey = key;
            _lastAt = now;
        }

        _ = ShowAsync(title, message);
    }

    private async Task ShowAsync(string title, string message)
    {
        await _gate.WaitAsync();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null) return;

                await page.DisplayAlertAsync(title, message, _loc.Get("Common.OK"));
            });
        }
        catch (Exception ex)
        {
            App.LogManager?.Error($"Show alert failed: {ex}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _appState.OnShowAlert -= OnShowAlert;
        _gate.Dispose();
    }
}
