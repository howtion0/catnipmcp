# Catnip 0.0 开发状态

更新时间：2026-08-10

## 当前目标

从已备份源代码建立独立的 Catnip 双平台代码库，完整替换产品命名，保留 Runtime 闸门、WorkBuddy MCP、SQLite/AES-GCM 和和风天气链路，并交付 macOS 应用与无外部运行时依赖的 Windows EXE。

## 当前基线

| 项目 | 值 |
|---|---|
| 产品版本 | `0.0` |
| 程序版本 | `0.0.0` |
| 施工分支 | `v0.0` |
| 目标仓库 | `git@github.com:howtion0/catnipmcp.git` |
| macOS UI | C# + Avalonia |
| Windows UI | C# + WPF |
| 天气提供商 | QWeather / 和风天气 |
| MCP 工具前缀 | `catnip_` |

## 交付门禁

- [x] 全目录旧品牌与旧命名扫描为零。
- [x] Release build 0 warning / 0 error。
- [x] 全部 258 项测试通过、0 skip，且连续 5 轮一致。
- [x] `dotnet format --verify-no-changes` 通过。
- [x] `catnip.exe` 为 PE32+ x86-64 GUI 且与版本化 EXE哈希一致。
- [x] Windows 便携包包含 Desktop、DemoApi、Runtime、WorkBuddy Bridge 四个 EXE。
- [x] `Catnip.app` 包含四个对应的 macOS arm64 apphost。
- [x] Mac 桌面入口可启动应用，DemoApi 与 Runtime 健康检查通过。
- [x] 已打包应用完成连续 5 轮 Runtime 停止/启动，版本与健康状态一致。
- [x] Release 目录凭据与运行数据扫描通过，SHA-256 清单完整。
- [x] 已提交并通过 SSH 推送 `v0.0`，随后快进 `main` 并回读确认。

## 平台边界

Windows 包在 macOS 上交叉构建，只能证明源码可编译、文件为有效 PE、完整载荷可解压且安全扫描通过。Windows 10/11 x64 真机的 UI、DPAPI、Pipe ACL、SmartScreen 和进程生命周期仍需目标电脑验证。

macOS 包会在当前 Mac 上执行冷启动、健康检查和 Runtime 生命周期验证。测试包不包含用户数据库、API Key、日志或真实 WorkBuddy 配置。

## 回滚

`v0.0` 作为独立版本分支保留。后续施工从已验证版本创建新分支，不直接在 `main` 修改；需要回滚时可把 `main` 快进或重置到已知稳定的版本分支提交。
