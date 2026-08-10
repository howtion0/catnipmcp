# Catnip 0.0 双平台独立副本施工记录

## 基线

- 来源仓库：已完成远端备份的原始项目，只读导出源代码。
- 来源提交：`de9d63346382982883644167dd888b36678c535c`。
- 目标目录：`/Users/howtion/demo/catnip`。
- 目标远端：`git@github.com:howtion0/catnipmcp.git`。
- 施工分支：`v0.0`，基于远端占位 `main` 的 `a6d7e201fce7cc71259b416d55709cb25b82fa4c` 创建。
- 本轮主模块：发布交付；接口影响覆盖命名基础、Windows WPF、macOS Avalonia、Runtime、DemoApi、WorkBuddy Bridge、测试及打包脚本。

## 唯一目标

建立完全独立的 Catnip 0.0 双平台代码库。产品名、命名空间、程序集、项目、工具 API、数据目录、环境变量、包名和用户可见品牌统一为 Catnip；端口、Runtime 闸门、SQLite/AES-GCM、WorkBuddy 调用链及和风天气业务保持不变。

## 范围

1. 复制完整源代码、测试、脚本、打包配置和必要文档，不复制来源 Git 历史、构建缓存、用户数据或历史施工档案。
2. 解决方案、项目目录、项目文件、namespace、程序集、可执行文件及 MCP 工具统一采用 `Catnip`、`catnip` 和 `catnip_*`。
3. 保留和风天气 API、专属 Host 配置模型、GeoAPI/实时天气调用、5210/5220 loopback 端口和现有安全行为。
4. 生成 `Catnip.app`、`Catnip-0.0.0-macos-arm64.zip`、`Catnip-0.0.0-win-x64.exe`、`catnip.exe`、Windows 便携 ZIP 和 SHA-256 清单。
5. 在 macOS 桌面建立可双击的 `Catnip.app` 入口，实际冷启动并验证 DemoApi 状态和 Runtime 生命周期。
6. 门禁全部通过后提交并推送 `v0.0`，再将同一已验证提交快进到 `main`，同时保留版本分支作为回滚点。

## 非范围与平台边界

- 不修改来源仓库或来源 Release。
- 不改变天气提供商、业务协议、数据结构语义或端口。
- Mac 上完成 Windows 交叉构建、PE/包结构与安全检查；不宣称 Windows 10/11 WPF 真机、DPAPI、Pipe ACL、SmartScreen 或代码签名已通过。
- 不把旧截图、旧施工历史、密钥、数据库、日志或本机 WorkBuddy 配置放入 Git。

## 验收门禁

- [x] 目标副本内文本、文件名和项目名不含旧品牌、旧命名空间或旧工具前缀。
- [x] `Catnip.sln` restore 成功，Release build 0 warning / 0 error。
- [x] 全部 258 项测试 100% 通过、0 skip，format verify 通过，并完成 5 轮重复验证。
- [x] 工具名为 `catnip_get_gateway_status`、`catnip_get_today_todos`、`catnip_get_weather`，和风天气实现与端点仍存在。
- [x] Windows 主资产是 PE32+ x86-64 GUI，包内四个 Catnip EXE 齐全，并额外交付内容相同的 `catnip.exe`。
- [x] macOS `Catnip.app` 四个 arm64 apphost 齐全，版本 0.0.0，可从桌面启动并返回健康状态。
- [x] 已打包应用完成连续 5 轮 Runtime 实际停止/启动，版本与健康状态一致。
- [x] Release 目录不含密钥、数据库、日志、主密钥或真实 MCP 配置；SHA-256 清单完整。
- [x] `v0.0` 和 `main` 均指向同一已验证提交，远端回读一致。

## 回滚

- GitHub 保留 `v0.0` 分支；后续版本从稳定点新建版本分支，不直接在 `main` 施工。
- 本地可删除或弃用 `/Users/howtion/demo/catnip`；来源仓库始终保持在已备份提交，不受本轮影响。
- 构建输出只写入 Catnip 副本的 `artifacts/`；桌面入口只指向该副本，不覆盖其他应用入口。
