import time
import requests

# ===== 改这里 =====
API_URL = "https://calamity.huijiwiki.com/api.php?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace=0&gapfilterredir=nonredirects&gaplimit=max"
PAGE_URL = "https://calamity.huijiwiki.com/wiki/首页"
# ===================


def test(label, make_session):
    try:
        session = make_session()

        # API
        start = time.perf_counter()
        print("  API ... ", end="", flush=True)
        api_resp = session.get(API_URL, timeout=15)
        api_body = api_resp.text
        elapsed = int((time.perf_counter() - start) * 1000)
        ok = api_resp.status_code == 200
        print(f"{'✅ 200' if ok else '❌ ' + str(api_resp.status_code)} ({len(api_body)}B, {elapsed}ms)")
        if not ok and len(api_body) < 300:
            print(f"     → {api_body.strip()}")

        # 页面
        start = time.perf_counter()
        print("  页面 ... ", end="", flush=True)
        page_resp = session.get(PAGE_URL, timeout=15)
        page_body = page_resp.text
        elapsed = int((time.perf_counter() - start) * 1000)
        ok = page_resp.status_code == 200
        print(f"{'✅ 200' if ok else '❌ ' + str(page_resp.status_code)} ({len(page_body)}B, {elapsed}ms)")
        if not ok and len(page_body) < 300:
            print(f"     → {page_body.strip()}")
        print(page_resp.cookies)
    except Exception as ex:
        print(f"  💥 {ex}")


# 版本1：默认 requests
print("=" * 50)
print("【版本1】默认 requests（无伪装）")
print("=" * 50)
test("v1 默认", lambda: requests.Session())

# 版本2：Chrome UA
print()
print("=" * 50)
print("【版本2】Chrome UA")
print("=" * 50)
test("v2 ChromeUA", lambda: (
    s := requests.Session(),
    s.headers.update({"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"}),
    s
)[2])

# 版本3：Chrome UA + Accept + Language
print()
print("=" * 50)
print("【版本3】Chrome UA + Accept 头")
print("=" * 50)
test("v3 Accept", lambda: (
    s := requests.Session(),
    s.headers.update({
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "zh-CN,zh;q=0.9",
    }),
    s
)[2])

# 版本4：完整伪装头 + Referer
print()
print("=" * 50)
print("【版本4】完整伪装 + Referer")
print("=" * 50)
test("v4 完整", lambda: (
    s := requests.Session(),
    s.headers.update({
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
        "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
        "Referer": "https://calamity.huijiwiki.com/",
    }),
    s
)[2])

print()
print("完成")
