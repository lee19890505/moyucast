[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$PublishDir = "C:\Users\36461\Desktop\代码\MoyuCast-发布版",
    [string]$SmokeTest = "C:\Users\36461\Desktop\代码\tests\MoyuCast\smoke-test.ps1",
    [string]$Proxy = "http://127.0.0.1:18081",
    [switch]$SkipTests,
    [switch]$NoPush,
    [switch]$WaitCi,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    if ($DryRun) { return }
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令失败（exit $LASTEXITCODE）：$FilePath $($Arguments -join ' ')"
    }
}

# ---------- 版本 ----------
$csprojPath = Join-Path $repo "MoyuCast.csproj"
$csprojText = [IO.File]::ReadAllText($csprojPath)
$m = [regex]::Match($csprojText, "<Version>([^<]+)</Version>")
if (-not $m.Success) { throw "MoyuCast.csproj 里找不到 <Version>" }
$currentVersion = $m.Groups[1].Value.Trim()

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $currentVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版本号格式应为 x.y.z，当前：$Version" }
$tag = "v$Version"

# ---------- CHANGELOG 校验 ----------
$changelog = [IO.File]::ReadAllText((Join-Path $repo "CHANGELOG.md"))
if ($changelog -notmatch ("(?m)^##\s*\[" + [regex]::Escape($tag) + "\]")) {
    throw "CHANGELOG.md 缺少 ## [$tag] 段落，请先补更新日志再发布"
}

Write-Host "仓库: $repo"
Write-Host "版本: $currentVersion -> $Version"
Write-Host "标签: $tag"

# ---------- 改版本号 ----------
if ($Version -ne $currentVersion) {
    Write-Step "更新 csproj 版本号"
    if (-not $DryRun) {
        $newText = [regex]::Replace($csprojText, "<Version>[^<]+</Version>", "<Version>$Version</Version>", 1)
        [IO.File]::WriteAllText($csprojPath, $newText, (New-Object System.Text.UTF8Encoding($true)))
    }
}

# ---------- 构建 ----------
Write-Step "构建"
Invoke-Native "dotnet" @("build", "-c", "Release", "--nologo")

# ---------- 打包 ----------
Write-Step "打包到 $PublishDir"
if (-not $DryRun) {
    Get-Process MoyuCast -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}
Invoke-Native "dotnet" @(
    "publish", "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $PublishDir, "--nologo"
)

$exePath = Join-Path $PublishDir "MoyuCast.exe"
if (-not $DryRun -and -not (Test-Path $exePath)) { throw "没有生成 $exePath" }

# ---------- 冒烟测试 ----------
if ($SkipTests) {
    Write-Step "跳过冒烟测试"
} else {
    Write-Step "冒烟测试"
    if (-not (Test-Path $SmokeTest)) { throw "找不到冒烟测试脚本：$SmokeTest" }
    Invoke-Native "powershell" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $SmokeTest, "-Exe", $exePath)
}

# ---------- 提交 / 打标签 / 推送 ----------
if ($NoPush) {
    Write-Step "NoPush：不提交、不推送"
    return
}

Write-Step "提交并打标签"
if (-not $DryRun) {
    Invoke-Native "git" @("add", "-A")
    & git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        Invoke-Native "git" @("commit", "-m", "release: $tag")
    } else {
        Write-Host "没有需要提交的改动，直接打标签" -ForegroundColor Yellow
    }
}
Invoke-Native "git" @("tag", "-f", $tag)

Write-Step "推送"
$proxyArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Proxy)) {
    $proxyArgs = @("-c", "http.proxy=$Proxy")
}
Invoke-Native "git" ($proxyArgs + @("push", "origin", "main"))
Invoke-Native "git" ($proxyArgs + @("push", "origin", $tag, "--force"))

# ---------- 等 CI ----------
if ($WaitCi -and -not $DryRun) {
    Write-Step "等待 GitHub Actions"
    $ok = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 10
        try {
            $r = Invoke-RestMethod -Uri "https://api.github.com/repos/lee19890505/moyucast/actions/runs?per_page=5" -Headers @{ "User-Agent" = "MoyuCast-release" } -TimeoutSec 30
            $run = $r.workflow_runs | Where-Object { $_.head_branch -eq $tag } | Select-Object -First 1
            if ($null -ne $run -and $run.status -eq "completed") {
                if ($run.conclusion -ne "success") { throw "CI 失败：$($run.html_url)" }
                Write-Host "CI 成功：$($run.html_url)" -ForegroundColor Green
                $ok = $true
                break
            }
            Write-Host "CI 进行中… ($($i + 1)/40)"
        } catch {
            Write-Host "查询 CI 失败（继续等待）：$($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    if (-not $ok) { Write-Host "等待 CI 超时，请手动查看 Actions" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "完成：$tag" -ForegroundColor Green
