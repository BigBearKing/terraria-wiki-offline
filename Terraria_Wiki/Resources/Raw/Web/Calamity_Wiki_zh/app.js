// 1. 从网址中提取参数 (例如 ?theme=dark)
const urlParams = new URLSearchParams(window.location.search);
const initialTheme = urlParams.get('theme');
const isMobile=urlParams.get('isMobile');

// 2. 瞬间应用主题
if (initialTheme === "dark") {
    changTheme('True');
} else if (initialTheme === "light") {
    changTheme('False');
}

//监听操作
{
// 向外层父窗口汇报交互
    const notifyParent = () => {
        window.parent.postMessage('iframe_user_active', '*');
    };

    // 监听来自外层 MAUI Blazor 的命令
    window.addEventListener('message', (e) => {
        // 安全起见，如果在真实环境可以把 '*' 换成允许的域名
        
        if (e.data === 'start_iframe_monitor') {
            // 收到开启命令，挂载交互监听
            window.addEventListener('pointerdown', notifyParent);
            window.addEventListener('scroll', notifyParent, true);
            window.addEventListener('keydown', notifyParent);
        } 
        else if (e.data === 'stop_iframe_monitor') {
            // 收到关闭命令，卸载交互监听
            window.removeEventListener('pointerdown', notifyParent);
            window.removeEventListener('scroll', notifyParent, true);
            window.removeEventListener('keydown', notifyParent);
        }
    });
}

