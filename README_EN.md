# Terraria Wiki Offline

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Android%20%7C%20iOS%20%7C%20macOS-informational)
![.NET](https://img.shields.io/badge/.NET-11.0-512BD4)
![Version](https://img.shields.io/badge/version-0.4.0-success)
![License](https://img.shields.io/badge/license-Apache--2.0-blue)

[简体中文](README.md) | English

An offline Terraria wiki reader built with .NET MAUI Blazor Hybrid.

The app crawls wiki page content, images, audio and video into a local SQLite database, then renders them inside an embedded WebView through a built-in local HTTP server — so you get the same layout and reading experience as the original site even with no network connection. It currently supports four wikis — vanilla Terraria, Calamity, Fargo's Mods and Thorium — in both Chinese and English, for a total of 8 data sources.

![App screenshot](Screenshot/main.png)

## Features

- **Offline reading**: Articles, images, audio and video are all stored locally, no network required. A built-in local server renders content in an iframe, preserving the original site's styles, infoboxes, navboxes, collapsible tables and math formulas (MathJax).
- **In-wiki navigation**: Click links to navigate without reloading. Redirects and `#anchors` are resolved automatically; content collapsed behind an anchor is expanded and scrolled into view.
- **Image viewer**: Click an image in an article to open it with Viewer.js — zoom and drag to inspect the original.
- **In-page search**: "Find in page" in the function bar is implemented as a native panel and shows the current match index and total count in real time.
- **Desktop context menu**: Copy selected text or images, open an article in a new tab, or open the current article's original page.
- **Live search**: The top bar search box matches keywords against the local SQLite database, with a dropdown preview of articles and redirect targets, debounced input. The maximum result count is configurable (25 / 50 / 75 / 100).
- **All pages**: Lists every article in the database with keyword filtering and virtual scrolling — handy for lookup by name.
- **Multiple tabs**: Up to 10 tabs open at once, each maintaining its own title, scroll position and browsing history. On desktop the tab strip sits in the top bar; on narrow screens it collapses into the function bar panel.
- **Favorites and history**: Star the current article from the top bar. Browsing history is grouped by time and can be cleared in one tap. Each wiki uses its own database file, so favorites and history never mix.
- **Multi-wiki support**: Filter by language on the Data Management page and click a card to switch wikis — no app restart needed.
- **Data management**: Download online (all content or text only), incrementally update pages, download or delete image assets, export and import `.pkg` data packages, delete a database — plus database size, page count, redirect count, asset count and last update time.
- **Task management**: Download tasks support pause, resume, interruption recovery and retry of failed items. They pause automatically when offline and can continue once you're back online. The failure list can be cleared separately.
- **Theme and appearance**: Interface language supports Chinese and English. Interface theme follows system, or forces light / dark. Content theme follows the interface or uses the wiki's original colors.
- **Reading zoom**: The function bar provides zoom from 50% to 200% (10% steps, with one-tap reset to 100%).
- **Desktop extras**: Always-on-top window, Windows floating window (floating bubble or pinned mini window, with a tray icon for exit), and mouse side-button back navigation.
- **Mobile support**: On Android there are three permission toggles (network, notifications, background execution) and downloads keep running in the background. On iOS the screen dims automatically during long downloads while idle, to prevent burn-in.

## Interface Guide

| Location | Description |
| --- | --- |
| Home | Read articles; the top bar offers back, home, favorite and search |
| Favorites | View favorited articles and remove individual entries |
| History | Browsing history grouped by time, clearable in one tap |
| All pages | Full article index with keyword filtering |
| Data Management | Choose a wiki, download and update data, export data packages |
| Settings | Language, theme, search, downloads, permissions, data location, logs and about |
| Function bar | The "More" button at the right of the top bar: find in page, always on top, floating window, zoom, tab management |
| Logs | Bottom of the sidebar, shows the current task and runtime logs |

## Supported Wikis

| Wiki | Language | Data directory |
| --- | --- | --- |
| 泰拉瑞亚中文百科 | Chinese | `Terraria_Wiki_zh` |
| Terraria Wiki | English | `Terraria_Wiki_en` |
| 灾厄中文百科 | Chinese | `Calamity_Wiki_zh` |
| Calamity Mod Wiki | English | `Calamity_Wiki_en` |
| 法狗中文百科 | Chinese | `Fargo_Wiki_zh` |
| Fargo's Mods Wiki | English | `Fargo_Wiki_en` |
| 瑟银中文百科 | Chinese | `Thorium_Wiki_zh` |
| Thorium Mod Wiki | English | `Thorium_Wiki_en` |

## Supported Platforms

| Platform | Minimum version |
| --- | --- |
| Windows | Windows 10 1809 (10.0.17763) |
| Android | Android 7.0 (API 24) |
| iOS / iPadOS | iOS 16.4 |

## Installation

Get the installers from the [Releases](https://github.com/BigBearKing/terraria-wiki-offline/releases) page. Current version is v0.4.0.

- **Windows**: No installation needed — extract and run. Requires the Microsoft Edge WebView2 runtime (usually built into Windows 11; on Windows 10 you may need to install it manually).
- **Android**: Install the APK directly. On first use it is recommended to grant network, notification and background execution permissions under "Settings - Permissions", otherwise downloads may be interrupted by the system.
- **iOS / iPadOS**: Not on the App Store yet — you need to sign and sideload it yourself. Not fully tested; runtime issues may occur.
- **macOS**: Untested and requires building from source. Not supported for now; expect problems.

## Screenshots

| Search | Multiple tabs |
| --- | --- |
| ![](Screenshot/search.png) | ![](Screenshot/tabs.png) |

| Light mode | Favorites and history |
| --- | --- |
| ![](Screenshot/light.png) | ![](Screenshot/collection.png) |

| Data Management | Data details |
| --- | --- |
| ![](Screenshot/data.png) | ![](Screenshot/data-detail.png) |

| Settings | Floating window (Windows) |
| --- | --- |
| ![](Screenshot/settings.png) | ![](Screenshot/floating.png) |

## Getting Started

The app ships without any wiki data, so you need to load data on first launch. Pick either method below.

### Option 1: Import a data package (recommended)

Crawling the whole site online is rate-limited by the source sites, and a full download may take hours or longer — importing an offline data package is recommended instead.

1. Download the offline data package (`.pkg` file).
   - Quark cloud drive (China): <https://pan.quark.cn/s/9e4116487189>, extraction code `J2zU`
2. Open the app and go to "Settings - Data - Import data".
3. Select the `.pkg` file and wait for verification and import to finish.
4. Once the import completes you can browse offline.

### Option 2: Download online

1. Go to "Data Management", filter by language and select the wiki you want.
2. Choose what to download:
   - **Base data**: Text content, properties and link relations only, about 200 MB.
   - **All content**: Base data plus images, audio and video — can reach several GB.
3. You can pause or resume at any time. If the network drops, the task pauses automatically and continues once the connection is back.
4. If you only downloaded the base data, you can click "Download assets" later on the same page to fetch images.
5. Due to server limits, downloads may take 5 hours or more.

### Keeping data up to date

- Tap "Check for updates" in "Data Management - Details" to fetch only pages that changed — much faster than a full download.
- Enable "Auto update pages" in "Settings - Downloads" and the app will check and update pages in the background after launch.

## FAQ

**The article says "Please download data first", or the page list is empty?**

The data for that wiki hasn't been loaded yet. Import a data package or download online as described in "Getting Started" above. If only images are missing, tap "Download assets" in "Data Management - Details".

**The data takes up too much space?**

Tap "Delete assets" in "Data Management - Details" to keep text content only; you can re-download images at any time. To free space completely, tap "Delete database" — note this also clears that wiki's favorites and browsing history.

**Want to move the data to another disk (Windows)?**

Go to "Settings - Data - Save location", choose "Custom directory" and tap "Apply" — the app will migrate existing data. Do not close the app during migration; afterwards you can delete the old directory or keep it as a backup.

**A download was interrupted or produced many failed items?**

Downloads support resume. Re-open the "Data Management" page to see unfinished tasks and continue. A "Retry failed items" button appears for retries; you can also clear the failure list in "Settings - Downloads" and download again.

**Why is the download so slow?**

Data comes from each wiki's public API, and the sites rate-limit requests. Increasing the download thread count in Settings is not recommended — too frequent requests may get you blocked by the server.

**Windows says no WebView detected and the app won't start?**

Install the [Microsoft Edge WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/) and re-open the app.

**Favorites and history disappeared after switching wikis?**

Each wiki uses its own data directory and database, and favorites and history are stored per wiki. Switch back to the original wiki and your records will be there.

**Where is data stored by default?**

On Windows the default is `%LOCALAPPDATA%\BigBearKing\com.bigbearking.terrariawiki`; on other platforms it is the app's private directory. You can view the current path in "Settings - Data - Save location".

**How do I report a problem?**

Export the logs in "Settings - Logs" first, then open an [Issue](https://github.com/BigBearKing/terraria-wiki-offline/issues) with the app version, OS version and steps to reproduce.

## Building from Source

You need the .NET 11 SDK with the MAUI workload installed:

```bash
dotnet workload install maui
```

Build for a specific platform (Windows example):

```bash
dotnet build Terraria_Wiki/Terraria_Wiki.csproj -f net11.0-windows10.0.19041.0 -c Release
```

The other target frameworks are `net11.0-android`, `net11.0-ios` and `net11.0-maccatalyst` (Apple platforms are unavailable on Linux). To publish an unsigned iOS IPA you can trigger the `Build Unsigned iOS IPA` workflow manually in GitHub Actions; artifacts are kept for 7 days.

> **Note**: .NET 11 has not reached GA yet. This project uses RC.1 (`SDK 11.0.100-rc.1` / MAUI `11.0.0-rc.1.26451.6`). Once the stable release is out, change the dependency versions to the non-`rc` releases.

## Data Sources and Copyright

- The game *Terraria* and its related art assets are copyrighted by **Re-Logic**.
- The app processes content from various wikis; their original licenses, attribution and redistribution terms are documented in [CONTENT_ATTRIBUTION.md](CONTENT_ATTRIBUTION.md). Wiki pages, images, audio, video and game assets are **not** covered by this repository's Apache-2.0 license.
- The original client code of the app is licensed under Apache-2.0. Third-party components are provided under their own licenses — see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- This software is not affiliated with **wiki.gg** or **Re-Logic** in any way; it is a hobby project developed by an individual.

## Credits

- [Terraria Chinese Wiki](https://terraria.wiki.gg/zh/wiki/Terraria_Wiki) and the mod wiki communities: all content and data structures in this app come from community contributors' editing and maintenance.
- [.NET MAUI](https://github.com/dotnet/maui) and [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) (11.0.0-rc.1): cross-platform UI and frontend component frameworks.
- [Microsoft.Web.WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) (1.0.4191.47): WebView engine on Windows.
- [HtmlAgilityPack](https://github.com/zzzprojects/html-agility-pack) (1.12.4): page data cleaning and parsing.
- [sqlite-net-pcl](https://github.com/praeclarum/sqlite-net) (1.11.285): local database and persistence.
- [LuYao.TlsClient](https://github.com/coderbusy/luyao-tls-client) (1.2.0): TLS fingerprint handling for some sites.
- [handy-scroll](https://amphiluke.github.io/handy-scroll/): scrollbar handling for wide lists.
- [Viewer.js](https://fengyuanchen.github.io/viewerjs/): image viewer.
- [MathJax](https://www.mathjax.org/): math formula rendering.
- [Fluenticons](https://fluenticons.co/): source of the in-app icons.
- [Nunito](https://fonts.google.com/specimen/Nunito): UI and page font, licensed under the SIL Open Font License 1.1 (full text at [licenses/OFL-1.1.txt](licenses/OFL-1.1.txt)).

## Sponsorship

This is a personal open-source project with no ads. Donations go toward the iOS App Store listing fee.

If you'd like to support the project, donations can be sent via PayPal to [paypal.me/BigBearKing](https://paypal.me/BigBearKing).

Total donations so far: 180 CNY.

<details>
<summary>Donation thanks</summary>

\*浩、L\*B、\*\*辉、\*南、\*\*鑫、不卜庐七七、\*服、冷月酌OMO

</details>

If this project helps you, feel free to star it on GitHub.

## License

The original client code of this project is licensed under [Apache-2.0](LICENSE). Wiki content, game assets and third-party components are not covered by that license; please also read [CONTENT_ATTRIBUTION.md](CONTENT_ATTRIBUTION.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
