#if ANDROID
using Android.Views;
using Android.Window;
using AndroidX.Core.View;
using Microsoft.Maui.Devices;
#endif

using Terraria_Wiki.Services;
using Terraria_Wiki.Components.Shared;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.JSInterop;

namespace Terraria_Wiki
{
    public partial class MainPage : ContentPage
    {
#if WINDOWS
        private bool _windowsIntegrationInitialized;
        private bool _dragBridgeRegistered;
        private bool _titleBarConfigured;
#endif
#if IOS
        private readonly BurnInProtectionService _burnInService;
        private float _originalBrightness = 0.5f;
#endif
        private readonly INativeFindInPageService _findInPageService;

        // 原生遮罩加载框的"返回键拦截"登记 Id：显示期间吞掉安卓返回键。
        // 两个加载框各用一个 Id，互不覆盖（见 BackLayerCoordinator / BackEventsService）。
        private readonly Guid _loadingOverlayBackBlocker = Guid.NewGuid();
        private readonly Guid _loadingPopupBackBlocker = Guid.NewGuid();

        // ===== 页内搜索：原生自绘弹窗 =====
        private readonly FindPanelDrawable _findDrawable = new();
        private IDispatcherTimer? _findCaretTimer;
        /// <summary>选区/光标采样周期：决定拖选高亮的跟手程度（实测单次绘制 &lt;1ms，可放心取小）。</summary>
        private const int FindCaretSampleMs = 40;
        private int _findCaretPhaseMs;
        private bool _syncingFindEntryText;
        private bool _findPanelReady;

#if IOS
        public MainPage(BurnInProtectionService burnInService, INativeFindInPageService findInPageService)
#else
        public MainPage(INativeFindInPageService findInPageService) // Android/Windows 版本
#endif
        {
            InitializeComponent();
            // LoadingOverlay 在 XAML 里默认就是可见的（启动加载层），
            // 所以从构造起就拦住返回键，直到 HideLoadingScreen 才解除。
            BackLayerCoordinator.SetBackBlocked(_loadingOverlayBackBlocker, true);
            _findInPageService = findInPageService;
            bool isDark = App.AppStateManager.IsDarkTheme;
            //根据判断，瞬间给原生加载层上色
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
#if IOS
            _burnInService = burnInService;

            // 订阅状态改变事件
            _burnInService.OnStateChanged += (isActive) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (isActive) EnableProtectionUI(); else DisableProtectionUI();
                });
            };
#endif
            this.Loaded += MainPage_Loaded;
#if IOS || MACCATALYST
            // 私有协议必须在 WKWebView 创建之前注册进配置，而 BlazorWebViewInitializing
            // 是唯一能拿到 WKWebViewConfiguration 的时机（创建之后再注册无效）。
            blazorWebView.BlazorWebViewInitializing += OnBlazorWebViewInitializing;
#endif
        }

#if IOS || MACCATALYST
        /// <summary>注册 iframe 内容源的私有协议（terrariawiki://）。</summary>
        /// <remarks>
        /// ★ 事件参数类型在 <c>Microsoft.AspNetCore.Components.WebView</c>（**不带** .Maui），
        ///   不是 Maui 子命名空间。写错成 .Maui 时 Windows 构建不会报错（这段在 #if 里），
        ///   只有真正构建 iOS 目标时才会暴露为 CS0234。
        /// </remarks>
        private void OnBlazorWebViewInitializing(object? sender, BlazorWebViewInitializingEventArgs e)
        {
            App.VirtualHost?.TryRegisterAppleScheme(e.Configuration);
        }
