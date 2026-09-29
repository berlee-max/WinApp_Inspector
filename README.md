# WinApp Inspector

Windows 10 / 11 上的第三方软件发现、归属判断、卸载与残留清理工具。

> WinApp Inspector 不是"垃圾清理工具"，而是一款帮助用户看清 Windows 电脑里"到底装了什么、这些文件属于谁、应该怎么卸载"的程序发现与卸载分析工具。

- 需求文档：[WinApp_Inspector需求文档_V0.1.md](WinApp_Inspector需求文档_V0.1.md)
- 开发指南（技术栈、结构、构建方式、安全红线、开发阶段）：[CLAUDE.md](CLAUDE.md)
- 技术栈：C# 12 / .NET 8 / WPF

## 解决方案结构

```text
WinAppInspector.sln
src/WinAppInspector.Core        net8.0          模型、接口、规则（ProtectedPathRule、DeletionGuard）、证据权重
src/WinAppInspector.Analysis    net8.0          归属 / 分类 / 残留判断，纯逻辑
src/WinAppInspector.Scanners    net8.0-windows  注册表 / AppX / 目录 / 进程 / 服务 / 启动项 / 计划任务扫描器
src/WinAppInspector.Actions     net8.0-windows  卸载、清理、系统还原点
src/WinAppInspector.UI          net8.0-windows  WPF 界面
tests/WinAppInspector.Core.Tests
tests/WinAppInspector.Analysis.Tests
```

## 构建与测试

需要 .NET 8 SDK。整个解决方案（含 WPF 项目）可以在 Windows、macOS、Linux 上编译；只能在 Windows 上运行。

```bash
dotnet build WinAppInspector.sln
dotnet test WinAppInspector.sln
```

CI（`.github/workflows/ci.yml`）在 `windows-latest` 和 `ubuntu-latest` 上分别执行构建和测试。

## 安全边界

- 任何删除 / 卸载 / 结束进程 / 停止服务操作都必须先展示证据、由用户确认，再执行；禁止扫描后自动删除。
- 优先调用软件自己的官方卸载程序；只有不存在官方卸载方式时才进入手动清理。
- `C:\Windows`、`System32`、`SysWOW64`、`WinSxS`、`Windows\Installer`、`Program Files\WindowsApps` 以及各扫描根目录本身禁止删除。
- 系统组件、驱动、共享运行库默认保留；"待判断"类型永远不显示"可安全删除"。
