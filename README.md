# Steam HDR Guard

Steam HDR Guard 是一个 Windows 小工具：自动扫描 Steam 已安装游戏，并在指定游戏启动时自动开启 Windows HDR，退出游戏后按配置恢复。

这个仓库的目标是**不依赖 HDRTray、HDRCmd、PyAutoActions 等第三方 HDR 工具**。HDR 控制逻辑由本项目直接调用 Windows DisplayConfig / Advanced Color API 实现。

## 功能

- 自动扫描 Steam 已安装游戏
- 支持多个 Steam Library
- 维护本地游戏列表和 HDR 启用开关
- CLI 端：
  - 扫描游戏
  - 查看游戏列表
  - 启用/禁用某个游戏的自动 HDR
  - 查看/开启/关闭系统 HDR
  - 后台监控游戏进程
- GUI 端：
  - 扫描游戏
  - 勾选哪些游戏需要自动开 HDR
  - 查看显示器 HDR 支持状态
  - 手动开启/关闭 HDR
  - 启动/停止监控

## 技术方案

- 平台：Windows 10/11
- 运行时：.NET 8
- CLI：C# Console
- GUI：WPF
- HDR 控制：直接 P/Invoke `user32.dll` 的 DisplayConfig API
- Steam 扫描：读取注册表、`libraryfolders.vdf`、`appmanifest_*.acf`
- 配置文件：`%APPDATA%\SteamHdrGuard\config.json`

没有使用 Electron，因为 Electron 会带 Chromium 和 Node，体积更大。WPF 对这个工具来说更轻。

## 项目结构

```text
src/
  SteamHdrGuard.Core/   核心库：HDR 控制、Steam 扫描、配置、监控
  SteamHdrGuard.Cli/    命令行端
  SteamHdrGuard.Gui/    WPF 图形界面
scripts/
  install-startup-task.ps1
  remove-startup-task.ps1
```

## 构建

需要安装 .NET 8 SDK。

```powershell
dotnet build .\src\SteamHdrGuard.Cli\SteamHdrGuard.Cli.csproj -c Release
dotnet build .\src\SteamHdrGuard.Gui\SteamHdrGuard.Gui.csproj -c Release
```

## CLI 使用

查看配置文件路径：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- config
```

扫描 Steam 已安装游戏：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- scan
```

列出游戏：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- list
```

按 appid 或名称关键字启用某个游戏：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- enable cyberpunk
```

禁用某个游戏：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- disable dota
```

查看 HDR 状态：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- hdr status
```

手动开启/关闭 HDR：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- hdr on
dotnet run --project .\src\SteamHdrGuard.Cli -- hdr off
```

启动监控：

```powershell
dotnet run --project .\src\SteamHdrGuard.Cli -- watch
```

## 发布 exe

CLI：

```powershell
dotnet publish .\src\SteamHdrGuard.Cli\SteamHdrGuard.Cli.csproj -c Release -r win-x64 --self-contained false
```

GUI：

```powershell
dotnet publish .\src\SteamHdrGuard.Gui\SteamHdrGuard.Gui.csproj -c Release -r win-x64 --self-contained false
```

输出位置：

```text
src\SteamHdrGuard.Cli\bin\Release\net8.0-windows\win-x64\publish\
src\SteamHdrGuard.Gui\bin\Release\net8.0-windows\win-x64\publish\
```

## 开机自启

先发布 CLI，再按你的实际 exe 路径修改脚本参数，或在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-startup-task.ps1
```

删除开机自启：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\remove-startup-task.ps1
```

## 注意

1. 这个工具控制的是 Windows 系统 HDR，也就是系统设置里的“使用 HDR”。
2. Windows 的 “Auto HDR” 是另一个系统功能；通常需要系统 HDR 处于开启状态才会对 SDR 游戏生效。
3. 多显示器场景下，本项目默认对所有支持 Advanced Color / HDR 的活动显示器执行开关。
4. 有些游戏的启动器或反作弊进程不在 Steam 游戏目录下，可能需要后续增加自定义进程匹配规则。
5. 如果显示器不支持 HDR，或者 Windows 驱动没有暴露 Advanced Color 能力，`hdr on` 会提示没有可用显示器。
