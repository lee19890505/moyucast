# 生成 GitHub Social preview（1280x640）
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$W = 1280; $H = 640
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# ---------- 背景 ----------
$rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
$c1 = [System.Drawing.ColorTranslator]::FromHtml('#0E0F12')
$c2 = [System.Drawing.ColorTranslator]::FromHtml('#22242B')
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 30.0)
$g.FillRectangle($bg, $rect)
$bg.Dispose()

# 左上角青色光晕
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse(30, -180, 620, 620)
$gp = New-Object System.Drawing.Drawing2D.PathGradientBrush($glow)
$gp.CenterColor = [System.Drawing.Color]::FromArgb(45, 79, 209, 197)
$gp.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 79, 209, 197))
$g.FillPath($gp, $glow)
$gp.Dispose(); $glow.Dispose()

# ---------- 右侧截图 ----------
$shots = @(
    @{ File = "device.png";  X = 636; Y = 150; W = 196 },
    @{ File = "display.png"; X = 852; Y = 104; W = 208 },
    @{ File = "more.png";    X = 1076; Y = 158; W = 196 }
)
foreach ($s in $shots) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $root ("docs/screenshots/" + $s.File)))
    $imgH = [int]($s.W * $img.Height / $img.Width)

    # 阴影
    $sh = New-RoundedPath ($s.X + 6) ($s.Y + 8) $s.W $imgH 14
    $shb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
    $g.FillPath($shb, $sh); $shb.Dispose(); $sh.Dispose()

    # 圆角裁切
    $clip = New-RoundedPath $s.X $s.Y $s.W $imgH 14
    $old = $g.Clip
    $g.SetClip($clip)
    $g.DrawImage($img, $s.X, $s.Y, $s.W, $imgH)
    $g.Clip = $old
    $clip.Dispose()

    # 边框
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), 1.4)
    $bp = New-RoundedPath $s.X $s.Y $s.W $imgH 14
    $g.DrawPath($pen, $bp)
    $pen.Dispose(); $bp.Dispose()
    $img.Dispose()
}

# ---------- 左侧图标 ----------
$icon = [System.Drawing.Image]::FromFile((Join-Path $root "Assets/icon-256.png"))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($icon, 80, 96, 104, 104)
$icon.Dispose()
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# ---------- 文字 ----------
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(242, 242, 247))
$muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(154, 154, 160))
$accent = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#4FD1C5'))

$fTitle = New-Object System.Drawing.Font("Segoe UI", 50, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fSub   = New-Object System.Drawing.Font("Microsoft YaHei UI", 25, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fBody  = New-Object System.Drawing.Font("Microsoft YaHei UI", 18, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fSmall = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

$g.DrawString("MoyuCast", $fTitle, $white, 196, 88)
$g.DrawString("摸鱼投屏 · 老板键一键隐身", $fSub, $accent, 202, 156)
$g.DrawString("基于 scrcpy 3.3.4 的 Windows 图形化投屏控制台", $fBody, $muted, 204, 200)

$feats = @(
    "✓ 老板键一键隐藏 / 恢复（可自定义）",
    "✓ 窗口透明度 · 比例锁定 · 置顶",
    "✓ 托盘常驻 · 中英双语 · 检查更新"
)
$y = 262
foreach ($f in $feats) {
    $g.DrawString($f, $fBody, $white, 204, $y)
    $y += 34
}

$g.DrawString("Windows 10 / 11    ·    MIT License    ·    github.com/lee19890505/moyucast", $fSmall, $muted, 204, 404)

# 底部强调线
$barTop = $H - 7
$barRect = New-Object System.Drawing.Rectangle -ArgumentList 0, $barTop, $W, 7
$barColorA = [System.Drawing.ColorTranslator]::FromHtml('#0A84FF')
$barColorB = [System.Drawing.ColorTranslator]::FromHtml('#4FD1C5')
$bar = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $barRect, $barColorA, $barColorB, ([single]0)
$g.FillRectangle($bar, 0, $barTop, $W, 7)
$bar.Dispose()

foreach ($o in @($white, $muted, $accent, $fTitle, $fSub, $fBody, $fSmall)) { $o.Dispose() }
$g.Dispose()

$out = Join-Path $root "docs/social-preview.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$f = Get-Item $out
"OK  $($f.FullName)  $($f.Length) bytes"


