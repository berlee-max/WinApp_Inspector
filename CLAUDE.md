# WinApp Inspector — 开发指南

## 项目是什么

Windows 10/11 桌面工具：发现第三方软件、判断归属、调用官方卸载、识别明确残留。
完整需求见 `WinApp_Inspector需求文档_V0.1.md`（48 节）。第 48 节的边界是最高原则：
只做"发现 → 识别 → 解释依据 → 查看详情 → 调用官方卸载 → 识别明确残留 → 用户确认后清理"，不做"万能电脑管家"。

## 技术栈

- C# 12 / .NET 8；UI 用 WPF（TargetFramework `net8.0-windows`）
- MVVM：CommunityToolkit.Mvvm
- 依赖注入 / 宿主：Microsoft.Extensions.DependencyInjection + Microsoft.Extensions.Hosting
- 日志：Microsoft.Extensions.Logging，写入本地文件（需求 §25 操作日志单独建模）
- 测试：xUnit（+ FluentAssertions 可选）
- UI 风格：Windows 11 风格、中性主色、以信息和证据为核心，不做红黄绿评分（需求 §29–30）

## 解决方案结构（对应需求 §37）

```text
WinAppInspector.sln
src/WinAppInspector.Core        net8.0          模型、接口、规则、通用服务；不依赖 Windows API
src/WinAppInspector.Analysis    net8.0          AppResolver / RiskClassifier / ResidueDetector；纯逻辑
src/WinAppInspector.Scanners    net8.0-windows  Registry / Directory / Appx / Process / Service / Startup / Task 扫描器
src/WinAppInspector.Actions     net8.0-windows  UninstallManager / CleanupManager / RestorePointManager
src/WinAppInspector.UI          net8.0-windows  WPF 应用：Views / ViewModels / Controls
tests/WinAppInspector.Core.Tests      net8.0
tests/WinAppInspector.Analysis.Tests  net8.0
```

原则：所有 Windows 专属访问（注册表、WMI、Win32、AppX）只出现在 Scanners / Actions，
通过 Core 中的接口暴露；Analysis 只消费 Core 模型。这样归属判断、分类、评分逻辑可以在 macOS / Linux 上用假数据测试。

## 在非 Windows 机器上构建

开发者机器是 macOS，云端会话是 Linux，两者都不能运行 WPF，但必须能编译整个解决方案：

- `Directory.Build.props` 统一设置 `EnableWindowsTargeting=true`、`Nullable=enable`、`ImplicitUsings=enable`、`TreatWarningsAsErrors=true`
- `dotnet build WinAppInspector.sln` 与 `dotnet test WinAppInspector.sln` 必须在 macOS / Linux 上通过
- 若某个 `net8.0-windows` 项目确实无法在非 Windows 上编译，用解决方案筛选器（.slnf）把它排除在跨平台构建之外，并在 PR 中说明，不要静默跳过
- 真实运行和手动测试只能在 Windows 上；`.github/workflows/ci.yml` 在 `windows-latest` 上做 build + test，可再加一个 `ubuntu-latest` 任务验证跨平台编译
- 沙箱没有 .NET SDK 时，用 `https://dot.net/v1/dotnet-install.sh --channel 8.0` 安装到 `~/.dotnet`
- 若 `builds.dotnet.microsoft.com` 被网络策略拦截，改用 `packages.microsoft.com`（可达）：从
  `https://packages.microsoft.com/ubuntu/22.04/prod/pool/main/d/` 下载 Microsoft 官方构建的
  `dotnet-sdk-8.0`、`dotnet-runtime-8.0`、`dotnet-hostfxr-8.0`、`dotnet-host`、`dotnet-targeting-pack-8.0`、
  `dotnet-apphost-pack-8.0`、`aspnetcore-runtime-8.0`、`aspnetcore-targeting-pack-8.0`、`netstandard-targeting-pack-2.1`
  的 .deb，`dpkg -x` 解包后把 `usr/share/dotnet` 或 `usr/lib/dotnet` 的内容合并到 `~/.dotnet`。
  注意：Ubuntu 自带的 `apt install dotnet-sdk-8.0` 是 Canonical 源码构建，缺少 `Microsoft.NET.Sdk.WindowsDesktop`，无法编译 WPF 项目。

## 安全红线（需求 §5、§11、§23、§24、§45）

- 任何删除 / 卸载 / 结束进程 / 停止服务 / 删除任务：展示证据 → 用户确认 → 执行。禁止扫描后自动删除。
- 优先调用官方卸载：UninstallString → QuietUninstallString → MSI → AppX → 自带 uninstall.exe。
- 禁止删除 `C:\Windows`、`System32`、`SysWOW64`、`WinSxS`、`Windows\Installer`、`Program Files\WindowsApps`。
- 系统组件、驱动 / 硬件组件、共享运行库默认"保留"，不提供直接删除入口。
- "待判断"类型永远不显示"可安全删除"。
- 文件删除默认进回收站；无法进回收站的目录必须明示"此操作将永久删除"。
- 这些规则在 Core 中实现为可测试的规则类（如 `ProtectedPathRule`、`DeletionGuard`），并配单元测试。

## 代码约定

- 标识符、注释、提交信息用英文；UI 文案用简体中文，集中放在资源文件中。
- 一个扫描器一个类，实现统一的 `IScanner<T>`；扫描结果用不可变 `record`。
- 扫描异步、可取消（`CancellationToken`）、用 `IProgress<ScanProgress>` 报告进度；不阻塞 UI 线程，分批更新列表。
- 目录大小计算放在第二阶段后台任务，不在发现阶段递归（需求 §32）。
- 错误必须携带具体原因（需求 §34），不要吞掉异常；权限不足时给出明确提示而不是整个应用以管理员运行（需求 §28）。
- 每个判断都要能回答"为什么"：实体上保留依据列表（需求 §42）。

## 开发阶段（对应需求 §36，按 P0 → P1 → P2）

1. 脚手架：解决方案、`Directory.Build.props`、CI、Core 模型（需求 §38）与接口、`AppType` 十类枚举（§9）、证据权重模型（§10.2）、保护规则（§11）
2. 扫描引擎：RegistryScanner、AppxScanner、DirectoryScanner、ExeMetadataReader、SignatureReader、ProcessScanner
3. 归属引擎：ApplicationResolver、DirectoryMatcher、PublisherMatcher、ExecutableMatcher；分类与评分；依据输出
4. 应用总览 UI：列表、筛选、搜索、统计、详情面板
5. 卸载：UninstallString / Quiet / MSI / AppX / uninstall.exe，卸载后重扫残留
6. 残留检测与手动移除：逐项复选确认、回收站、操作日志
7. P1 / P2：启动项、服务、计划任务、进程关联，磁盘占用，右键菜单，系统还原点，导出报告

## Git 工作流

- 每个阶段一个功能分支（如 `feature/scaffold`），开 PR 到 `main`；PR 描述写清楚做了什么、如何验证。
- 提交信息用 Conventional Commits（feat / fix / docs / test / chore）。
- 提交前本地 `dotnet build` + `dotnet test` 必须通过。
