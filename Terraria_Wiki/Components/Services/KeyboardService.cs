using System;

#if ANDROID
using Android.Graphics;
using Android.Views;
using Microsoft.JSInterop;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Terraria_Wiki.Services;
#endif

namespace Terraria_Wiki.Services;

public class KeyboardService
#if ANDROID
    : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
#endif
{
    public static KeyboardService Default { get; } = new KeyboardService();
    public event Action<double> OnKeyboardHeightChanged;
    private KeyboardService() { }

#if ANDROID
    private Android.Views.View _rootView;
    private double _lastReportedHeight = -1;
#endif

    public void Start()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity?.Window == null) return;

        // 直接监听最顶层的主窗口
        _rootView = activity.Window.DecorView;
        _rootView.ViewTreeObserver?.AddOnGlobalLayoutListener(this);
#endif
    }

    public void Stop()
    {
#if ANDROID
        if (_rootView?.ViewTreeObserver != null && _rootView.ViewTreeObserver.IsAlive)
        {
            _rootView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);
        }
        _rootView = null;
#endif
    }

#if ANDROID
    public void OnGlobalLayout()
    {
        if (_rootView == null) return;

        Android.Graphics.Rect r = new Android.Graphics.Rect();
        _rootView.GetWindowVisibleDisplayFrame(r);

        // 主窗口总高度 - 可视区域底部 = 纯净的键盘高度
        int screenHeight = _rootView.RootView.Height;
        int keypadHeight = screenHeight - r.Bottom;

        double density = DeviceDisplay.Current.MainDisplayInfo.Density;
        double keyboardHeightDp = keypadHeight / density;

        // 设置 100dp 阈值：过滤掉底部导航栏/手势条的影响
        double targetHeightDp = keyboardHeightDp > 100 ? keyboardHeightDp : 0;

        // 🚀 核心性能保护伞：防抖
        // 过滤掉小于 1dp 的微小抖动，彻底解决打开键盘时的卡顿（通讯风暴）
        if (Math.Abs(targetHeightDp - _lastReportedHeight) < 1.0)
        {
            return;
        }

        _lastReportedHeight = targetHeightDp;

        AppState.JS?.InvokeVoidAsync("setGlobalKeyboardHeight", targetHeightDp);
    }
#endif
}