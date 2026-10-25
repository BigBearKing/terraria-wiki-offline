/**
 * Iframe 通信桥接库
 * 提供主窗口和 iframe 之间的双向通信能力
 */

// ============================================================
// 主窗口端 (Main Window / Host) - 用于 MAUI Blazor
// ============================================================

/**
 * 主窗口的 iframe 桥接对象
 * 用于向 iframe 发送消息
 */
window.hostBridge = window.hostBridge || {
    iframeWin: null,

    /**
     * 向 iframe 发送消息
     * @param {object} msg - 消息对象
     */
    sendToIframe: function (msg) {
        const iframe = document.getElementById("wiki-frame");
        if (iframe && iframe.contentWindow) {
            iframe.contentWindow.postMessage(msg, '*');
        }
    },

    /**
     * 获取 iframe 当前滚动位置
     * @returns {Promise<number>} 返回当前滚动位置 (像素)
     */
    getIframeScrollPosition: async function () {
        return new Promise((resolve) => {
            const id = Math.random().toString(36).substr(2);
            const timeout = setTimeout(() => {
                delete window.pendingMainRequests[id];
                resolve(0); // 超时返回 0
            }, 1000);

            window.pendingMainRequests = window.pendingMainRequests || {};
            window.pendingMainRequests[id] = (position) => {
                clearTimeout(timeout);
                resolve(position);
            };

            this.sendToIframe({ type: 'req', id, method: 'GetCurrentPosition', data: null });
        });
    }
};

/**
 * 监听来自 iframe 的消息并转发给 C#
 */
window.addEventListener('message', function (e) {
    // 原始字符串消息由宿主页面的专用监听器处理，不属于 C# 请求/响应协议。
    if (typeof e.data === 'string') {
        return;
    }

    // 处理主窗口的响应消息
    if (e.data?.type === 'res' && e.data.id && window.pendingMainRequests?.[e.data.id]) {
        window.pendingMainRequests[e.data.id](e.data.data);
        delete window.pendingMainRequests[e.data.id];
        return;
    }

    // 转发其他消息给 C#
    if (typeof DotNet !== 'undefined') {
        DotNet.invokeMethodAsync('Terraria_Wiki', 'ReceiveMessage', JSON.stringify(e.data));
    }
});

// ============================================================
// Iframe 端 (Iframe) - 用于 Wiki 页面
// ============================================================

/**
 * Iframe 的 C# 调用桥接对象
 */
window.iframeBridge = window.iframeBridge || {
    handlers: {},  // JS 方法处理器
    pending: {},   // 等待 C# 响应的 Promise

    /**
     * 注册 JS 处理器
     * @param {string} method - 方法名
     * @param {function} handler - 处理函数
     */
    registerHandler: function (method, handler) {
        this.handlers[method] = handler;
    },

    /**
     * 调用 C# 方法
     * @param {string} method - C# 方法名
     * @param {any} data - 传递的数据
     * @returns {Promise<any>} C# 返回的结果
     */
    callCSharpAsync: function (method, data) {
        return new Promise(resolve => {
            const id = Math.random().toString(36).substr(2);
            this.pending[id] = resolve;
            window.parent.postMessage({ type: 'req', id, method, data }, '*');
        });
    },

    /**
     * 获取当前滚动位置
     * @returns {number} 当前滚动位置 (像素)
     */
    getCurrentPosition: function () {
        return window.pageYOffset || document.documentElement.scrollTop || document.body.scrollTop || 0;
    },

    /**
     * 向父窗口汇报用户活跃状态（用于防烧屏）
     */
    notifyUserActive: function () {
        window.parent.postMessage('iframe_user_active', '*');
    }
};

/**
 * 监听来自父窗口的消息
 */
