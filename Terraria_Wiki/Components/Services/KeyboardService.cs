using System;
using Microsoft.JSInterop;
using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using Microsoft.Maui.Devices;
#endif

#if IOS
using Foundation;
using UIKit;
#endif

namespace Terraria_Wiki.Services;

public class KeyboardService
#if ANDROID
    : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
#endif
{
    public static KeyboardService Default { get; } = new KeyboardService();
    private KeyboardService() { }

    // ==========================================
    // 双端共用：防抖与 JS 调用
    // ==========================================
    private double _lastHeightDp = -1;

    internal void InvokeJS(double heightDp)
    {
        // 防抖：防止重复发送相同的高度导致 JS 卡顿
        if (Math.Abs(_lastHeightDp - heightDp) < 0.5) return;
        _lastHeightDp = heightDp;

        AppState.JS?.InvokeVoidAsync("setGlobalKeyboardHeight", heightDp);
    }

#if ANDROID
    private Android.Views.View _parentView;
    private KeyboardInsetsListener _insetsListener;
    private KeyboardAnimationCallback _animationCallback;
    private Android.Views.View _popupView;
    private PopupWindow _popupWindow;
#endif

#if IOS
    private NSObject _keyboardShowObserver;
    private NSObject _keyboardHideObserver;
#endif

    public void Start()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        if (activity?.Window == null) return;

        _parentView = activity.Window.DecorView.FindViewById(Android.Resource.Id.Content) ?? activity.Window.DecorView;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            StartModernEngine();
        }
        else
        {
            StartGhostWindowEngine(activity);
        }
#endif

#if IOS
        // iOS 监听键盘弹出和改变大小的通知
        _keyboardShowObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillShowNotification, OnKeyboardChanged);
        // iOS 监听键盘收起的通知
        _keyboardHideObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillHideNotification, OnKeyboardHidden);
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
#endif

#if IOS
        // 注销 iOS 通知监听，防止内存泄漏
        if (_keyboardShowObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_keyboardShowObserver);
            _keyboardShowObserver = null;
        }
        if (_keyboardHideObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_keyboardHideObserver);
            _keyboardHideObserver = null;
        }
#endif

        _lastHeightDp = -1; // 重置防抖状态
    }

#if IOS
    // ==========================================
    // 引擎 3: iOS 原生通知方案
    // ==========================================
    private void OnKeyboardChanged(NSNotification notification)
    {
        // 从系统通知字典中安全提取键盘结束时的 Frame (包含高宽和位置信息)
        if (notification.UserInfo != null && 
            notification.UserInfo.TryGetValue(UIKeyboard.FrameEndUserInfoKey, out var frameValue))
        {
            var frame = ((NSValue)frameValue).CGRectValue;
            
            // 注意：iOS 的 CGRect 值已经是逻辑点(pt)了，等同于 CSS 的 px (或 Android 的 dp)
            // 所以直接传 Height 即可，千万不要再除以屏幕密度！
            InvokeJS(frame.Height);
        }
    }

    private void OnKeyboardHidden(NSNotification notification)
    {
        // 键盘收起时，高度归零
        InvokeJS(0);
    }
#endif

#if ANDROID
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

        int overlapPixels = _parentView.Height - r.Bottom;
        double density = DeviceDisplay.Current.MainDisplayInfo.Density;
        double targetHeightDp = overlapPixels / density;

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