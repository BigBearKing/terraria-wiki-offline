using System;
#if ANDROID
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using Microsoft.JSInterop;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
#endif

namespace Terraria_Wiki.Services;

public class KeyboardService
#if ANDROID
    : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
#endif
{
    public static KeyboardService Default { get; } = new KeyboardService();
    private KeyboardService() { }

#if ANDROID
    private Android.Views.View _parentView;

    // Android 11+ 专用
    private KeyboardInsetsListener _insetsListener;
    private KeyboardAnimationCallback _animationCallback;

    // 幽灵窗口专用 (Android 10 及以下)
    private Android.Views.View _popupView;
    private PopupWindow _popupWindow;

    // 防抖状态
    private double _lastHeightDp = -1;
#endif

    public void Start()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity?.Window == null) return;

        _parentView = activity.Window.DecorView.FindViewById(Android.Resource.Id.Content) ?? activity.Window.DecorView;

        // 【运行时判断】：不再使用错误的 #if ANDROID30_0_OR_GREATER 宏
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            StartModernEngine();
        }
        else
        {
            StartGhostWindowEngine(activity);
        }
#endif
    }

    public void Stop()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (_parentView != null)
            {
                ViewCompat.SetOnApplyWindowInsetsListener(_parentView, null);
                ViewCompat.SetWindowInsetsAnimationCallback(_parentView, null);
            }
        }
        else
        {
            // 清理幽灵窗口
            if (_popupView?.ViewTreeObserver != null && _popupView.ViewTreeObserver.IsAlive)
            {
                _popupView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);
            }
            if (_popupWindow != null)
            {
                _popupWindow.Dismiss();
                _popupWindow = null;
            }
            _popupView = null;
        }

        _parentView = null;
        _insetsListener = null;
        _animationCallback = null;
        _lastHeightDp = -1; // 重置防抖状态
#endif
    }

#if ANDROID
    internal void InvokeJS(double heightDp)
    {
        // 防抖：防止重复发送相同的高度导致 JS 卡顿
        if (Math.Abs(_lastHeightDp - heightDp) < 0.5) return;
        _lastHeightDp = heightDp;

        AppState.JS?.InvokeVoidAsync("setGlobalKeyboardHeight", heightDp);
    }

    // ==========================================
    // 引擎 1: Android 11+ (现代无延迟方案)
    // ==========================================
    private void StartModernEngine()
    {
        _insetsListener = new KeyboardInsetsListener(this);
        ViewCompat.SetOnApplyWindowInsetsListener(_parentView, _insetsListener);

        _animationCallback = new KeyboardAnimationCallback(this);
        ViewCompat.SetWindowInsetsAnimationCallback(_parentView, _animationCallback);
    }

    private class KeyboardAnimationCallback : WindowInsetsAnimationCompat.Callback
    {
        private readonly KeyboardService _service;
        public KeyboardAnimationCallback(KeyboardService service) : base(DispatchModeStop) { _service = service; }

        public override WindowInsetsAnimationCompat.BoundsCompat OnStart(
            WindowInsetsAnimationCompat animation, WindowInsetsAnimationCompat.BoundsCompat bounds)
        {
            return base.OnStart(animation, bounds);
        }

        public override WindowInsetsCompat OnProgress(
            WindowInsetsCompat insets, System.Collections.Generic.IList<WindowInsetsAnimationCompat> runningAnimations)
        {
            return insets;
        }
    }

    private class KeyboardInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        private readonly KeyboardService _service;
        public KeyboardInsetsListener(KeyboardService service) { _service = service; }

        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View v, WindowInsetsCompat insets)
        {
            if (!insets.IsVisible(WindowInsetsCompat.Type.Ime()))
            {
                _service.InvokeJS(0);
            }
            else
            {
                int targetPixels = insets.GetInsets(WindowInsetsCompat.Type.Ime()).Bottom;
                double density = DeviceDisplay.Current.MainDisplayInfo.Density;
                _service.InvokeJS(targetPixels / density);
            }
            return ViewCompat.OnApplyWindowInsets(v, insets);
        }
    }

    // ==========================================
    // 引擎 2: Android 10 及以下 (原味保留：幽灵窗口方案)
    // ==========================================
    private void StartGhostWindowEngine(Android.App.Activity activity)
    {
        _popupView = new Android.Views.View(activity);

        // 依然按照你的要求，创建宽度为 0 的透明窗口
        _popupWindow = new PopupWindow(_popupView, 0, ViewGroup.LayoutParams.MatchParent);
        _popupWindow.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.Transparent));

        _popupWindow.SoftInputMode = SoftInput.AdjustResize;
        _popupWindow.InputMethodMode = Android.Widget.InputMethod.Needed;

        _popupView.ViewTreeObserver?.AddOnGlobalLayoutListener(this);

        _parentView.Post(() =>
        {
            if (!activity.IsFinishing && !activity.IsDestroyed)
            {
                _popupWindow.ShowAtLocation(_parentView, GravityFlags.NoGravity, 0, 0);
            }
        });
    }

    public void OnGlobalLayout()
    {
        if (_popupView == null || _parentView == null) return;

        Android.Graphics.Rect r = new Android.Graphics.Rect();
        _popupView.GetWindowVisibleDisplayFrame(r);

        // 按照你原本的计算逻辑测量挤压高度
        int overlapPixels = _parentView.Height - r.Bottom;

        double density = DeviceDisplay.Current.MainDisplayInfo.Density;
        double targetHeightDp = overlapPixels / density;

        // 这里我稍微加了一个极小值过滤(>10)，防止全面屏手势条的轻微抖动误判
        // 并且通过 InvokeJS 统一发送，享受防抖红利
        if (targetHeightDp > 10)
        {
            InvokeJS(targetHeightDp);
        }
        else
        {
            InvokeJS(0);
        }
    }
#endif
}