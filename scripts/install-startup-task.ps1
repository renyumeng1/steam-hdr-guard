$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Exe = Join-Path $ProjectRoot "src\SteamHdrGuard.Cli\bin\Release\net8.0-windows\win-x64\publish\steam-hdr-guard.exe"

if (!(Test-Path $Exe)) {
    Write-Host "未找到发布后的 CLI exe：$Exe"
    Write-Host "请先运行："
    Write-Host "dotnet publish .\src\SteamHdrGuard.Cli\SteamHdrGuard.Cli.csproj -c Release -r win-x64 --self-contained false"
    exit 1
}

$Action = New-ScheduledTaskAction -Execute $Exe -Argument "watch"
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited

Register-ScheduledTask `
    -TaskName "Steam HDR Guard" `
    -Action $Action `
    -Trigger $Trigger `
    -Principal $Principal `
    -Description "Auto enable Windows HDR when configured Steam games are running." `
    -Force

Write-Host "已创建计划任务：Steam HDR Guard"
