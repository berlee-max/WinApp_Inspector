# WinApp Inspector

Windows 10 / 11 上的第三方软件发现、归属判断、卸载与残留清理工具。

> WinApp Inspector 不是"垃圾清理工具"，而是一款帮助用户看清 Windows 电脑里"到底装了什么、这些文件属于谁、应该怎么卸载"的程序发现与卸载分析工具。

- 需求文档：[WinApp_Inspector需求文档_V0.1.md](WinApp_Inspector需求文档_V0.1.md)
- 开发指南（技术栈、结构、构建方式、安全红线、开发阶段）：[CLAUDE.md](CLAUDE.md)
- 技术栈：C# 12 / .NET 8 / WPF

## 功能状态

| 需求 | 状态 |
|---|---|
| 注册表 / AppX / 目录 / 进程 / 服务 / 启动项 / 计划任务 / 快捷方式扫描（§7） | 已实现 |
| EXE 元数据与数字签名读取（§7.3–7.4） | 已实现（仅内嵌 Authenticode 签名；目录签名的系统文件报告为"无内嵌签名"） |
| 应用归属、证据评分（§10.2）、十类分类（§9）、"为什么"依据（§42） | 已实现，纯逻辑，可在任意平台测试 |
| 界面：扫描首页（一键扫描 → 结果摘要卡片）+ 应用程序管理器（侧边栏分类、勾选、排序、搜索、批量卸载）+ 详情面板 | 已实现（第二版界面，按实际使用反馈重做） |
| 官方卸载（UninstallString / Quiet / MSI / AppX / 自带卸载器）（§20） | 已实现 |
| 卸载后残留扫描、逐项复选手动移除、回收站、操作日志（§21–25） | 已实现 |
| 设置、系统还原点、资源管理器右键菜单、导出 CSV / JSON / HTML（§19, §26–27, §44） | 已实现 |
| 扫描结果缓存（§33） | 已实现（启动时先显示上次结果，由用户手动重新扫描） |
| 自定义扫描目录（绿色软件文件夹，如 D:\Tools） | 已实现（设置中添加；子文件夹按便携程序发现，文件夹本身不会被删除） |
| 扫描范围之外启动的程序（任务管理器里的第三方后台进程） | 已实现（作为"绿色 / 便携"应用列出，显示从哪里运行；不提供删除其所在文件夹） |

## 解决方案结构

```text
WinAppInspector.sln
src/WinAppInspector.Core        net8.0                   模型、接口、规则（ProtectedPathRule、DeletionGuard）、证据权重、解析器
src/WinAppInspector.Analysis    net8.0                   归属 / 分类 / 残留 / 风险 / 清理计划 / 搜索 / 导出，纯逻辑
src/WinAppInspector.Scanners    net8.0-windows10.0.17763 注册表 / AppX / 目录 / 进程 / 服务 / 启动项 / 计划任务 / 快捷方式扫描器
src/WinAppInspector.Actions     net8.0-windows10.0.17763 卸载、清理（回收站）、残留扫描、还原点、操作日志、右键菜单
src/WinAppInspector.UI          net8.0-windows10.0.17763 WPF 界面
tests/WinAppInspector.Core.Tests        跨平台单元测试
tests/WinAppInspector.Analysis.Tests    跨平台单元测试
tests/WinAppInspector.Scanners.Tests    Windows 冒烟测试（[WindowsFact]，非 Windows 上自动跳过）
```

## 下载与运行

[Releases](https://github.com/berlee-max/WinApp_Inspector/releases) 提供两种 win-x64 包：

- `WinAppInspector-<版本>-win-x64-portable.zip`：**便携版**，不需要安装 .NET。解压得到一个文件夹：

  ```text
  WinAppInspector-<版本>\
    WinAppInspector.exe   启动器（原生程序，双击这个）
    README.md
    app\WinAppInspector.exe   程序本体
    runtime\                  自带的 .NET 8 桌面运行时
  ```

  启动器把 `runtime\` 交给 `app\WinAppInspector.exe` 使用，然后退出。WPF 不支持裁剪，所以 `runtime\` 是完整的 .NET 8 + WPF 运行时，解压后约 190 MB；这是自包含 WPF 程序的下限。
- `WinAppInspector-<版本>-win-x64-lite.zip`：**精简版**，单个 exe 约 8 MB（与便携版的 `app\WinAppInspector.exe` 是同一个文件），需要已安装 .NET 8 Desktop Runtime（没装时 exe 会提示下载）。

不提供压缩的单文件自包含 exe：字节数一样多，却要在每次启动时解压到临时目录。

如果程序没有出现窗口，请查看 `%LocalAppData%\WinAppInspector\logs\crash-*.log`，启动失败时会弹出错误框并把异常写到那里。

要求 Windows 10 1809 及以上。程序未签名，首次运行 SmartScreen 可能提示"未知发布者"。发布由 `.github/workflows/release.yml` 完成：推送 `v*` 标签，或在 Actions 里手动运行并填写版本号。在正式版发布之前，所有版本（`v0.1.1`、`v0.2.0` 等）都是测试版，标记为预发布；只有手动运行并勾选 `stable` 才会生成正式版。

## 构建与测试

需要 .NET 8 SDK（Microsoft 官方构建；Ubuntu 自带的 SDK 缺少 WPF 支持）。整个解决方案可以在 Windows、macOS、Linux 上编译；只能在 Windows 上运行。

```bash
dotnet build WinAppInspector.sln
dotnet test WinAppInspector.sln
```

CI（`.github/workflows/ci.yml`）在 `windows-latest` 上运行全部测试（包括真实调用扫描器的冒烟测试），在 `ubuntu-latest` 上验证跨平台编译与纯逻辑测试。

## 安全边界

- 任何删除 / 卸载 / 结束进程 / 停止服务操作都必须先展示证据、由用户逐项确认，再执行；禁止扫描后自动删除。
- 优先调用软件自己的官方卸载程序；只有不存在官方卸载方式时才进入手动清理。
- `C:\Windows`、`System32`、`SysWOW64`、`WinSxS`、`Windows\Installer`、`Program Files\WindowsApps` 以及各扫描根目录本身禁止删除；与其它程序共享的厂商目录（如 `Program Files\Google`）不会整体删除。
- 系统组件、驱动、共享运行库默认保留；"待判断"类型永远不显示"可安全删除"。
- 文件默认进回收站，无法进回收站的项目（注册表、服务、计划任务）必须单独确认"此操作将永久删除"。
- 需要管理员权限的操作（HKLM、系统级计划任务、服务、还原点）逐次触发 UAC，应用本身以普通权限运行。