#endif



        private void MainPage_Loaded(object sender, EventArgs e)
        {
            UpdateSafeAreaToWeb();
#if MACCATALYST
            // 启动时 Insets 往往还是 0（窗口尚未完全挂好），等窗口稳定后再取一次标题栏高度。
            _ = RefreshSafeAreaAfterWindowAnimationAsync();
#endif
#if WINDOWS
            InitializeWindowsIntegration();
#endif
            _findInPageService.ResultChanged += OnFindResultChanged;
            App.AppStateManager!.PropertyChanged += OnFindAppStateChanged;
#if WINDOWS
            // 窄/宽切换会改变 TopStripVisible：顶部 32px 要么由顶部条承担，要么由 safe-area-top 承担，
            // 所以 IsSmallScreen 一变就重算一次（JS 的 matchMedia 回调是唯一来源）。
            App.AppStateManager.PropertyChanged += OnWindowsLayoutChanged;
#endif
            // 窗口尺寸变化时当帧重算布局：面板宽度随之收缩，而不是从左边溢出
            blazorWebView.SizeChanged += (_, _) =>
            {
#if ANDROID || IOS
                // 安全区只跟随布局尺寸重算：旋转、分屏/自由窗口、Stage Manager 改尺寸都会
                // 走到这里，且此刻新的 Insets 已经生效（Insets 更新早于布局）。
                // 不用 DeviceDisplay.MainDisplayInfoChanged：iOS 27 起它不再随旋转触发
                // （MAUI 仍在监听已废弃的 DidChangeStatusBarOrientation，dotnet/maui#39182）；
                // Android 侧它的底层是方向传感器，分屏只改窗口尺寸时同样不发。
                _ = RefreshSafeAreaAsync();
#endif
#if MACCATALYST
                // 进出系统全屏也是窗口尺寸变化。标题栏消失 / 重新出现时 Insets 会晚一拍，
                // 这里先按当前值算一次，再在系统动画结束后补算一次，避免把全屏停在窗口态。
                _ = RefreshSafeAreaAsync();
                _ = RefreshSafeAreaAfterWindowAnimationAsync();
#endif
                if (App.AppStateManager?.FindInPageOpen == true)
                    ApplyFindPanelLayout();
            };
            SetupFindPanel();
        }

        // =====================================================================
        // 页内搜索：原生自绘弹窗
        // =====================================================================

        /// <summary>初始化自绘画布与透明输入框。只在页面加载时做一次。</summary>
        private void SetupFindPanel()
        {
            if (_findPanelReady) return;

#if IOS || MACCATALYST
            // ★ 不能保持 XAML 里的 Opacity=0：UIKit 的 hitTest 会跳过 alpha ≤ 0.01 的视图，
            // 输入框就永远收不到点击/拖动 —— 既点不回来聚焦，也没法用鼠标或手指拖选文字。
            // 0.02 肉眼不可见（文字、光标、选区都由画布绘制），但能正常参与命中测试。
            FindEntry.Opacity = 0.02;
#endif

            FindPanelView.Drawable = _findDrawable;
            FindPanelView.HeightRequest = FindPanelDrawable.PanelHeight;
            FindPanelHost.HeightRequest = FindPanelDrawable.PanelHeight;
            ApplyFindPanelLayout();
            _findPanelReady = true;
        }

        /// <summary>
        /// 解算并应用面板布局。
        ///
        /// ★ 响应式收缩：面板宽度不再固定，而是按 WebView 的实时宽度解算
        ///   （可用宽度 = WebView 宽 − 右侧留白），窄窗时收缩而不是从左边溢出。
        /// 内部降级规则与网页 flex 等价：先压缩搜索胶囊，再收起命中计数，极窄时收起图标。
        /// 同时把透明输入框对准解算出的搜索胶囊，保证文字区域随面板一起收缩。
        /// </summary>
        private void ApplyFindPanelLayout()
        {
            var state = App.AppStateManager;
            if (state is null) return;

            // 计数区只在真有计数文本时才预留
            var hasCountText = _findDrawable.ShowCount && !string.IsNullOrEmpty(_findDrawable.CountText);

            var rightInset = FindPanelDrawable.PanelMarginRight + state.SafeAreaRight;
            var leftInset = FindPanelDrawable.PanelMarginRight + state.SafeAreaLeft;
            var viewportWidth = blazorWebView.Width;
            // 理想宽度随"当前是否真的显示计数"变化：
            // 无计数时不该为计数区预留空间，否则白白少掉 48px
            var ideal = FindPanelDrawable.GetPanelWidthIdeal(hasCountText);
            // ★ 可用宽度要同时扣掉左右两侧的外边距：
            //   只扣右侧的话面板会一路顶到右边，左边距被挤成 0（就是"左右不一样"的根因）
            var available = viewportWidth > 0
                ? viewportWidth - rightInset - leftInset
                : ideal;

            var panelWidth = FindPanelDrawable.ResolvePanelWidth(available, ideal);
            var geo = _findDrawable.LayoutFor(panelWidth);

            FindPanelHost.WidthRequest = geo.PanelWidth;
            FindPanelView.WidthRequest = geo.PanelWidth;

            // 透明输入框：左边界与文字缩进对齐，宽度吃掉胶囊剩余空间
            var entryWidth = geo.SearchWidth - geo.TextInset;
            if (entryWidth < 40f) entryWidth = 40f;

            FindEntry.WidthRequest = entryWidth;
            FindEntry.HeightRequest = geo.SearchHeight;
            FindEntry.Margin = new Thickness(geo.SearchX + geo.TextInset, geo.SearchY, 0, 0);

            PositionFindPanel();
            FindPanelView.Invalidate();
        }

        /// <summary>
        /// 面板定位（外部边距），与网页 .find-in-page 的定位一一对应：
        ///   position: fixed;
        ///   top:   calc(var(--header-height) + 4px);
        ///   right: calc(10px + var(--safe-area-right));
        ///
        /// 垂直位置：safe-area-top + 6（topbar-top-padding）+ 42（topbar-content-height）
        /// + 顶部条高度（TopStripVisible 时 AppState.TopStripHeight = 32），再 +4。
        /// ★ CSS 侧的安全区是由顶部条还是顶栏吃（--strip-inset / --topbar-inset）会随平台和
        ///   是否主页变化，但两者相加恒等于 --header-height；这里照旧"安全区 + 6 + 42 + 条高"
        ///   算出来的就是同一个总值，所以不需要再区分谁吃。改那边时务必回来核对这个和。
        /// ★ 前提：面板容器必须是 SafeAreaEdges="None"（见 MainPage.xaml）——否则容器会再内缩
        ///   一次安全区，面板会比这里算出来的位置低一整段。
        /// 这里刻意不再走 JS 实测：getBoundingClientRect() 会强制 wiki 页面同步布局，
        /// 在 iOS 上正好卡住打开动画。
        ///
        /// 全部走布局（HorizontalOptions=End + Margin），不用 Translation，避免合成层滞后。
        /// </summary>
        private void PositionFindPanel()
        {
            var state = App.AppStateManager;
            if (state is null) return;

            var stripHeight = state.TopStripVisible ? AppState.TopStripHeight : 0d;
            var headerHeight = FindPanelDrawable.HeaderHeight(state.SafeAreaTop) + stripHeight;
            var top = headerHeight + FindPanelDrawable.PanelMarginTop;

            var right = FindPanelDrawable.PanelMarginRight + state.SafeAreaRight;

            FindPanelHost.Margin = new Thickness(0, top, right, 0);
        }

        /// <summary>
        /// 原生 Entry 现在负责显示文字，所以文字色与占位色必须跟随主题。
        /// 取值与 variables.css 的 --text-primary / --text-secondary 一致。
        /// </summary>
        private void ApplyFindEntryThemeColors()
        {
            var isDark = App.AppStateManager?.IsDarkTheme ?? false;
            FindEntry.TextColor = isDark ? Color.FromArgb("#F9FAFB") : Color.FromArgb("#111827");
            FindEntry.PlaceholderColor = isDark ? Color.FromArgb("#9CA3AF") : Color.FromArgb("#6B7280");
        }

        private void OnFindAppStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppState.FindInPageOpen):
                    SyncFindPanel();
                    break;

                case nameof(AppState.FindInPageQuery):
                    PullQueryIntoFindEntry();
                    break;

                case nameof(AppState.IsDarkTheme):
                    _findDrawable.IsDarkTheme = App.AppStateManager?.IsDarkTheme ?? false;
                    ApplyFindEntryThemeColors();
                    FindPanelView.Invalidate();
                    break;

                case nameof(AppState.SafeAreaTop):
                case nameof(AppState.SafeAreaRight):
                    ApplyFindPanelLayout();
                    break;
            }
        }

