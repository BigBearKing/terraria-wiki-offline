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
}

// 导出给模块使用
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { hostBridge: window.hostBridge, iframeBridge: window.iframeBridge };
}
