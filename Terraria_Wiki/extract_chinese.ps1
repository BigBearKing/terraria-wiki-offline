# extract_chinese.ps1
# 扫描项目中所有源文件，提取中文字符串，生成国际化 JSON 文件

$projectRoot = "C:\Users\BBK\source\repos\MAUI\Terraria_Wiki"
$outputJson = Join-Path $projectRoot "i18n_strings.json"

# 要扫描的文件扩展名
$extensions = @("*.cs", "*.razor", "*.xaml", "*.html", "*.js", "*.css")

# 要排除的目录
$excludeDirs = @(
	[regex]::Escape("\bin\"),
	[regex]::Escape("\obj\"),
	[regex]::Escape("\.nuget\"),
	[regex]::Escape("\node_modules\"),
	[regex]::Escape("\.git\"),
	[regex]::Escape("\Debug\"),
	[regex]::Escape("\Release\"),
	[regex]::Escape("\viewer\")
)

# 中文 Unicode 范围：基本汉字 + 扩展 A + 兼容汉字
$chineseRegex = '[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff]'

# 结果容器
$result = [ordered]@{}

# -------------------------------------------------------------------
# 工具函数：从行中移除代码注释（保留字符串字面量中的内容）
# -------------------------------------------------------------------
function Remove-CodeComments {
	param([string]$line)

	# 1. 移除 /* ... */ 多行注释块
	$line = [regex]::Replace($line, '/\*.*?\*/', ' ')

	# 2. 移除 @* ... *@ Razor 注释块
	$line = [regex]::Replace($line, '@\*.*?\*@', ' ')

	# 3. 移除 // 单行注释（保护字符串内的 // 不被移除）
	#    策略：用 Matches 找到所有字符串，倒序替换为标记，移除 //，再倒序恢复
	$marker = '____PROTSTR____'
	$strMatches = [regex]::Matches($line, '("(?:[^"\\]|\\.)*")')
	# 倒序处理，避免索引偏移
	for ($i = $strMatches.Count - 1; $i -ge 0; $i--) {
		$m = $strMatches[$i]
		$line = $line.Remove($m.Index, $m.Length).Insert($m.Index, $marker + $i + '____')
	}
	# 移除 // 到行尾
	$line = $line -replace '//.*$', ''
	# 恢复字符串（倒序）
	$markedMatches = [regex]::Matches($line, [regex]::Escape($marker) + '\d+____')
	for ($i = $markedMatches.Count - 1; $i -ge 0; $i--) {
		$m = $markedMatches[$i]
		$num = [int]($m.Value -replace [regex]::Escape($marker) -replace '____', '')
		$orig = $strMatches[$num].Value
		$line = $line.Remove($m.Index, $m.Length).Insert($m.Index, $orig)
	}

	return $line
}

# -------------------------------------------------------------------
# 工具函数：从 C# / Razor 代码行中提取含中文的字符串字面量
# -------------------------------------------------------------------
function Extract-CSharpStrings {
	param([string]$line, [string]$ext)

	# 先移除注释
	$line = Remove-CodeComments -line $line

	$found = @()

	# 1. 匹配 $"..." 插值字符串 (含中文)
	$interpolatedPattern = '\$"((?:[^"\\]|\\.|(?:\{[^}]*\}))*)"'
	$matches_interp = [regex]::Matches($line, $interpolatedPattern)
	foreach ($m in $matches_interp) {
		$raw = $m.Groups[1].Value
		if ($raw -match $chineseRegex) {
			# 将 {variable} 替换为 {0}, {1}, ...
			$template = $raw
			$varMatches = [regex]::Matches($raw, '\{[^}]*\}')
			for ($idx = $varMatches.Count - 1; $idx -ge 0; $idx--) {
				$template = $template.Remove($varMatches[$idx].Index, $varMatches[$idx].Length).Insert($varMatches[$idx].Index, "{$idx}")
			}
			$found += @{ raw = $raw; template = $template }
		}
	}

	# 2. 匹配普通 "..." 字符串 (含中文)，但要排除已经在 $"..." 中的
	# 先用一个占位符把 $"..." 替换掉，再匹配普通字符串
	$tempLine = [regex]::Replace($line, '\$"([^"\\]|\\.|(?:\{[^}]*\}))*"', '___INTERP___')
	$plainPattern = '"((?:[^"\\]|\\.)*)"'
	$matches_plain = [regex]::Matches($tempLine, $plainPattern)
	foreach ($m in $matches_plain) {
		$raw = $m.Groups[1].Value
		if ($raw -match $chineseRegex) {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	# 3. 匹配 '@"..."' 逐字字符串 (含中文)
	$verbatimPattern = '@"((?:[^"]|"")*)"'
	$matches_verb = [regex]::Matches($line, $verbatimPattern)
	foreach ($m in $matches_verb) {
		$raw = $m.Groups[1].Value -replace '""', '"'
		if ($raw -match $chineseRegex) {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	return $found
}

# -------------------------------------------------------------------
# 工具函数：从 XAML 行中提取含中文的属性值
# -------------------------------------------------------------------
function Extract-XamlStrings {
	param([string]$line)

	$found = @()

	# 匹配 Text="..." 或属性="中文值"
	$attrPattern = '(\w+)="([^"]*' + $chineseRegex + '[^"]*)"'
	$matches_attr = [regex]::Matches($line, $attrPattern)
	foreach ($m in $matches_attr) {
		$raw = $m.Groups[2].Value
		$found += @{ raw = $raw; template = $raw }
	}

	return $found
}

# -------------------------------------------------------------------
# 工具函数：从 HTML 行中提取含中文的内容
# -------------------------------------------------------------------
function Extract-HtmlStrings {
	param([string]$line)

	$found = @()

	# 匹配 >中文文本< 或是属性值中的中文
	# 标签间文本
	$textPattern = '>([^<]*' + $chineseRegex + '[^<]*)<'
	$matches_text = [regex]::Matches($line, $textPattern)
	foreach ($m in $matches_text) {
		$raw = $m.Groups[1].Value.Trim()
		if ($raw -and $raw -notmatch '^\s*$') {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	# 属性值中的中文
	$attrPattern = '="([^"]*' + $chineseRegex + '[^"]*)"'
	$matches_attr = [regex]::Matches($line, $attrPattern)
	foreach ($m in $matches_attr) {
		$raw = $m.Groups[1].Value
		if ($raw -match $chineseRegex) {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	return $found
}

# -------------------------------------------------------------------
# 工具函数：从 JS 行中提取含中文的字符串
# -------------------------------------------------------------------
function Extract-JsStrings {
	param([string]$line)

	$found = @()

	# 1. JS 模板字符串 `...${var}...`
	$templatePattern = '`((?:[^`\\]|\\.|(?:\$\{[^}]*\}))*)`'
	$matches_tpl = [regex]::Matches($line, $templatePattern)
	foreach ($m in $matches_tpl) {
		$raw = $m.Groups[1].Value
		if ($raw -match $chineseRegex) {
			$template = $raw
			$varMatches = [regex]::Matches($raw, '\$\{[^}]*\}')
			for ($idx = $varMatches.Count - 1; $idx -ge 0; $idx--) {
				$template = $template.Remove($varMatches[$idx].Index, $varMatches[$idx].Length).Insert($varMatches[$idx].Index, "{$idx}")
			}
			$found += @{ raw = $raw; template = $template }
		}
	}

	# 2. 普通字符串 '...' 或 "..."
	$tempLine = [regex]::Replace($line, '`([^`\\]|\\.|(?:\$\{[^}]*\}))*`', '___TPL___')

	$plainSingle = "'((?:[^'\\]|\\.)*" + $chineseRegex + "(?:[^'\\]|\\.)*)'"
	$matches_s = [regex]::Matches($tempLine, $plainSingle)
	foreach ($m in $matches_s) {
		$raw = $m.Groups[1].Value
		$found += @{ raw = $raw; template = $raw }
	}

	$plainDouble = '"((?:[^"\\]|\\.)*' + $chineseRegex + '(?:[^"\\]|\\.)*)"'
	$matches_d = [regex]::Matches($tempLine, $plainDouble)
	foreach ($m in $matches_d) {
		$raw = $m.Groups[1].Value
		$found += @{ raw = $raw; template = $raw }
	}

	return $found
}

# -------------------------------------------------------------------
# 工具函数：从 CSS 行中提取含中文的内容（跳过注释）
# -------------------------------------------------------------------
function Extract-CssStrings {
	param([string]$line)

	$found = @()

	# 先移除 CSS 注释（/* */）——注释中的中文不是用户可见文本
	$cleanLine = $line -replace '/\*.*?\*/', ' '

	# 匹配 content: "中文" 或 content: '中文' 这样的 CSS content 属性
	$q = "['`"]"
	$contentPattern = 'content\s*:\s*' + $q + '([^''"]*' + $chineseRegex + '[^''"]*)' + $q
	$matches_c = [regex]::Matches($cleanLine, $contentPattern)
	foreach ($m in $matches_c) {
		$raw = $m.Groups[1].Value.Trim()
		if ($raw) {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	return $found
}

# -------------------------------------------------------------------
# 工具函数：从 Razor 行中提取中文文本（含 @变量）
# -------------------------------------------------------------------
function Extract-RazorStrings {
	param([string]$line)

	$found = @()

	# 提取 C# 字符串字面量（和 .cs 一样，内部已做注释过滤）
	$csStrings = Extract-CSharpStrings -line $line -ext ".razor"
	$found += $csStrings

	# Razor 特有的：标签间的中文文本（包含 @variable 插值）
	# 例如：<span>共找到 @totalPageCount 个页面</span>
	# 例如：<span class="text-container">主页</span>

	# 先移除已处理的 C# 字符串和注释，避免重复
	# 注意：只移除 @* *@ 和 /* */ 注释，不做字符串保护（避免保护标记泄漏）
	$tempLine = $line
	$tempLine = [regex]::Replace($tempLine, '@\*.*?\*@', ' ')
	$tempLine = [regex]::Replace($tempLine, '/\*.*?\*/', ' ')
	# 先把 "@variable" 模式的引号去掉，避免之后被当作普通字符串移除
	$tempLine = [regex]::Replace($tempLine, '"(@\w+(?:\.\w+)*)"', '$1')
	$tempLine = [regex]::Replace($tempLine, '\$"([^"\\]|\\.|(?:\{[^}]*\}))*"', '')
	$tempLine = [regex]::Replace($tempLine, '@"((?:[^"]|"")*)"', '')
	$tempLine = [regex]::Replace($tempLine, '"((?:[^"\\]|\\.)*)"', '')

	# 匹配 >中文@变量中文<
	$razorTextPattern = '>([^<]*' + $chineseRegex + '[^<]*)<'
	$matches_rt = [regex]::Matches($tempLine, $razorTextPattern)
	foreach ($m in $matches_rt) {
		$raw = $m.Groups[1].Value.Trim()
		if ($raw -and $raw -match $chineseRegex) {
			# 将 @variable 替换为 {0}, {1}, ...
			$template = $raw
			$varMatches = [regex]::Matches($raw, '@\w+(?:\.\w+)*')
			for ($idx = $varMatches.Count - 1; $idx -ge 0; $idx--) {
				$template = $template.Remove($varMatches[$idx].Index, $varMatches[$idx].Length).Insert($varMatches[$idx].Index, "{$idx}")
			}
			# 跳过纯变量名（无中文实质内容被替换后只剩占位符的情况）
			if ($template -match $chineseRegex) {
				# 清理方法调用残留的空括号，如 {0}() → {0}
				$template = $template -replace '\{(\d+)\}\(\)', '{$1}'
				$found += @{ raw = $raw; template = $template }
			}
		}
	}

	# 属性值中的中文 Razor 表达式（如 title="菜单"）
	$razorAttrPattern = '\b(title|placeholder|alt|aria-label)="([^"]*' + $chineseRegex + '[^"]*)"'
	$matches_ra = [regex]::Matches($tempLine, $razorAttrPattern)
	foreach ($m in $matches_ra) {
		$raw = $m.Groups[2].Value
		if ($raw -match $chineseRegex) {
			$found += @{ raw = $raw; template = $raw }
		}
	}

	return $found
}

# -------------------------------------------------------------------
# 主逻辑：遍历所有文件
# -------------------------------------------------------------------

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  中文文本提取脚本 - 国际化准备" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$allFiles = @()
foreach ($ext in $extensions) {
	$files = Get-ChildItem -Path $projectRoot -Filter $ext -Recurse -File -ErrorAction SilentlyContinue
	foreach ($f in $files) {
		$fullPath = $f.FullName
		$shouldSkip = $false
		foreach ($exDir in $excludeDirs) {
			if ($fullPath -match $exDir) {
				$shouldSkip = $true
				break
			}
		}
		if (-not $shouldSkip) {
			$allFiles += $fullPath
		}
	}
}

# 排序
$allFiles = $allFiles | Sort-Object

Write-Host "找到 $($allFiles.Count) 个需要扫描的文件" -ForegroundColor Green
Write-Host ""

foreach ($filePath in $allFiles) {
	$relativePath = $filePath.Substring($projectRoot.Length).TrimStart('\', '/')
	$ext = [System.IO.Path]::GetExtension($filePath).ToLower()

	# 跳过一些纯二进制或无关文件
	if ($relativePath -match '\\viewer\\' -or $relativePath -match '\.min\.') {
		continue
	}

	$fileStrings = @()

	try {
		$lines = Get-Content -Path $filePath -Encoding UTF8 -ErrorAction Stop
	} catch {
		try {
			$lines = Get-Content -Path $filePath -Encoding Default -ErrorAction Stop
		} catch {
			Write-Host "⚠️  无法读取: $relativePath" -ForegroundColor Yellow
			continue
		}
	}

	for ($lineNum = 0; $lineNum -lt $lines.Count; $lineNum++) {
		$line = $lines[$lineNum]

		# 跳过没有中文字符的行
		if ($line -notmatch $chineseRegex) { continue }

		# 跳过纯注释行（整行只有注释内容，不含实际代码）
		$trimmed = $line.Trim()
		if ($trimmed -match '^//' -or $trimmed -match '^@\*' -or $trimmed -match '^\*@' -or
			$trimmed -match '^#' -or $trimmed -match '^/\*' -or $trimmed -match '^\*/') {
			continue
		}

		$extracted = @()

		switch ($ext) {
			".cs"    { $extracted = Extract-CSharpStrings -line $line -ext $ext }
			".razor" { $extracted = Extract-RazorStrings -line $line }
			".xaml"  { $extracted = Extract-XamlStrings -line $line }
			".html"  { $extracted = Extract-HtmlStrings -line $line }
			".js"    { $extracted = Extract-JsStrings -line $line }
			".css"   { $extracted = Extract-CssStrings -line $line }
			default  { }
		}

		foreach ($ex in $extracted) {
			$template = $ex.template
			$fileStrings += [ordered]@{
				key      = $template
				chinese  = $ex.raw
				template = $template
				line     = $lineNum + 1
			}
		}
	}

	if ($fileStrings.Count -gt 0) {
		$result[$relativePath] = $fileStrings
		Write-Host "✅ $relativePath — $($fileStrings.Count) 条" -ForegroundColor Green
	} else {
		$result[$relativePath] = "无"
		Write-Host "⬜ $relativePath — 无" -ForegroundColor Gray
	}
}

# -------------------------------------------------------------------
# 输出 JSON（i18n_strings.json 格式）
# -------------------------------------------------------------------

# 手动构建 JSON 以保证特殊字符正确编码
$jsonBuilder = [System.Text.StringBuilder]::new()
[void]$jsonBuilder.AppendLine("{")

$fileKeys = @($result.Keys)
for ($i = 0; $i -lt $fileKeys.Count; $i++) {
	$fileKey = $fileKeys[$i]
	$value = $result[$fileKey]
	$comma = if ($i -lt $fileKeys.Count - 1) { "," } else { "" }

	$escapedKey = $fileKey -replace '\\', '\\' -replace '"', '\"'

	if ($value -eq "无") {
		[void]$jsonBuilder.AppendLine("    `"$escapedKey`": `"无`"$comma")
	} else {
		[void]$jsonBuilder.AppendLine("    `"$escapedKey`": [")
		for ($j = 0; $j -lt $value.Count; $j++) {
			$item = $value[$j]
			$itemComma = if ($j -lt $value.Count - 1) { "," } else { "" }

			$escapedChinese = $item.chinese -replace '\\', '\\\\' -replace '"', '\"' -replace "`n", '\n' -replace "`r", '\r' -replace "`t", '\t'

			[void]$jsonBuilder.AppendLine("      {")
			[void]$jsonBuilder.AppendLine("        `"chinese`": `"$escapedChinese`",")
			[void]$jsonBuilder.AppendLine("        `"line`": $($item.line)")
			[void]$jsonBuilder.Append("      }$itemComma")
			[void]$jsonBuilder.AppendLine()
		}
		[void]$jsonBuilder.Append("    ]$comma")
		[void]$jsonBuilder.AppendLine()
	}
}

[void]$jsonBuilder.AppendLine("}")

$jsonContent = $jsonBuilder.ToString()
[System.IO.File]::WriteAllText($outputJson, $jsonContent, [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  提取完成！" -ForegroundColor Green
Write-Host "  输出文件: $outputJson" -ForegroundColor Green
Write-Host "  共扫描: $($allFiles.Count) 个文件" -ForegroundColor Green
Write-Host "  含中文文件: $(($result.Values | Where-Object { $_ -ne '无' }).Count) 个" -ForegroundColor Green
Write-Host "  总字符串数: $(($result.Values | Where-Object { $_ -ne '无' } | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) 条" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
