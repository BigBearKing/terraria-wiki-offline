/**
 * Terraria Wiki 离线公式渲染脚本 (wiki-math.js)
 *
 * 模拟 wiki.gg 上 SimpleMathJax 扩展的客户端行为：
 *   1. 服务器离线保存的页面 HTML 中包含：
 *      <span style="opacity:.5" class="smj-container">[math]\displaystyle{ ... }[/math]</span>
 *   2. 本脚本用 MathJax v3（本地 tex-chtml-full.js，无需联网）将这些公式渲染成数学排版；
 *   3. 渲染完成后把占位透明度 opacity 从 0.5 恢复为 1（防止 FOUC）。
 *
 * 与 SimpleMathJax 保持一致的配置：
 *   - [math] / [/math] 作为行内公式分隔符
 *   - processHtmlClass 包含 smj-container
 *   - 完整 HTML 实体 -> TeX 宏映射表（wikitext 中 &alpha; 等实体会被 HTML 解析成
 *     Unicode 字符，映射表把它们转回 TeX 宏以获得正确的数学排版）
 *   - displaymjx 环境用于块级公式（<math display="block">）
 */
(function () {
    'use strict';

    // 防止重复初始化（例如脚本被多次注入）
    if (window.__wikiMathJaxInitialized) return;
    window.__wikiMathJaxInitialized = true;

    var SMJ_SELECTOR = 'span.smj-container';
    var MATHJAX_SRC = '/_common/mathjax/tex-chtml-full.js';

    // ===== 与 SimpleMathJax (jmnote/SimpleMathJax) 一致的 MathJax v3 配置 =====
    window.MathJax = {
        tex: {
            inlineMath: [['[math]', '[/math]']],
            displayMath: [],
            processEnvironments: true,
            processRefs: false,
            processEscapes: false,
            packages: { '[+]': ['autoload', 'mhchem'] },
            macros: {
                AA: "{\u00c5}",
                alef: "{\\aleph}",
                alefsym: "{\\aleph}",
                Alpha: "{\\mathrm{A}}",
                and: "{\\land}",
                ang: "{\\angle}",
                Bbb: "{\\mathbb}",
                Beta: "{\\mathrm{B}}",
                bold: "{\\mathbf}",
                bull: "{\\bullet}",
                C: "{\\mathbb{C}}",
                Chi: "{\\mathrm{X}}",
                clubs: "{\\clubsuit}",
                cnums: "{\\mathbb{C}}",
                Complex: "{\\mathbb{C}}",
                coppa: "{\u03D9}",
                Coppa: "{\u03D8}",
                Dagger: "{\\ddagger}",
                Digamma: "{\u03DC}",
                darr: "{\\downarrow}",
                dArr: "{\\Downarrow}",
                Darr: "{\\Downarrow}",
                dashint: "{\\unicodeInt{x2A0D}}",
                ddashint: "{\\unicodeInt{x2A0E}}",
                diamonds: "{\\diamondsuit}",
                empty: "{\\emptyset}",
                Epsilon: "{\\mathrm{E}}",
                Eta: "{\\mathrm{H}}",
                euro: "{\u20AC}",
                exist: "{\\exists}",
                geneuro: "{\u20AC}",
                geneuronarrow: "{\u20AC}",
                geneurowide: "{\u20AC}",
                H: "{\\mathbb{H}}",
                hAar: "{\\Leftrightarrow}",
                harr: "{\\leftrightarrow}",
                Harr: "{\\Leftrightarrow}",
                hearts: "{\\heartsuit}",
                image: "{\\Im}",
                infin: "{\\infty}",
                Iota: "{\\mathrm{I}}",
                isin: "{\\in}",
                Kappa: "{\\mathrm{K}}",
                koppa: "{\u03DF}",
                Koppa: "{\u03DE}",
                lang: "{\\langle}",
                larr: "{\\leftarrow}",
                Larr: "{\\Leftarrow}",
                lArr: "{\\Leftarrow}",
                lrarr: "{\\leftrightarrow}",
                Lrarr: "{\\Leftrightarrow}",
                lrArr: "{\\Leftrightarrow}",
                Mu: "{\\mathrm{M}}",
                N: "{\\mathbb{N}}",
                natnums: "{\\mathbb{N}}",
                Nu: "{\\mathrm{N}}",
                O: "{\\emptyset}",
                oiint: "{\\unicodeInt{x222F}}",
                oiiint: "{\\unicodeInt{x2230}}",
                ointctrclockwise: "{\\unicodeInt{x2233}}",
                officialeuro: "{\u20AC}",
                Omicron: "{\\mathrm{O}}",
                or: "{\\lor}",
                P: "{\u00B6}",
                pagecolor: ["", 1],
                part: "{\\partial}",
                plusmn: "{\\pm}",
                Q: "{\\mathbb{Q}}",
                R: "{\\mathbb{R}}",
                rang: "{\\rangle}",
                rarr: "{\\rightarrow}",
                Rarr: "{\\Rightarrow}",
                rArr: "{\\Rightarrow}",
                real: "{\\Re}",
                reals: "{\\mathbb{R}}",
                Reals: "{\\mathbb{R}}",
                Rho: "{\\mathrm{P}}",
                sdot: "{\\cdot}",
                sampi: "{\u03E1}",
                Sampi: "{\u03E0}",
                sect: "{\\S}",
                spades: "{\\spadesuit}",
                stigma: "{\u03DB}",
                Stigma: "{\u03DA}",
                sub: "{\\subset}",
                sube: "{\\subseteq}",
                supe: "{\\supseteq}",
                Tau: "{\\mathrm{T}}",
                textvisiblespace: "{\u2423}",
                thetasym: "{\\vartheta}",
                uarr: "{\\uparrow}",
                uArr: "{\\Uparrow}",
                Uarr: "{\\Uparrow}",
                unicodeInt: ["{\\mathop{\\vcenter{\\mathchoice{\\huge\\unicode{#1}\\,}{\\unicode{#1}}{\\unicode{#1}}{\\unicode{#1}}}\\,}\\nolimits}", 1],
                varcoppa: "{\u03D9}",
                varstigma: "{\u03DB}",
                varointclockwise: "{\\unicodeInt{x2232}}",
                vline: ["{\\smash{\\large\\lvert #1}", 0],
                weierp: "{\\wp}",
                Z: "{\\mathbb{Z}}",
                Zeta: "{\\mathrm{Z}}"
            },
            environments: {
                displaymjx: ["", ""]
            }
        },
        options: {
            ignoreHtmlClass: 'mathjax_ignore|comment|diff-context|diff-addedline|diff-deletedline',
            processHtmlClass: 'mathjax_process|smj-container'
        },
        chtml: {
            scale: 1,
            displayAlign: 'left'
        }
        // 使用 tex-chtml-full.js：全部组件内联，无需 loader.load
    };

    /**
     * 动态加载本地 MathJax 脚本，并等待 startup 完成。
     * @returns {Promise<void>}
     */
    function loadMathJax() {
        return new Promise(function (resolve, reject) {
            if (window.MathJax && window.MathJax.startup && window.MathJax.startup.promise) {
                window.MathJax.startup.promise.then(resolve, reject);
                return;
            }
            var script = document.createElement('script');
            script.src = MATHJAX_SRC;
            script.async = true;
            script.onload = function () {
                if (window.MathJax && window.MathJax.startup && window.MathJax.startup.promise) {
                    window.MathJax.startup.promise.then(resolve, reject);
                } else {
                    resolve();
                }
            };
            script.onerror = function () {
                reject(new Error('MathJax 本地资源加载失败: ' + MATHJAX_SRC));
            };
            document.head.appendChild(script);
        });
    }

    /**
     * 找出尚未渲染的公式容器（内部还没有 mjx-container 的 smj-container span）。
     */
    function pendingElements() {
        return Array.prototype.filter.call(
            document.querySelectorAll(SMJ_SELECTOR),
            function (span) { return !span.querySelector('mjx-container'); }
        );
    }

    var rendering = false;

    /**
     * 渲染当前页面中所有待渲染的公式，并把占位透明度恢复为 1。
     */
    function renderMath() {
        if (rendering) return;
        var elements = pendingElements();
        if (!elements.length) return;

        rendering = true;
        loadMathJax()
            .then(function () {
                return window.MathJax.typesetPromise(elements);
            })
            .then(function () {
                elements.forEach(function (span) { span.style.opacity = 1; });
            })
            .catch(function (err) {
                console.error('[wiki-math] 公式渲染失败:', err);
            })
            .finally(function () {
                rendering = false;
            });
    }

    /**
     * 监听内容容器变化：wiki 页面切换 / 动态注入内容时自动渲染新公式。
     * 渲染过程产生的 DOM 变化由 pendingElements() 过滤（已渲染的 span 不再处理）。
     */
    function observeContent() {
        var container = document.getElementById('mw-content-text');
        if (!container) return;
        var timer = null;
        var observer = new MutationObserver(function () {
            if (timer) clearTimeout(timer);
            timer = setTimeout(renderMath, 60);
        });
        observer.observe(container, { childList: true, subtree: true });
    }

    function init() {
        renderMath();
        observeContent();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
