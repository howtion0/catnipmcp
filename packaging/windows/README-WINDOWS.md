# Catnip 0.0 · Windows x64

推荐直接双击 `catnip.exe`。它与 `Catnip-0.0.0-win-x64.exe` 内容完全相同，均为自包含、自解包的 64 位 Windows GUI 程序，不要求预装 .NET。

首次运行会把完整套件解包到：

```text
%LOCALAPPDATA%\Catnip\app-0.0.0-<payload-hash>\
```

然后启动 `Catnip.Desktop.exe`。Desktop、DemoApi、Runtime 和 WorkBuddy Bridge 四个进程共同组成完整应用。

`Catnip-0.0.0-win-x64.zip` 是便携与审计包。解压后运行根目录的 `Catnip.Desktop.exe`，不要单独复制内部某一个 EXE。

当前 0.0 为测试版：文件未进行代码签名，Windows 可能显示 SmartScreen 提示；Windows 10/11 真机验证仍需在目标电脑上完成。
