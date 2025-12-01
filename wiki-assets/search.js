const input = document.getElementById('search-input');
const btn = document.getElementById('search-button');
const resultsContainer = document.getElementById('search-results');
var indexjson = {};
var redirectjson = {};
await fetch('redirect.json') // 替换成你的 JSON 文件路径或 API URL
    .then(response => response.json())
    .then(data => { redirectjson = data });
await fetch('searchtitleindex.json') // 替换成你的 JSON 文件路径或 API URL
    .then(response => response.json())
    .then(data => { indexjson = data });
const pagefind = await import('/_pagefind/pagefind.js');
function replacePunctuationWithSpaces(inputString) {
    const punctuationRegex = /[!"#$%&'()*+,-./:;<=>?@[\\\]^_`{|}~，。？！；：“”【】（）《》、]/g;
    return inputString.replace(punctuationRegex, ' ');
}
async function searchJsonKeys(keyword) {
    keyword = replacePunctuationWithSpaces(keyword);
    const lowerKeyword = keyword.toLowerCase();
    if (lowerKeyword.trim() === '') {
        resultsContainer.style.display = 'none';
        resultsContainer.innerHTML = '';
        return;
    }
    resultsContainer.style.display = 'block';
    resultsContainer.innerHTML = '<div class="loading">正在搜索...</div>';
    const indexarr = Object.entries(indexjson);
    const redirectarr = Object.entries(redirectjson);
    const matches = indexarr.filter(([key, value]) => {
        return key.toLowerCase().includes(lowerKeyword);
    }).map(([key, value]) => {
        return [key, value, null];
    });
    const matchesrd = redirectarr.filter(([key, value]) => {
        return key.toLowerCase().includes(lowerKeyword);
    });
    matchesrd.sort((a, b) => {
        const lenA = a[0].length; // 键 A 的长度
        const lenB = b[0].length; // 键 B 的长度


        // 升序排序：短标题在前 (lenA - lenB)
        return lenA - lenB;

        // 降序排序：长标题在前 (lenB - lenA)

    }
    );


    // 对匹配项进行排序（按键的长度）
    matches.sort((a, b) => {
        const lenA = a[0].length; // 键 A 的长度
        const lenB = b[0].length; // 键 B 的长度


        // 升序排序：短标题在前 (lenA - lenB)
        return lenA - lenB;

        // 降序排序：长标题在前 (lenB - lenA)

    }
    );
    const lookupMap = new Map(indexarr);
    let enoughMatches = false;
    for (const [key, value] of matchesrd) {
        if (matches.length >= 20) {
            enoughMatches = true;
            break;
        }
        matches.push([key, lookupMap.get(value.replace(/#.*/, '')), value]);
    }
    if (!enoughMatches) {
        const search = await pagefind.search(lowerKeyword, {
            pageSize: 50
        });
        for (const result of search.results) {
            if (matches.length >= 20) {
                break;
            }
            const data = await result.data();
            let resulttitle = data.url;

            // 处理 URL 格式
            if (resulttitle.startsWith('/')) {
                resulttitle = resulttitle.slice(1);
            }
            resulttitle = resulttitle.replace(/\.html$/, '');

            // 检查重复
            const isDuplicate = matches.some(match => {
                return match[0] === resulttitle;
            });

            // 如果不是重复项，则添加
            if (!isDuplicate) {
                matches.push([resulttitle, data.excerpt, null]);
            }
        }
    }
    const regex = new RegExp(keyword, 'gi');
    if (matches.length === 0) {
        resultsContainer.innerHTML = '<div class="no-results">未找到相关结果</div>';
        return;
    }
    resultsContainer.innerHTML = '';
    matches.forEach(([key, value, redirect]) => {
        const highlightedText = key.replace(regex, (match) => `<mark>${match}</mark>`);
        const item = document.createElement('div');
        item.className = 'result-item';
        if (redirect) {
            item.innerHTML = `<div class="title-redirect">
                        <a title="${key}">${highlightedText}</a>
                        <p class="redirect-p">↪重定向: ${redirect}</p>
                        </div>
                        
                        <p class="value-p">${value}</p>
                    `;
        } else {
            item.innerHTML = `
                        <a title="${key}">${highlightedText}</a>
                        <p class="value-p">${value}</p>
                    `;
        }
        resultsContainer.appendChild(item);
    });
}
// 3. 绑定事件
// 点击按钮搜索
btn.addEventListener('click', () => {

    searchJsonKeys(input.value.trim());


});

// 按下回车键也可以搜索
input.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {

        searchJsonKeys(input.value.trim());


    }
});

// 点击页面其他地方关闭搜索框
document.addEventListener('click', (e) => {
    if (!document.querySelector('#search').contains(e.target)) {
        resultsContainer.innerHTML = '';
        resultsContainer.style.display = 'none';
    }
});