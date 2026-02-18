!function (e, t) { "object" == typeof exports && "undefined" != typeof module ? module.exports = t() : "function" == typeof define && define.amd ? define(t) : (e = "undefined" != typeof globalThis ? globalThis : e || self).handyScroll = t() }(this, (function () { "use strict"; let e = Array.prototype.slice, t = { isDOMAvailable: "object" == typeof document && !!document.documentElement, ready(e) { "loading" === document.readyState ? document.addEventListener("DOMContentLoaded", (() => { e() }), { once: !0 }) : e() }, $: e => "string" == typeof e ? document.body.querySelector(e) : e, $$: t => Array.isArray(t) ? t : t.nodeType === Node.ELEMENT_NODE ? [t] : "string" == typeof t ? e.call(document.body.querySelectorAll(t)) : e.call(t) }, n = { init(e) { let n = this, i = t.$$(".handy-scroll-body").filter((t => t.contains(e))); i.length && (n.scrollBody = i[0]), n.container = e, n.visible = !0, n.initWidget(), n.update(), n.addEventHandlers(), n.skipSyncContainer = n.skipSyncWidget = !1 }, initWidget() { let e = this, t = e.widget = document.createElement("div"); t.classList.add("handy-scroll"); let n = document.createElement("div"); n.style.width = `${e.container.scrollWidth}px`, t.appendChild(n), e.container.appendChild(t) }, addEventHandlers() { let e = this; (e.eventHandlers = [{ el: e.scrollBody || window, handlers: { scroll() { e.checkVisibility() }, resize() { e.update() } } }, { el: e.widget, handlers: { scroll() { e.visible && !e.skipSyncContainer && e.syncContainer(), e.skipSyncContainer = !1 } } }, { el: e.container, handlers: { scroll() { e.skipSyncWidget || e.syncWidget(), e.skipSyncWidget = !1 }, focusin() { setTimeout((() => { e.widget && e.syncWidget() }), 0) } } }]).forEach((({ el: e, handlers: t }) => { Object.keys(t).forEach((n => e.addEventListener(n, t[n], !1))) })) }, checkVisibility() { let e = this, { widget: t, container: n, scrollBody: i } = e, l = t.scrollWidth <= t.offsetWidth; if (!l) { let e = n.getBoundingClientRect(), t = i ? i.getBoundingClientRect().bottom : window.innerHeight || document.documentElement.clientHeight; l = e.bottom <= t || e.top > t } e.visible === l && (e.visible = !l, t.classList.toggle("handy-scroll-hidden")) }, syncContainer() { let e = this, { scrollLeft: t } = e.widget; e.container.scrollLeft !== t && (e.skipSyncWidget = !0, e.container.scrollLeft = t) }, syncWidget() { let e = this, { scrollLeft: t } = e.container; e.widget.scrollLeft !== t && (e.skipSyncContainer = !0, e.widget.scrollLeft = t) }, update() { let e = this, { widget: t, container: n, scrollBody: i } = e, { clientWidth: l, scrollWidth: o } = n; t.style.width = `${l}px`, i || (t.style.left = `${n.getBoundingClientRect().left}px`), t.firstElementChild.style.width = `${o}px`, o > l && (t.style.height = t.offsetHeight - t.clientHeight + 1 + "px"), e.syncWidget(), e.checkVisibility() }, destroy() { let e = this; e.eventHandlers.forEach((({ el: e, handlers: t }) => { Object.keys(t).forEach((n => e.removeEventListener(n, t[n], !1))) })), e.widget.remove(), e.eventHandlers = e.widget = e.container = e.scrollBody = null } }, i = [], l = { mount(e) { t.$$(e).forEach((e => { if (l.mounted(e)) return; let t = Object.create(n); i.push(t), t.init(e) })) }, mounted(e) { let n = t.$(e); return i.some((e => e.container === n)) }, update(e) { t.$$(e).forEach((e => { i.some((t => t.container === e && (t.update(), !0))) })) }, destroy(e) { t.$$(e).forEach((e => { i.some(((t, n) => t.container === e && (i.splice(n, 1)[0].destroy(), !0))) })) }, destroyDetached() { i = i.filter((e => !!document.body.contains(e.container) || (e.destroy(), !1))) } }; return t.isDOMAvailable && t.ready((() => { l.mount("[data-handy-scroll]") })), l }));