if (window.parent !== window) {
    function stopIframeBurnInListeners() {
        if (!window._burnInListeners) {
            return;
        }

        window.removeEventListener('pointerdown', window._burnInListeners.pointerdown);
        window.removeEventListener('scroll', window._burnInListeners.scroll, true);
        window.removeEventListener('keydown', window._burnInListeners.keydown);
        delete window._burnInListeners;
    }

    window.addEventListener('message', async (e) => {
        const msg = e.data;

        if (msg === 'start_iframe_monitor') {
            stopIframeBurnInListeners();

            const notifyParent = () => window.iframeBridge.notifyUserActive();
            window._burnInListeners = {
                pointerdown: notifyParent,
                scroll: notifyParent,
                keydown: notifyParent
            };
            window.addEventListener('pointerdown', notifyParent);
            window.addEventListener('scroll', notifyParent, true);
            window.addEventListener('keydown', notifyParent);
            return;
        }

        if (msg === 'stop_iframe_monitor') {
            stopIframeBurnInListeners();
            return;
        }

        if (!msg || typeof msg !== 'object') {
            return;
        }

        if (msg.type === 'res') {
            // C# 返回结果
            if (window.iframeBridge.pending[msg.id]) {
                window.iframeBridge.pending[msg.id](msg.data);
                delete window.iframeBridge.pending[msg.id];
            }
        } else if (msg.type === 'req') {
            // C# 请求执行 JS
            let result = null;

            // 内置处理器：获取当前位置
            if (msg.method === 'GetCurrentPosition') {
                result = window.iframeBridge.getCurrentPosition();
            }
            // 自定义处理器
            else if (window.iframeBridge.handlers[msg.method]) {
                result = await window.iframeBridge.handlers[msg.method](msg.data);
            }

            // 回复父窗口
            window.parent.postMessage({ type: 'res', id: msg.id, data: result }, '*');
        }
    });

    // ===== 页内搜索：自己查找 + 高亮 + 计数 =====
    // WKWebView 自带的 Find 只回 MatchFound，拿不到匹配数量，也不保证把命中高亮出来；
    // 正文在 iframe 里，所以由这里接管：按可见文本节点建立字符映射，跨节点匹配，
    // 用 <mark> 包裹命中（跨节点时拆成相邻的多个 mark），返回 "count|index"。
    const findState = { query: '', matches: [], index: -1 };

    function ensureFindStyle() {
        if (document.getElementById('wiki-find-style')) return;
        const style = document.createElement('style');
        style.id = 'wiki-find-style';
        style.textContent =
            'mark.wiki-find-hit{background-color:#ffe58a;color:inherit;padding:0;border-radius:2px;}' +
            'mark.wiki-find-hit.wiki-find-current{background-color:#ffb300;box-shadow:0 0 0 2px rgba(255,179,0,.45);}';
        (document.head || document.documentElement).appendChild(style);
    }

    function clearFindMarks() {
        document.querySelectorAll('mark.wiki-find-hit').forEach(mark => {
            const parent = mark.parentNode;
            if (!parent) return;
            parent.replaceChild(document.createTextNode(mark.textContent || ''), mark);
            parent.normalize();
        });
        findState.matches = [];
        findState.index = -1;
    }

    function collectFindText() {
        let text = '';
        const map = [];
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
            acceptNode(node) {
                // 空白节点也要保留：跨 <b>/<i> 边界的短语要靠它们在拼接文本里留住空格
                if (!node.nodeValue) return NodeFilter.FILTER_REJECT;
                const parent = node.parentElement;
                if (!parent) return NodeFilter.FILTER_REJECT;
                if (parent.closest('script, style, noscript, textarea, select, option, svg, mark.wiki-find-hit')) {
                    return NodeFilter.FILTER_REJECT;
                }
                // 跳过 display:none / visibility:hidden 等不可见文本。
                // 用文本节点自己的 Range 量：父元素是 display:contents 时 parent 本身没有盒子，
                // 直接量 parent 会误杀，量 Range 则只看这段文字有没有渲染出来。
                const probe = document.createRange();
                probe.selectNodeContents(node);
                if (probe.getClientRects().length === 0) return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
            }
        });
        let node;
        while ((node = walker.nextNode())) {
            map.push({ node, start: text.length, end: text.length + node.nodeValue.length });
            text += node.nodeValue;
        }
        return { text, map };
    }

    function wrapFindSegment(node, start, end) {
        const range = document.createRange();
        range.setStart(node, start);
        range.setEnd(node, end);
        const mark = document.createElement('mark');
        mark.className = 'wiki-find-hit';
        range.surroundContents(mark);
        return mark;
    }

    function highlightFindMatches(text, map, query) {
        const haystack = text.toLocaleLowerCase();
        const needle = query.toLocaleLowerCase();
        const matches = [];
        let from = 0;
        while (needle.length > 0 && from <= haystack.length - needle.length) {
            const at = haystack.indexOf(needle, from);
            if (at < 0) break;
            matches.push({ start: at, end: at + needle.length, marks: [] });
            from = at + needle.length;
        }

        // 反向包裹：同一文本节点里从后往前切分，前面命中的字符偏移不会被破坏
        for (let i = matches.length - 1; i >= 0; i--) {
            const match = matches[i];
            const segments = [];
            for (const entry of map) {
                if (entry.end <= match.start) continue;
                if (entry.start >= match.end) break;
                segments.push({
                    node: entry.node,
                    start: Math.max(match.start, entry.start) - entry.start,
                    end: Math.min(match.end, entry.end) - entry.start
                });
            }
            for (let j = segments.length - 1; j >= 0; j--) {
                match.marks.unshift(wrapFindSegment(segments[j].node, segments[j].start, segments[j].end));
            }
        }
        return matches;
    }

    function selectFindMatch(index) {
        document.querySelectorAll('mark.wiki-find-hit.wiki-find-current')
            .forEach(mark => mark.classList.remove('wiki-find-current'));
        const match = findState.matches[index];
        if (!match || !match.marks.length) return;
        match.marks.forEach(mark => mark.classList.add('wiki-find-current'));
        const rect = match.marks[0].getBoundingClientRect();
        if (rect.top < 0 || rect.bottom > window.innerHeight) {
            match.marks[0].scrollIntoView({ block: 'center', inline: 'nearest' });
        }
    }

    function findReply(count, index) {
        return `${count}|${index}`;
    }

    function startFind(query) {
        ensureFindStyle();
        clearFindMarks();
        findState.query = query;
        if (!query) return findReply(0, -1);

        const { text, map } = collectFindText();
        findState.matches = highlightFindMatches(text, map, query);
        findState.index = findState.matches.length ? 0 : -1;
        if (findState.index >= 0) selectFindMatch(0);
        return findReply(findState.matches.length, findState.index);
    }

    function stepFind(delta) {
        const count = findState.matches.length;
        if (!count) return findReply(0, -1);
        findState.index = (findState.index + delta + count) % count;
        selectFindMatch(findState.index);
        return findReply(count, findState.index);
    }

    window.iframeBridge.registerHandler('FindInPage', data => startFind(String(data ?? '')));
    window.iframeBridge.registerHandler('FindInPageNext', () => stepFind(1));
    window.iframeBridge.registerHandler('FindInPagePrevious', () => stepFind(-1));
    window.iframeBridge.registerHandler('FindInPageClear', () => {
        clearFindMarks();
        findState.query = '';
        return findReply(0, -1);
    });
}

// 导出给模块使用
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { hostBridge: window.hostBridge, iframeBridge: window.iframeBridge };
}
