<p align="center">
  <img src="Assets/AppIcon.png" alt="MoxTerm Logo" width="128" height="128"/>
</p>

<h1 align="center">MoxTerm</h1>

<p align="center">
  <b>A modern Windows terminal client built with WinUI 3 & C#</b><br/>
  <b>基于 WinUI 3 与 C# 打造的现代 Windows 终端客户端</b>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="License"/></a>
  <a href="https://github.com/wu-yiqiang/moxterm"><img src="https://img.shields.io/badge/platform-Windows-blue.svg" alt="Platform"/></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10-512BD4.svg" alt=".NET"/></a>
  <a href="https://learn.microsoft.com/windows/apps/winui/winui3/"><img src="https://img.shields.io/badge/WinUI-3-0078D4.svg" alt="WinUI 3"/></a>
  <img src="https://img.shields.io/badge/version-1.0.0-orange.svg" alt="Version"/>
</p>

<p align="center">
  <a href="#english">English</a> · <a href="#中文">中文</a> · <a href="https://github.com/wu-yiqiang/moxterm/releases">Download</a>
</p>

---

## English

**MoxTerm** is a modern, lightweight terminal client for Windows, built with **C#** and **WinUI 3** (.NET 10 + Windows App SDK). Inspired by **MobaXterm**, it brings an all-in-one remote computing workflow — sessions, tabs, tools and servers — into a native Fluent Design interface.

The project is positioned as an open-source **functional replica of MobaXterm** on the WinUI 3 + C# stack. Rather than wrapping a web view or porting an aging toolkit, MoxTerm builds directly on WinUI 3 controls and the Windows **ConPTY** pseudo console. The result is a natively rendered, GPU-friendly terminal that follows the Windows 11 look and feel out of the box.

### Features

**Terminal**

- Multi-tab terminal sessions with a native tab bar
- **ConPTY** backend with full VT sequence and window-size negotiation
- Configurable default shell: Windows PowerShell, PowerShell 7, CMD, WSL, Git Bash, and more
- Built-in color schemes including Retro Green, with light and dark variants
- Any installed monospace font, size 8–32, line height 1.0–2.0
- Cursor shape (block / underline / bar) and blinking control
- Scrollback buffer from 200 to 100,000 lines
- Customizable working directory for new sessions

**Workspace**

- Left navigation rail with **Sessions**, **Tools** and **Servers** sections
- Save and organize connections for quick reconnect
- Minimize to the notification area to keep sessions alive in the background
- Multi-line paste confirmation to prevent accidental command execution
- Follow-system, light and dark application themes
- Built-in English and 中文 UI, switchable at runtime

### Screenshots

<p align="center">

| Main window                   | Settings · Dark                                 | Settings · Light                                  |
|:-----------------------------:|:-----------------------------------------------:|:-------------------------------------------------:|
| ![Main](docs/images/main.png) | ![Settings Dark](docs/images/settings-dark.png) | ![Settings Light](docs/images/settings-light.png) |

</p>

> Place screenshots in `docs/images/` using the filenames above to render them.

### Requirements

|                  |                                                                      |
|:---------------- |:-------------------------------------------------------------------- |
| **OS**           | Windows 10 version 1809 (build 17763) or later, including Windows 11 |
| **Runtime**      | .NET 10 Desktop Runtime (bundled in the self-contained installer)    |
| **Architecture** | x64 / ARM64                                                          |

### Installation

**From a release package**

