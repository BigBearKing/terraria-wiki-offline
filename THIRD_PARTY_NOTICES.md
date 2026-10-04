# Third-party software notices

This file identifies software and fonts that are committed to this repository
or distributed in the application. Their terms apply to those components; the
root Apache-2.0 license does not replace them.

| Component | Bundled path | License | Source |
| --- | --- | --- | --- |
| Viewer.js v1.11.6 | `Terraria_Wiki/Resources/Raw/Web/_common/viewer/` | MIT | https://github.com/fengyuanchen/viewerjs |
| MathJax | `Terraria_Wiki/Resources/Raw/Web/_common/mathjax/` | Apache-2.0 | https://www.mathjax.org/ |
| handy-scroll v2.0.6 | `Terraria_Wiki/Resources/Raw/Web/_common/handy-scroll.js` | MIT | https://github.com/Amphiluke/handy-scroll |
| Open Sans Regular v1.10 | `Terraria_Wiki/Resources/Fonts/OpenSans-Regular.ttf` | Apache-2.0 | https://fonts.google.com/specimen/Open+Sans |
| Nunito | `Terraria_Wiki/Resources/Raw/Web/*/nunito.ttf` (7 copies) | SIL Open Font License 1.1 | https://fonts.google.com/specimen/Nunito |
| Fluent UI System Icons | `Terraria_Wiki/Components/Icons/Icon.razor` (33 paths) | MIT | https://github.com/microsoft/fluentui-system-icons |

## Open Sans is Apache-2.0, not OFL

The bundled `OpenSans-Regular.ttf` is version 1.10. Its embedded font
metadata (name ID 13 and 14 of the `name` table) reads
`Licensed under the Apache License, Version 2.0` and
`http://www.apache.org/licenses/LICENSE-2.0`, with
`Digitized data copyright (c) 2010-2011, Google Corporation.`

Open Sans 1.x was released under Apache-2.0; only the 2020 and later
releases moved to the SIL Open Font License. This file is an Apache-2.0
font. The repository root contains the same Apache-2.0 legal text for
convenience, but this font remains separately copyrighted by Google and is
licensed under its own Apache-2.0 grant. No OFL notice applies to this file.

## Nunito is OFL 1.1 and must keep that license

Copyright 2014 The Nunito Project Authors (https://github.com/googlefonts/nunito)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
The complete license text is in `licenses/OFL-1.1.txt` and at
https://openfontlicense.org/

The OFL requires that every distributed copy carry the copyright notice and
the license text, and requires that the font "must be distributed entirely
under this license, and must not be distributed under any other license."
The root Apache-2.0 license therefore does not apply to `nunito.ttf`. Ship
`licenses/OFL-1.1.txt` with any build that contains the font.

## Viewer.js MIT license

Copyright 2015-present Chen Fengyuan

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Apache-2.0 components

The complete Apache-2.0 text is in the repository root `LICENSE` file. It
also applies to MathJax as specified by its upstream project. Preserve any
copyright and NOTICE information present in upstream MathJax distributions.

## handy-scroll MIT license

Copyright (c) 2018 Amphiluke

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Fluent UI System Icons MIT license

Copyright (c) Microsoft Corporation.

The icon paths in `Terraria_Wiki/Components/Icons/Icon.razor` are taken from
the Microsoft Fluent UI System Icons set (24x24 grid). `README.md` refers to
this set as "Fluenticons". The same permission and warranty text as the
handy-scroll MIT license above applies to these paths.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

## NuGet package dependencies

These packages are not committed to this repository as source. The Windows
Release build is NativeAOT and self-contained, so their code is linked into the
distributed binary. Ship this list with any build.

| Package | Version | License | Copyright |
| --- | --- | --- | --- |
| HtmlAgilityPack | 1.12.4 | MIT | Copyright (c) ZZZ Projects Inc. |
| LuYao.TlsClient | 1.2.0 | MIT | The upstream nuspec declares MIT and states no copyright holder. |
| sqlite-net-pcl | 1.11.285 | MIT | Copyright (c) Krueger Systems, Inc. |
| Microsoft.Maui.Controls | 11.0.0-rc.1.26451.6 | MIT | (c) Microsoft Corporation. All rights reserved. |
| Microsoft.AspNetCore.Components.WebView.Maui | 11.0.0-rc.1.26451.6 | MIT | (c) Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Logging.Debug | 11.0.0-rc.1.26425.128 | MIT | (c) Microsoft Corporation. All rights reserved. |
| Microsoft.Web.WebView2 | 1.0.4191.47 | BSD-3-Clause | (C) Microsoft Corporation. All rights reserved. See `licenses/WebView2-LICENSE.txt` and `licenses/WebView2-NOTICE.txt`. |

Transitive packages pulled in by `sqlite-net-pcl`:

| Package | Version | License | Copyright |
| --- | --- | --- | --- |
| SQLitePCLRaw.core | 3.0.3 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SQLitePCLRaw.provider.e_sqlite3 | 3.0.3 | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| SourceGear.sqlite3 | 3.53.3 | Public domain | https://sqlite.org/copyright.html |

### WebView2 runtime is a separate license

The `Microsoft.Web.WebView2` package above is only the .NET interop assembly and
is BSD-3-Clause. The WebView2 **Runtime** that Windows users install is not
covered by that license; it is governed by the Microsoft software license terms
that accompany the runtime installer. Do not present the BSD-3-Clause notice as
covering the runtime.

## Distribution of notices

The project file packages `LICENSE`, `NOTICE`, this notice, the content
attribution, the OFL text, and the WebView2 license/notice as application
assets. Windows builds also copy them next to the executable. Keep these files
with every release archive and make them available from the application's
About page where the platform permits.
