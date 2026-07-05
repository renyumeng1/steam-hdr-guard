# Steam HDR Guard

Steam HDR Guard 是一个 Windows 小工具：自动扫描 Steam 已安装游戏，并在指定游戏启动时自动开启 Windows HDR，退出游戏后按配置恢复。

这个仓库的目标是**不依赖 HDRTray、HDRCmd、PyAutoActions 等第三方 HDR 工具**。HDR 控制逻辑由本项目直接调用 Windows DisplayConfig / Advanced Color API 实现。

## 功能

- 自动扫描 Steam 已安装游戏
- 支持多个 Steam Library
- 维护本地游戏列表和 HDR 启用开关
- GUI 端：
  - 极简黑白灰界面，少量蓝色强调色
  - 扫描游戏并勾选哪些游戏需要自动开 HDR
  - 查看显示器 HDR / Advanced Color 状态
  - 手动开启/关闭 HDR
  - 配置“游戏退出后关闭 HDR 延迟”
  - 配置“开机自启 GUI”
  - 配置“启动后自动开始监控”
  - 配置“关闭窗口时最小化到托盘”
  - 配置 HDR 切换提示：开关、位置、文案预设、自定义文案、显示时长
  - 托盘右键菜单支持打开、开始监控、停止监控、退出
  - 自绘 HDR 图标，不使用外部图标资源
- CLI 端：
  - 扫描游戏
  - 查看游戏列表
  - 启用/禁用某个游戏的自动 HDR
  - 查看/开启/关闭系统 HDR
  - 后台监控游戏进程

## 直接下载使用

进入仓库的 Actions 页面，打开最新的 `Build Windows Package`，在页面底部 `Artifacts` 下载两种版本：

```text
steam-hdr-guard-portable-win-x64    绿色版 zip，解压即用
steam-hdr-guard-installer-win-x64   安装版 exe，运行后安装到当前用户目录
```

绿色版解压后直接运行：

```text
gui\SteamHdrGuard.Gui.exe
```

安装版运行：

```text
SteamHdrGuardSetup-win-x64.exe
```

安装版会安装到：

```text
%LOCALAPPDATA%\Programs\SteamHdrGuard
```

并创建：

```text
开始菜单快捷方式
桌面快捷方式
Windows“应用和功能/程序和功能”里的卸载项
```

也可以使用 CLI：

```powershell
cli\steam-hdr-guard.exe scan
cli\steam-hdr-guard.exe list
cli\steam-hdr-guard.exe hdr status
cli\steam-hdr-guard.exe watch
```

## 技术方案

- 平台：Windows 10/11
- 运行时：.NET 8
- CLI：C# Console
- GUI：WPF
- 安装器：自研 .NET 单文件安装器，内嵌绿色版 payload
- HDR 提示：WPF 无焦点置顶 Toast 窗口，短暂淡入淡出，不复用 InputTip 代码
- HDR 控制：直接 P/Invoke `user32.dll` 的 DisplayConfig API
- Steam 扫描：读取注册表、`libraryfolders.vdf`、`appmanifest_*.acf`
- 开机自启：当前用户 `Run` 注册表项
- 配置文件：`%APPDATA%\SteamHdrGuard\config.json`

没有使用 Electron，因为 Electron 会带 Chromium 和 Node，体积更大。WPF 对这个工具来说更轻。

## 项目结构

```text
src/
  SteamHdrGuard.Core/   核心库：HDR 控制、Steam 扫描、配置、监控
  SteamHdrGuard.Cli/    命令行端
  SteamHdrGuard.Gui/    WPF 图形界面
  SteamHdrGuard.Setup/  安装版单文件安装器
scripts/
  publish.ps1
  install-startup-task.ps1
  remove-startup-task.ps1
.github/workflows/
  build-windows.yml
```

## 构建

需要安装 .NET 8 SDK。

```powershell
dotnet build .\src\SteamHdrGuard.Cli\SteamHdrGuard.Cli.csproj -c Release
dotnet build .\src\SteamHdrGuard.Gui\SteamHdrGuard.Gui.csproj -c Release
```

## 发布 exe

本地简单发布 CLI/GUI：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Actions 会额外生成绿色版 zip 和安装版 exe。

## GUI 使用

1. 打开 `SteamHdrGuard.Gui.exe`。
2. 点击“扫描 Steam 游戏”。
3. 在游戏列表里勾选需要自动开启 HDR 的游戏。
4. 在左侧设置游戏退出后关闭 HDR 的延迟秒数。
5. 需要启动后自动进入监控时，勾选“启动后自动开始监控”。
6. 需要后台运行时，勾选“关闭窗口时最小化到托盘”。
7. 需要开机自动运行时，勾选“开机自启 GUI”。
8. 需要切换时提示状态，勾选“启用 HDR 切换提示”，并设置提示位置和文案。
9. 点击“开始监控”。

HDR 切换提示支持三种文案模式：

```text
简洁：HDR ON / HDR OFF
中文：HDR 已开启 / HDR 已关闭
自定义：自由填写开启/关闭文案
```

提示位置支持：顶部居中、屏幕中央、底部居中、左上角、右上角、左下角、右下角。

如果启用了托盘模式，点窗口关闭按钮不会直接退出；程序会隐藏到托盘并继续监控。真正退出请右键托盘图标选择“退出”。

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

## 开机自启

GUI 里可以直接勾选“开机自启 GUI”。它会写入当前用户的 Windows `Run` 注册表项，并在开机后以 `--start-minimized` 启动。

是否启动后自动开始监控由 GUI 里的“启动后自动开始监控”控制。

也可以用脚本方式设置 CLI 开机监控：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-startup-task.ps1
```

删除脚本创建的计划任务：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\remove-startup-task.ps1
```

## 安装版卸载

安装版会注册当前用户卸载项。可以从 Windows 设置里的“应用和功能”卸载，也可以运行：

```powershell
%LOCALAPPDATA%\Programs\SteamHdrGuard\SteamHdrGuardSetup.exe /uninstall
```

卸载时会移除开始菜单快捷方式、桌面快捷方式、开机自启项和安装目录。

## 注意

1. 这个工具控制的是 Windows 系统 HDR，也就是系统设置里的“使用 HDR”。
2. Windows 的 “Auto HDR” 是另一个系统功能；通常需要系统 HDR 处于开启状态才会对 SDR 游戏生效。
3. 多显示器场景下，本项目默认对所有支持 Advanced Color / HDR 的活动显示器执行开关。
4. 有些游戏的启动器或反作弊进程不在 Steam 游戏目录下，可能需要后续增加自定义进程匹配规则。
5. 如果显示器不支持 HDR，或者 Windows 驱动没有暴露 Advanced Color 能力，`hdr on` 会提示没有可用显示器。
