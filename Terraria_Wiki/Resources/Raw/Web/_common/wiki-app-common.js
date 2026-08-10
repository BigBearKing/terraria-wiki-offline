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

    /**
     * 开始一次新的导航：隐藏加载遮罩并使导航版本号自增。
     * @returns {number} 本次导航的版本号
     */
    function beginNavigation() {
        const loadingMask = document.getElementById("loading-mask");
        if (loadingMask) {
            loadingMask.style.display = "none";
        }

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
    function start(config) {
        // 从 URL 查询参数中读取初始主题与设备类型
        const urlParams = new URLSearchParams(window.location.search);
        const initialTheme = urlParams.get('theme');
        const isMobile = urlParams.get('isMobile');

        // 应用初始主题（若 URL 中指定了 theme 参数）
        if (initialTheme === "dark") {
            changeTheme('True');
        } else if (initialTheme === "light") {
            changeTheme('False');
        }

        ensureCommonUI(); // 注入公共 UI（加载遮罩、右键菜单结构、公共样式）

        window.pageTitle = null; // 当前页面标题，初始为空
        registerHandlers(config); // 注册 C# -> JS 的消息处理器
        bindNavigation(config);   // 绑定链接点击、鼠标侧键等导航交互

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
    function registerHandlers(config) {
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
            // 显示加载遮罩，避免内容替换瞬间的视觉跳跃
            const loadingMask = document.getElementById("loading-mask");
            if (loadingMask) {
                loadingMask.style.display = "block";
            }
            try {
                await redirect(config.homePage, config);
                window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
            } finally {
                if (loadingMask) {
                    loadingMask.style.display = "none";
                }
            }
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
        const loadingMask = document.getElementById("loading-mask");
        // 显示加载遮罩（仅当仍是当前导航时）
        if (loadingMask && isCurrentNavigation(navigationId)) {
            loadingMask.style.display = "block";
        }

        try {
            // 向 C# 查询：标题可能被重定向（如别名 -> 正式名），还可能带 #锚点
            const titleWithAnchor = JSON.parse(await callCSharpAsync("GetRedirectedTitleAndAnchorAsync", title));
            if (!isCurrentNavigation(navigationId)) return;

            // 渲染重定向后的页面；若返回 null 说明导航已失效则中止
            if (await redirect(titleWithAnchor.title, config, navigationId) == null || !isCurrentNavigation(navigationId)) return;
            // 等待页面内所有图片加载完成，确保滚动定位基于最终布局高度
            await waitForImages(document.getElementById("mw-content-text"));
            if (!isCurrentNavigation(navigationId)) return;
            // 跳到页面顶部
            window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
            // 若带锚点，则平滑滚动到对应元素
            if (titleWithAnchor.anchor) {
                const element = document.getElementById(titleWithAnchor.anchor);
                if (element) {
                    element.scrollIntoView({ behavior: "smooth" });
                }
            }
        } finally {
            // 无论成功与否，隐藏加载遮罩
            if (loadingMask && isCurrentNavigation(navigationId)) {
                loadingMask.style.display = "none";
            }
        }

        // 导航仍有效时，把上一页信息存入临时历史
        if (isCurrentNavigation(navigationId)) {
            await callCSharpAsync("SaveToTabHistory", JSON.stringify(args));
        }
    }

    /**
     * 等待指定容器内的所有图片加载完成（成功或失败均视为完成），
     * 确保页面布局高度已稳定后再进行滚动定位。
     * @param {HTMLElement} root 需要等待图片的容器
     * @returns {Promise<void>}
     */
    function waitForImages(root) {
        const images = root.querySelectorAll('img');
        const pending = Array.from(images).map((img) => {
            // 已加载完成（含加载失败的空图）直接视为完成
            if (img.complete) return Promise.resolve();
            // 强制立即加载，防止 loading="lazy" 的图片在视口外不加载导致等待挂起
            img.loading = 'eager';
            return new Promise((resolve) => {
                img.addEventListener('load', resolve, { once: true });
                img.addEventListener('error', resolve, { once: true });
                // 超时兜底，避免异常情况下永久挂起
                setTimeout(resolve, 5000);
            });
        });
        return Promise.all(pending);
    }

    /**
     * 返回到历史记录中的某一页：渲染该页并恢复其滚动位置。
     * @param {string} title 目标词条标题
     * @param {number} position 需要恢复的滚动位置（像素）
     * @param {object} config 配置对象
     * @param {number} [navigationId] 导航版本号
     */
    async function backToPage(title, position, config, navigationId = beginNavigation()) {
        // 显示加载遮罩：页面内容替换与滚动定位在遮罩下完成，避免视觉跳跃
        const loadingMask = document.getElementById("loading-mask");
        if (loadingMask && isCurrentNavigation(navigationId)) {
            loadingMask.style.display = "block";
        }

        try {
            if (await redirect(title, config, navigationId) == null || !isCurrentNavigation(navigationId)) return;
            // 等待页面内所有图片加载完成，确保滚动位置对应最终布局高度
            await waitForImages(document.getElementById("mw-content-text"));
            if (!isCurrentNavigation(navigationId)) return;
            // 恢复滚动位置（瞬时滚动，不带动画）
            window.scrollTo({ top: position, left: 0, behavior: 'instant' });
        } finally {
            // 新页面内容与滚动位置全部就绪后再隐藏遮罩，避免“跳一下”
            if (loadingMask && isCurrentNavigation(navigationId)) {
                loadingMask.style.display = "none";
            }
        }
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
        document.getElementById("footer-info-lastmod").textContent = config.lastModifiedPrefix + result.lastModified;

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
     * 原先这些内容（加载遮罩、自定义右键菜单的 HTML 结构，以及滚动条、
     * Viewer 动画、右键菜单等公共 CSS）在各站点的 index.html 中重复出现，
     * 现统一由本函数动态创建，保证所有页面外观与行为一致。
     */
    function ensureCommonUI() {
        // ---- 加载遮罩：页面切换时用于遮挡内容替换过程 ----
        if (!document.getElementById("loading-mask")) {
            const loadingMask = document.createElement("div");
            loadingMask.id = "loading-mask";
            loadingMask.style.cssText =
                "display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; z-index: 99999; -webkit-backdrop-filter: blur(150px); backdrop-filter: blur(150px);";
            document.body.appendChild(loadingMask);
        }

        // ---- 自定义右键菜单结构（仅桌面端使用，initContextMenu 中绑定行为）----
        if (!document.getElementById("custom-context-menu")) {
            const menu = document.createElement("div");
            menu.id = "custom-context-menu";
            menu.className = "hidden-menu";

            const copyItem = document.createElement("div");
            copyItem.className = "menu-item";
            copyItem.id = "menu-copy";
            copyItem.textContent = "复制";

            const openNewTabItem = document.createElement("div");
            openNewTabItem.className = "menu-item";
            openNewTabItem.id = "menu-new-tab";
            openNewTabItem.textContent = "在新标签页打开";

            const openSourceItem = document.createElement("div");
            openSourceItem.className = "menu-item";
            openSourceItem.id = "menu-open-source";
            openSourceItem.textContent = "打开原文";

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
            `;
            document.head.appendChild(style);
        }
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
        start
    };
})();