/*!
handy-scroll v2.0.6
https://amphiluke.github.io/handy-scroll/
(c) 2026 Amphiluke
*/
const t = new CSSStyleSheet; t.replaceSync(':host{bottom:0;min-height:17px;overflow:auto;position:fixed}.strut{height:1px;overflow:hidden;pointer-events:none;&:before{content:" "}}:host,.strut{font-size:1px;line-height:0;margin:0;padding:0}:host(:state(latent)){clip-path:inset(100%);.strut:before{content:"  "}}:host([viewport]:not([hidden])){display:block}:host([viewport]:not(:state(latent))){position:sticky}'); let e = t => `Attribute ‘${t}’ must reference a valid container ‘id’`; class i extends HTMLElement { static get observedAttributes() { return ["owner", "viewport", "hidden"] } #t = null; #e = null; #i = null; #s = null; #h = null; #n = null; #o = !0; #l = !0; get owner() { return this.getAttribute("owner") } set owner(t) { this.setAttribute("owner", t) } get viewport() { return this.getAttribute("viewport") } set viewport(t) { this.setAttribute("viewport", t) } get #r() { return this.#t.states.has("latent") } set #r(t) { this.#t.states[t ? "add" : "delete"]("latent") } constructor() { super(); let e = this.attachShadow({ mode: "open" }); e.adoptedStyleSheets = [t], this.#s = document.createElement("div"), this.#s.classList.add("strut"), e.appendChild(this.#s), this.#t = this.attachInternals() } connectedCallback() { this.#d(), this.#a(), this.#c(), this.#u(), this.update() } disconnectedCallback() { this.#w(), this.#p(), this.#i = this.#e = null } attributeChangedCallback(t) { if (this.#h) { if ("hidden" === t) return void (this.hasAttribute("hidden") || this.update()); "owner" === t ? this.#d() : "viewport" === t && this.#a(), this.#w(), this.#p(), this.#c(), this.#u(), this.update() } } #d() { let t = this.getAttribute("owner"); if (this.#i = document.getElementById(t), !this.#i) throw new DOMException(e("owner")) } #a() { if (!this.hasAttribute("viewport")) return void (this.#e = window); let t = this.getAttribute("viewport"); if (this.#e = document.getElementById(t), !this.#e) throw new DOMException(e("viewport")) } #c() { this.#h = new AbortController; let t = { signal: this.#h.signal }; this.#e.addEventListener("scroll", () => this.#f(), t), this.#e === window && this.#e.addEventListener("resize", () => this.update(), t), this.addEventListener("scroll", () => { this.#o && !this.#r && this.#g(), this.#o = !0 }, t), this.#i.addEventListener("scroll", () => { this.#l && this.#v(), this.#l = !0 }, t), this.#i.addEventListener("focusin", () => { setTimeout(() => { this.isConnected && this.#v() }, 0) }, t) } #w() { this.#h?.abort(), this.#h = null } #u() { this.#e !== window && (this.#n = new ResizeObserver(([t]) => { t.contentBoxSize?.[0]?.inlineSize && this.update() }), this.#n.observe(this.#e)) } #p() { this.#n?.disconnect(), this.#n = null } #g() { let { scrollLeft: t } = this; this.#i.scrollLeft !== t && (this.#l = !1, this.#i.scrollLeft = t) } #v() { let { scrollLeft: t } = this.#i; this.scrollLeft !== t && (this.#o = !1, this.scrollLeft = t) } #f() { let t = this.scrollWidth <= this.offsetWidth; if (!t) { let e = this.#i.getBoundingClientRect(), i = this.#e === window ? window.innerHeight || document.documentElement.clientHeight : this.#e.getBoundingClientRect().bottom; t = e.bottom <= i || e.top > i } this.#r !== t && (this.#r = t) } update() { let { clientWidth: t, scrollWidth: e } = this.#i, { style: i } = this; i.width = `${t}px`, this.#e === window && (i.left = `${this.#i.getBoundingClientRect().left}px`), this.#s.style.width = `${e}px`, e > t && (i.height = this.offsetHeight - this.clientHeight + 1 + "px"), this.#v(), this.#f() } } customElements.define("handy-scroll", i); export { i as default };



window.pageTitle = null; // 当前页面标题，初始为空
const handlers = {}; // 存 JS 方法
const pending = {};  // 存等待 C# 的 Promise


handlers["GotoPage"] = async (msg) => {
    gotoPage(msg);
    return null;
}
handlers["BackToPage"] = async (msg) => {
    const args = JSON.parse(msg);
    backToPage(args.title, args.position);
    return null;
}
handlers["BackHome"] = async () => {
    await redirect("首页")
    window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
    return null;
}

handlers["ToTop"] = () => {
    window.scrollTo({ top: 0, left: 0, behavior: 'smooth' });
    return null;
}

handlers["ChangeTheme"] = (isDarkTheme) => {

    changTheme(isDarkTheme)
    return null;
}

handlers["ClearPage"] = () => {

    document.getElementById("mw-content-text").innerHTML = "";
    return null;
}



// B. 调用 C# 方法
function callCSharpAsync(method, data) {
    return new Promise(resolve => {
        const id = Math.random().toString(36).substr(2);
        pending[id] = resolve;
        // 发消息给父级
        window.parent.postMessage({ type: 'req', id, method, data }, '*');
    });
}

// C. 监听消息
window.addEventListener('message', async e => {
    const msg = e.data;
    if (msg.type === 'res') {
        // C# 返回结果了
        if (pending[msg.id]) { pending[msg.id](msg.data); delete pending[msg.id]; }
    } else if (msg.type === 'req') {
        // C# 请求执行 JS
        let result = "";
        if (handlers[msg.method]) result = await handlers[msg.method](msg.data);
        // 回复 C#
        window.parent.postMessage({ type: 'res', id: msg.id, data: result }, '*');
    }
});

//点击事件
document.addEventListener('click', function (e) {
    // 1. 使用 closest('a') 查找最近的 a 标签祖先
    // 这样做是为了防止用户点击了 a 标签内部的 span 或 img，导致 e.target 不是 a 标签
    const targetLink = e.target.closest('a');

    // 2. 判断是否找到了 a 标签
    if (targetLink) {
        if (targetLink.closest("div.thumb")) {
            openThumb(targetLink);
            return;
        }
        const title = targetLink.getAttribute('title');
        const href = targetLink.getAttribute('href') || '';
        if (href.startsWith('http')) {
            e.preventDefault();
            callCSharpAsync("OpenExternalWebsite", href);
            return;
        }
        if (title && !href) {
            gotoPage(title);
        }
    }
});

//鼠标侧键
document.addEventListener('mouseup', function (e) {
    // e.button === 3 是侧键后退，e.button === 4 是侧键前进
    if (e.button === 3 || e.button === 4) {
        e.preventDefault();
        callCSharpAsync("WikiBackAsync", "");
    }
});


redirect("首页");



async function gotoPage(title) {
    const args = {
        title: window.pageTitle,
        position: window.pageYOffset
    };
    document.getElementById("loading-mask").style.display = "block";
    try {

        const titleWithAnchor = JSON.parse(await callCSharpAsync("GetRedirectedTitleAndAnchorAsync", title));

        if (await redirect(titleWithAnchor.title) == null) return;
        window.scrollTo({ top: 0, left: 0, behavior: 'instant' });
        if (titleWithAnchor.anchor) {
            const element = document.getElementById(titleWithAnchor.anchor);
            if (element) {
                element.scrollIntoView({ behavior: "smooth" });
            }
        }
    } finally {
        document.getElementById("loading-mask").style.display = "none";
    }




    callCSharpAsync("SaveToTempHistory", JSON.stringify(args))


}

async function backToPage(title, position) {
    if (await redirect(title) == null) return;
    window.scrollTo({ top: position, left: 0, behavior: 'instant' });
}

async function redirect(title) {
    const result = JSON.parse(await callCSharpAsync("PageRedirectAsync", title));
    if (result == null) return null;
    window.pageTitle = result.title;
    document.getElementById("firstHeading-h1").textContent = result.title;
    document.getElementById("mw-content-text").innerHTML = result.content;
    document.getElementById("footer-info-lastmod").textContent = "此页面最后编辑于 " + result.lastModified;
    if (title == "Terraria Wiki") {
        document.body.classList.add("rootpage-Terraria_Wiki");
        document.getElementById("firstHeading").setAttribute("style", "display:none");
    } else {
        document.body.classList.remove("rootpage-Terraria_Wiki");
        document.getElementById("firstHeading").removeAttribute("style");
    }
    refresh();
    return true;
}

function openThumb(thumb) {
    const img = thumb.querySelector('img');
    if (!img) return;

    // 每次点击实例化一个 Viewer
    const viewer = new Viewer(img, {
        inline: false,       // 模态框全屏模式
        button: true,        // 显示右上角关闭按钮
        navbar: false,       // 隐藏底部的缩略图导航栏 (单图不需要)
        title: true,        // 隐藏图片标题
        toolbar: false,       // 显示底部的放大/缩小/复原等工具栏
        backdrop: true,      // 点击黑色背景关闭
        zoomRatio: 0.3,      // 滚轮缩放的灵敏度
        hidden: function () {
            // 当模态框完全隐藏后，销毁实例释放内存
            viewer.destroy();
        },

    });

    // 主动触发显示
    viewer.show();
}

function changTheme(isDarkTheme) {
    if (isDarkTheme == "True") {
        document.documentElement.classList.remove("light");
        document.documentElement.classList.add("dark");
    } else {
        document.documentElement.classList.remove("dark");
        document.documentElement.classList.add("light");
    }
}


// 自定义右键菜单逻辑

function initContextMenu() {
    const contextMenu = document.getElementById('custom-context-menu');
    if (!contextMenu) return; // 安全检查

    let rightClickTarget = null;
    let rightClickSelectedText = "";

    // --- 处理全局点击事件的具名函数 ---
    function handleGlobalClick(e) {
        // 如果点击的不是菜单内部，则关闭菜单
        if (!contextMenu.contains(e.target)) {
            hideMenu();
        }
    }

    // --- 提取公共的隐藏菜单方法 ---
    function hideMenu() {
        if (contextMenu.classList.contains('show-menu')) {
            contextMenu.classList.remove('show-menu');

            // 💡 核心优化：菜单关闭时，立即注销所有高频监听器
            window.removeEventListener('scroll', hideMenu);
            window.removeEventListener('wheel', hideMenu);
            window.removeEventListener('resize', hideMenu);
            document.removeEventListener('click', handleGlobalClick);
        }
    }

    // 1. 监听全局右键事件
    document.addEventListener('contextmenu', function (e) {
        e.preventDefault();
        rightClickTarget = e.target;
        rightClickSelectedText = window.getSelection().toString().trim();
        // 显示菜单以获取尺寸
        contextMenu.classList.add('show-menu');

        const winWidth = window.innerWidth;
        const winHeight = window.innerHeight;
        const menuWidth = contextMenu.offsetWidth;
        const menuHeight = contextMenu.offsetHeight;

        let x = e.clientX;
        let y = e.clientY;

        // 边缘碰撞检测
        if (x + menuWidth > winWidth) x = winWidth - menuWidth - 5;
        if (y + menuHeight > winHeight) y = winHeight - menuHeight - 5;

        contextMenu.style.left = `${x}px`;
        contextMenu.style.top = `${y}px`;

        // 💡 核心优化：只有在菜单真正打开时，才挂载高频监听器
        // 使用 setTimeout 是为了跳过当前的事件冒泡流，防止误触发 click 导致菜单瞬间关闭
        setTimeout(() => {
            window.addEventListener('scroll', hideMenu, { passive: true });
            window.addEventListener('wheel', hideMenu, { passive: true });
            window.addEventListener('resize', hideMenu, { passive: true });
            document.addEventListener('click', handleGlobalClick);
        }, 0);
    });

    // ==========================================
    // 菜单按钮本身的点击逻辑 (保持绑定一次即可)
    // ==========================================

    // 3. 复制逻辑
    const btnCopy = document.getElementById('menu-copy');
    if (btnCopy) {
        btnCopy.addEventListener('click', () => {

            if (rightClickSelectedText) {
                callCSharpAsync("CopyTextToClipboard", rightClickSelectedText);
            }
            else if (rightClickTarget && rightClickTarget.tagName === 'IMG') {
                // 如果没有文字，再判断是不是图片
                callCSharpAsync("CopyImageToClipboard", rightClickTarget.src.split('/').pop());
            }

            hideMenu(); // 调用 hideMenu 会自动清理那 4 个高频监听器
        });
    }

    // 4. 打开原文逻辑
    const btnOpenSource = document.getElementById('menu-open-source');
    if (btnOpenSource) {
        btnOpenSource.addEventListener('click', () => {
            const aTag = rightClickTarget ? rightClickTarget.closest('a') : null;
            let targetUrl = '';

            if (aTag && aTag.href && aTag.href.startsWith('http')) {
                targetUrl = aTag.href;
            } else {
                const title = window.pageTitle || "Terraria Wiki";
                targetUrl = "https://terraria.wiki.gg/zh/wiki/" + encodeURIComponent(title.replace(/ /g, "_"));
            }

            if (targetUrl) {
                callCSharpAsync("OpenExternalWebsite", targetUrl);
            }

            hideMenu(); // 调用 hideMenu 会自动清理那 4 个高频监听器
        });
    }
}

if(isMobile==="False"){
    initContextMenu();
}



function refresh() {

    // ============================================================
    // 1 & 2. Handle Wide Tables (宽表格处理 + 滚动条)
    // 原理：检测表格宽度，如果超出容器，就包裹一个 div 让它横向滚动
    // ============================================================

    function initHandyScrollForTables(containerSelector = '#bodyContent') {
        const TABLE_WIDE_CLASS = 'table-wide';
        const TABLE_WIDE_INNER_CLASS = 'table-wide-inner';

        // 防抖函数
        const debounce = (func, wait) => {
            let timeout;
            return function (...args) {
                clearTimeout(timeout);
                timeout = setTimeout(() => func.apply(this, args), wait);
            };
        };

        const processWideTables = () => {
            const containerEl = document.querySelector(containerSelector);
            if (!containerEl) return;

            const tables = containerEl.querySelectorAll('table');
            if (tables.length === 0) return;

            tables.forEach((table) => {
                if (!table._originalContainer) {
                    table._originalContainer = table.parentNode;
                }
                const originalContainer = table._originalContainer;
                if (!originalContainer) return;

                // 检查是否已包装
                const isWrapped = table.parentNode && table.parentNode.classList.contains(TABLE_WIDE_INNER_CLASS);
                const innerBox = isWrapped ? table.parentNode : null;
                const outerBox = isWrapped ? innerBox.parentNode : null;

                // 测量宽度
                const overwide = table.getBoundingClientRect().width > originalContainer.getBoundingClientRect().width;

                if (isWrapped) {
                    if (overwide) {
                        // 表格依然过宽：找到对应的 custom element 并调用官方的 .update()
                        const handyComponent = outerBox.querySelector('handy-scroll');
                        if (handyComponent && typeof handyComponent.update === 'function') {
                            handyComponent.update();
                        }
                    } else {
                        // 宽度足够了，不需要滚动条：解包并移除 custom element
                        outerBox.parentNode.insertBefore(table, outerBox);
                        outerBox.remove();
                    }
                } else {
                    if (overwide) {
                        // 需要生成滚动条：创建包装层和自定义标签
                        const newOuter = document.createElement('div');
                        newOuter.className = TABLE_WIDE_CLASS;

                        const newInner = document.createElement('div');
                        newInner.className = TABLE_WIDE_INNER_CLASS;

                        // Web Component 需要通过 ID 来绑定目标容器
                        // 我们给内层容器生成一个唯一的 ID
                        const uniqueId = 'scroll-inner-' + Math.random().toString(36).substring(2, 9);
                        newInner.id = uniqueId;

                        // 组装 DOM
                        table.parentNode.insertBefore(newOuter, table);
                        newInner.appendChild(table);
                        newOuter.appendChild(newInner);

                        // 创建 <handy-scroll> 自定义标签
                        const handyComponent = document.createElement('handy-scroll');
                        // 绑定 owner 属性到刚才生成的内部容器 ID
                        handyComponent.setAttribute('owner', uniqueId);

                        // 将组件放到包裹层内（位于滚动容器后面）
                        newOuter.appendChild(handyComponent);
                    }
                }
            });
        };

        // 立即执行一次
        processWideTables();

        // 绑定 resize 事件
        if (!initHandyScrollForTables._resizeBound) {
            window.addEventListener('resize', debounce(processWideTables, 100));
            initHandyScrollForTables._resizeBound = true;
        }
    }
    initHandyScrollForTables();

    // ============================================================
    // 3. Mobile Floating Fix (移动端浮动修复)
    // 原理：屏幕小的时候，强制取消图片的 float:right，防止挤压文字
    // ============================================================
    function fixMobileFloating() {
        const contentBox = document.querySelector('.mw-parser-output') || document.body;
        const fullWidth = contentBox.offsetWidth;

        // 获取所有可能是侧边栏或浮动图片的元素
        const elements = contentBox.querySelectorAll('.infobox, .tright, .floatright, figure[class*="float-right"]');

        elements.forEach(el => {
            el.classList.remove('mobile-floating-fix'); // 先重置

            if (fullWidth <= 720) {
                // 如果是小屏幕，强制添加修复类
                // 这里的逻辑简化了原版复杂的 offset 计算，直接针对小屏全宽处理
                el.classList.add('mobile-floating-fix');
            }
        });
    }
    // 初始化和调整窗口时执行
    fixMobileFloating();
    window.addEventListener('resize', () => {
        // 简单的防抖 (debounce)
        clearTimeout(window.resizeTimer);
        window.resizeTimer = setTimeout(fixMobileFloating, 200);
    });

    // ============================================================
    // 4. Template:Sound (音频播放控制)
    // ============================================================
    const sounds = document.querySelectorAll('.sound');
    sounds.forEach(container => {
        container.style.cursor = 'pointer';
        container.title = '点击播放';

        const audio = container.querySelector('audio');
        if (!audio) return;

        // ✅ 新增：监听当前音频自然播放结束的事件
        audio.addEventListener('ended', function () {
            container.classList.remove('sound-playing');
            container.title = '点击播放';
            audio.currentTime = 0; // 将进度条重置回开头
        });

        container.addEventListener('click', function (e) {
            if (e.target.tagName === 'A') return;

            // 1. 停止页面上所有其他正在播放的音频
            document.querySelectorAll('audio').forEach(otherAudio => {
                if (otherAudio !== audio && !otherAudio.paused) {
                    otherAudio.pause();
                    otherAudio.currentTime = 0;
                    otherAudio.closest('.sound')?.classList.remove('sound-playing');
                }
            });

            // 2. 切换当前音频状态
            if (audio.paused) {
                audio.play();
                this.classList.add('sound-playing');
                this.title = '点击停止';
            } else {
                audio.pause();
                audio.currentTime = 0;
                this.classList.remove('sound-playing');
                this.title = '点击播放';
            }
        });
    });

    // ============================================================
    // 5. 目录生成
    // ============================================================

    let tocObserver = null; // 目录滚动监听实例

    function initTocScrollSpy(tocList, articleContent) {
        // 先销毁旧的 observer，避免页面切换后累积
        if (tocObserver) {
            tocObserver.disconnect();
            tocObserver = null;
        }

        // 收集所有标题
        const allHeadings = articleContent.querySelectorAll('h2, h3');
        if (allHeadings.length === 0) return;

        // 建立标题 → toc LI 的映射
        const headingToLi = new Map();
        const tocItems = tocList.querySelectorAll('li.toclevel-1, li.toclevel-2');
        tocItems.forEach((li, i) => {
            if (i < allHeadings.length) headingToLi.set(allHeadings[i], li);
        });

        // 建立 h2 → 其子列表 ul 的映射
        const h2headings = articleContent.querySelectorAll('h2');
        const h2ToSubUl = new Map();
        tocList.querySelectorAll('li.toclevel-1').forEach((li, i) => {
            if (i < h2headings.length) {
                const subUl = li.querySelector('ul');
                if (subUl) h2ToSubUl.set(h2headings[i], subUl);
            }
        });

        let lastActive = null;

        tocObserver = new IntersectionObserver((entries) => {
            // 找到最靠上的、在视口中的标题
            let topHeading = null;
            let topY = Infinity;
            for (const e of entries) {
                if (e.isIntersecting && e.boundingClientRect.top < topY) {
                    topY = e.boundingClientRect.top;
                    topHeading = e.target;
                }
            }

            // 切换 active
            if (topHeading) {
                const newLi = headingToLi.get(topHeading);
                if (newLi && newLi !== lastActive) {
                    if (lastActive) lastActive.classList.remove('active');
                    newLi.classList.add('active');
                    lastActive = newLi;
                }
            }

            // h3 子列表展开/收起
            for (const [h2, subUl] of h2ToSubUl) {
                const rect = h2.getBoundingClientRect();
                subUl.classList.toggle('toc-sub-visible',
                    rect.top < window.innerHeight && rect.bottom > 0);
            }
        }, { rootMargin: '-10% 0px', threshold: 0 });

        allHeadings.forEach(h => tocObserver.observe(h));
    }

    function generateToc() {
        // 0. 删除文章内容里自带的旧目录 (避免重复 #toc 干扰)
        const oldToc = document.querySelector('#mw-content-text #toc');
        if (oldToc) oldToc.remove();

        // 1. 获取基础 DOM 节点
        const tocList = document.querySelector('#toc .toc-ul-wrap ul');
        const articleContent = document.getElementById('mw-content-text');

        // 防错处理：如果页面上没有目录容器或文章容器，直接退出
        if (!tocList || !articleContent) return;

        // 清空已有的目录条目，避免页面切换后累积
        tocList.innerHTML = '';

        const tocSidebar = document.getElementById('toc');

        // 2. 抓取文章中所有的 h2 和 h3 标题（保持文档中的先后顺序）
        const headings = articleContent.querySelectorAll('h2, h3');

        // 没有标题就隐藏目录栏
        if (headings.length === 0) {
            tocSidebar.classList.add('toc-hidden');
            return;
        }
        tocSidebar.classList.remove('toc-hidden');

        let h2Count = 0;
        let h3Count = 0;
        let currentH2Li = null;
        let currentSubUl = null;

        // 3. 循环遍历所有标题
        headings.forEach(function (heading) {
            let id = heading.id;

            // 如果标题没有写 id，自动帮它生成一个随机 id 供锚点跳转
            if (!id) {
                id = 'toc-' + Math.random().toString(36).substring(2, 7);
                heading.id = id;
            }

            const text = heading.textContent;
            const tagName = heading.tagName.toLowerCase();

            // 4. 处理主标题 (H2)
            if (tagName === 'h2') {
                h2Count++;
                h3Count = 0; // 遇到新的 h2，重置 h3 的计数

                // 创建一级目录的 li 和 a 标签
                currentH2Li = document.createElement('li');
                currentH2Li.className = 'toclevel-1';

                const link = document.createElement('a');
                link.setAttribute('href', '#' + id);

                // 创建序号 span
                const numberSpan = document.createElement('span');
                numberSpan.className = 'tocnumber';
                numberSpan.textContent = h2Count + ' ';

                // 创建文本 span
                const textSpan = document.createElement('span');
                textSpan.className = 'toctext';
                textSpan.textContent = text;

                // 组装并塞入大目录
                link.appendChild(numberSpan);
                link.appendChild(textSpan);
                currentH2Li.appendChild(link);
                tocList.appendChild(currentH2Li);

                currentSubUl = null; // 重置子列表引用
            }
            // 5. 处理子标题 (H3)
            else if (tagName === 'h3' && currentH2Li) {
                h3Count++;

                // 如果当前的 H2 下面还没有子列表容器 (ul)，就帮它建一个
                if (!currentSubUl) {
                    currentSubUl = document.createElement('ul');
                    currentSubUl.className = 'nav nav-list';
                    currentH2Li.appendChild(currentSubUl);
                }

                // 创建二级目录的 li 和 a 标签
                const h3Li = document.createElement('li');
                h3Li.className = 'toclevel-2';

                const link = document.createElement('a');
                link.setAttribute('href', '#' + id);

                // 创建二级序号（形如 2.1）
                const numberSpan = document.createElement('span');
                numberSpan.className = 'tocnumber';
                numberSpan.textContent = h2Count + '.' + h3Count + ' ';

                // 创建二级文本
                const textSpan = document.createElement('span');
                textSpan.className = 'toctext';
                textSpan.textContent = text;

                // 组装并塞入二级子列表
                link.appendChild(numberSpan);
                link.appendChild(textSpan);
                h3Li.appendChild(link);
                currentSubUl.appendChild(h3Li);
            }
        });

        // 6. 滚动监听：只在当前 h2 区域展开其 h3 子列表
        initTocScrollSpy(tocList, articleContent);

    };
    generateToc();

}

