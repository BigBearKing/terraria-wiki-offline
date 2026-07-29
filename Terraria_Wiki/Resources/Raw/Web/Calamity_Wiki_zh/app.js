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
                // 如果是 <a class="image" data-wiki="File:..."> 内含 <img>，直接打开图片
        if (targetLink.classList.contains('image') && targetLink.querySelector('img')) {
            e.preventDefault();
            openThumb(targetLink);
            return;
        }
        const wikiTitle = targetLink.getAttribute('data-wiki');
        const href = targetLink.getAttribute('href') || '';
        if (href.startsWith('http')) {
            e.preventDefault();
            callCSharpAsync("OpenExternalWebsite", href);
            return;
        }
        if (wikiTitle && !href) {
            gotoPage(wikiTitle);
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
    if (title == "首页") {
        document.getElementById("firstHeading").setAttribute("style", "display:none");
    } else {
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
                const title = window.pageTitle || "首页";
                targetUrl = "https://calamity.huijiwiki.com/wiki/" + encodeURIComponent(title.replace(/ /g, "_"));
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



// ============================================================
// ScrollSpy — Bootstrap 3.2.0 兼容纯 JS 实现
// 作用：根据页面滚动位置，自动高亮侧边栏目录的当前章节
// 用法：new ScrollSpy(document.body, { target: '#toc .toc-ul-wrap', offset: 10 })
// ============================================================
class ScrollSpy {
    constructor(element, options) {
        // body 上初始化时实际监听 window
        this.$scrollElement = element === document.body ? window : element;
        this.options = Object.assign({ offset: 10 }, options);
        // 选择器：target 容器内 .nav li > a
        this.selector = (this.options.target || '') + ' .nav li > a';
        this.offsets = [];
        this.targets = [];
        this.activeTarget = null;
        this.scrollHeight = 0;

        this._process = this.process.bind(this);
        this.$scrollElement.addEventListener('scroll', this._process, { passive: true });
        this.refresh();
        this.process();
    }

    getScrollHeight() {
        return Math.max(
            document.body.scrollHeight,
            document.documentElement.scrollHeight
        );
    }

    refresh() {
        const self = this;
        const offsetBase = this.$scrollElement === window ? 0 : this.$scrollElement.scrollTop;

        this.offsets = [];
        this.targets = [];
        this.scrollHeight = this.getScrollHeight();

        // 遍历目录中所有 a[href^=#]，找到对应页面标题元素，记录其 top offset
        const targetList = [];
        const links = document.querySelectorAll(this.selector);

        links.forEach(function (link) {
            const href = link.getAttribute('href');
            if (!href || !/^#./.test(href)) return;
            const targetEl = document.querySelector(href);
            // 只考虑可见的标题元素
            if (!targetEl || targetEl.offsetParent === null) return;

            const rect = targetEl.getBoundingClientRect();
            const top = rect.top + (self.$scrollElement === window ? window.pageYOffset : self.$scrollElement.scrollTop) + offsetBase;
            targetList.push([top, href]);
        });

        // 按 offset 从小到大排序
        targetList.sort(function (a, b) { return a[0] - b[0]; });
        targetList.forEach(function (item) {
            self.offsets.push(item[0]);
            self.targets.push(item[1]);
        });
    }

    process() {
        const scrollTop = (this.$scrollElement === window ? window.pageYOffset : this.$scrollElement.scrollTop) + this.options.offset;
        const scrollHeight = this.getScrollHeight();
        const maxScroll = this.options.offset + scrollHeight - (this.$scrollElement === window ? window.innerHeight : this.$scrollElement.clientHeight);

        if (this.scrollHeight !== scrollHeight) this.refresh();

        const offsets = this.offsets;
        const targets = this.targets;
        let i;

        // 已滚到底部，激活最后一个
        if (targets.length && scrollTop >= maxScroll) {
            if (this.activeTarget !== targets[targets.length - 1]) {
                this.activate(targets[targets.length - 1]);
            }
            return;
        }

        // 还在第一个标题之前，激活第一个
        if (this.activeTarget && targets.length && scrollTop <= offsets[0]) {
            if (this.activeTarget !== targets[0]) {
                this.activate(targets[0]);
            }
            return;
        }

        // 从后往前找，第一个 offset <= scrollTop 的就是当前章节
        for (i = offsets.length; i--;) {
            if (this.activeTarget !== targets[i] &&
                scrollTop >= offsets[i] &&
                (!offsets[i + 1] || scrollTop < offsets[i + 1])) {
                this.activate(targets[i]);
            }
        }
    }

    activate(target) {
        this.activeTarget = target;

        // 清除所有 li.active
        const allLis = document.querySelectorAll(this.selector.replace(' > a', ''));
        allLis.forEach(function (li) { li.classList.remove('active'); });

        // 给当前 section 对应的 li 加上 .active
        const escapedHref = CSS.escape(target.substring(1));
        const activeLink = document.querySelector(this.selector + '[href="#' + escapedHref + '"]');
        if (activeLink) {
            const li = activeLink.closest('li');
            if (li) {
                li.classList.add('active');
                // 同时激活所有祖先 li（使子列表能通过 .nav>.active>ul 展开）
                let ancestor = li.parentElement;
                while (ancestor) {
                    if (ancestor.tagName === 'LI') ancestor.classList.add('active');
                    ancestor = ancestor.parentElement;
                }
            }
        }
    }

    destroy() {
        this.$scrollElement.removeEventListener('scroll', this._process);
    }
}


// ============================================================
// Tabber — 1:1 参照灰机wiki ext.gadget.Tabber (richtab 部分)
// 处理 Boss介绍/Boss指南 等 tab 切换
// ============================================================
function initTabber() {
    document.querySelectorAll('.tabber.richtab').forEach(function (tabber) {
        const filter = tabber.querySelector('.tabber-filter');
        if (!filter) return;

        const filterItems = filter.querySelectorAll('.tabber-filter-item');
        const panes = tabber.querySelectorAll('.tab-pane.tabber-item');

        filterItems.forEach(function (item) {
            // 确保内容被 <a> 包裹（参照 simRichTab）
            if (!item.querySelector('a')) {
                item.innerHTML = '<a href="javascript:void(0);">' + item.innerHTML + '</a>';
            }

            item.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();

                if (item.classList.contains('active')) return;

                // 移除所有按钮的 active
                filterItems.forEach(function (fi) { fi.classList.remove('active'); });
                // 当前按钮加 active
                item.classList.add('active');

                // 切换 pane
                const richtabId = item.getAttribute('data-richtab');
                panes.forEach(function (pane) {
                    pane.style.display = pane.getAttribute('data-richtab') === richtabId ? 'block' : 'none';
                });
            });
        });

        // 初始化：根据已有 active 按钮显示对应 pane（HTML 中第一个按钮已带 active 类）
        const activeBtn = filter.querySelector('.tabber-filter-item.active') || filterItems[0];
        if (activeBtn) {
            const activeId = activeBtn.getAttribute('data-richtab');
            panes.forEach(function (pane) {
                pane.style.display = pane.getAttribute('data-richtab') === activeId ? 'block' : 'none';
            });
        }
    });
}


function refresh() {

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
    // 自动包装 <audio> 为 .sound 容器，替换为 SVG 播放按钮
    // ============================================================

    // 4a. 包装所有独立的 <audio> → <div.sound>
    document.querySelectorAll('audio').forEach(audio => {
        if (audio.closest('.sound')) return;

        const src = audio.currentSrc || audio.querySelector('source')?.getAttribute('src') || audio.getAttribute('src');
        if (!src) return;

        const container = document.createElement('div');
        container.className = 'sound';
        container.title = '点击播放';
        container.style.cursor = 'pointer';
        container.dataset.src = src;

        // SVG 播放按钮
        container.innerHTML = `<svg class="sound-play-icon" viewBox="0 0 24 24" width="32" height="32">
    <circle cx="12" cy="12" r="10" fill="currentColor" opacity="0.8"/>
    <polygon points="10,7 17,12 10,17" fill="white"/>
</svg>`;

        audio.parentNode.replaceChild(container, audio);
    });

    // 4b. 处理所有 .sound 容器的点击
    const sounds = document.querySelectorAll('.sound');
    sounds.forEach(container => {
        container.style.cursor = 'pointer';
        container.title = container.title || '点击播放';

        container.addEventListener('click', function (e) {
            if (e.target.closest('a')) return;

            // 停止当前正在播放的音频
            if (window._soundCurrent && !window._soundCurrent.paused) {
                window._soundCurrent.pause();
                window._soundCurrent.currentTime = 0;
                if (window._soundCurrent._container) {
                    window._soundCurrent._container.classList.remove('sound-playing');
                    window._soundCurrent._container.title = '点击播放';
                }

                // 如果点击的是同一个容器，就是暂停
                if (window._soundCurrent._container === this) {
                    window._soundCurrent = null;
                    return;
                }
            }

            const src = this.dataset.src;
            if (!src) return;

            const audio = new Audio(src);
            audio._container = this;

            audio.addEventListener('ended', function () {
                this._container.classList.remove('sound-playing');
                this._container.title = '点击播放';
                window._soundCurrent = null;
            });

            audio.play();
            this.classList.add('sound-playing');
            this.title = '点击停止';
            window._soundCurrent = audio;
        });
    });

    // ============================================================
    // 5. 目录生成 — 1:1 参照灰机wiki (Bootstrap ScrollSpy)
    // ============================================================

    let scrollSpyInstance = null; // ScrollSpy 实例

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
                h3Count = 0;

                currentH2Li = document.createElement('li');
                currentH2Li.className = 'toclevel-1';

                const link = document.createElement('a');
                link.setAttribute('href', '#' + id);

                const numberSpan = document.createElement('span');
                numberSpan.className = 'tocnumber';
                numberSpan.textContent = h2Count + ' ';

                const textSpan = document.createElement('span');
                textSpan.className = 'toctext';
                textSpan.textContent = text;

                link.appendChild(numberSpan);
                link.appendChild(textSpan);
                currentH2Li.appendChild(link);
                tocList.appendChild(currentH2Li);

                currentSubUl = null;
            }
            // 5. 处理子标题 (H3)
            else if (tagName === 'h3' && currentH2Li) {
                h3Count++;

                if (!currentSubUl) {
                    currentSubUl = document.createElement('ul');
                    currentSubUl.className = 'nav nav-list';
                    currentH2Li.appendChild(currentSubUl);
                }

                const h3Li = document.createElement('li');
                h3Li.className = 'toclevel-2';

                const link = document.createElement('a');
                link.setAttribute('href', '#' + id);

                const numberSpan = document.createElement('span');
                numberSpan.className = 'tocnumber';
                numberSpan.textContent = h2Count + '.' + h3Count + ' ';

                const textSpan = document.createElement('span');
                textSpan.className = 'toctext';
                textSpan.textContent = text;

                link.appendChild(numberSpan);
                link.appendChild(textSpan);
                h3Li.appendChild(link);
                currentSubUl.appendChild(h3Li);
            }
        });

        // 6. 添加"回到顶部"项，默认 active（参照灰机wiki）
        const backToTopLi = document.createElement('li');
        backToTopLi.className = 'active';
        const backLink = document.createElement('a');
        backLink.setAttribute('href', '#firstHeading');
        backLink.textContent = '回到顶部';
        backToTopLi.appendChild(backLink);
        tocList.appendChild(backToTopLi);

        // 7. 初始化 ScrollSpy（1:1 参照灰机wiki: $('body').scrollspy({ target: '#toc .toc-ul-wrap', offset: 10 })）
        if (scrollSpyInstance) scrollSpyInstance.destroy();
        scrollSpyInstance = new ScrollSpy(document.body, {
            target: '#toc .toc-ul-wrap',
            offset: 10
        });
    }
    generateToc();
    initTabber();

}