#if WINDOWS
        /// <summary>
        /// 窄/宽切换会改变 TopStripVisible，顶部 32px 的承担者随之在顶部条与 safe-area-top 之间切换。
        /// 只在 IsSmallScreen 变化时重算，其余属性与本逻辑无关。
        /// </summary>
        private void OnWindowsLayoutChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.IsSmallScreen))
                UpdateSafeAreaToWeb();
        }
#endif

        private void SyncFindPanel()
        {
            var state = App.AppStateManager;
            if (state is null) return;

            if (!state.FindInPageOpen)
            {
                FindEntry.Unfocus();
                StopFindCaretBlink();
                _findDrawable.IsFocused = false;
            StopFindCaretBlink();
                _findDrawable.HoveredButton = -1;
                _findDrawable.PressedButton = -1;
                // 收起动画结束后才隐藏宿主（见 OnFindAnimTick）
                if (FindPanelContainer.IsVisible) StartFindPanelAnimation(opening: false);
                _ = _findInPageService.ClearAsync();
                return;
            }

            _findDrawable.IsDarkTheme = state.IsDarkTheme;
            _findDrawable.CountText = string.Empty;
            ApplyFindEntryThemeColors();

            FindPanelContainer.IsVisible = true;
            FindEntry.IsVisible = true;
            ApplyFindPanelLayout();

            // 打开即清空并重置查找状态（与"重新打开搜索"语义一致）
            if (state.FindInPageQuery.Length > 0)
                state.FindInPageQuery = string.Empty;
            SyncFindInputToDrawable();
            StartFindPanelAnimation(opening: true);

            Dispatcher.Dispatch(() =>
            {
                FindEntry.Focus();
                ApplyFindEntryPadding();
            });
        }

        /// <summary>
        /// 原生 TextBox 的文字缩进。网页里 .search-input 是 padding-left:36px，
        /// 而画布上的搜索框从面板左边 12px 处开始，所以文字要再缩进 36px。
        /// </summary>
        private void ApplyFindEntryPadding()
        {
#if WINDOWS
            if (FindEntry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox tb)
            {
                // Entry 只作为输入接收器：文字、光标、选区全部由画布绘制。
                // 去掉它的内边距与横向滚动，避免它的内部偏移与画布绘制不一致。
                tb.Padding = new Microsoft.UI.Xaml.Thickness(0);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(
                    tb, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(
                    tb, Microsoft.UI.Xaml.Controls.ScrollMode.Disabled);
            }
#elif ANDROID
            if (FindEntry.Handler?.PlatformView is Android.Widget.EditText et)
                et.SetPadding(0, et.PaddingTop, et.PaddingRight, et.PaddingBottom);
#elif IOS || MACCATALYST
            if (FindEntry.Handler?.PlatformView is UIKit.UITextField tf)
            {
                tf.LeftView = null;
                tf.LeftViewMode = UIKit.UITextFieldViewMode.Never;

                // Entry 只作为输入接收器，任何系统外观都不能露出来：
                // 默认的圆角边框 / 聚焦光圈 / 背景会在 Opacity=0.02 时变成一圈淡蓝色描边。
                tf.BorderStyle = UIKit.UITextBorderStyle.None;
                tf.BackgroundColor = UIKit.UIColor.Clear;
                tf.Layer.BorderWidth = 0;
                tf.Layer.CornerRadius = 0;
                tf.Layer.ShadowOpacity = 0;
            }
#endif
        }

        /// <summary>把 Entry 的文本、光标、选区同步给画布（由画布负责显示）。</summary>

        /// <summary>光标闪烁：只重绘画布。</summary>

        /// <summary>

        /// <summary>
        /// 把共享状态里的查询词写回输入接收器。文字显示由画布负责，
        /// 这里只保证 Entry 的文本与光标状态与状态一致。
        /// </summary>
        private void PullQueryIntoFindEntry()
        {
            var query = App.AppStateManager?.FindInPageQuery ?? string.Empty;
            if (FindEntry.Text != query)
            {
                _syncingFindEntryText = true;
                try
                {
                    FindEntry.Text = query;
                    FindEntry.CursorPosition = query.Length;
                    FindEntry.SelectionLength = 0;
                }
                finally
                {
                    _syncingFindEntryText = false;
                }
            }
            SyncFindInputToDrawable();
            FindPanelView.Invalidate();
        }

        /// <summary>
        /// 把 Entry 的文本、光标、选区同步给画布（由画布负责显示）。
        /// 选区起点必须取原生控件的 SelectionStart：MAUI 的 Entry 没有暴露它，
        /// 而 CursorPosition/SelectionLength 缺少方向信息，反向拖选会错一位。
        /// </summary>
        private void SyncFindInputToDrawable()
        {
            _findDrawable.InputText = FindEntry.Text ?? string.Empty;
            _findDrawable.CursorPosition = FindEntry.CursorPosition;
            _findDrawable.SelectionLength = FindEntry.SelectionLength;

#if WINDOWS
            if (FindEntry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox tb)
                _findDrawable.NativeSelectionStart = tb.SelectionStart;
#elif ANDROID
            if (FindEntry.Handler?.PlatformView is Android.Widget.EditText et)
                _findDrawable.NativeSelectionStart = Math.Max(0, et.SelectionStart);
#elif IOS || MACCATALYST
            if (FindEntry.Handler?.PlatformView is UIKit.UITextField tf)
            {
                var range = tf.SelectedTextRange;
                _findDrawable.NativeSelectionStart = range is null
                    ? tf.Text?.Length ?? 0
                    : (int)tf.GetOffsetFromPosition(tf.BeginningOfDocument, range.Start);
            }
#endif
        }

        /// <summary>
        /// 光标闪烁 + 选区同步。
        /// MAUI 的 Entry 没有"选中变化"事件，而拖选/Shift+方向键不改变文本，
        /// 所以这里轮询：只有光标位置/选区长度/光标相位真的变化时才重绘。
        /// </summary>
        private void OnFindCaretTick(object? sender, EventArgs e)
        {
            if (App.AppStateManager?.FindInPageOpen != true) return;

            var cursor = FindEntry.CursorPosition;
            var selection = FindEntry.SelectionLength;
            var changed = cursor != _findDrawable.CursorPosition
                          || selection != _findDrawable.SelectionLength;

            if (changed)
            {
                // 选区/光标真的动了：立即同步重绘，保证拖选跟手
                SyncFindInputToDrawable();
                _findDrawable.CaretVisible = true;
                _findCaretPhaseMs = 0;
                FindPanelView.Invalidate();
                return;
            }

            // 无变化时只折算光标闪烁，相位与采样周期解耦
            _findCaretPhaseMs += FindCaretSampleMs;
            if (_findCaretPhaseMs >= 530)
            {
                _findCaretPhaseMs = 0;
                _findDrawable.CaretVisible = !_findDrawable.CaretVisible;
                FindPanelView.Invalidate();
            }
        }

        private void StartFindCaretBlink()
        {
            _findCaretTimer ??= Dispatcher.CreateTimer();
            _findCaretTimer.Interval = TimeSpan.FromMilliseconds(FindCaretSampleMs);
            _findCaretTimer.Tick -= OnFindCaretTick;
            _findCaretTimer.Tick += OnFindCaretTick;
            _findDrawable.CaretVisible = true;
            _findCaretTimer.Start();
        }

        private void StopFindCaretBlink()
        {
            _findCaretTimer?.Stop();
            _findDrawable.CaretVisible = false;
        }

        private async void OnFindEntryTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_syncingFindEntryText) return;

            var query = e.NewTextValue ?? string.Empty;
            if (App.AppStateManager is { } state)
                state.FindInPageQuery = query;

            SyncFindInputToDrawable();

            _findDrawable.CaretVisible = true;

            FindPanelView.Invalidate();


            // 原生查找只在 WebView 文档里检索，自绘弹窗不在其中，计数因此准确
            await _findInPageService.SearchAsync(query);
        }

        private void OnFindEntryFocused(object? sender, FocusEventArgs e)
        {
            // 只用于画布上的胶囊聚焦外观；文字/光标/选区都由原生 Entry 自己处理
            _findDrawable.IsFocused = true;
            StartFindCaretBlink();
            FindPanelView.Invalidate();
        }

        private void OnFindEntryUnfocused(object? sender, FocusEventArgs e)
        {
            _findDrawable.IsFocused = false;
            StopFindCaretBlink();
            FindPanelView.Invalidate();
        }

        private void OnFindResultChanged(FindInPageResult result)
            => MainThread.BeginInvokeOnMainThread(() =>
            {
                var hadCount = _findDrawable.ShowCount;
                _findDrawable.CountText = result.Count == 0
                    ? string.Empty
                    : $"{result.Index + 1}/{result.Count}";

                // 计数从无到有（或有到无）会改变面板宽度，需要重新解算布局
                var hasCount = result.Count > 0;
                if (hasCount != hadCount || _findPanelReady)
                    ApplyFindPanelLayout();
                else
                    FindPanelView.Invalidate();
            });

        // ===== 自绘面板交互：hover / 按下 / 点击 =====

        private void OnFindPanelHover(object? sender, TouchEventArgs e)
        {
            if (e.Touches is not { Length: > 0 }) return;

            var hit = _findDrawable.HitTestButton(e.Touches[0]);
            if (hit != _findDrawable.HoveredButton)
            {
                _findDrawable.HoveredButton = hit;
                FindPanelView.Invalidate();
            }
        }

        private void OnFindPanelHoverExit(object? sender, EventArgs e)
        {
            if (_findDrawable.HoveredButton == -1) return;
            _findDrawable.HoveredButton = -1;
            FindPanelView.Invalidate();
        }

        private void OnFindPanelStartInteraction(object? sender, TouchEventArgs e)
        {
            if (e.Touches is not { Length: > 0 }) return;

            var point = e.Touches[0];
            _findPressCandidate = _findDrawable.HitTestButton(point);
            if (_findPressCandidate >= 0)
            {
                _findDrawable.PressedButton = _findPressCandidate;
                FindPanelView.Invalidate();
                return;
            }

            // 画布接住搜索胶囊上的点击：透明 Entry 在 iOS/Mac 上不可被命中（Opacity=0 会被
            // UIKit 的 hitTest 直接跳过），失焦后点它不会重新聚焦，表现就是"只能打一次字"。
            if (_findDrawable.HitTestSearch(point, (float)FindPanelHost.WidthRequest))
                FocusFindEntry();
        }

        /// <summary>把输入焦点交还给透明 Entry（文字/光标仍由画布绘制）。</summary>
        private void FocusFindEntry()
        {
            if (FindEntry.IsFocused) return;
            FindEntry.Focus();
            _findDrawable.IsFocused = true;
            StartFindCaretBlink();
            FindPanelView.Invalidate();
        }

        private void OnFindPanelDragInteraction(object? sender, TouchEventArgs e)
        {
            // 拖出按钮范围就取消按下态（与网页 :active 的行为一致）
            if (_findDrawable.PressedButton < 0 || e.Touches is not { Length: > 0 }) return;

            var hit = _findDrawable.HitTestButton(e.Touches[0]);
            var pressed = hit == _findDrawable.PressedButton ? _findDrawable.PressedButton : -1;
            if (pressed != _findDrawable.PressedButton)
            {
                _findDrawable.PressedButton = pressed;
                FindPanelView.Invalidate();
            }
        }

        private async void OnFindPanelEndInteraction(object? sender, TouchEventArgs e)
        {
            var pressed = _findDrawable.PressedButton;
            _findDrawable.PressedButton = -1;
            _findPressCandidate = -1;
            FindPanelView.Invalidate();

            if (pressed < 0 || e.Touches is not { Length: > 0 }) return;

            // 松开点必须仍在该按钮上才算点击
            if (_findDrawable.HitTestButton(e.Touches[0]) != pressed) return;

            switch (pressed)
            {
                case 0:
                    await _findInPageService.PreviousAsync();
                    break;
                case 1:
                    await _findInPageService.NextAsync();
                    break;
                case 2:
                    App.AppStateManager!.FindInPageOpen = false;
                    break;
            }
        }

        // ===== 开关动画 =====

        private IDispatcherTimer? _findAnimTimer;
        private int _findPressCandidate = -1;
        private DateTime _findAnimStart;
        private bool _findAnimOpening;
        private const int FindAnimMs = 150;

        private void StartFindPanelAnimation(bool opening)
        {
            _findAnimTimer ??= Dispatcher.CreateTimer();
            _findAnimTimer.Interval = TimeSpan.FromMilliseconds(16);
            _findAnimTimer.Tick -= OnFindAnimTick;
            _findAnimTimer.Tick += OnFindAnimTick;

            _findAnimOpening = opening;
            _findAnimStart = DateTime.UtcNow;
            _findDrawable.Progress = opening ? 0f : 1f;
            _findAnimTimer.Start();
        }

        private void OnFindAnimTick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _findAnimStart).TotalMilliseconds;
            var t = Math.Clamp(elapsed / FindAnimMs, 0d, 1d);
            // 缓出，观感更接近网页弹窗
            var eased = 1d - Math.Pow(1d - t, 3d);

            _findDrawable.Progress = _findAnimOpening ? (float)eased : (float)(1d - eased);
            FindPanelView.Invalidate();

            if (t < 1d) return;

            _findAnimTimer?.Stop();
            _findDrawable.Progress = _findAnimOpening ? 1f : 0f;
            FindPanelView.Invalidate();

            // 收起动画结束才真正隐藏
            if (!_findAnimOpening)
            {
                FindPanelContainer.IsVisible = false;
                FindEntry.IsVisible = false;
            }
        }

        public async Task RefreshSafeAreaAsync()
        {
            // 等待系统 Insets 刷新完成后再读取，避免回到前台时拿到旧值。
            await Task.Delay(50);
            await MainThread.InvokeOnMainThreadAsync(UpdateSafeAreaToWeb);
        }

