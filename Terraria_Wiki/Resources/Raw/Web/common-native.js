document.addEventListener('DOMContentLoaded', function() {
    console.log("Terraria Wiki Scripts Loaded (Native Mode)");

    // ============================================================
    // 1 & 2. Handle Wide Tables (宽表格处理 + 滚动条)
    // 原理：检测表格宽度，如果超出容器，就包裹一个 div 让它横向滚动
    // ============================================================
    function handleWideTables() {
        const tables = document.querySelectorAll('table');
        tables.forEach(table => {
            // 如果已经被处理过，跳过
            if (table.parentElement.classList.contains('table-scroll-wrapper')) return;

            // 简单的包裹逻辑，不管是否超宽都包裹，防止 resize 时闪烁
            // 如果你想严格判断宽度，可以对比 table.offsetWidth > table.parentElement.offsetWidth
            const wrapper = document.createElement('div');
            wrapper.className = 'table-scroll-wrapper';
            
            // 插入 wrapper 并移动 table
            table.parentNode.insertBefore(wrapper, table);
            wrapper.appendChild(table);
        });
    }
    handleWideTables(); // 初始化运行

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
    // 原理：点击 .sound 容器播放内部的 <audio>，并停止其他音频
    // ============================================================
    const sounds = document.querySelectorAll('.sound');
    sounds.forEach(container => {
        // 设置鼠标样式提示可点击
        container.style.cursor = 'pointer';
        container.title = '点击播放';

        container.addEventListener('click', function(e) {
            // 如果点击的是链接，不触发播放
            if (e.target.tagName === 'A') return;

            const audio = this.querySelector('audio');
            if (!audio) return;

            // 1. 停止页面上所有其他正在播放的音频
            document.querySelectorAll('audio').forEach(otherAudio => {
                if (otherAudio !== audio && !otherAudio.paused) {
                    otherAudio.pause();
                    otherAudio.currentTime = 0;
                    // 移除其他容器的播放状态样式（如果有）
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
                audio.currentTime = 0; // 回到开头
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
        tab.addEventListener('click', function() {
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
    // 6. Main Page Layout (首页响应式布局)
    // 原理：根据宽度给首页特定 ID 的元素添加 width-a, width-b 等类名
    // ============================================================
    function updateMainPageLayout() {
        if (!document.querySelector('body').classList.contains('rootpage-Terraria_Wiki')) return; 
        const content = document.getElementById('content') || document.body;
        const width = content.offsetWidth;
        // 偏移量计算 (原版逻辑)
        const offset = width > 980 ? 250 : (width > 500 ? 42 : 12);

        // 辅助函数：简化 class 切换
        const toggleClass = (el, className, condition) => {
            if (!el) return;
            if (condition) el.classList.add(className);
            else el.classList.remove(className);
        };

        // --- 核心断点逻辑 (直接翻译自原版 common.js) ---
        
toggleClass('box-game', 'width-a', (width <= 4500 - offset) && (width >= 3250 - offset));
        toggleClass('box-game', 'width-b', (width <= 3249 - offset) && (width >= 1670 - offset));
        toggleClass('box-game', 'width-c', (width <= 1669 - offset));
        toggleClass('box-game', 'width-d', (width <= 1200 - offset));
        toggleClass('box-game', 'width-e', (width <= 1160 - offset));
        toggleClass('box-game', 'width-f', (width <= 700 - offset));
        toggleClass('box-game', 'width-g', (width <= 540 - offset));

        // --- 4. Box-News (新闻) ---
        toggleClass('box-news', 'width-a', (width >= 1750 - offset) || (width <= 1669 - offset));
        toggleClass('box-news', 'width-b', (width <= 400 - offset));

        // --- 5. Box-Items (物品) ---
        toggleClass('box-items', 'width-a', (width <= 4500 - offset) && (width >= 3250 - offset));
        toggleClass('box-items', 'width-b', (width <= 1769 - offset));
        toggleClass('box-items', 'width-c', (width <= 1669 - offset));
        toggleClass('box-items', 'width-d', (width <= 1320 - offset));
        toggleClass('box-items', 'width-e', (width <= 1140 - offset));
        toggleClass('box-items', 'width-f', (width <= 1040 - offset));
        toggleClass('box-items', 'width-g', (width <= 980 - offset));
        toggleClass('box-items', 'width-h', (width <= 870 - offset));
        toggleClass('box-items', 'width-i', (width <= 620 - offset));
        toggleClass('box-items', 'width-j', (width <= 450 - offset));

        // --- 6. Box-Biomes (生物群落) ---
        toggleClass('box-biomes', 'width-a', (width <= 3250 - offset) && (width >= 2560 - offset));
        toggleClass('box-biomes', 'width-b', (width <= 1769 - offset));
        toggleClass('box-biomes', 'width-c', (width <= 1669 - offset));
        toggleClass('box-biomes', 'width-d', (width <= 1320 - offset));
        toggleClass('box-biomes', 'width-e', (width <= 1140 - offset));
        toggleClass('box-biomes', 'width-f', (width <= 1040 - offset));
        toggleClass('box-biomes', 'width-g', (width <= 980 - offset));
        toggleClass('box-biomes', 'width-h', (width <= 830 - offset));
        toggleClass('box-biomes', 'width-i', (width <= 630 - offset));
        toggleClass('box-biomes', 'width-j', (width <= 428 - offset));

        // --- 7. Box-Mechanics (游戏机制) ---
        toggleClass('box-mechanics', 'width-a', ((width <= 4500 - offset) && (width >= 3250 - offset)) || (width <= 1470 - offset));
        toggleClass('box-mechanics', 'width-b', (width <= 1769 - offset) && (width >= 1670 - offset));
        toggleClass('box-mechanics', 'width-c', (width <= 1080 - offset));
        toggleClass('box-mechanics', 'width-d', (width <= 750 - offset));
        toggleClass('box-mechanics', 'width-e', (width <= 550 - offset));
        toggleClass('box-mechanics', 'width-f', (width <= 359 - offset));

        // --- 8. Box-NPCs (NPC) ---
        toggleClass('box-npcs', 'width-a', (width <= 4500 - offset) && (width >= 3250 - offset));
        toggleClass('box-npcs', 'width-b', (width <= 3249 - offset) && (width >= 2560 - offset));
        toggleClass('box-npcs', 'width-c', (width <= 1470 - offset));
        toggleClass('box-npcs', 'width-d', (width <= 1080 - offset));
        toggleClass('box-npcs', 'width-e', (width <= 720 - offset));
        toggleClass('box-npcs', 'width-f', (width <= 570 - offset));
        toggleClass('box-npcs', 'width-g', (width <= 350 - offset));

        // --- 9. Box-Bosses (Boss) ---
        toggleClass('box-bosses', 'width-a', (width <= 4500 - offset) && (width >= 3250 - offset));
        toggleClass('box-bosses', 'width-b', (width <= 3249 - offset) && (width >= 2560 - offset));
        toggleClass('box-bosses', 'width-c', (width <= 1669 - offset));
        toggleClass('box-bosses', 'width-d', (width <= 1365 - offset));
        toggleClass('box-bosses', 'width-e', (width <= 800 - offset));
        toggleClass('box-bosses', 'width-f', (width <= 720 - offset));
        toggleClass('box-bosses', 'width-g', (width <= 480 - offset));

        // --- 10. Box-Events (事件) ---
        toggleClass('box-events', 'width-a', (width <= 4500 - offset) && (width >= 3250 - offset));
        toggleClass('box-events', 'width-b', (width <= 1669 - offset));
        toggleClass('box-events', 'width-c', (width <= 1365 - offset));
        toggleClass('box-events', 'width-d', (width <= 800 - offset));
        toggleClass('box-events', 'width-e', (width <= 720 - offset));
        toggleClass('box-events', 'width-f', (width <= 650 - offset));
        toggleClass('box-events', 'width-g', (width <= 540 - offset));

        // --- 11. Sect-Ext (扩展区域) ---
        toggleClass('sect-ext', 'width-a', width >= 2300 - offset);

        // --- 12. Box-Software (软件) ---
        toggleClass('box-software', 'width-a', (width <= 2299 - offset));
        toggleClass('box-software', 'width-b', (width <= 1100 - offset));
        toggleClass('box-software', 'width-c', (width <= 680 - offset));

        // --- 13. Box-Wiki (Wiki信息) ---
        toggleClass('box-wiki', 'width-a', (width <= 2299 - offset));
        toggleClass('box-wiki', 'width-b', (width <= 1499 - offset));
        toggleClass('box-wiki', 'width-c', (width <= 680 - offset));
        
        // 注意：原版代码非常长，针对每个 box 都有判断。
        // 如果你的数据库里的首页 HTML ID 和原版一致，这些代码会生效。
        // 如果不一致，你可能需要去 CSS 里用 Grid/Flexbox 重写，比 JS 更高效。
    }

    // 初始化首页布局
    updateMainPageLayout();
    window.addEventListener('resize', () => {
        clearTimeout(window.mainLayoutTimer);
        window.mainLayoutTimer = setTimeout(updateMainPageLayout, 100);
    });

});