window.contentfile = '/pages/Terraria Wiki.html'
var history = [];
document.addEventListener('click', async (e) => {
    // 查找最近的 a 标签
    const link = e.target.closest('a');
    if (!link) return;

    const title = link.getAttribute('title');
    const anchor = link.getAttribute('anchor') || '';
    const href = link.getAttribute('href')||'';
    if (href.startsWith('http')) {
        e.preventDefault();
        await window.__TAURI__.shell.open(href);
        return;
    }
    // 如果是站内链接（有 title 属性且没有 target="_blank"）
    if (title && !href) {
        redirect(title, anchor);
    }
});
var redirectjson = {};
await fetch('redirect.json') // 替换成你的 JSON 文件路径或 API URL
    .then(response => response.json())
    .then(data => { redirectjson = data });

async function redirect(title, anchor) {
    let path = document.getElementById('top').getAttribute('path');
    let top = document.getElementsByClassName('content-wrapper')[0].scrollTop;
    let newcontentfile = '';
    if (redirectjson.hasOwnProperty(title)) {
        let redirect = redirectjson[title];
        if (redirect.includes("#")) {
            newcontentfile = '/pages/' + redirect.replace(':', '-').replace(/#.*/, "") + ".html"
            anchor = /#.*/.exec(redirect)

        } else {
            newcontentfile = '/pages/' + redirect.replace(':', '-') + ".html"
        }

    } else {
        newcontentfile = '/pages/' + title.replace(':', '-') + ".html"
    }
    if (newcontentfile == contentfile) {
        popupmessage('这已经是当前页面');
        return;
    } else {
        contentfile = newcontentfile;
    }
    if (await loader()) {
        console.log("跳转到" + contentfile);
        //添加历史记录

        history.push({ path: path, top: top })

        console.log(history)
        if (anchor != '') {
            document.getElementById('top').setAttribute('href', anchor)
            document.getElementById('top').click()
        }
    }
}
function debounce(func, delay) {
    let timeout;
    return function (...args) {
        const context = this;
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(context, args), delay);
    };
}
function initiate_collapsible_native() {
    const contentEl = document.getElementById('content');
    const headerEl = document.getElementById('box-wikiheader');

    // 如果核心元素不存在，则退出函数
    if (!contentEl || !headerEl) return;

    // 使用 clientWidth 获取内容的内部宽度（取代 $content.width()）
    const width = contentEl.clientWidth;

    // --- 1. 计算偏移量 ($offset) ---
    // 逻辑：如果宽度 > 980px 使用 250，> 500px 使用 42，否则使用 12
    const offset = width > 980 ? 250 : (width > 500 ? 42 : 12);

    // --- 2. 头部 (box-wikiheader) 的折叠状态 ---
    // $header.toggleClass('collapsable', $width < 1300);
    headerEl.classList.toggle('collapsable', width < 1300);
    // $header.toggleClass('collapsed', $width < 730);
    headerEl.classList.toggle('collapsed', width < 730);

    // --- 3. 内容行 (content) 的布局类 ---
    // $content.toggleClass('box-row-l', ...).toggleClass('box-row-m', ...).toggleClass('box-row-s', ...)
    contentEl.classList.toggle('box-row-l', (width <= 3500 - offset) && (width >= 2400 - offset));
    contentEl.classList.toggle('box-row-m', (width <= 2399 - offset) && (width >= 1670 - offset));
    contentEl.classList.toggle('box-row-s', (width <= 1669 - offset));

    // --- 4. 辅助函数：根据 ID 切换类 ---
    const toggleBoxClasses = (id, classLogic) => {
        const el = document.getElementById(id);
        if (!el) return;

        // 遍历所有类名和条件
        for (const [className, condition] of Object.entries(classLogic)) {
            el.classList.toggle(className, condition);
        }
    };

    // --- 5. 各内容框的宽度切换逻辑 ---

    // #box-game
    toggleBoxClasses('box-game', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset),
        'width-b': (width <= 3249 - offset) && (width >= 1670 - offset),
        'width-c': (width <= 1669 - offset),
        'width-d': (width <= 1200 - offset),
        'width-e': (width <= 1160 - offset),
        'width-f': (width <= 700 - offset),
        'width-g': (width <= 540 - offset)
    });

    // #box-news
    toggleBoxClasses('box-news', {
        'width-a': (width >= 1750 - offset) || (width <= 1669 - offset),
        'width-b': (width <= 400 - offset)
    });

    // #box-items
    toggleBoxClasses('box-items', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset),
        'width-b': (width <= 1769 - offset),
        'width-c': (width <= 1669 - offset),
        'width-d': (width <= 1320 - offset),
        'width-e': (width <= 1140 - offset),
        'width-f': (width <= 1040 - offset),
        'width-g': (width <= 980 - offset),
        'width-h': (width <= 870 - offset),
        'width-i': (width <= 620 - offset),
        'width-j': (width <= 450 - offset)
    });

    // #box-biomes
    toggleBoxClasses('box-biomes', {
        'width-a': (width <= 3250 - offset) && (width >= 2560 - offset),
        'width-b': (width <= 1769 - offset),
        'width-c': (width <= 1669 - offset),
        'width-d': (width <= 1320 - offset),
        'width-e': (width <= 1140 - offset),
        'width-f': (width <= 1040 - offset),
        'width-g': (width <= 980 - offset),
        'width-h': (width <= 830 - offset),
        'width-i': (width <= 630 - offset),
        'width-j': (width <= 428 - offset)
    });

    // #box-mechanics
    toggleBoxClasses('box-mechanics', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset) || (width <= 1470 - offset),
        'width-b': (width <= 1769 - offset) && (width >= 1670 - offset),
        'width-c': (width <= 1080 - offset),
        'width-d': (width <= 750 - offset),
        'width-e': (width <= 550 - offset),
        'width-f': (width <= 359 - offset)
    });

    // #box-npcs
    toggleBoxClasses('box-npcs', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset),
        'width-b': (width <= 3249 - offset) && (width >= 2560 - offset),
        'width-c': (width <= 1470 - offset),
        'width-d': (width <= 1080 - offset),
        'width-e': (width <= 720 - offset),
        'width-f': (width <= 570 - offset),
        'width-g': (width <= 350 - offset)
    });

    // #box-bosses
    toggleBoxClasses('box-bosses', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset),
        'width-b': (width <= 3249 - offset) && (width >= 2560 - offset),
        'width-c': (width <= 1669 - offset),
        'width-d': (width <= 1365 - offset),
        'width-e': (width <= 800 - offset),
        'width-f': (width <= 720 - offset),
        'width-g': (width <= 480 - offset)
    });

    // #box-events
    toggleBoxClasses('box-events', {
        'width-a': (width <= 4500 - offset) && (width >= 3250 - offset),
        'width-b': (width <= 1669 - offset),
        'width-c': (width <= 1365 - offset),
        'width-d': (width <= 800 - offset),
        'width-e': (width <= 720 - offset),
        'width-f': (width <= 650 - offset),
        'width-g': (width <= 540 - offset)
    });

    // #sect-ext
    toggleBoxClasses('sect-ext', {
        'width-a': width >= 2300 - offset
    });

    // #box-software
    toggleBoxClasses('box-software', {
        'width-a': (width <= 2299 - offset),
        'width-b': (width <= 1100 - offset),
        'width-c': (width <= 680 - offset)
    });

    // #box-wiki
    toggleBoxClasses('box-wiki', {
        'width-a': (width <= 2299 - offset),
        'width-b': (width <= 1499 - offset),
        'width-c': (width <= 680 - offset)
    });
}
function handleMobileLayoutFixes() {
    const contentBox = document.querySelector('#mw-content-text .mw-parser-output');
    // 如果找不到内容容器，则退出
    if (!contentBox) {
        return;
    }

    const elements = Array.from(contentBox.children);
    const fullWidth = contentBox.offsetWidth;

    if (fullWidth === 0) {
        return;
    }

    const offset = contentBox.getBoundingClientRect().left;

    // 1. 移除所有元素的修正类
    elements.forEach(el => {
        el.classList.remove('mobile-floating-fix', 'mobile-fullwidth');
    });

    // 2. 只有当宽度小于或等于 720px 时才应用移动端修正
    if (fullWidth > 720) {
        return;
    }

    // 3. 浮动元素修正 (mobile-floating-fix) 
    let maxLeft = 0;

    // 从后往前遍历内容元素的子元素
    for (let i = elements.length - 1; i >= 0; i--) {
        const el = elements[i];
        const elStyle = window.getComputedStyle(el);

        if (elStyle.float === 'right') {
            const elRect = el.getBoundingClientRect();
            const left = elRect.left;
            const outerWidth = el.offsetWidth;

            // 如果右浮动元素距离容器左侧太近（< 300px）或与上一个修正元素堆叠（< 12px 间隔）
            if ((left - offset < 300) || (maxLeft && left < maxLeft + 12)) {
                el.classList.add('mobile-floating-fix');
                maxLeft = Math.max(maxLeft, left + outerWidth);
                continue;
            }
        }
        maxLeft = 0;
    }

    // 4. 信息框全宽修正 (mobile-fullwidth)
    const threshold = Math.min(90, fullWidth * 0.25);

    const infoboxes = contentBox.querySelectorAll('.infobox, .portable-infobox');

    infoboxes.forEach(el => {
        const outerWidth = el.offsetWidth;
        // 如果容器宽度减去信息框宽度小于阈值，则强制全宽
        if (fullWidth - outerWidth < threshold) {
            el.classList.add('mobile-fullwidth');
        }
    });
}
function popupmessage(title, message = '操作已成功完成！请点击下方按钮继续下一步操作。') {
    document.querySelector('body > div.popup > h2').innerHTML = title;
    document.querySelector('body > div.popup > p').innerHTML = message;
    const popup = document.querySelector('body > div.popup');
    popup.style.display = 'inline-block';
    setTimeout(() => {
        popup.classList.add('is-visible');
    }, 10);

    document.querySelector('body').style.pointerEvents = 'none';
    document.querySelector('body > .content-wrapper').style.filter = 'blur(2px)';

    document.querySelector('body > div.popup > button.primary').onclick = function () {
        // 1. 移除显示类，开始隐藏动画

        popup.classList.remove('is-visible');
        // 3. **关键：等待动画播放完毕** (等待 300ms，与 CSS transition 时长一致)
        const animationDuration = 200;
        setTimeout(() => {
            // 4. 动画结束后，将 display 设置回 none，彻底隐藏元素
            popup.style.display = 'none';


        }, animationDuration);
        document.querySelector('body').style.pointerEvents = 'auto';
        document.querySelector('body > .content-wrapper').style.filter = 'none';
    }
}
window.loader = async function () {

    //内容添加
    try {
        //滚动条
        const response = await fetch(contentfile);
        var main = document.createElement("div");
        var contenttext = await response.text();
        main.innerHTML = contenttext;
        document.getElementById('main-content').innerHTML = main.getElementsByTagName("main")[0].innerHTML;
        console.log(main.getElementsByTagName("title")[0].innerHTML)
        document.querySelector('title').innerHTML = main.getElementsByTagName("title")[0].innerHTML;

        document.getElementById('footer-info-lastmod').innerHTML = main.querySelector('#footer-info-lastmod').innerHTML;
        console.log("开始判断是否是主页")
        //判断是否是首页
        if (contentfile == '/pages/Terraria Wiki.html') {
            document.querySelector('body').setAttribute('class', 'wgg-dom-version-1_43 skin-vector-legacy mediawiki ltr sitedir-ltr mw-hide-empty-elt ns-0 ns-subject page-Terraria_Wiki page-Main_Page rootpage-Terraria_Wiki skin-vector action-view skin--responsive')
            console.log(document.querySelector('body').attributes)

        } else {
            document.querySelector('body').setAttribute('class', 'wgg-dom-version-1_43 skin-vector-legacy mediawiki ltr sitedir-ltr mw-hide-empty-elt ns-0 ns-subject mw-editable skin-vector action-view skin--responsive')

            console.log("Not home")

        }
        //设置标题
        document.querySelector('#title-span').innerHTML = document.querySelector('title').innerHTML


        //点击图片播放音频
        {
            document.querySelectorAll(".sound.iconlast audio").forEach(function (peraudio) {
                peraudio.removeAttribute('controls');
            });
            const l10n = (key) => {
                const data = {
                    playTitle: { 'zh-cn': '点击播放' },
                    stopTitle: { 'zh-cn': '点击停止' }
                };
                return data[key]['zh-cn'];
            };

            // 找到所有声音元素
            const soundElements = document.querySelectorAll('.mw-parser-output .sound');

            soundElements.forEach(soundEl => {
                // 设置初始提示文本
                soundEl.title = l10n('playTitle');


                // 绑定点击事件
                soundEl.addEventListener('click', function (e) {
                    // 如果点击目标是链接（A标签），则不阻止默认行为
                    if (e.target.tagName === 'A') {
                        return;
                    }

                    // 找到内部的 audio 元素
                    const audio = soundEl.querySelector('audio');

                    if (audio) {
                        // 切换播放状态
                        audio.paused ? audio.play() : audio.pause();
                        audio.removeAttribute('controls');
                    }
                });

                const audio = soundEl.querySelector('audio');
                if (audio) {
                    // 监听 audio 播放事件
                    audio.addEventListener('play', function () {
                        // 暂停正在播放的其他声音
                        const playingSound = document.querySelector('.sound-playing audio');
                        if (playingSound && playingSound !== this) {
                            playingSound.pause();
                        }

                        // 更新当前声音元素的样式和提示文本
                        soundEl.classList.add('sound-playing');
                        soundEl.title = l10n('stopTitle');
                    });

                    // 监听 audio 暂停事件
                    audio.addEventListener('pause', function () {
                        // 重置播放时间到开头
                        this.currentTime = 0;

                        // 更新样式和提示文本
                        soundEl.classList.remove('sound-playing');
                        soundEl.title = l10n('playTitle');
                    });
                }
            });
        }


        //表格横向滚动条
        {


            let tables = document.querySelectorAll('table');
            if (tables) {
                tables.forEach(function (perTable) {
                    let tableFather = document.createElement('div');
                    tableFather.className = 'table-box';
                    tableFather.setAttribute('style', perTable.getAttribute('style'));
                    perTable.removeAttribute('style');
                    perTable.parentNode.replaceChild(tableFather, perTable);
                    tableFather.appendChild(perTable);
                });
            }
            handyScroll.mount(document.getElementsByClassName("table-box"));


        }


        //表格更改专家大师模式
        {
            const tabs = document.querySelectorAll(".tab")
            tabs.forEach((tab) => {
                tab.addEventListener('click', (event) => {
                    if (tab.classList.contains('current')) {
                        return;
                    }
                    const bros = Array.from(tab.parentElement.children);
                    bros.forEach((bro) => {
                        bro.classList.remove('current');
                    })
                    tab.classList.add('current');
                    let modesbox = tab.closest('.modesbox')
                    modesbox.classList.remove('c-expert');
                    modesbox.classList.remove('c-master');
                    modesbox.classList.remove('c-normal');
                    let modesboxClass = 'c-master';
                    if (tab.classList.contains('normal')) {
                        modesboxClass = 'c-normal';
                    }
                    else if (tab.classList.contains('expert')) {
                        modesboxClass = 'c-expert';
                    }
                    modesbox.classList.add(modesboxClass);
                })
            })
        }


        document.getElementById('top').setAttribute('path', contentfile);
        initiate_collapsible_native();
        handleMobileLayoutFixes();
        document.getElementsByClassName('content-wrapper')[0].scrollTo({ top: 0, left: 0, behavior: 'instant' });
        return true;
    } catch (error) {
        popupmessage('页面不存在', error.message);
        console.log(error);
        console.log("error");
        return false;
    }

}
window.allpages = function () {
    if (document.getElementById('top').getAttribute('path') == '/pages/allpages.html') {
        popupmessage('这已经是所有页面');
    } else { redirect('allpages', '') }
}

