// 1. 从网址中提取参数 (例如 ?theme=dark)
const urlParams = new URLSearchParams(window.location.search);
const initialTheme = urlParams.get('theme');

// 2. 瞬间应用主题
if (initialTheme === "dark") {
    changTheme('True');
} else if (initialTheme === "light") {
    changTheme('False');
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
    await redirect("Terraria Wiki")
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


redirect("Terraria Wiki");



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
    document.getElementById("firstHeading").textContent = result.title;
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
    let rightClickTarget = null;

    // --- 提取公共的隐藏菜单方法 ---
    function hideMenu() {
        if (contextMenu.classList.contains('show-menu')) {
            contextMenu.classList.remove('show-menu');
            window.removeEventListener('scroll', hideMenu);
        window.removeEventListener('wheel', hideMenu);
        }
    }

    // 1. 监听全局右键事件
    document.addEventListener('contextmenu', function (e) {
        contextMenu.classList.add('show-menu');
        e.preventDefault();
        rightClickTarget = e.target;

        const winWidth = window.innerWidth;
        const winHeight = window.innerHeight;
        let x = e.clientX;
        let y = e.clientY;

        contextMenu.classList.add('show-menu');
        const menuWidth = contextMenu.offsetWidth;
        const menuHeight = contextMenu.offsetHeight;

        // 边缘碰撞检测
        if (x + menuWidth > winWidth) x = winWidth - menuWidth - 5;
        if (y + menuHeight > winHeight) y = winHeight - menuHeight - 5;

        contextMenu.style.left = `${x}px`;
        contextMenu.style.top = `${y}px`;
        // 2. 任何点击（除了点击菜单本身）都会隐藏菜单
        document.addEventListener('click', function (e) {
            if (!contextMenu.contains(e.target)) {
                hideMenu();
            }
        });

        // --- 新增：捕捉用户的其他所有操作来隐藏菜单 ---
        // 监听页面滚动
        window.addEventListener('scroll', hideMenu, { passive: true });
        // 监听鼠标滚轮 (即使页面没有滚动条，滑动滚轮也会触发)
        window.addEventListener('wheel', hideMenu, { passive: true });
        // 监听窗口大小改变
        window.addEventListener('resize', hideMenu, { passive: true });

        // 3. 复制逻辑：判断选中的是文字还是图片
        document.getElementById('menu-copy').addEventListener('click', () => {
            const selectedText = window.getSelection().toString().trim();

            if (selectedText) {
                callCSharpAsync("CopyTextToClipboard", selectedText);
                console.log("复制文字: " + selectedText);
            }
            else if (rightClickTarget && rightClickTarget.tagName === 'IMG') {
                callCSharpAsync("CopyImageToClipboard", rightClickTarget.src);
                console.log("复制图片: " + rightClickTarget.src);
            }

            hideMenu();
        });

        // 4. 打开原文逻辑
        document.getElementById('menu-open-source').addEventListener('click', () => {
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
                console.log("打开原文: " + targetUrl);
            }

            hideMenu();
        });

    });

}

