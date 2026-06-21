# Palworld Server Manager | 幻兽帕鲁服务器管理器

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0-5C2D91)](PSM/PalworldServerManager.csproj)
[![Version](https://img.shields.io/badge/version-1.3.0-orange)](VERSION)

Windows 桌面应用程序，用于管理幻兽帕鲁（Palworld）专用服务器。提供现代化的图形界面，可创建、配置、监控和控制多个帕鲁服务器实例。

---

## 功能

- **服务器管理** — 创建、导入、编辑、启动/停止多个帕鲁服务器实例
- **配置编辑器** — 统一编辑器管理所有服务器配置（性能、功能、游戏平衡等）
- **Mod 管理器** — 浏览、安装、卸载 Steam 创意工坊 Mod，支持拖拽排序
- **RCON 控制台** — 远程命令执行（踢人、封禁、保存、关机、广播）
- **REST API 客户端** — 完整的帕鲁 REST API v1 集成（信息、玩家、公告、踢封、保存、关机、监控）
- **存档导入** — 拖放导入 `world.db`、`account.db` 或完整服务器文件夹
- **管理员/白名单管理** — 通过界面编辑 `adminlist.txt`
- **Webhook 通知** — Discord 集成，推送服务器事件通知
- **自动重启 & 自动更新** — 计划任务维护服务器
- **日志监控** — 实时跟踪服务器日志
- **备份管理** — 崩溃日志备份、自动清理存档

---

## 环境要求

- [.NET 6.0 运行时](https://dotnet.microsoft.com/download/dotnet/6.0)（构建需 SDK）
- Windows 7 或更高版本
- 帕鲁专用服务器（Palworld Dedicated Server）

---

## 快速开始

1. 从 [Releases](https://github.com/aghosto/Palworld-Server-Manager/releases) 下载最新版本
2. 解压并运行 `PalworldServerManager.exe`
3. 创建或导入服务器实例
4. 配置服务器设置并启动

### 从源码构建

```bash
git clone https://github.com/aghosto/Palworld-Server-Manager.git
cd Palworld-Server-Manager
dotnet build PSM/PalworldServerManager.csproj -c Release
```

---

## 项目结构

```
├── PSM/                    # 主程序（WPF 应用）
│   ├── Controls/           # 自定义 WPF 控件
│   ├── RCON/               # Source RCON 协议实现
│   ├── REST/               # 帕鲁 REST API 客户端
│   ├── Utils/              # 工具类
│   └── Resources/          # 应用图标
├── PSMUpdater/             # 自动更新工具（控制台）
├── Resources/              # 截图和 Wiki 资源
├── SSM/                    # 预编译的更新程序
├── Palworld Server Manager.sln
└── 配置文件（.gitignore / .gitattributes / .editorconfig）
```

---

## 技术栈

- **语言：** C#（.NET 6.0）
- **UI 框架：** WPF + ModernWpfUI（Fluent Design 风格）
- **NuGet 依赖：** Newtonsoft.Json、Hardcodet.NotifyIcon.Wpf

---

## 许可证

MIT — 详见 [LICENSE](LICENSE)。

## 鸣谢

- [Lacyway](https://github.com/Lacyway) — V-Rising-Server-Manager 原作者
- [Pocketpair](https://www.pocketpair.jp/) — 幻兽帕鲁
