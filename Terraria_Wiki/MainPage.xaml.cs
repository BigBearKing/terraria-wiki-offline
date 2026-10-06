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
            DeviceDisplay.Current.MainDisplayInfoChanged += Current_MainDisplayInfoChanged;
        }



        private void MainPage_Loaded(object sender, EventArgs e)
        {
            UpdateSafeAreaToWeb();
#if WINDOWS
            InitializeWindowsIntegration();
#endif
            _findInPageService.ResultChanged += OnFindResultChanged;
            App.AppStateManager!.PropertyChanged += OnFindAppStateChanged;
            // 窗口尺寸变化时当帧重算布局：面板宽度随之收缩，而不是从左边溢出
            blazorWebView.SizeChanged += (_, _) =>
            {
                if (App.AppStateManager?.FindInPageOpen == true)
                    ApplyFindPanelLayout();
            };
            SetupFindPanel();
        }

        private void Current_MainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
        {
            _ = RefreshSafeAreaAsync();
        }

        // =====================================================================
        // 页内搜索：原生自绘弹窗
        // =====================================================================

        /// <summary>初始化自绘画布与透明输入框。只在页面加载时做一次。</summary>
        private void SetupFindPanel()
        {
            if (_findPanelReady) return;

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
        /// header-height 与网页同式：safe-area-top + 6（topbar-top-padding）+ 42（topbar-content-height）
        /// + tabbar-height（仅 Windows 为 32）。
        ///
        /// ★ 强耦合：TopBar 的真实渲染高度**必须**等于 safe-area-top + 6 + 42，
        ///   否则面板会与之错位。历史上 TopBar 在 CSS 里写死 height:48px（border-box 含 padding），
        ///   安全区一大，TopBar 实际底边就停在 48px，而这里按 safe-area-top + 48 定位，
        ///   于是安卓上面板比顶栏低出一整个状态栏高度。
        ///   修法见 TopBar.razor.css：height 改成 calc(topbar-top-padding + topbar-content-height)。
        ///   改 TopBar 垂直尺寸时务必同步本方法。
        ///
        /// 全部走布局（HorizontalOptions=End + Margin），不用 Translation，避免合成层滞后。
        /// </summary>
        private void PositionFindPanel()
        {
            var state = App.AppStateManager;
            if (state is null) return;

            var tabBarHeight = state.IsWindows ? 32d : 0d;
            var headerHeight = FindPanelDrawable.HeaderHeight(state.SafeAreaTop) + tabBarHeight;
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

            _findPressCandidate = _findDrawable.HitTestButton(e.Touches[0]);
            if (_findPressCandidate >= 0)
            {
                _findDrawable.PressedButton = _findPressCandidate;
                FindPanelView.Invalidate();
            }
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

        public void HideLoadingScreen()
        {

            LoadingOverlay.IsVisible = false;

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
        }

        // 关闭弹窗
        public void HideLoadingPopup()
        {
            CustomAlertMask.IsVisible = false;
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
            }
            else
            {
                webView.CoreWebView2Initialized += (s, e) =>
                {
                    if (e.Exception != null) return;
                    if (webView.CoreWebView2 != null)
                        RegisterDragBridge(webView.CoreWebView2);
                };
            }

            ConfigureTabBarTitleBar();
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
            // 如果主机名是 127.0.0.1 ，强制在应用内打开
            if (e.Url.Host == "127.0.0.1")
            {
                e.UrlLoadingStrategy = UrlLoadingStrategy.OpenInWebView;
            }
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