// 初始化
initContextMenu();




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
    // 5. NPC/Item Infobox Mode Switch (模式切换 Tab)
    // 原理：点击 Tab，切换父容器的 class (c-normal/c-expert/c-master)
    // ============================================================
    const tabs = document.querySelectorAll('.modesbox .modetabs .tab');
    tabs.forEach(tab => {
        tab.addEventListener('click', function () {
            // 1. 移除兄弟节点的 current 类
            const siblings = this.parentElement.children;
            for (let sib of siblings) {
                sib.classList.remove('current');
            }
            // 2. 自己加上 current
            this.classList.add('current');

            // 3. 找到最近的父容器 .modesbox
            const box = this.closest('.modesbox');
            if (!box) return;

            // 4. 切换父容器的 class
            box.classList.remove('c-normal', 'c-expert', 'c-master');

            if (this.classList.contains('normal')) {
                box.classList.add('c-normal');
            } else if (this.classList.contains('expert')) {
                box.classList.add('c-expert');
            } else if (this.classList.contains('master')) {
                box.classList.add('c-master');
            }
        });
    });

    // ============================================================
    // 6. 首页切换显示
    // ============================================================

    if (document.querySelector('#box-wikiheader-toggle-link')) {
        const toggleBtn = document.querySelector('#box-wikiheader #box-wikiheader-toggle-link');
        const wikiHeader = document.querySelector('#box-wikiheader');
        const content = document.querySelector('#content');

        if (!toggleBtn || !wikiHeader || !content) return;

        // 防止重复绑定
        if (toggleBtn.dataset.toggleBound === 'true') return;
        toggleBtn.dataset.toggleBound = 'true';

        // 原生防抖函数
        function debounce(func, wait) {
            let timeout;
            return function () {
                const context = this, args = arguments;
                clearTimeout(timeout);
                timeout = setTimeout(() => func.apply(context, args), wait);
            };
        }

        // 更新头部状态逻辑
        function updateHeaderState() {
            const width = content.offsetWidth;

            // 对应 CSS 中的 .collapsable 逻辑
            if (width < 1300) {
                wikiHeader.classList.add('collapsable');
            } else {
                wikiHeader.classList.remove('collapsable');
            }

            // 对应 CSS 中的 .collapsed 逻辑
            if (width < 730) {
                wikiHeader.classList.add('collapsed');
            } else {
                wikiHeader.classList.remove('collapsed');
            }
        }

        // 初始化
        updateHeaderState();

        // 监听窗口缩放
        window.addEventListener('resize', debounce(updateHeaderState, 200));

        // 点击展开/折叠按钮
        toggleBtn.addEventListener('click', function () {
            console.log('Toggle wiki header');
            wikiHeader.classList.toggle('collapsed');
        });
    }

    // ============================================================
    // 7.表格展开和折叠功能
    // ============================================================
    function initToggleBox() {
        // 1. 全局事件委托 (防止多次调用此函数时重复绑定)
        if (!window._toggleBoxInitialized) {
            document.addEventListener('click', function (event) {
                const handle = event.target.closest('.trw-togglehandle');
                if (handle) {
                    const toggleable = handle.closest('.trw-toggleable');
                    if (toggleable) {
                        toggleable.classList.toggle('toggled');
                        toggleable.classList.toggle('not-toggled');
                    }
                }
            });
            window._toggleBoxInitialized = true; // 标记为已初始化
        }

        // 2. 处理 URL 锚点 (Hash) 自动展开
        const anchor = window.location.hash.substring(1);
        if (anchor) {
            const targetId = decodeURI(anchor).replaceAll(' ', '_');
            const target = document.getElementById(targetId);

            if (target) {
                let parent = target.parentElement;
                while (parent) {
                    if (parent.matches('.trw-toggleable.trw-toggled-with-anchor')) {
                        // 对于锚点定位，确保强制切换到展开状态
                        parent.classList.add('toggled');
                        parent.classList.remove('not-toggled');
                    }
                    parent = parent.parentElement;
                }
            }
        }
    }
    initToggleBox();

    // ============================================================
    // 8. 表格头尾处理
    // ============================================================
    function emulateTHeadAndFoot(table) {
        // 确保传入的是一个 DOM 元素
        if (!table || table.tagName.toLowerCase() !== 'table') return;

        // 获取 table 的直接子元素 tbody 里的所有直接子元素 tr
        const tbody = table.querySelector(':scope > tbody') || table;
        const rows = Array.from(tbody.querySelectorAll(':scope > tr'));

        // 1. 处理 Thead
        if (!table.tHead) {
            const thead = document.createElement('thead');
            for (let row of rows) {
                // 如果这一行里面包含了 td，说明表头结束，退出循环
                if (row.querySelector('td')) {
                    break;
                }
                // 否则（全是 th），将其移动到 thead 中
                thead.appendChild(row);
            }

            // 如果成功提取到了表头行，将其插入到 tbody 的前面
            if (thead.children.length > 0) {
                table.insertBefore(thead, tbody);
            }
        }

        // 2. 处理 Tfoot
        if (!table.tFoot) {
            const tfoot = document.createElement('tfoot');
            let tfootRows = [];
            let remainingCellRowSpan = 0;

            // 重新遍历所有行（注意：刚刚被移走变成 thead 的行不在 tbody 里了）
            const remainingRows = Array.from(tbody.querySelectorAll(':scope > tr'));

            for (let row of remainingRows) {
                const cells = row.querySelectorAll('td');

                for (let cell of cells) {
                    // 原生 DOM 属性 rowSpan，如果没有显式设置通常为 1
                    remainingCellRowSpan = Math.max(cell.rowSpan, remainingCellRowSpan);
                }

                if (remainingCellRowSpan > 0) {
                    // 如果还有剩余的 rowSpan 没消耗完，说明当前的行仍然和上面的数据行相连，不能做表尾
                    tfootRows = [];
                    remainingCellRowSpan--;
                } else {
                    // 如果当前行完全没有受到上面 rowSpan 的影响，暂时将其视为表尾的候选行
                    tfootRows.push(row);
                }
            }

            // 如果收集到了符合条件的表尾行，将它们追加到 tfoot
            if (tfootRows.length > 0) {
                for (let row of tfootRows) {
                    tfoot.appendChild(row);
                }
                table.appendChild(tfoot);
            }
        }
    }
    document.querySelectorAll('table').forEach(table => {
        emulateTHeadAndFoot(table);
    });
}

