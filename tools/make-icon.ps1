# MoyuCast 图标生成器（像素风格）
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# 输出：Assets\MoyuCast.ico（16/24/32/48/64/128/256）+ Assets\icon-256.png
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

# 16x16 像素网格：. 透明 / # 底板 / b 鱼身 / h 高光 / s 暗部 / t 鱼尾 / e 眼睛
$grid = @(
  "................",
  "................",
  "................",
  "........hhhh....",
  "...t...bbbbbb...",
  "..ttt.bbbbbbbb..",
  ".tttt.bbebbbbbb.",
  "..ttt.bbbbbbbb..",
  "...t...ssssss...",
  "........ssss....",
  "................",
  "................",
  "................",
  "................",
  "................",
  "................"
)

$colors = @{
  '#' = [System.Drawing.ColorTranslator]::FromHtml("#1B1C20")
  'b' = [System.Drawing.ColorTranslator]::FromHtml("#4FD1C5")
  'h' = [System.Drawing.ColorTranslator]::FromHtml("#8FE9DF")
  's' = [System.Drawing.ColorTranslator]::FromHtml("#2FA79C")
  't' = [System.Drawing.ColorTranslator]::FromHtml("#3BBFB2")
  'e' = [System.Drawing.ColorTranslator]::FromHtml("#101216")
}

function New-IconBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g.Clear([System.Drawing.Color]::Transparent)

  $block = $size / 16.0
  $brush = New-Object System.Drawing.SolidBrush($colors['#'])
  $g.FillRectangle($brush, 0, 0, $size, $size)
  $brush.Dispose()

  # 圆角：清掉四个角的 1 像素块，做出像素化圆角
  $corner = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
  $c = [Math]::Max(1, [int][Math]::Round($block))
  $g.FillRectangle($corner, 0, 0, $c, $c)
  $g.FillRectangle($corner, $size - $c, 0, $c, $c)
  $g.FillRectangle($corner, 0, $size - $c, $c, $c)
  $g.FillRectangle($corner, $size - $c, $size - $c, $c, $c)
  $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
  $corner.Dispose()

  for ($y = 0; $y -lt 16; $y++) {
    $row = $grid[$y]
    for ($x = 0; $x -lt 16; $x++) {
      $ch = [string]$row[$x]
      if ($ch -eq '.' -or -not $colors.ContainsKey($ch)) { continue }
      $b = New-Object System.Drawing.SolidBrush($colors[$ch])
      $px = [int][Math]::Floor($x * $block)
      $py = [int][Math]::Floor($y * $block)
      $pw = [int][Math]::Ceiling(($x + 1) * $block) - $px
      $ph = [int][Math]::Ceiling(($y + 1) * $block) - $py
      $g.FillRectangle($b, $px, $py, $pw, $ph)
      $b.Dispose()
    }
  }
  $g.Dispose()
  return $bmp
}

function Write-Ico([string]$path, [int[]]$sizes) {
  $frames = New-Object System.Collections.Generic.List[byte[]]
  foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $frames.Add($ms.ToArray())
    $ms.Dispose()
  }
  $fs = [System.IO.File]::Create($path)
  $bw = New-Object System.IO.BinaryWriter($fs)
  $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int32]$frames[$i].Length)
    $bw.Write([int32]$offset)
    $offset += $frames[$i].Length
  }
  foreach ($f in $frames) { $bw.Write($f) }
  $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

$ico = Join-Path $assets "MoyuCast.ico"
Write-Ico $ico @(16, 24, 32, 48, 64, 128, 256)

$preview = New-IconBitmap 256
$preview.Save((Join-Path $assets "icon-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Get-Item $ico | ForEach-Object { "OK  $($_.FullName)  $($_.Length) bytes" }
Get-Item (Join-Path $assets "icon-256.png") | ForEach-Object { "OK  $($_.FullName)  $($_.Length) bytes" }
