
let mobileFloatingResizeHandler = null;
let mobileFloatingResizeTimer = null;
let scrollSpyInstance = null;

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
            const targetEl = document.getElementById(href.substring(1));
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

    if (mobileFloatingResizeHandler) {
        window.removeEventListener('resize', mobileFloatingResizeHandler);
    }
    clearTimeout(mobileFloatingResizeTimer);

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
    mobileFloatingResizeHandler = () => {
        clearTimeout(mobileFloatingResizeTimer);
        mobileFloatingResizeTimer = setTimeout(fixMobileFloating, 200);
    };
    window.addEventListener('resize', mobileFloatingResizeHandler);

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

    function generateToc() {
        if (scrollSpyInstance) {
            scrollSpyInstance.destroy();
            scrollSpyInstance = null;
        }

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
        scrollSpyInstance = new ScrollSpy(document.body, {
            target: '#toc .toc-ul-wrap',
            offset: 10
        });
    }
    generateToc();
    initTabber();

}

window.wikiApp.start({ homePage: "首页", headingId: "firstHeading-h1", homeHeadingId: "firstHeading", lastModifiedPrefix: "此页面最后编辑于 ", homePageClass: null, openImageLinks: true, sourceUrl: (title) => "https://calamity.huijiwiki.com/wiki/" + encodeURIComponent(title.replace(/ /g, "_")), refresh });