1. Open the [releases page](https://github.com/wu-yiqiang/moxterm/releases).
2. Download the latest `MoxTerm-x.y.z.msix` (or the `.exe` bootstrapper).
3. Install the package and launch **MoxTerm** from the Start menu.

> Enable *sideloading* in Windows settings before installing an `.msix` package.

**From source**

```bash
git clone https://github.com/wu-yiqiang/moxterm.git
cd moxterm

dotnet restore
dotnet build --configuration Release
dotnet run --configuration Release --project src/MoxTerm
```

Visual Studio 2022 (17.10 or later) with the *WinUI application development* workload is recommended for debugging and XAML hot reload.

### Project layout

```
moxterm/
├── src/
│   └── MoxTerm/
│       ├── Assets/        # app icon (logo), logo variants and image resources
│       ├── Controls/      # custom WinUI controls (terminal view, tab bar, ...)
│       ├── Services/      # ConPTY wrapper, session store, settings, i18n
│       ├── ViewModels/    # MVVM view models
│       └── Views/         # XAML pages: terminal, settings, sessions, servers
├── docs/
│   └── images/            # logo and screenshots used by this README
├── installer/             # MSIX packaging and signing scripts
├── dist/                  # release artifacts
└── MoxTerm.sln
```

### Icon

The MoxTerm icon is a rounded square with a dark navy gradient background, carrying a white `>` prompt chevron and a block cursor bar — a direct visual reference to the command-line terminal the app is built around.

- Source assets live in `src/MoxTerm/Assets/`
- A PNG copy for documentation is placed at `docs/images/logo.png`
- The same artwork is exported to `.ico` / `.png` variants for the taskbar, Start menu and MSIX package manifest

### Roadmap

- [x] WinUI 3 shell with Fluent Design and theme switching
- [x] ConPTY terminal engine with tabs and full settings
- [x] Session / tool / server navigation and persistence
- [x] English and Chinese localization
- [ ] SSH, Telnet and serial connections
- [ ] SFTP file browser with drag-and-drop transfer
- [ ] X11 forwarding and built-in X server
- [ ] Port forwarding and session recording
- [ ] Plugin / script API

### Contributing

1. Fork the repository and create a feature branch.
2. Keep UI changes consistent with the Fluent Design guidelines.
3. Run `dotnet build` and verify the app starts before opening a pull request.
4. For large changes, please open an issue first to discuss the approach.

### License

MoxTerm is released under the [MIT License](LICENSE).

MoxTerm is an independent open-source project and is not affiliated with, endorsed by, or derived from MobaXterm.

---

## 中文

**MoxTerm** 是一款使用 **C#** 与 **WinUI 3**（.NET 10 + Windows App SDK）开发的现代轻量级 Windows 终端客户端。项目受 **MobaXterm** 启发，将“会话 + 标签页 + 工具 + 服务器”的一站式远程计算工作流带入原生 Fluent Design 界面。

项目定位为基于 WinUI 3 + C# 技术栈的 **MobaXterm 功能复刻**开源方案。它没有使用 WebView 套壳，也没有移植老旧工具包，而是直接基于 WinUI 3 控件与 Windows **ConPTY** 伪控制台实现。因此终端为原生渲染、GPU 友好，并天然契合 Windows 11 的视觉风格。

### 功能特性

**终端能力**

- 原生标签栏，支持多标签终端会话
- 基于 **ConPTY** 伪控制台，完整支持 VT 转义序列与窗口尺寸协商
- 默认 Shell 可配置：Windows PowerShell、PowerShell 7、CMD、WSL、Git Bash 等
- 内置复古绿等多种配色方案，并提供浅色与深色变体
- 字体可任选系统已安装等宽字体，字号 8–32，行高 1.0–2.0
- 光标形状（方块 / 下划线 / 竖线）与闪烁开关
- 回滚缓冲行数 200 – 100000 可调
- 可自定义新会话的起始目录

**工作区**

- 左侧导航栏包含**会话**、**工具**、**服务器**三个分区
- 连接信息可保存并分组，便于快速重连
- 最小化到通知区域，后台保持会话不断线
- 多行粘贴安全确认，避免误执行命令
- 支持跟随系统 / 浅色 / 深色应用主题
- 内置英文与中文界面，可在设置中随时切换

### 界面预览

<p align="center">

| 主界面                          | 设置页 · 深色                               | 设置页 · 浅色                                |
|:----------------------------:|:--------------------------------------:|:---------------------------------------:|
| ![主界面](docs/images/main.png) | ![设置深色](docs/images/settings-dark.png) | ![设置浅色](docs/images/settings-light.png) |

</p>

> 将截图按上述文件名放入 `docs/images/` 目录即可正常显示。

### 运行环境

| 项目       | 要求                                             |
|:-------- |:---------------------------------------------- |
| **操作系统** | Windows 10 版本 1809（内部版本 17763）及以上，含 Windows 11 |
| **运行时**  | .NET 10 桌面运行时（自包含安装包已内置）                       |
| **架构**   | x64 / ARM64                                    |

### 安装方式

**使用发布包安装**

1. 打开 [Releases 页面](https://github.com/wu-yiqiang/moxterm/releases)。
2. 下载最新的 `MoxTerm-x.y.z.msix`（或 `.exe` 引导安装程序）。
3. 安装后从开始菜单启动 **MoxTerm**。

> 安装 `.msix` 包前，请在 Windows 设置中开启“侧载应用”权限。

**从源码编译**

```bash
git clone https://github.com/wu-yiqiang/moxterm.git
cd moxterm

dotnet restore
dotnet build --configuration Release
dotnet run --configuration Release --project src/MoxTerm
```

调试与 XAML 热重载建议使用 Visual Studio 2022（17.10 及以上），并安装“WinUI 应用程序开发”工作负载。

### 项目结构

```
moxterm/
├── src/
│   └── MoxTerm/
│       ├── Assets/        # 应用图标（Logo）、Logo 变体与图片资源
│       ├── Controls/      # 自定义 WinUI 控件（终端视图、标签栏等）
│       ├── Services/      # ConPTY 封装、会话存储、设置、国际化
│       ├── ViewModels/    # MVVM 视图模型
│       └── Views/         # XAML 页面：终端、设置、会话、服务器
├── docs/
│   └── images/            # 本文档使用的 Logo 与截图
├── installer/             # MSIX 打包与签名脚本
├── dist/                  # 发布产物
└── MoxTerm.sln
```

### 图标说明

MoxTerm 图标为深蓝渐变背景的圆角方块，中央是白色的 `>` 提示符尖括号与方块光标条，直接呼应应用所围绕的命令行终端定位。

- 图标源文件位于 `src/MoxTerm/Assets/`
- 文档展示用的 PNG 副本放置于 `docs/images/logo.png`
- 同一份设计会导出为 `.ico` / `.png` 多种尺寸变体，用于任务栏、开始菜单与 MSIX 包清单

### 开发计划

- [x] WinUI 3 外壳，Fluent Design 与主题切换
- [x] ConPTY 终端引擎，支持标签页与完整设置项
- [x] 会话 / 工具 / 服务器导航与本地持久化
- [x] 中英文界面国际化
- [ ] SSH、Telnet 与串口连接
- [ ] SFTP 文件浏览器，支持拖拽传输
- [ ] X11 转发与内置 X Server
- [ ] 端口转发与会话录制
- [ ] 插件 / 脚本扩展 API

### 参与贡献

1. Fork 本仓库并创建特性分支。
2. 界面改动请遵循 Fluent Design 设计规范。
3. 提交 Pull Request 前请执行 `dotnet build` 并确认程序可正常启动。
4. 涉及较大改动时，建议先开启 Issue 讨论方案。

### 开源许可

MoxTerm 基于 [MIT 许可证](LICENSE) 发布。

MoxTerm 是独立的开源项目，与 MobaXterm 无任何隶属、授权或代码衍生关系。

---

内容由 AI 生成
