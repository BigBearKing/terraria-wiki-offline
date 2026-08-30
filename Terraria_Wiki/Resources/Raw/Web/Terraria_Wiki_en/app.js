// ============================================================

let tableResizeHandler = null;
let mobileFloatingResizeHandler = null;
let headerResizeHandler = null;
let mobileFloatingResizeTimer = null;

function refresh() {

    if (tableResizeHandler) {
        window.removeEventListener('resize', tableResizeHandler);
    }
    if (mobileFloatingResizeHandler) {
        window.removeEventListener('resize', mobileFloatingResizeHandler);
    }
    if (headerResizeHandler) {
        window.removeEventListener('resize', headerResizeHandler);
    }
    clearTimeout(mobileFloatingResizeTimer);

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

        tableResizeHandler = debounce(processWideTables, 100);
        window.addEventListener('resize', tableResizeHandler);
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
    mobileFloatingResizeHandler = () => {
        clearTimeout(mobileFloatingResizeTimer);
        mobileFloatingResizeTimer = setTimeout(fixMobileFloating, 200);
    };
    window.addEventListener('resize', mobileFloatingResizeHandler);

    // ============================================================
    // 4. Template:Sound (音频播放控制)
    // ============================================================
    const sounds = document.querySelectorAll('.sound');
    sounds.forEach(container => {
        container.style.cursor = 'pointer';
        container.title = window.wikiApp.t('Web.ClickToPlay', 'Click to play');

        const audio = container.querySelector('audio');
        if (!audio) return;

        // ✅ 新增：监听当前音频自然播放结束的事件
        audio.addEventListener('ended', function () {
            container.classList.remove('sound-playing');
            container.title = window.wikiApp.t('Web.ClickToPlay', 'Click to play');
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
                this.title = window.wikiApp.t('Web.ClickToStop', 'Click to stop');
            } else {
                audio.pause();
                audio.currentTime = 0;
                this.classList.remove('sound-playing');
                this.title = window.wikiApp.t('Web.ClickToPlay', 'Click to play');
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

        headerResizeHandler = debounce(updateHeaderState, 200);
        window.addEventListener('resize', headerResizeHandler);

        // 点击展开/折叠按钮
        toggleBtn.addEventListener('click', function () {
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

window.wikiApp.start({ homePage: "Terraria Wiki", headingId: "firstHeading", homeHeadingId: "firstHeading", lastModifiedPrefix: "This page was last edited on ", homePageClass: "rootpage-Terraria_Wiki", openImageLinks: false, sourceUrl: (title) => "https://terraria.wiki.gg/wiki/" + encodeURIComponent(title.replace(/ /g, "_")), refresh });