#if MACCATALYST
        /// <summary>本轮"动画后重采样"的取消源：拖动窗口会连续触发尺寸变化，只留最后一次。</summary>
        private CancellationTokenSource? _macSafeAreaSampleCts;

        /// <summary>
        /// 是否已经可以判定"Mac 是否系统全屏"。
        /// 启动初期 SafeAreaInsets 恒为 0（窗口还没完全挂好），那时按 Insets 判会把窗口态
        /// 误判成全屏、标签条闪一下再消失；所以第一次判定要等窗口稳定（本类里的 450ms 采样）。
        /// 一旦armed 就永久保持，之后每次尺寸变化都能立即判定。
        /// </summary>
        private bool _macFullScreenCheckArmed;

        /// <summary>
        /// 进出系统全屏 / 拖动窗口时，窗口尺寸与 Insets 都在动，而且系统动画约 0.5s；
        /// Insets 的变化往往晚于尺寸回调，只延时一次容易采到旧值（红绿灯那条的高度会记错）。
        /// 因此在一段时间里采样几次，以最后一次为准。每次尺寸变化都会重开一个循环
        /// （旧循环取消），拖窗口时不会堆积。
        /// </summary>
        private async Task RefreshSafeAreaAfterWindowAnimationAsync()
        {
            _macSafeAreaSampleCts?.Cancel();
            _macSafeAreaSampleCts?.Dispose();
            var cts = new CancellationTokenSource();
            _macSafeAreaSampleCts = cts;

            int elapsed = 0;
            foreach (int targetMs in new[] { 120, 450, 900, 1500 })
            {
                try
                {
                    await Task.Delay(targetMs - elapsed, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    return;             // 又发生了一次尺寸变化，交给新的那一轮
                }

                elapsed = targetMs;
                if (cts.IsCancellationRequested) return;

                // 窗口稳定之后才允许做全屏判定（见 _macFullScreenCheckArmed 的说明）
                if (targetMs >= 450) _macFullScreenCheckArmed = true;

                await MainThread.InvokeOnMainThreadAsync(UpdateSafeAreaToWeb);
            }
        }
#endif

        public void HideLoadingScreen()
        {

            LoadingOverlay.IsVisible = false;
            // 启动加载层收起：解除返回键拦截
            BackLayerCoordinator.SetBackBlocked(_loadingOverlayBackBlocker, false);

        }
        public async Task<bool> WaitForWebViewAsync(TimeSpan timeout)
        {
#if WINDOWS
            try
            {
                if (string.IsNullOrWhiteSpace(
                        Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()))
                    return false;
            }
            catch
            {
                return false;
            }
#endif

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                if (blazorWebView.Handler?.PlatformView != null)
                    return true;

                await Task.Delay(100);
            }

            return blazorWebView.Handler?.PlatformView != null;
        }

        public Task ShowWebViewMissingAlertAsync()
        {
            if (Application.Current?.Windows[0] is { } window)
                window.Page = new WebViewUnavailablePage(App.Localization!);

            return Task.CompletedTask;
        }

        public void ShowLoadingPopup(string title, string message)
        {
            AlertTitle.Text = title;
            AlertMessage.Text = message;
            CustomAlertMask.IsVisible = true;
            // 遮罩进度框显示中：吞掉返回键，避免加载途中被返回打断（会重复调用，登记是幂等的）
            BackLayerCoordinator.SetBackBlocked(_loadingPopupBackBlocker, true);
        }

        // 关闭弹窗
        public void HideLoadingPopup()
        {
            CustomAlertMask.IsVisible = false;
            BackLayerCoordinator.SetBackBlocked(_loadingPopupBackBlocker, false);
        }

        protected override void OnHandlerChanging(HandlerChangingEventArgs args)
        {
            // 本页被换掉时（如 WebView 不可用页顶替、窗口关闭）解除返回键拦截，
            // 否则登记会跟着页面一起"留"在 BackLayerCoordinator 里，返回键再也回不来。
            if (args.NewHandler is null)
            {
                BackLayerCoordinator.SetBackBlocked(_loadingOverlayBackBlocker, false);
                BackLayerCoordinator.SetBackBlocked(_loadingPopupBackBlocker, false);
            }

            base.OnHandlerChanging(args);
        }

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

