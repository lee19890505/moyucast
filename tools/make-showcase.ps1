# 把模板里的占位符替换为 base64 图片，生成单文件展示页
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tplPath = Join-Path $PSScriptRoot "showcase-template.html"
$outPath = Join-Path $root "docs/showcase.html"

function ToDataUri([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $ext = [IO.Path]::GetExtension($path).TrimStart('.').ToLower()
    $mime = if ($ext -eq "jpg" -or $ext -eq "jpeg") { "image/jpeg" } else { "image/png" }
    return "data:$mime;base64," + [Convert]::ToBase64String($bytes)
}

$html = [IO.File]::ReadAllText($tplPath, [Text.Encoding]::UTF8)
$html = $html.Replace("__BANNER__", (ToDataUri (Join-Path $root "docs/social-preview.png")))
$html = $html.Replace("__S1__", (ToDataUri (Join-Path $root "docs/screenshots/device.png")))
$html = $html.Replace("__S2__", (ToDataUri (Join-Path $root "docs/screenshots/display.png")))
$html = $html.Replace("__S3__", (ToDataUri (Join-Path $root "docs/screenshots/audio.png")))
$html = $html.Replace("__S4__", (ToDataUri (Join-Path $root "docs/screenshots/more.png")))

[IO.File]::WriteAllText($outPath, $html, (New-Object System.Text.UTF8Encoding($true)))
$f = Get-Item $outPath
Write-Host ("OK  " + $f.FullName + "  " + [math]::Round($f.Length/1MB,2) + " MB")
