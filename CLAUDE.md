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
- UI 风格：两种页面——深色渐变的扫描首页（一个大圆形"扫描"按钮、结果摘要卡片）和浅色的应用程序管理器（侧边栏分类 + 列表 + 底部批量操作栏）；以信息和证据为核心，不做红黄绿评分（需求 §29–30）。不要再加流程指引文字和统计磁贴。
- 列表以"应用"为单位，不以目录为单位：目录扫描只用于归属和残留判断。默认根目录下没有运行、也没有任何关联项的绿色软件不进"所有应用程序"，只在"绿色 / 便携"分类里可见。
- `tools/check_xaml_resources.py` 在 CI 上检查所有 `{StaticResource}` / `Localize.Get` 键都有定义、没有重复（同一文件内或跨 `Strings.zh-CN.xaml` / `Theme.xaml`；WPF 只会在运行时报缺失资源，跨文件重名会让 `Text="{StaticResource X}"` 拿到一个 Style 而在窗口构造时崩溃）。字符串键和样式键不要同名。
- 任何未捕获异常都由 `CrashReporter` 写到 `%LocalAppData%\WinAppInspector\logs\crash-*.log` 并弹框；`WinAppInspector.exe --self-test` 会显示窗口、切换每个页面后以 0 退出，CI 的 Windows 任务和发布流程都用 `tools/smoke-launch.ps1` 真机跑一次，启动失败的构建不会进入 Release。

## 解决方案结构（对应需求 §37）

```text
WinAppInspector.sln
src/WinAppInspector.Core        net8.0          模型、接口、规则、通用服务；不依赖 Windows API
src/WinAppInspector.Analysis    net8.0          AppResolver / RiskClassifier / ResidueDetector；纯逻辑
src/WinAppInspector.Scanners    net8.0-windows  Registry / Directory / Appx / Process / Service / Startup / Task 扫描器
src/WinAppInspector.Actions     net8.0-windows  UninstallManager / CleanupManager / RestorePointManager
src/WinAppInspector.UI          net8.0-windows  WPF 应用：Views / ViewModels / Controls
src/WinAppInspector.Launcher    net8.0-windows  便携版启动器（发布时 NativeAOT）：设置 DOTNET_ROOT=runtime\ 后启动 app\WinAppInspector.exe
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

## 版本与发布策略

- 项目处于测试期（预计一到两个月、多轮不同环境测试）。所有发布都是**测试版**，按常规语义化版本递增：修复和小调整升补丁号（`v0.1.1`），功能或界面有明显变化升次版本号（`v0.2.0`）；不再使用 `-preview.N` 后缀。
- 测试版通过 Actions 手动运行 `release.yml` 并填写版本号发布，工作流默认标记为预发布（pre-release）。
- **正式版只在用户明确说"可以发正式版"之后发布**：手动运行 `release.yml` 并勾选 `stable`。在此之前不要自行勾选，也不要把任何版本描述为正式版。
- 云端会话的凭据不能推送 tag；如需打 tag，由用户在本地执行 `git tag vX.Y.Z && git push origin vX.Y.Z`。

## 已完成阶段与分支（按顺序叠加，每个分支包含前一个）

| 阶段 | 分支 | 内容 |
|---|---|---|
| 1 | `main-l05rvm` | 脚手架、Core 模型、ProtectedPathRule / DeletionGuard、CI |
| 2 | `feature/scan-engine` | 全部扫描器 + Core 解析器 + Windows 冒烟测试 |
| 3 | `feature/attribution` | ApplicationResolver、匹配器、分类器、残留判断 |
| 4 | `feature/ui` | WPF 三页面 + 详情面板 + 设置 + 搜索 |
| 5 | `feature/uninstall-cleanup` | 官方卸载、残留扫描、手动清理、操作日志 |
| 6 | `feature/export-cache-shell` | 导出报告、扫描缓存、资源管理器右键菜单 |
| 7 | `feature/review-fixes` | Windows 专属代码审查修复（回收站布局、启动文件夹清理、AppX 错误等） |
| 8 | `feature/orphan-processes-custom-roots` | 未归属运行项列表、自定义扫描目录 |
| 9 | `feature/app-manager-ui` | 第二版界面：扫描首页 + 应用程序管理器；扫描范围外的运行程序成为独立条目 |
| 10 | `fix/startup-crash-launcher` | 修复启动即崩溃（字符串与样式重名）；崩溃日志与错误框；`--self-test` 在 CI 真机启动；便携版改为启动器 + `app\` + `runtime\`；应用图标 |

## 验证状态（请如实更新）

- 在 Linux 上：`dotnet build` 全部通过（0 警告），Core / Analysis 单元测试全部通过。
- 在 Windows 上（CI windows-latest，Windows Server 2025）：`tests/WinAppInspector.Scanners.Tests` 中的 `[WindowsFact]` 冒烟测试在每个 PR 上真实执行并全部通过，覆盖注册表 / AppX（PackageManager 与 PowerShell）/ 目录 / 进程 / 服务 / 启动项 / 计划任务扫描器、WinVerifyTrust 签名读取、操作日志、注册表探测、临时目录的回收站删除、Startup 文件夹清理和卸载管理器的拒绝路径。
- 首次 Windows 运行暴露并已修复的问题（都在 Linux 无法执行的代码里）：签名者取的是证书 CN（".NET"）而不是 O（"Microsoft Corporation"）；根目录计划任务的 `TaskPath` 为空；`SHFILEOPSTRUCT` 用了 `Pack=1`（32 位布局），x64 上 `SHFileOperationW` 直接 AccessViolation。
- 用户首次在 Windows 上运行 v0.1.0-preview.2：便携版与精简版双击都没有任何反应。根因是 `Manager.Back` 同时是字符串键和样式键，主窗口构造时 XamlParseException，进程在窗口出现前退出且没有任何提示。已修复，并加了崩溃日志、`--self-test` 和 CI 真机启动。
- 在 Windows 上（CI）：`--self-test` 会启动 WPF 界面并渲染首页 / 扫描中 / 摘要 / 管理器四个页面（验证 XAML 与 DI 能加载），发布流程对便携版（经启动器）和精简版各跑一次。
- 仍未在 Windows 上验证：界面的实际布局效果与数据绑定（关注输出窗口的 System.Windows.Data Error）、对真实软件的卸载与残留清理、资源管理器右键菜单注册与 `--analyze` 启动参数、系统还原点创建。