#if WINDOWS
            InitializeWindowsIntegration();
#endif

#if ANDROID
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView androidWebView)
            {
                androidWebView.Settings.TextZoom = 100;
                // 传入当前页面的 Dispatcher
                androidWebView.SetOnKeyListener(new WebViewBackInterceptor(this.Dispatcher));
                _findInPageService.Attach(androidWebView);

                // iframe 内容源的虚拟主机承载：给宿主 WebView 套一层 WebViewClient
                // （只拦 wiki.local，其余回调转交 MAUI 原来的 client，不会破坏 Blazor）。
                App.VirtualHost?.TryRegisterAndroid(androidWebView);
            }
#endif
#if IOS || MACCATALYST
            if (blazorWebView.Handler?.PlatformView is WebKit.WKWebView appleWebView)
                _findInPageService.Attach(appleWebView);
#endif
        }

#if WINDOWS
        private const int TabBarHeightDip = 32;

        private void InitializeWindowsIntegration()
        {
            if (blazorWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webView)
                return;

            _findInPageService.Attach(webView);

            if (!_windowsIntegrationInitialized)
            {
                webView.SizeChanged += (s, e) => UpdateTabBarDragRectangle();
                _windowsIntegrationInitialized = true;
            }

            if (webView.CoreWebView2 != null)
            {
                RegisterDragBridge(webView.CoreWebView2);
                AttachWikiVirtualHost(webView.CoreWebView2);
            }
            else
            {
                webView.CoreWebView2Initialized += (s, e) =>
                {
                    if (e.Exception != null) return;
                    if (webView.CoreWebView2 != null)
                    {
                        RegisterDragBridge(webView.CoreWebView2);
                        AttachWikiVirtualHost(webView.CoreWebView2);
                    }
                };
            }

            ConfigureTabBarTitleBar();
        }

        /// <summary>
        /// 把 https://wiki.local/**（iframe 的内容源）挂到 WebView2 上。
        /// 只在 CoreWebView2 已经（或刚刚）就绪时挂；重复调用是安全的（服务内部按实例去重）。
        /// 挂载成功/失败都会触发 WikiVirtualHost.ContentBaseChanged，MainLayout 据此重建 iframe 的 URL。
        /// </summary>
        private void AttachWikiVirtualHost(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            if (App.VirtualHost is null)
            {
                // 理论上不会发生（DI 里已注册）；留着是为了不让它变成 NullReferenceException
                System.Diagnostics.Debug.WriteLine("[VHost] App.VirtualHost 为 null，跳过挂载");
                return;
            }

            App.VirtualHost.TryAttach(core);
        }


        private void ConfigureTabBarTitleBar()
        {
            var mauiWindow = Application.Current?.Windows[0];
            if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
                nativeWindow.AppWindow?.TitleBar == null)
                return;

            nativeWindow.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            if (!_titleBarConfigured)
            {
                nativeWindow.AppWindow.Changed += OnAppWindowChanged;
                _titleBarConfigured = true;
            }

            nativeWindow.DispatcherQueue.TryEnqueue(UpdateTabBarDragRectangle);
        }

        private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange)
                UpdateTabBarDragRectangle();
        }

        private void UpdateTabBarDragRectangle()
        {
            var mauiWindow = Application.Current?.Windows[0];
            if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
                nativeWindow.Content == null ||
                blazorWebView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 webView ||
                webView.ActualWidth <= 0)
                return;

            var appWindow = nativeWindow.AppWindow;
            if (appWindow?.TitleBar == null)
                return;

            double scale = nativeWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;
            var webViewOrigin = webView.TransformToVisual(nativeWindow.Content)
                .TransformPoint(new Windows.Foundation.Point(0, 0));

            var tabBarDragRect = new Windows.Graphics.RectInt32(
                (int)Math.Round(webViewOrigin.X * scale),
                (int)Math.Round(webViewOrigin.Y * scale),
                (int)Math.Round(webView.ActualWidth * scale),
                (int)Math.Round(TabBarHeightDip * scale));

            try
            {
                appWindow.TitleBar.SetDragRectangles(new[] { tabBarDragRect });
            }
            catch (Exception ex)
            {
                // 拖动区属于"锦上添花"：presenter 关掉标题栏后标题栏高度可能为 0，
                // 某些环境会在这里抛参数异常——不能让尺寸变化回调把它甩到未处理异常里。
                System.Diagnostics.Debug.WriteLine($"SetDragRectangles failed: {ex}");
            }
        }

        private void RegisterDragBridge(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            if (_dragBridgeRegistered)
                return;

            try
            {
                // 暴露给 JS：window.chrome.webview.hostObjects.sync.dragBridge
                core.AddHostObjectToScript("dragBridge", new DragBridge());
                _dragBridgeRegistered = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RegisterDragBridge failed: {ex}");
            }
        }
