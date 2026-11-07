<#
.SYNOPSIS
    构建 AutoLogin Release 版，并把单文件 exe 复制到仓库根目录。

.DESCRIPTION
    需要已安装 .NET SDK（dotnet 命令）。构建产物位于
    src\AutoLogin\bin\Release\net48\AutoLogin.exe，复制后可直接拷到工控机使用。
    如果根目录 AutoLogin.exe 正在运行，请先退出程序再执行本脚本。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $root
try {
    Write-Host "==> 构建 AutoLogin ($Configuration)" -ForegroundColor Cyan
    dotnet build (Join-Path $root 'AutoLogin.sln') -c $Configuration -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失败（退出码 $LASTEXITCODE）"
    }

    $outputDir = Join-Path $root "src\AutoLogin\bin\$Configuration\net48"
    $exe = Join-Path $outputDir 'AutoLogin.exe'
    if (-not (Test-Path $exe)) {
        throw "未找到构建产物：$exe"
    }

    if (Test-Path (Join-Path $outputDir 'AutoLogin.exe.config')) {
        throw "检测到 AutoLogin.exe.config，单文件形态被破坏，请检查 csproj。"
    }

    $target = Join-Path $root 'AutoLogin.exe'
    Copy-Item $exe $target -Force

    Write-Host "==> 已生成：$target" -ForegroundColor Green
    Get-Item $target | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
}
finally {
    Pop-Location
}

