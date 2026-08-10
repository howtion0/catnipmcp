# Catnip 开发入口

本目录是独立的 Catnip 0.0 双平台源代码与交付副本。

开始修改前依次阅读：

1. `CATNIP_REBRAND_PLAN.md`
2. `README.md`
3. `docs/PROGRESS.md`

强制规则：

- 保持 C#、Windows WPF、macOS Avalonia、Runtime、DemoApi 与 WorkBuddy Bridge 的现有分层。
- 和风天气、5210/5220 loopback 端口及凭据加密行为不得在纯品牌修改中改变。
- 任何改动必须通过 `Catnip.sln` 的 Release build、全部测试和 format verify。
- 不提交 API Key、专属 Host、数据库、日志、主密钥或本机 MCP 配置。
- Mac 交叉构建不代表 Windows 真机验收；Windows WPF、DPAPI、Pipe ACL 与 SmartScreen 结论必须在 Windows 记录。