#endif

        public async Task ClearWebViewCacheAsync()
        {
#if WINDOWS
            if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView &&
                webView.CoreWebView2 is { Profile: not null } coreWebView2)
            {
                await coreWebView2.Profile.ClearBrowsingDataAsync(
                    Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache);
            }
#endif
        }

#if ANDROID
        // 专门为 Android WebView 编写的按键拦截器
        private class WebViewBackInterceptor : Java.Lang.Object, Android.Views.View.IOnKeyListener
        {
            private readonly IDispatcher _dispatcher;

            // 构造函数：接收来自页面的 Dispatcher
            public WebViewBackInterceptor(IDispatcher dispatcher)
            {
                _dispatcher = dispatcher;
            }

            public bool OnKey(Android.Views.View? v, [Android.Runtime.GeneratedEnum] Android.Views.Keycode keyCode, Android.Views.KeyEvent? e)
            {
                if (keyCode == Android.Views.Keycode.Back && e?.Action == Android.Views.KeyEventActions.Down)
                {
                    _dispatcher.Dispatch(() =>
                    {
                        _ = BackEventsService.BackEvents();
                    });

                    return true; // 表示拦截了按键事件
                }
                return false;
            }
        }
#endif

        private void BlazorWebView_UrlLoading(object sender, UrlLoadingEventArgs e)
        {
            // 本机内容一律留在应用内打开。
            // R4 之后唯一的内容基址就是虚拟主机（wiki.local / terrariawiki），
            // 原先给本地 HTTP 端口（127.0.0.1）开的那个分支已随服务器一起删除。
            if (string.Equals(e.Url.Host, WikiVirtualHost.Host, StringComparison.OrdinalIgnoreCase))
            {
                e.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            }
#if IOS || MACCATALYST
            // Apple 侧内容走私有 scheme（terrariawiki://wiki/...），host 是 "wiki" 而不是 wiki.local。
            // 不放行的话会落到默认策略，iframe 里的内容请求会被直接取消。
            if (string.Equals(e.Url.Scheme, WikiVirtualHost.AppleScheme, StringComparison.OrdinalIgnoreCase))
            {
                e.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            }
#endif
        }
