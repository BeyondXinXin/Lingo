# Lingo 发布脚本：产出 Release win-x64 自包含单文件 Lingo.exe
# 用法：在任意目录执行  powershell -ExecutionPolicy Bypass -File publish.ps1
$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
$project = Join-Path $projectDir "Lingo.csproj"
$outDir = Join-Path $projectDir "artifacts\self-contained"

Write-Host "==> dotnet publish (Release, win-x64, self-contained, single-file)" -ForegroundColor Cyan
dotnet publish $project -c Release /p:PublishProfile=SelfContained --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "发布失败，退出码 $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $outDir "Lingo.exe"
if (-not (Test-Path $exe)) {
    Write-Host "未找到发布产物：$exe" -ForegroundColor Red
    exit 1
}

$info = Get-Item $exe
$version = (Get-Item $exe).VersionInfo.ProductVersion
Write-Host ""
Write-Host "==> 发布成功" -ForegroundColor Green
Write-Host ("    文件: {0}" -f $info.FullName)
Write-Host ("    大小: {0:N1} MB" -f ($info.Length / 1MB))
Write-Host ("    版本: {0}" -f $version)