window.back = async function () {

    if (history.length == 0) {
        popupmessage('这已经是首页');
    } else {
        contentfile = history[history.length - 1].path
        await loader()
        document.getElementsByClassName('content-wrapper')[0].scrollTo({ top: history[history.length - 1].top, left: 0, behavior: 'instant' });
        history.pop()
    }

}

window.totop = function () {
    document.getElementsByClassName('content-wrapper')[0].scrollTo({ top: 0, behavior: 'smooth' });
}
window.tohome = function () {
    if (history.length == 0) {
        popupmessage('这已经是首页');
    } else {
        location.reload();
        document.getElementsByClassName('content-wrapper')[0].scrollTo({ top: 0, left: 0, behavior: 'instant' });
    }
}


await window.loader()
document.getElementById('more-button').addEventListener('click', async () => {
    const version = await window.__TAURI__.app.getVersion();
    popupmessage('更多', `<h1>离线Terraria Wiki</h1><p>离线Terraria Wiki由<a href="https://github.com/BigBearKing">BigBearKing</a>制作，内容基于<a href="https://terraria.wiki.gg/zh/wiki/Terraria_Wiki">Terraria Wiki网站</a>，旨在为用户提供无需网络连接即可访问的Terraria游戏资料和信息。</p><p>version ${version}</p><p>更多信息请访问<a href="https://github.com/BigBearKing/Offline-Terraria-Wiki">Offline Terraria Wiki</a></p>`);


});


window.addEventListener("resize", debounce(handleMobileLayoutFixes, 50));
window.addEventListener("resize", debounce(initiate_collapsible_native, 50));