#if IOS
        private void EnableProtectionUI()
        {
            BurnInProtectionOverlay.IsVisible = true;
            _originalBrightness = (float)UIKit.UIScreen.MainScreen.Brightness;
            UIKit.UIScreen.MainScreen.Brightness = 0.0f; // 调到最暗
            StartFloatingAnimation();
        }

        private void DisableProtectionUI()
        {
            UIKit.UIScreen.MainScreen.Brightness = _originalBrightness;
            BurnInProtectionOverlay.IsVisible = false;
            // 恢复亮度逻辑...
        }

        private void OnProtectionMaskTapped(object sender, TappedEventArgs e)
        {
            _burnInService.Deactivate();
            _burnInService.ResetTimer();
        }
        private async void StartFloatingAnimation()
        {
            while (_burnInService.IsActive)
            {
                await FloatingText.TranslateTo(0, -60, 4000, Easing.SinInOut);
                await FloatingText.TranslateTo(0, 60, 4000, Easing.SinInOut);
            }
            FloatingText.TranslationY = 0;
        }
#else
        private void OnProtectionMaskTapped(object sender, TappedEventArgs e)
        {

        }
#endif

        private void UpdateSafeAreaToWeb()
        {
#if ANDROID
            // 1. 正确获取 Android 的 Window 对象
            var window = Platform.CurrentActivity?.Window;
            var decorView = window?.DecorView;

            if (decorView == null) return;

            // 2. 读取安全区
            var insets = ViewCompat.GetRootWindowInsets(decorView);
            if (insets != null)
            {
                var statusInsets = insets.GetInsets(WindowInsetsCompat.Type.StatusBars());
                var navInsets = insets.GetInsets(WindowInsetsCompat.Type.NavigationBars());
                var cutoutInsets = insets.GetInsets(WindowInsetsCompat.Type.DisplayCutout());

                // 获取屏幕密度进行换算
                var density = DeviceDisplay.Current.MainDisplayInfo.Density;
                if (density <= 0) density = 1; // 防止除以0

                double topDp = Math.Max(statusInsets.Top, cutoutInsets.Top) / density;
                double bottomDp = navInsets.Bottom / density;
                // 横屏时的刘海会变成 Left 或 Right
                double leftDp = cutoutInsets.Left / density;
                double rightDp = Math.Max(navInsets.Right, cutoutInsets.Right) / density;

                // 3. 注入给前端 CSS 变量
                System.Diagnostics.Debug.WriteLine($"安全区 - 上: {topDp}dp, 下: {bottomDp}dp");
                System.Diagnostics.Debug.WriteLine($"安全区 - 左: {leftDp}dp, 右: {rightDp}dp");
                App.AppStateManager.SafeAreaTop = topDp;
                App.AppStateManager.SafeAreaBottom = bottomDp;
                App.AppStateManager.SafeAreaLeft = leftDp;
                App.AppStateManager.SafeAreaRight = rightDp;
            }
#elif IOS
            // 1. 获取 iOS 当前的 UIViewController
            var viewController = Platform.GetCurrentUIViewController();
            var view = viewController?.View;

            if (view != null)
            {
                // 2. 直接读取 iOS 的 SafeAreaInsets
                var insets = view.SafeAreaInsets;

                // 重点注意：iOS 的返回值已经是逻辑像素 (Points/DP) 了！
                // 绝对不能像 Android 那样再去除非以屏幕密度 (Density)，直接用即可！
                double topDp = insets.Top;
                double bottomDp = insets.Bottom;
                double leftDp = insets.Left;
                double rightDp = insets.Right;

                App.AppStateManager.SafeAreaTop = topDp;
                App.AppStateManager.SafeAreaBottom = bottomDp;
                App.AppStateManager.SafeAreaLeft = leftDp;
                App.AppStateManager.SafeAreaRight = rightDp;
            }
#elif WINDOWS
            // Windows 窄窗口：顶部条不显示（TopStripVisible == false，标签归 MoreList），
            // 但顶部 32px 仍是无边框窗口的拖动/系统按钮区，用 safe-area-top 原样保留；
            // 宽窗口时顶部条本身就是那 32px（TopStripVisible 恒为 true），安全区为 0。
            // 两者都保证内容从 32px 之下开始。
            App.AppStateManager.SafeAreaTop =
                App.AppStateManager.TopStripVisible ? 0d : AppState.TopStripHeight;
#elif MACCATALYST
            // Mac Catalyst：内容默认延伸到标题栏下面，窗口态的红绿灯会压在内容上。
            // UIKit 给的 SafeAreaInsets.Top 正好是标题栏（红绿灯）那条的高度：窗口态 ≈ 41，系统全屏时归 0。
            // 于是它同时就是"是否全屏"的判据 —— 不需要 AppKit，也不需要拿窗口/屏幕尺寸去推断
            // （那套宽高比推断是错的：Catalyst 里窗口用 UIKit 点、屏幕用 macOS 点，比例对不齐）。
            var viewController = Platform.GetCurrentUIViewController();
            var view = viewController?.View;

            if (view != null)
            {
                var insets = view.SafeAreaInsets;

                // 启动初期 Insets 恒为 0（窗口还没完全挂好），那一刻会误判成全屏，
                // 所以第一次判定要等窗口稳定（_macFullScreenCheckArmed，见采样循环）。
                if (_macFullScreenCheckArmed)
                    App.AppStateManager.IsMacFullScreen = (double)insets.Top <= 0.5;

                App.AppStateManager.SafeAreaTop = (double)insets.Top;
                App.AppStateManager.SafeAreaBottom = (double)insets.Bottom;
                App.AppStateManager.SafeAreaLeft = (double)insets.Left;
                App.AppStateManager.SafeAreaRight = (double)insets.Right;
            }
#endif
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            KeyboardService.Default.Start();

        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            KeyboardService.Default.Stop();

        }


    }
}







