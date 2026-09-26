/**
 * Terraria Wiki 网页端通用脚本
 *
 * 该脚本运行在 MAUI 应用内嵌的 WebView 中，负责：
 * 1. 通过 iframeBridge 与 C# 原生代码通信（页面加载、历史记录、主题切换等）；
 * 2. 处理页面内的链接点击、鼠标侧键返回、图片查看等交互；
 * 3. 桌面端（非移动端）提供自定义右键菜单（复制文本/图片、打开源码页面）。
 */
(function () {
    // 导航版本号：每次发起导航都会自增。
    // 用于丢弃过期导航的异步结果，避免快速连续点击时旧页面覆盖新页面。
    let navigationVersion = 0;

    /**
     * 调用 C# 原生方法（异步）。
     * @param {string} method C# 侧注册的方法名
     * @param {*} [data] 传给 C# 的参数
     * @returns {Promise<string>} C# 返回的字符串结果
     */
    function callCSharpAsync(method, data) {
        return window.iframeBridge.callCSharpAsync(method, data);
    }

    let localization = {};
    let lastModifiedValue = "";

    function t(key, fallback) {
        return localization[key] || fallback;
    }

    async function loadLocalization() {
        try {
            const json = await callCSharpAsync("GetIframeLocalization", "");
            localization = json ? JSON.parse(json) : {};
        } catch (error) {
            console.warn('Failed to load iframe localization:', error);
            localization = {};
        }
    }

    function refreshLocalizedUi(config) {
        const contextMenu = document.getElementById('custom-context-menu');
        if (contextMenu) {
            contextMenu.querySelector('#menu-copy').textContent = t('Web.Copy', 'Copy');
            contextMenu.querySelector('#menu-new-tab').textContent = t('Web.OpenInNewTab', 'Open in new tab');
            contextMenu.querySelector('#menu-open-source').textContent = t('Web.OpenOriginal', 'Open original');
        }

        const lastModified = document.getElementById('footer-info-lastmod');
        if (lastModified && lastModifiedValue) {
            lastModified.textContent = (config?.lastModifiedPrefix || t('Web.LastEdited', 'This page was last edited on ')) + lastModifiedValue;
        }
    }

    /**
     * 开始一次新的导航并使导航版本号自增。
     * @returns {number} 本次导航的版本号
     */
    function beginNavigation() {
        navigationVersion += 1;
        return navigationVersion;
    }

    /**
     * 判断给定的版本号是否为最新导航，若不是则说明导航已被更新导航取代。
     * @param {number} version 发起导航时记录的版本号
     * @returns {boolean} 是否为当前有效的导航
     */
    function isCurrentNavigation(version) {
        return version === navigationVersion;
    }

    /**
     * 脚本入口：读取 URL 参数、注册 C# 消息处理器、绑定页面交互，最后加载首页。
     * @param {object} config 配置对象（由各平台页面传入）
     */
    async function start(config) {
        // 从 URL 查询参数中读取初始主题与设备类型
        const urlParams = new URLSearchParams(window.location.search);
        const initialTheme = urlParams.get('theme');
        const isMobile = urlParams.get('isMobile');
        const initialZoom = Number.parseInt(urlParams.get('zoom') || '100', 10);
        applyWikiZoom(initialZoom);

        // 应用初始主题（若 URL 中指定了 theme 参数）
        if (initialTheme === "dark") {
            changeTheme('True');
        } else if (initialTheme === "light") {
            changeTheme('False');
        }

        await loadLocalization();
        ensureCommonUI(); // 注入公共 UI（右键菜单结构、公共样式）
        refreshLocalizedUi(config);

        window.pageTitle = null; // 当前页面标题，初始为空
        registerHandlers(config); // 注册 C# -> JS 的消息处理器
        bindNavigation(config);   // 绑定链接点击、鼠标侧键等导航交互
        window.addEventListener('hashchange', expandCollapsiblesForHash);

        // 仅桌面端启用自定义右键菜单
        if (isMobile === "False") {
            initContextMenu(config);
        }

        // 加载首页（redirect 内部会发起导航并渲染页面）
        redirect(config.homePage, config).catch(error => console.error('Failed to load wiki page:', error));
    }

    /**
     * 注册 C# 原生代码调用的消息处理器（C# -> JS 方向）。
     * @param {object} config 配置对象
     */
    function applyWikiZoom(value) {
        const zoom = Number.isFinite(value) ? Math.min(200, Math.max(50, value)) : 100;
        document.documentElement.style.zoom = `${zoom}%`;
    }

    function registerHandlers(config) {
        window.iframeBridge.registerHandler("SetZoom", (value) => {
            applyWikiZoom(Number.parseInt(value, 10));
            return null;
        });

        window.iframeBridge.registerHandler("SetLocalization", (json) => {
            try {
                localization = typeof json === 'string' ? JSON.parse(json) : (json || {});
            } catch (error) {
                console.warn('Failed to apply iframe localization:', error);
            }
            refreshLocalizedUi(config);
            config.refresh();
            return null;
        });

        // 跳转到指定词条页面（msg 为词条标题）
        window.iframeBridge.registerHandler("GotoPage", async (msg) => {
            await gotoPage(msg, config);
            return null;
        });

        // 返回历史中的某一页：msg 为 JSON 字符串，包含 Title（标题）与 Position（滚动位置）
        window.iframeBridge.registerHandler("BackToPage", async (msg) => {
            const args = JSON.parse(msg);
            await backToPage(args.title, args.position, config);
            return null;
        });

        // 返回首页并滚到顶部
        window.iframeBridge.registerHandler("BackHome", async () => {
            await redirect(config.homePage, config);
            window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
            return null;
        });

        // 平滑滚动到页面顶部
        window.iframeBridge.registerHandler("ToTop", () => {
            window.scrollTo({ top: 0, left: 0, behavior: 'smooth' });
            return null;
        });

        // 切换深色/浅色主题（参数为 "True"/"False"）
        window.iframeBridge.registerHandler("ChangeTheme", (isDarkTheme) => {
            changeTheme(isDarkTheme);
            return null;
        });

        // 清空页面正文（例如 C# 侧销毁页面时调用）
        window.iframeBridge.registerHandler("ClearPage", () => {
            beginNavigation();
            document.getElementById("mw-content-text").innerHTML = "";
            return null;
        });
    }

    /**
     * 绑定页面内的导航交互：链接点击与鼠标侧键返回。
     * @param {object} config 配置对象
     */
    function bindNavigation(config) {
        // 禁止 WebView 中除复制、粘贴和查找之外的 Ctrl/Alt 浏览器快捷键。
        document.addEventListener('keydown', function (e) {
            const key = e.key.toLowerCase();
            const isAllowedCtrlShortcut = e.ctrlKey && !e.altKey && !e.metaKey && !e.shiftKey
                && (key === 'c' || key === 'v' || key === 'f');

            if ((e.ctrlKey || e.altKey) && !isAllowedCtrlShortcut) {
                e.preventDefault();
            }
        }, { capture: true });

        document.addEventListener('wheel', function (e) {
            if (e.ctrlKey) {
                e.preventDefault();
            }
        }, { capture: true, passive: false });

        // 处理所有链接点击
        document.addEventListener('click', function (e) {
            const targetLink = e.target.closest('a');
            console.log('点击了',targetLink);
            if (targetLink) {
                // 点击缩略图（thumb）中的图片链接 -> 打开图片查看器
                // 注意：thumbcaption 说明文字里的普通文本链接（无 <img>）不属于图片，
                // 不应被劫持，应继续走下方正常的站内跳转逻辑
                if (targetLink.closest("div.thumb") && targetLink.querySelector('img')) {
                    openThumb(targetLink);
                    return;
                }

                // 配置允许时：点击图片链接 -> 打开图片查看器
                if (config.openImageLinks && targetLink.classList.contains('image') && targetLink.querySelector('img')) {
                    e.preventDefault();
                    openThumb(targetLink);
                    return;
                }

                const wikiTitle = targetLink.getAttribute('data-wiki');
                const href = targetLink.getAttribute('href') || '';
                // 外链 -> 交给 C# 用系统浏览器打开
                if (href.startsWith('http')) {
                    e.preventDefault();
                    callCSharpAsync("OpenExternalWebsite", href);
                    return;
                }
                // 带 data-wiki 属性的站内链接 -> 应用内跳转
                if (wikiTitle && !href) {
                    gotoPage(wikiTitle, config);
                }
            }
        });

        // 鼠标侧键（前进/后退键）按下 -> 通知 C# 执行返回操作
        document.addEventListener('mouseup', function (e) {
            if (e.button === 3 || e.button === 4) {
                e.preventDefault();
                callCSharpAsync("WikiBackAsync", "");
            }
        });
    }

    /**
     * 跳转到指定词条：先向 C# 查询重定向后的真实标题与锚点，再渲染页面，
     * 最后把当前页（标题 + 滚动位置）存入 C# 侧的临时历史记录。
     * @param {string} title 目标词条标题
     * @param {object} config 配置对象
     * @param {number} [navigationId] 导航版本号，默认开始一次新导航
     */
    async function gotoPage(title, config, navigationId = beginNavigation()) {
        // 记录当前页的标题与滚动位置，供后续返回使用
        const args = {
            title: window.pageTitle,
            position: window.pageYOffset
        };
        // 向 C# 查询：标题可能被重定向（如别名 -> 正式名），还可能带 #锚点
        const titleWithAnchor = JSON.parse(await callCSharpAsync("GetRedirectedTitleAndAnchorAsync", title));
        if (!isCurrentNavigation(navigationId)) return;

        // 渲染重定向后的页面；若返回 null 说明导航已失效则中止
        if (await redirect(titleWithAnchor.title, config, navigationId) == null || !isCurrentNavigation(navigationId)) return;
        // 跳到页面顶部
        window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
        // 若带锚点，则平滑滚动到对应元素
        if (titleWithAnchor.anchor) {
            const element = document.getElementById(titleWithAnchor.anchor);
            if (element) {
                element.scrollIntoView({ behavior: "smooth" });
            }
        }

        // 导航仍有效时，把上一页信息存入临时历史
        if (isCurrentNavigation(navigationId)) {
            await callCSharpAsync("SaveToTabHistory", JSON.stringify(args));
        }
    }

    /**
     * 返回到历史记录中的某一页：渲染该页并恢复其滚动位置。
     * @param {string} title 目标词条标题
     * @param {number} position 需要恢复的滚动位置（像素）
     * @param {object} config 配置对象
     * @param {number} [navigationId] 导航版本号
     */
    async function backToPage(title, position, config, navigationId = beginNavigation()) {
        if (await redirect(title, config, navigationId) == null || !isCurrentNavigation(navigationId)) return;
        // 恢复滚动位置（瞬时滚动，不带动画）
        window.scrollTo({ top: position, left: 0, behavior: 'instant' });
    }

    /**
     * 核心渲染函数：向 C# 请求词条内容，并将其填入页面 DOM。
     * @param {string} title 目标词条标题
     * @param {object} config 配置对象
     * @param {number} [navigationId] 导航版本号
     * @returns {Promise<boolean|null>} 渲染成功返回 true；导航已失效返回 null
     */
    async function redirect(title, config, navigationId = beginNavigation()) {
        // 请求 C# 渲染词条 HTML，返回 { title, content, lastModified }
        const result = JSON.parse(await callCSharpAsync("PageRedirectAsync", title));
        if (result == null || !isCurrentNavigation(navigationId)) return null;

        // 更新页面标题、正文内容与最后修改时间
        window.pageTitle = result.title;
        document.getElementById(config.headingId).textContent = result.title;
        document.getElementById("mw-content-text").innerHTML = result.content;
        lastModifiedValue = result.lastModified;
        document.getElementById("footer-info-lastmod").textContent =
            (config.lastModifiedPrefix || t('Web.LastEdited', 'This page was last edited on ')) + result.lastModified;

        // 首页特殊处理：通过 body 上的类名控制首页样式
        const isHomePage = title === config.homePage;
        if (config.homePageClass) {
            document.body.classList.toggle(config.homePageClass, isHomePage);
        }

        // 首页时隐藏页面标题栏
        const homeHeading = document.getElementById(config.homeHeadingId || config.headingId);
        if (isHomePage) {
            homeHeading.setAttribute("style", "display:none");
        } else {
            homeHeading.removeAttribute("style");
        }

        // 调用配置中的刷新回调（例如重新运行页面脚本、刷新锚点等）
        config.refresh();
        // 启用正文里的可折叠元素（必须在 refresh 之后，此时 DOM 结构已稳定）
        initCollapsibles();
        window.parent.postMessage({ type: "event", method: "IframePageReady", data: null }, '*');
        return true;
    }

    /**
     * 用 Viewer（viewerjs）以弹窗形式打开图片查看器。
     * @param {HTMLElement} thumb 缩略图链接（<a>）元素
     */
    function openThumb(thumb) {
        const img = thumb.querySelector('img');
        if (!img) return;

        const viewer = new Viewer(img, {
            inline: false,   // 非内嵌模式，使用弹窗预览
            button: true,    // 显示关闭按钮
            navbar: false,   // 不显示底部缩略图导航栏
            title: true,     // 显示标题
            toolbar: false,  // 不显示顶部工具栏
            backdrop: true,  // 点击遮罩可关闭
            zoomRatio: 0.3,  // 每次滚轮/按钮缩放的倍率
            hidden: function () {
                viewer.destroy(); // 关闭后销毁实例，避免内存泄漏
            },
        });

        viewer.show();
    }

    /**
     * 切换深色/浅色主题：通过给 <html> 添加 light/dark 类实现。
     * @param {string|boolean} isDarkTheme 是否深色（"True"/"False" 或布尔值）
     */
    function changeTheme(isDarkTheme) {
        if (isDarkTheme == "True") {
            document.documentElement.classList.remove("light");
            document.documentElement.classList.add("dark");
        } else {
            document.documentElement.classList.remove("dark");
            document.documentElement.classList.add("light");
        }
    }

    /**
     * 注入所有 wiki 页面共用的 UI 元素与样式。
     *
     * 原先这些内容（自定义右键菜单的 HTML 结构，以及滚动条、
     * Viewer 动画、右键菜单等公共 CSS）在各站点的 index.html 中重复出现，
     * 现统一由本函数动态创建，保证所有页面外观与行为一致。
     */
    function ensureCommonUI() {
        // ---- 自定义右键菜单结构（仅桌面端使用，initContextMenu 中绑定行为）----
        if (!document.getElementById("custom-context-menu")) {
            const menu = document.createElement("div");
            menu.id = "custom-context-menu";
            menu.className = "hidden-menu";

            const copyItem = document.createElement("div");
            copyItem.className = "menu-item";
            copyItem.id = "menu-copy";
            copyItem.textContent = t('Web.Copy', 'Copy');

            const openNewTabItem = document.createElement("div");
            openNewTabItem.className = "menu-item";
            openNewTabItem.id = "menu-new-tab";
            openNewTabItem.textContent = t('Web.OpenInNewTab', 'Open in new tab');

            const openSourceItem = document.createElement("div");
            openSourceItem.className = "menu-item";
            openSourceItem.id = "menu-open-source";
            openSourceItem.textContent = t('Web.OpenOriginal', 'Open original');

            menu.appendChild(copyItem);
            menu.appendChild(openNewTabItem);
            menu.appendChild(openSourceItem);
            document.body.appendChild(menu);
        }

        // ---- 公共样式：只注入一次，避免重复 ----
        if (!document.getElementById("wiki-common-style")) {
            const style = document.createElement("style");
            style.id = "wiki-common-style";
            style.textContent = `
            body {
                padding-right: 0 !important;
            }

            /* 覆盖 Viewer.js 全局的形变/缩放动画速度 */
            .viewer-transition {
                transition: all 0.15s !important;
                /* 默认通常是 0.3s，数值越小越快 */
            }

            /* 覆盖 Viewer.js 背景和遮罩层的淡入淡出速度 */
            .viewer-fade {
                transition: opacity 0.15s !important;
            }

            .mclist {
                overflow-y: hidden;
            }

            /* 1. 基础宽度 - 稍微收窄一点，显得更精致 */
            ::-webkit-scrollbar {
                width: 6px;
                height: 6px;
            }

            /* 2. 轨道 - 保持完全透明 */
            ::-webkit-scrollbar-track {
                background: transparent;
            }

            /* 3. 滑块 - 使用半透明色，确保在深色/浅色背景下都能"透"出来 */
            ::-webkit-scrollbar-thumb {
                background-color: rgba(128, 128, 128, 0.3);
                border-radius: 999px;
                transition: background-color 0.2s;
            }

            /* 4. 悬停状态 - 宽度不变，但颜色加深，增加互动反馈 */
            ::-webkit-scrollbar-thumb:hover {
                background-color: rgba(128, 128, 128, 0.5);
            }

            /* 自定义右键菜单基础样式 */
            #custom-context-menu {
                position: fixed;
                background: #ffffff;
                border-radius: 6px;
                box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
                z-index: 100000;
                min-width: 140px;
                padding: 4px 0;
                /* 用 opacity + visibility 实现淡入淡出：
                   visibility 支持过渡，会在淡出动画结束后才真正隐藏，
                   淡入时立即可见再配合 opacity 过渡，避免 display 切换无动画 */
                opacity: 0;
                visibility: hidden;
                transition: opacity 0.08s ease, visibility 0.08s ease;
                font-family: inherit;
            }

            /* 显示菜单的状态 */
            #custom-context-menu.show-menu {
                opacity: 1;
                visibility: visible;
            }

            /* 菜单项样式 */
            .menu-item {
                padding: 6px 16px;
                font-size: 14px;
                color: #333;
                cursor: pointer;
                transition: background-color 0.1s;
            }

            .menu-item:hover {
                background-color: #f0f0f0;
            }


            /* ---- 深色模式 (dark theme) 适配 ---- */
            html.dark #custom-context-menu {
                background: #2b2b2b;
                border-color: #444;
                box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5);
            }

            html.dark .menu-item {
                color: #eee;
            }

            html.dark .menu-item:hover {
                background-color: #3a3a3a;
            }

            html.dark .menu-divider {
                background-color: #444;
            }

            /* ---- MediaWiki 可折叠元素（jquery.makeCollapsible.styles）----
               离线包没有 ResourceLoader，核心样式表也没打包。这里补上 wiki.gg 实际
               下发的规则，否则 <button> 会带浏览器默认外观（灰底圆角+外框）。 */
            .mw-collapsible-toggle {
                float: right;
                -webkit-user-select: none;
                user-select: none;
                cursor: pointer;
            }

            .mw-collapsible-toggle-default {
                -webkit-appearance: none;
                appearance: none;
                background: none;
                margin: 0;
                padding: 0;
                border: 0;
                font: inherit;
                cursor: pointer;
            }

            .mw-collapsible-toggle-default .mw-collapsible-text {
                /* 依次回退到各 wiki 自己的链接色变量，都没有则继承 */
                color: var(--color-link, var(--wiki-content-link-color, var(--theme-link-color, inherit)));
                text-decoration: none;
            }

            .mw-collapsible-toggle-default .mw-collapsible-text:hover,
            .mw-collapsible-toggle-default .mw-collapsible-text:active {
                text-decoration: underline;
            }

            .mw-collapsible-toggle-default::before {
                content: '[';
            }

            .mw-collapsible-toggle-default::after {
                content: ']';
            }

            .mw-customtoggle {
                cursor: pointer;
            }

            caption .mw-collapsible-toggle {
                float: none;
            }
            `;
            document.head.appendChild(style);
        }
    }

    // ============================================================
    // 可折叠元素（mw-collapsible）
    //
    // 原生 JS 移植版，逐函数对应 MediaWiki 核心模块 jquery.makeCollapsible
    // (REL1_43)，只是把 jQuery 操作换成 DOM API：
    //   https://gerrit.wikimedia.org/r/plugins/gitiles/mediawiki/core/+/refs/heads/REL1_43/resources/src/jquery/jquery.makeCollapsible.js
    // 入口与官方一致：mediawiki.page.ready 里的
    //   $content.find('.mw-collapsible').makeCollapsible()
    //
    // 为什么要自己实现：折叠完全依赖 ResourceLoader 下发的核心 JS，
    // 离线包里既没有 ResourceLoader 也没有任何替代实现，于是
    // .mw-collapsible / .mw-collapsed 退化成惰性类名 ——
    // 没有 toggle 元素可点、没有事件、也没有任何隐藏样式。
    // ============================================================

    /** 查询单个元素。 */
    function qs(selector, root) {
        return (root || document).querySelector(selector);
    }

    /** 查询多个元素并返回真正的数组（便于 filter 等操作）。 */
    function qsa(selector, root) {
        return Array.prototype.slice.call((root || document).querySelectorAll(selector));
    }

    /** 取直接子元素中匹配选择器的那些（等价于 jQuery 的 "> sel"）。 */
    function directChildren(el, selector) {
        return Array.prototype.filter.call(el.children, function (child) {
            return child.matches(selector);
        });
    }

    /**
     * 在 root 中查找「预制」toggle。
     *
     * 对应官方选择器 `'> .mw-collapsible-toggle, .mw-collapsible-toggle-placeholder'`
     * —— 注意选择器列表里**只有第一项带 `>`**：
     *   · `.mw-collapsible-toggle`        只找**直接子元素**
     *   · `.mw-collapsible-toggle-placeholder`  在**全部后代**里找
     * 各 wiki 的模板正依赖第二点，例如 Terraria 的
     * `.ranger-navbox > .ranger-title > .mw-collapsible-toggle-placeholder`
     * 与 `.ranger-section > .ranger-header > .mw-collapsible-toggle-placeholder`，
     * 配套 CSS 也写成 `.ranger-navbox .ranger-header > .mw-collapsible-toggle`。
     *
     * @param {HTMLElement|HTMLElement[]} roots 一个或多个查找起点
     * @returns {HTMLElement|null}
     */
    function findToggle(roots) {
        const list = Array.isArray(roots) ? roots : [roots];
        let direct = null;
        let placeholder = null;
        list.forEach(function (root) {
            if (!root) return;
            if (!direct) {
                direct = Array.prototype.find.call(root.children, function (child) {
                    return child.classList.contains('mw-collapsible-toggle');
                }) || null;
            }
            if (!placeholder) {
                placeholder = root.querySelector('.mw-collapsible-toggle-placeholder');
            }
        });
        // 两者都存在时按文档顺序取靠前的（与官方 .first() 一致）
        if (direct && placeholder) {
            return (direct.compareDocumentPosition(placeholder) & Node.DOCUMENT_POSITION_FOLLOWING)
                ? direct
                : placeholder;
        }
        return direct || placeholder || null;
    }

    /** CSS.escape 兜底（把 id 拼成选择器时用）。 */
    function escapeSelector(value) {
        if (window.CSS && CSS.escape) return CSS.escape(value);
        return String(value).replace(/[^\w-]/g, '\\$&');
    }

    /** 是否中文页面：官方消息 collapsible-collapse / collapsible-expand 取页面内容语言。 */
    function isZhContent() {
        return (document.documentElement.getAttribute('lang') || 'en').toLowerCase().indexOf('zh') === 0;
    }

    /** 折叠时 toggle 上的文字（collapsible-collapse）。 */
    function collapsibleCollapseText() {
        return isZhContent() ? '折叠' : 'Collapse';
    }

    /** 展开时 toggle 上的文字（collapsible-expand）。 */
    function collapsibleExpandText() {
        return isZhContent() ? '展开' : 'Expand';
    }

    /**
     * 生成默认 toggle（官方 buildDefaultToggleLink）：
     * <button type="button" class="mw-collapsible-toggle mw-collapsible-toggle-default">
     *     <span class="mw-collapsible-text">折叠</span>
     * </button>
     * @param {string} text 初始文字（官方固定用 collapseText）
     * @returns {HTMLButtonElement}
     */
    function buildDefaultToggleLink(text) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'mw-collapsible-toggle mw-collapsible-toggle-default';
        const span = document.createElement('span');
        span.className = 'mw-collapsible-text';
        span.textContent = text;
        button.appendChild(span);
        return button;
    }

    /**
     * 真正展开/折叠容器内容（官方 toggleElement）。
     * @param {HTMLElement} collapsible .mw-collapsible 元素
     * @param {boolean} expand true 展开，false 折叠
     * @param {HTMLElement|null} defaultToggle 默认 toggle（表格/列表要排除它所在的那一行/项）
     * @param {object} [options] 支持 plainMode
     */
    function toggleCollapsibleElement(collapsible, expand, defaultToggle, options) {
        options = options || {};
        if (defaultToggle === undefined) defaultToggle = null;

        const tag = collapsible.tagName.toLowerCase();
        let containers;

        if (!options.plainMode && tag === 'table') {
            // 表格：有 caption 就折叠除 caption 外的所有行，否则折叠 tbody 的行
            if (directChildren(collapsible, 'caption').length) {
                containers = qsa(':scope > * > tr', collapsible);
            } else {
                containers = qsa(':scope > tbody > tr', collapsible);
            }
            if (defaultToggle) {
                const toggleRow = defaultToggle.closest('tr');
                containers = containers.filter(function (row) { return row !== toggleRow; });
            }
        } else if (!options.plainMode && (tag === 'ul' || tag === 'ol')) {
            // 列表：折叠每一项（toggle 自己所在项除外）
            containers = directChildren(collapsible, 'li');
            if (defaultToggle) {
                const toggleItem = defaultToggle.parentElement;
                containers = containers.filter(function (item) { return item !== toggleItem; });
            }
        } else {
            // 其余（div/p 等）：优先折叠 .mw-collapsible-content；
            // 没有它说明是「自定义 toggle + 整块切换」的 remote 场景，折叠元素自身
            const content = directChildren(collapsible, '.mw-collapsible-content');
            containers = (!options.plainMode && content.length) ? content : [collapsible];
        }

        containers.forEach(function (el) {
            el.style.display = expand ? '' : 'none';
        });
    }

    /**
     * 处理一次折叠/展开（官方 togglingHandler）。
     * @param {HTMLElement} toggle 被操作的 toggle
     * @param {HTMLElement} collapsible 受控容器
     * @param {Event|null} e 触发事件（初始化时传 null）
     * @param {object} [options] toggleClasses / toggleARIA / toggleText / wasCollapsed
     */
    function handleCollapsibleToggle(toggle, collapsible, e, options) {
        options = options || {};

        if (e) {
            // 点到 toggle 内部的链接（预置 toggle 的情况）→ 放行，不折叠
            if (e.type === 'click'
                && e.target.nodeName.toLowerCase() === 'a'
                && e.target.getAttribute('href')) {
                return;
            }
            // 键盘只认 Enter / Space（官方用 e.which 13 / 32）
            if (e.type === 'keydown'
                && e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') {
                return;
            }
            e.preventDefault();
            e.stopPropagation();
        }

        // options.wasCollapsed 用于初始化时绕开类名判断
        const wasCollapsed = options.wasCollapsed !== undefined
            ? options.wasCollapsed
            : collapsible.classList.contains('mw-collapsed');

        collapsible.classList.toggle('mw-collapsed', !wasCollapsed);

        if (options.toggleClasses) {
            toggle.classList.toggle('mw-collapsible-toggle-collapsed', !wasCollapsed);
            toggle.classList.toggle('mw-collapsible-toggle-expanded', wasCollapsed);
        }
        if (options.toggleARIA) {
            toggle.setAttribute('aria-expanded', wasCollapsed ? 'true' : 'false');
        }
        if (options.toggleText) {
            const textContainer = qs('.mw-collapsible-text', toggle);
            if (textContainer) {
                textContainer.textContent = wasCollapsed
                    ? options.toggleText.collapseText
                    : options.toggleText.expandText;
            }
        }

        toggleCollapsibleElement(collapsible, !!wasCollapsed, toggle, options);
    }

    /**
     * 找到（或创建）默认 toggle，并把 .mw-collapsible-toggle-placeholder 换成真正的 toggle。
     * 对应官方 makeCollapsible 里 else 分支的元素分类逻辑。
     * @param {HTMLElement} collapsible 目标元素
     * @param {string} collapseText 初始文字
     * @returns {HTMLElement} 最终的 toggle 元素
     */
    function createDefaultToggle(collapsible, collapseText) {
        function build() { return buildDefaultToggleLink(collapseText); }

        const tag = collapsible.tagName.toLowerCase();
        let toggle = null;

        if (tag === 'table') {
            const caption = directChildren(collapsible, 'caption')[0];
            if (caption) {
                // 有 caption：toggle 放在 caption 末尾
                toggle = findToggle(caption);
                if (!toggle) {
                    toggle = build();
                    caption.appendChild(toggle);
                }
            } else {
                // 没有 caption：toggle 放在第一行最后一个 th/td 的最前面
                // table.rows 按 thead → tbody → tfoot 顺序返回，rows[0] 即第一行
                const firstRow = collapsible.rows.length ? collapsible.rows[0] : null;
                const cells = firstRow ? Array.prototype.slice.call(firstRow.cells) : [];
                const lastCell = cells[cells.length - 1];
                toggle = findToggle(cells);
                if (!toggle && lastCell) {
                    toggle = build();
                    lastCell.insertBefore(toggle, lastCell.firstChild);
                }
            }
        } else if (collapsible.parentElement
            && collapsible.parentElement.tagName.toLowerCase() === 'li'
            && directChildren(collapsible.parentElement, '.mw-collapsible').length === 1
            && !findToggle(collapsible)) {
            // 特例：<li> 中只有一个可折叠元素 → toggle 直接放在它前面
            toggle = build();
            collapsible.parentElement.insertBefore(toggle, collapsible);
        } else if (tag === 'ul' || tag === 'ol') {
            // 列表：toggle 放在第一个 li 里；没有则包一层 li 放到列表最前
            const firstItem = qs('li', collapsible);
            toggle = findToggle(firstItem);
            if (!toggle) {
                if (firstItem) {
                    // 保证序号不被挤乱：把第一项强制成 1（value 属性已被占用则不动）
                    const value = firstItem.getAttribute('value');
                    if (value === null || value === '' || value === '-1') {
                        firstItem.setAttribute('value', '1');
                    }
                }
                toggle = build();
                const wrapper = document.createElement('li');
                wrapper.className = 'mw-collapsible-toggle-li';
                wrapper.appendChild(toggle);
                collapsible.insertBefore(wrapper, collapsible.firstChild);
            }
        } else {
            // div / p 等：toggle 作为第一个子元素，其余内容整体包进 .mw-collapsible-content
            toggle = findToggle(collapsible);
            if (!directChildren(collapsible, '.mw-collapsible-content').length) {
                const wrapper = document.createElement('div');
                wrapper.className = 'mw-collapsible-content';
                while (collapsible.firstChild) wrapper.appendChild(collapsible.firstChild);
                collapsible.appendChild(wrapper);
            }
            if (!toggle) {
                toggle = build();
                collapsible.insertBefore(toggle, collapsible.firstChild);
            }
        }

        // 占位符 → 真正的 toggle
        if (toggle && toggle.classList.contains('mw-collapsible-toggle-placeholder')) {
            const real = build();
            toggle.parentNode.replaceChild(real, toggle);
            toggle = real;
        }
        return toggle;
    }

    /**
     * 让一个元素变成可折叠（官方 $.fn.makeCollapsible）。
     * @param {HTMLElement} collapsible 目标元素
     * @param {object} [options] collapseText / expandText / collapsed / plainMode / $customTogglers
     */
    function makeCollapsible(collapsible, options) {
        options = options || {};
        collapsible.classList.add('mw-collapsible');

        // 已初始化过就直接返回，避免重复绑定
        if (collapsible.dataset.mwMadeCollapsible) return;
        collapsible.classList.add('mw-made-collapsible');
        collapsible.dataset.mwMadeCollapsible = '1';

        // 文字优先取 options，再取 data-* 属性，最后用官方消息
        const collapseText = options.collapseText
            || collapsible.getAttribute('data-collapsetext') || collapsibleCollapseText();
        const expandText = options.expandText
            || collapsible.getAttribute('data-expandtext') || collapsibleExpandText();

        /** 为某个 toggle 生成事件处理器（官方 actionHandler）。 */
        function makeActionHandler(toggleEl, baseOptions) {
            return function (e, overrides) {
                // 处理克隆内容（如引用弹窗）：toggle 不在原容器内时，改用它所属的容器
                const target = collapsible.contains(toggleEl)
                    ? collapsible
                    : (toggleEl.closest('.mw-collapsible') || collapsible);
                handleCollapsibleToggle(toggleEl, target, e, Object.assign({}, baseOptions, overrides));
            };
        }

        // 自定义 toggle：id="mw-customcollapsible-XXX" 对应 class="mw-customtoggle-XXX"
        let customToggles = null;
        if (options.$customTogglers) {
            customToggles = options.$customTogglers;
        } else {
            const id = collapsible.getAttribute('id') || '';
            if (id.indexOf('mw-customcollapsible-') === 0) {
                customToggles = qsa('.' + escapeSelector(id.replace('mw-customcollapsible', 'mw-customtoggle')));
                customToggles.forEach(function (t) { t.classList.add('mw-customtoggle'); });
            }
        }

        let toggles;
        let defaultOptions;
        if (customToggles && customToggles.length) {
            // 自定义 toggle 不套用默认的 toggleText / toggleClasses / ARIA
            defaultOptions = {};
            toggles = customToggles;
        } else {
            defaultOptions = {
                toggleClasses: true,
                toggleARIA: true,
                toggleText: { collapseText: collapseText, expandText: expandText }
            };
            toggles = [createDefaultToggle(collapsible, collapseText)];
        }

        // 绑定点击/键盘，并设置无障碍属性
        toggles.forEach(function (toggleEl) {
            const handler = makeActionHandler(toggleEl, defaultOptions);
            toggleEl.addEventListener('click', handler);
            toggleEl.addEventListener('keydown', handler);
            toggleEl.setAttribute('aria-expanded', 'true');
            toggleEl.tabIndex = 0;
        });

        // 暴露折叠 API（对应官方 $.data('mw-collapsible')）
        const firstHandler = makeActionHandler(toggles[0], defaultOptions);
        collapsible.__mwCollapsible = {
            collapse: function () { firstHandler(null, { wasCollapsed: false }); },
            expand: function () { firstHandler(null, { wasCollapsed: true }); },
            toggle: function () { firstHandler(null, null); }
        };

        // 初始状态：带 mw-collapsed 类（或 options.collapsed）则立即折叠
        if (options.collapsed || collapsible.classList.contains('mw-collapsed')) {
            firstHandler(null, { wasCollapsed: false });
        }
    }

    /**
     * 对容器内所有 .mw-collapsible 启用折叠。
     * 等价于官方 mediawiki.page.ready 中的
     * `$content.find('.mw-collapsible').makeCollapsible()`（$content = #mw-content-text）。
     * @param {HTMLElement} [root] 搜索范围，默认 #mw-content-text
     */
    function initCollapsibles(root) {
        const container = root || document.getElementById('mw-content-text');
        if (!container) return;
        qsa('.mw-collapsible', container).forEach(function (el) { makeCollapsible(el); });
    }

    /**
     * 把 URL 片段（#id）所在的折叠容器全部展开并滚动过去（官方 hashHandler）。
     * 用于正文里点击 #cite_note-xxx 之类的锚点链接时，自动展开被折叠的祖先。
     */
    function expandCollapsiblesForHash() {
        const hash = decodeURIComponent(window.location.hash || '').slice(1);
        if (!hash) return;
        const target = document.getElementById(hash)
            || qs('[name="' + escapeSelector(hash) + '"]');
        if (!target) return;

        // 收集所有处于折叠状态的祖先容器
        const collapsedParents = [];
        for (let node = target.parentElement; node; node = node.parentElement) {
            if (node.classList && node.classList.contains('mw-collapsed')) collapsedParents.push(node);
        }
        if (!collapsedParents.length) return;

        collapsedParents.forEach(function (el) {
            if (el.__mwCollapsible) el.__mwCollapsible.expand();
            else el.classList.remove('mw-collapsed');
        });
        target.scrollIntoView();
    }

    /**
     * 初始化自定义右键菜单（仅桌面端使用，替代 WebView 默认右键菜单）。
     * 菜单项包括：复制文本/图片、打开源码页面。
     * @param {object} config 配置对象
     */
    function initContextMenu(config) {
        const contextMenu = document.getElementById('custom-context-menu');
        if (!contextMenu) return;

        // 右键时记录目标元素与选中的文本，供菜单按钮使用
        let rightClickTarget = null;
        let rightClickSelectedText = "";

        // 点击菜单以外的区域时关闭菜单
        function handleGlobalClick(e) {
            if (!contextMenu.contains(e.target)) {
                hideMenu();
            }
        }

        // 隐藏菜单并解除所有临时监听器
        function hideMenu() {
            if (contextMenu.classList.contains('show-menu')) {
                contextMenu.classList.remove('show-menu');
                window.removeEventListener('scroll', hideMenu);
                window.removeEventListener('wheel', hideMenu);
                window.removeEventListener('resize', hideMenu);
                document.removeEventListener('click', handleGlobalClick);
            }
        }

        // 监听右键事件：拦截默认菜单，在鼠标位置弹出自定义菜单
        document.addEventListener('contextmenu', function (e) {
            e.preventDefault();
            rightClickTarget = e.target; // 记录右键点击的目标元素
            rightClickSelectedText = window.getSelection().toString().trim(); // 记录当前选中的文本
            contextMenu.classList.add('show-menu');

            // 计算菜单位置，防止超出窗口边界（留 5px 边距）
            const winWidth = window.innerWidth;
            const winHeight = window.innerHeight;
            const menuWidth = contextMenu.offsetWidth;
            const menuHeight = contextMenu.offsetHeight;

            let x = e.clientX;
            let y = e.clientY;

            if (x + menuWidth > winWidth) x = winWidth - menuWidth - 5;
            if (y + menuHeight > winHeight) y = winHeight - menuHeight - 5;

            contextMenu.style.left = `${x}px`;
            contextMenu.style.top = `${y}px`;

            // 下一帧再注册监听，避免本次右键的 click 事件立即触发 handleGlobalClick
            setTimeout(() => {
                window.addEventListener('scroll', hideMenu, { passive: true });
                window.addEventListener('wheel', hideMenu, { passive: true });
                window.addEventListener('resize', hideMenu, { passive: true });
                document.addEventListener('click', handleGlobalClick);
            }, 0);
        });

        // 事件委托：在菜单容器上只注册一次 click，按菜单项 id 分发处理
        contextMenu.addEventListener('click', (e) => {
            const item = e.target.closest('.menu-item');
            if (!item) return;

            switch (item.id) {
                // “复制”菜单项：优先复制选中的文本；右键目标是图片时复制图片
                case 'menu-copy':
                    if (rightClickSelectedText) {
                        callCSharpAsync("CopyTextToClipboard", rightClickSelectedText);
                    } else if (rightClickTarget && rightClickTarget.tagName === 'IMG') {
                        // 通过图片文件名让 C# 侧定位并复制图片
                        callCSharpAsync("CopyImageToClipboard", rightClickTarget.src.split('/').pop());
                    }
                    break;

                // “打开源码”菜单项：右键的是外链则打开该链接，否则打开当前词条的源码页面
                case 'menu-open-source': {
                    const aTag = rightClickTarget ? rightClickTarget.closest('a') : null;
                    let targetUrl = '';

                    if (aTag && aTag.href && aTag.href.startsWith('http')) {
                        targetUrl = aTag.href;
                    } else {
                        const title = window.pageTitle || config.homePage;
                        targetUrl = config.sourceUrl(title);
                    }

                    if (targetUrl) {
                        callCSharpAsync("OpenExternalWebsite", targetUrl);
                    }
                    break;
                }

                // “在新标签页打开”菜单项：
                // 站内词条链接 -> 新标签页打开该词条；外链 -> 系统浏览器打开；其余 -> 新标签页打开当前页
                case 'menu-new-tab': {
                    const aTag = rightClickTarget ? rightClickTarget.closest('a') : null;
                    const href = aTag ? (aTag.getAttribute('href') || '') : '';
                    const wikiTitle = aTag ? aTag.getAttribute('data-wiki') : null;

                    // 外链 -> 交给 C# 用系统浏览器打开
                    if (href.startsWith('http')) {
                        callCSharpAsync("OpenExternalWebsite", href);
                    } else if (wikiTitle && !href) {
                        // 带 data-wiki 属性的站内链接 -> 在新标签页打开该词条
                        const args = {
                            title: wikiTitle,
                            position: 0
                        };
                        callCSharpAsync("OpenInNewTab", JSON.stringify(args));
                    } else {
                        // 空白处右键、相对链接或锚点链接 -> 在新标签页打开当前页
                        const args = {
                            title: window.pageTitle,
                            position: window.pageYOffset
                        };
                        callCSharpAsync("OpenInNewTab", JSON.stringify(args));
                    }
                    break; // 统一由下方 hideMenu() 关闭菜单
                }


                default:
                    // 未识别的菜单项：不关闭菜单，交由自定义逻辑处理
                    return;
            }

            hideMenu();
        });
    }

    // 对外暴露入口，供页面脚本调用 window.wikiApp.start(config)
    window.wikiApp = {
        start,
        t
    };
})();