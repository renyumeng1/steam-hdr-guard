$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $ProjectRoot

dotnet publish .\src\SteamHdrGuard.Cli\SteamHdrGuard.Cli.csproj -c Release -r win-x64 --self-contained false
dotnet publish .\src\SteamHdrGuard.Gui\SteamHdrGuard.Gui.csproj -c Release -r win-x64 --self-contained false

Write-Host "发布完成。"
Write-Host "CLI: src\SteamHdrGuard.Cli\bin\Release\net8.0-windows\win-x64\publish\"
Write-Host "GUI: src\SteamHdrGuard.Gui\bin\Release\net8.0-windows\win-x64\publish\"
