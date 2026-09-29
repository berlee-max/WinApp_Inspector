# WinApp Inspector —— Windows 第三方软件发现与卸载工具需求文档

> 文档版本：V0.1
  
> 目标平台：Windows 10 / Windows 11  
> 产品定位：本地第三方软件发现、归属判断、卸载与残留清理工具  
> 建议技术栈：C# + .NET 8 + WPF  
> 产品形态：Windows 桌面应用，可打包为独立 EXE，优先支持绿色版运行

---

# 1. 项目背景

Windows 用户在长期使用电脑后，通常会遇到以下问题：

- “设置 → 应用”中只能看到部分已经注册的软件；
- Geek Uninstaller、Revo 等卸载工具仍可能漏掉部分应用；
- `Program Files`、`ProgramData`、`AppData\Local`、`AppData\Roaming` 等目录中存在大量第三方文件夹；
- 很多目录名称无法直接判断属于哪个程序；
- 部分软件为绿色版、便携版、用户级安装、解压运行程序，没有标准卸载项；
- 软件卸载后，可能留下配置、缓存、启动项、服务、计划任务或文件目录；
- 用户通常无法判断某个目录究竟是：
  - 正常安装软件；
  - 微软系统组件；
  - 第三方程序；
  - 绿色软件；
  - 驱动或运行库；
  - 卸载残留；
  - 单纯缓存；
  - 可以安全删除的文件。

现有卸载工具多数以“已注册应用”为中心，而本项目需要进一步解决：

> **把 Windows 文件系统中与程序相关的目录、进程、注册表、服务、启动项、计划任务等信息聚合起来，还原成一个可理解的“应用实体”，帮助用户判断它是什么、是否正在使用、是否可以卸载，以及应该如何清理。**

---

# 2. 产品目标

## 2.1 核心目标

第一阶段只解决一个核心问题：

> 找出 Windows 电脑上的第三方程序，并尽可能判断它属于哪个软件、由谁发布、安装在哪里、是否还在使用、是否存在标准卸载方式，以及卸载后有哪些明确残留可以清理。

## 2.2 具体目标

工具需要实现：

1. 扫描 Windows 已注册的传统 Win32 应用；
2. 扫描 Microsoft Store / UWP / MSIX 应用；
3. 扫描常见程序目录；
4. 发现未注册的绿色版、便携版、用户级程序；
5. 自动识别程序归属；
6. 聚合同一程序的多个文件目录和系统关联项；
7. 判断是否属于微软系统组件或第三方应用；
8. 判断应用当前是否正在运行；
9. 判断是否存在标准卸载方式；
10. 调用程序自己的卸载器完成标准卸载；
11. 对明确属于已卸载软件的残留进行清理；
12. 支持对任意文件夹进行“程序归属分析”；
13. 支持从资源管理器右键调用分析；
14. 任何高风险操作必须经过用户确认。

---

# 3. 非目标

V1.0 不建设以下功能：

- 不做杀毒；
- 不做恶意软件查杀；
- 不做系统注册表“一键清理”；
- 不做驱动自动卸载；
- 不做系统组件强制删除；
- 不做 Windows 优化大师；
- 不做内存释放；
- 不做垃圾文件全面清理；
- 不做磁盘碎片整理；
- 不做系统补丁管理；
- 不做软件商店；
- 不做软件自动更新平台；
- 不自动删除任何“无法确认归属”的文件。

---

# 4. 用户画像

主要面向：

- 希望清理长期使用 Windows 电脑的普通用户；
- 需要排查未知第三方程序的用户；
- 经常在 `AppData`、`Program Files` 中发现陌生目录的用户；
- 需要卸载企业办公电脑非必要第三方软件的运维人员；
- 希望在重装系统前盘点本机程序的用户。

---

# 5. 产品设计原则

## 5.1 安全优先

所有删除操作遵循：

> 能识别 → 展示证据 → 用户确认 → 执行。

禁止：

> 扫描 → 自动判断 → 自动删除。

## 5.2 优先调用官方卸载方式

如果发现软件自己的卸载程序，必须优先：

- 调用 UninstallString；
- 调用 QuietUninstallString；
- 调用 MSI 卸载；
- 调用软件自带 `uninstall.exe`；
- 调用 MSIX / AppX 正常卸载。

只有不存在官方卸载方式时，才进入手动清理流程。

## 5.3 证据驱动判断

不能仅根据文件夹名称判断应用。

需要综合：

- 注册表；
- EXE 元数据；
- 数字签名；
- Publisher；
- ProductName；
- FileDescription；
- 服务；
- 启动项；
- 计划任务；
- 进程；
- 安装目录；
- UWP 包；
- 快捷方式；
- 文件创建/修改信息。

## 5.4 保守删除

只允许删除：

- 明确属于目标软件；
- 没有被其它程序共享；
- 用户已经确认；
- 不属于 Windows 系统目录；
- 不属于驱动、运行库、系统依赖项。

---

# 6. 系统范围

需要重点扫描以下位置。

## 6.1 系统级程序目录

```text
C:\Program Files
C:\Program Files (x86)
C:\ProgramData
```

## 6.2 当前用户程序目录

```text
%LOCALAPPDATA%
%APPDATA%
%USERPROFILE%\AppData\LocalLow
```

常见实际路径：

```text
C:\Users\<用户名>\AppData\Local
C:\Users\<用户名>\AppData\Roaming
C:\Users\<用户名>\AppData\LocalLow
```

## 6.3 可选扩展目录

```text
%USERPROFILE%\Downloads
%USERPROFILE%\Desktop
%USERPROFILE%\Documents
```

该部分默认不作为自动扫描主目录，仅用于“指定目录分析”。

---

# 7. 数据来源

系统应综合以下数据源。

## 7.1 Windows 卸载注册表

```text
HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall
HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall
```

提取：

- DisplayName
- DisplayVersion
- Publisher
- InstallLocation
- InstallDate
- EstimatedSize
- UninstallString
- QuietUninstallString
- DisplayIcon
- WindowsInstaller
- SystemComponent
- ReleaseType

---

## 7.2 AppX / MSIX / Microsoft Store

读取：

- Package Name
- PackageFullName
- PackageFamilyName
- Publisher
- Version
- InstallLocation
- Architecture
- IsFramework
- NonRemovable

---

## 7.3 EXE 文件信息

重点读取：

- FileDescription
- ProductName
- ProductVersion
- FileVersion
- CompanyName
- OriginalFilename
- InternalName
- Copyright

---

## 7.4 数字签名

检查：

- 是否存在数字签名；
- 签名是否有效；
- 证书 Subject；
- 证书 Issuer；
- 签名时间；
- 发布者名称。

数字签名仅作为归属判断依据之一，不直接代表安全。

---

## 7.5 当前进程

读取：

- ProcessName
- PID
- ExecutablePath
- CommandLine
- ParentProcess
- CompanyName
- ProductName

用于判断：

- 软件是否正在运行；
- 哪些文件夹当前被程序使用；
- 是否需要卸载前关闭程序。

---

## 7.6 Windows 服务

读取：

- Name
- DisplayName
- State
- StartMode
- PathName
- StartName
- Description

建立服务路径与程序目录之间的映射。

---

## 7.7 启动项

扫描：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce
HKLM\Software\Microsoft\Windows\CurrentVersion\Run
HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce
HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run
```

同时扫描：

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
%PROGRAMDATA%\Microsoft\Windows\Start Menu\Programs\Startup
```

---

## 7.8 计划任务

读取：

- TaskName
- TaskPath
- Execute
- Arguments
- WorkingDirectory
- Trigger
- State
- Author

用于发现：

- 软件自动更新任务；
- 后台驻留任务；
- 已卸载软件残留任务。

---

## 7.9 快捷方式

可扫描：

```text
开始菜单
桌面
公共桌面
任务栏固定项
```

解析 `.lnk` 目标路径。

用于帮助建立“程序目录 → 软件名称”的映射。

---

# 8. 应用实体模型

系统最终不应简单展示“文件夹”，而应生成统一的应用实体。

示例：

```text
应用名称：Spotify

开发者：
Spotify AB

版本：
1.2.x

类型：
用户级第三方应用

主要目录：
C:\Users\User\AppData\Roaming\Spotify

主程序：
Spotify.exe

安装来源：
注册表 + 文件扫描

卸载程序：
存在

启动项：
1

服务：
0

计划任务：
1

运行进程：
2

磁盘占用：
850 MB

数字签名：
有效

判断：
正常安装的第三方应用
```

---

# 9. 应用分类体系

系统至少需要区分以下类型。

## 9.1 正常安装的第三方程序

特征：

- 存在卸载注册表；
- 存在 Publisher；
- 存在明确安装目录；
- 存在标准卸载器。

状态：

```text
第三方应用
```

---

## 9.2 Microsoft Store / MSIX 应用

状态：

```text
商店应用
```

---

## 9.3 绿色 / 便携应用

特征：

- 有完整 EXE；
- 有 ProductName / Publisher；
- 没有卸载注册表；
- 没有 MSI；
- 文件集中在独立目录。

状态：

```text
便携应用
```

---

## 9.4 用户级安装程序

例如安装在：

```text
AppData\Local\Programs
AppData\Roaming
```

状态：

```text
用户级应用
```

---

## 9.5 疑似卸载残留

典型特征：

- 原卸载注册表已经不存在；
- 主 EXE 不存在；
- 不存在运行进程；
- 不存在有效启动项；
- 仅剩配置 / Cache / Logs；
- 文件长期未更新。

状态：

```text
疑似残留
```

---

## 9.6 缓存目录

典型名称：

```text
Cache
Caches
Code Cache
GPUCache
Temp
Logs
Crashpad
CrashReports
```

但必须首先找到所属应用。

状态：

```text
应用缓存
```

---

## 9.7 驱动 / 硬件组件

例如：

- NVIDIA；
- AMD；
- Intel；
- Realtek；
- HP；
- Lenovo；
- Dell。

状态：

```text
硬件或驱动组件
```

默认不建议删除。

---

## 9.8 运行库 / 共享组件

例如：

- Microsoft Visual C++ Runtime；
- .NET；
- WebView2；
- DirectX 组件；
- Java Runtime；
- VC Runtime；
- Python Runtime。

状态：

```text
共享运行组件
```

默认不建议清理。

---

## 9.9 Windows / Microsoft 系统组件

状态：

```text
系统组件
```

默认隐藏，可通过筛选器显示。

---

## 9.10 无法判断

状态：

```text
待判断
```

该类型禁止直接显示“安全删除”。

---

# 10. 识别规则

## 10.1 软件归属判断

建立以下关系：

```text
目录
→ EXE
→ ProductName
→ CompanyName
→ 数字签名
→ 注册表
→ 服务
→ 启动项
→ 计划任务
→ 当前进程
```

然后聚合为应用实体。

---

## 10.2 目录归属评分

建议建立内部可信度评分，不一定直接显示为百分制。

证据权重示例：

| 证据 | 权重 |
|---|---:|
| 注册表 InstallLocation 完全匹配 | 高 |
| 主 EXE ProductName 匹配 | 高 |
| 数字签名 Publisher 匹配 | 高 |
| UninstallString 指向目录 | 高 |
| 服务 executable 指向目录 | 中高 |
| 当前运行进程位于目录 | 中高 |
| 启动项指向目录 | 中 |
| 计划任务指向目录 | 中 |
| 文件夹名称相似 | 低 |
| 最近修改时间 | 辅助 |

---

# 11. 系统安全规则

以下目录禁止直接删除：

```text
C:\Windows
C:\Windows\System32
C:\Windows\SysWOW64
C:\Windows\WinSxS
C:\Windows\Installer
```

以及：

```text
C:\Program Files\WindowsApps
```

除非未来建立专门的 Store App 卸载流程。

---

# 12. 页面结构

V1.0 建议仅保留三个主页面。

```text
软件总览
程序扫描
卸载与清理
```

顶部右侧：

```text
重新扫描
设置
关于
```

---

# 13. 页面一：软件总览

## 13.1 页面目标

让用户快速看到：

> 电脑里到底有哪些程序。

顶部统计：

```text
已识别应用
第三方程序
便携应用
疑似残留
系统组件
待判断
```

---

## 13.2 软件列表

字段：

| 字段 | 说明 |
|---|---|
| 软件名称 | 应用名称 |
| 图标 | 从程序 EXE 提取 |
| 发布者 | Publisher |
| 版本 | Version |
| 类型 | 安装程序 / 便携 / 商店 / 残留等 |
| 安装位置 | 主路径 |
| 磁盘占用 | 估算容量 |
| 状态 | 正常 / 运行中 / 残留 / 待判断 |
| 操作 | 查看详情 |

---

## 13.3 筛选

支持：

```text
全部
第三方
Microsoft
便携程序
用户级程序
疑似残留
驱动组件
运行库
待判断
```

---

## 13.4 搜索

支持搜索：

- 软件名称；
- Publisher；
- 文件夹名称；
- EXE 名称；
- 安装路径。

---

# 14. 软件详情页面

点击软件进入详情。

建议使用右侧详情面板或独立详情页。

显示：

## 14.1 基本信息

```text
软件名称
图标
版本
开发者
安装日期
最后修改时间
数字签名
```

## 14.2 安装信息

```text
安装类型
安装目录
卸载命令
MSI ProductCode
AppX Package
```

## 14.3 关联目录

```text
Program Files
ProgramData
AppData\Local
AppData\Roaming
LocalLow
```

## 14.4 运行状态

```text
进程数量
PID
可执行文件路径
```

## 14.5 启动关联

```text
启动项
服务
计划任务
```

## 14.6 判断结果

例如：

```text
判断：
正常安装的第三方程序

依据：
✓ 注册表中存在安装记录
✓ 数字签名有效
✓ Publisher：Google LLC
✓ 主程序正在运行
✓ 官方卸载程序存在
```

---

# 15. 页面二：程序扫描

该页面用于发现 Geek 等卸载器看不到的程序。

---

# 16. 扫描流程

用户点击：

```text
开始扫描
```

扫描：

```text
Program Files
Program Files (x86)
ProgramData
AppData\Local
AppData\Roaming
AppData\LocalLow
```

扫描过程显示：

```text
正在扫描 Program Files
正在读取应用信息
正在校验数字签名
正在匹配注册表
正在分析启动项
正在分析服务
正在分析计划任务
正在建立应用归属关系
```

---

# 17. 扫描结果

展示：

```text
已识别程序
未注册程序
疑似绿色软件
疑似残留
未知目录
```

列表字段：

| 字段 | 内容 |
|---|---|
| 文件夹 | 目录名称 |
| 归属程序 | 判断的软件名称 |
| Publisher | 发布者 |
| 大小 | 文件夹容量 |
| 主程序 | EXE |
| 注册记录 | 有 / 无 |
| 运行状态 | 运行 / 未运行 |
| 判断 | 程序 / 残留 / 缓存 / 未知 |

---

# 18. 文件夹分析功能

用户可选择任意目录：

```text
分析此文件夹
```

系统扫描：

```text
文件数量
目录大小
EXE
DLL
数字签名
ProductName
CompanyName
注册表
启动项
服务
计划任务
正在运行进程
```

输出：

```text
程序：
ABC Desktop

开发者：
ABC Inc.

目录：
C:\Users\User\AppData\Local\ABC

主程序：
abc.exe

注册表安装信息：
未发现

启动项：
1

计划任务：
0

服务：
0

当前正在运行：
是

判断：
第三方用户级程序

建议：
未发现标准卸载入口。
建议首先检查程序自身设置或卸载程序。
```

---

# 19. 资源管理器右键功能

安装后向 Windows Explorer 增加：

```text
使用 WinApp Inspector 分析
```

适用于：

- 文件夹；
- EXE；
- DLL。

例如：

```text
右键
→ 使用 WinApp Inspector 分析
```

打开软件并自动进入分析结果。

---

# 20. 页面三：卸载与清理

## 20.1 标准卸载

如果存在：

```text
UninstallString
QuietUninstallString
MSI ProductCode
uninstall.exe
AppX
```

优先提供：

```text
卸载
```

点击后：

1. 检查程序是否运行；
2. 提醒用户保存数据；
3. 调用官方卸载程序；
4. 等待卸载完成；
5. 重新扫描应用相关目录；
6. 显示卸载后残留。

---

# 21. 卸载后残留扫描

卸载完成后检查：

```text
Program Files
ProgramData
AppData\Local
AppData\Roaming
启动项
服务
计划任务
软件注册表配置
```

输出：

```text
卸载完成

发现残留：

AppData\Local\ABC     420 MB
AppData\Roaming\ABC    8 MB
计划任务               1
启动项                 0
服务                   0
```

用户自行选择是否删除。

---

# 22. 手动移除

仅适用于：

```text
便携程序
明确残留
无卸载器的用户级程序
```

执行前展示：

```text
即将移除：
```

并明确列出：

```text
程序目录
配置目录
缓存
启动项
计划任务
服务
注册表项
```

每项都有复选框。

---

# 23. 删除保护

删除前判断：

- 是否存在运行进程；
- 是否被服务调用；
- 是否存在其它应用依赖；
- 是否属于系统目录；
- 是否属于共享 Runtime；
- 是否存在低可信度关联。

如存在风险，禁止“一键移除”。

---

# 24. 回收站机制

普通文件清理默认：

```text
移动到回收站
```

而不是永久删除。

对无法进入回收站的大型目录，应提示：

```text
此操作将永久删除。
```

---

# 25. 操作日志

记录：

```text
扫描
卸载
结束进程
禁用启动项
删除目录
删除计划任务
删除服务
```

日志字段：

```text
时间
操作
目标
结果
错误信息
```

---

# 26. 设置页面

设置保持简单。

## 26.1 扫描范围

```text
Program Files
Program Files (x86)
ProgramData
AppData Local
AppData Roaming
LocalLow
```

可开启或关闭。

## 26.2 默认行为

```text
删除文件优先进入回收站
卸载前创建系统还原点
隐藏系统组件
扫描数字签名
扫描计划任务
扫描服务
```

---

# 27. 系统还原点

对于高级清理，可选：

```text
执行前创建系统还原点
```

若系统未开启系统保护，则提示用户。

---

# 28. 权限设计

普通扫描：

```text
普通用户权限
```

需要管理员权限的操作：

```text
读取所有用户 AppX
删除系统级目录
修改 HKLM
停止服务
删除服务
删除系统级计划任务
卸载部分 MSI
```

需要管理员权限时再触发 UAC，而不是整个应用始终管理员运行。

---

# 29. UI 风格

建议：

- Windows 11 风格；
- 简洁；
- 轻量；
- 不做传统“电脑管家”视觉；
- 不使用大量红黄绿评分；
- 核心是信息和证据。

主色保持中性。

---

# 30. 状态颜色原则

颜色只辅助，不作为唯一信息。

例如：

```text
第三方程序    普通
疑似残留      提醒
系统组件      弱化
待判断        警示
运行中        状态标记
```

---

# 31. 性能要求

## 首次扫描

目标：

- 普通办公电脑：可接受；
- 扫描过程中允许用户查看已有结果；
- 不阻塞 UI。

采用：

```text
异步扫描
后台任务
分批更新 UI
缓存扫描结果
```

---

# 32. 文件夹大小计算

不要一开始递归计算所有目录。

建议：

第一阶段：

```text
快速发现应用
```

第二阶段后台：

```text
逐步计算空间占用
```

否则 AppData 扫描速度会严重下降。

---

# 33. 数据缓存

本地保存：

```text
应用实体
扫描时间
文件夹状态
版本
Publisher
签名结果
```

下一次扫描：

只重新分析发生变化的目录。

---

# 34. 错误处理

包括：

```text
文件访问被拒绝
目录被占用
卸载器不存在
卸载失败
服务无法停止
权限不足
签名读取失败
注册表项损坏
```

错误必须展示具体原因。

---

# 35. V1.0 MVP 功能

第一版只实现：

## P0

- 注册表应用扫描；
- AppX 应用扫描；
- Program Files 扫描；
- AppData 扫描；
- EXE 元数据识别；
- 数字签名读取；
- 软件归属判断；
- 应用列表；
- 软件详情；
- 标准卸载；
- 残留目录识别；
- 文件夹单独分析。

## P1

- 启动项关联；
- 服务关联；
- 计划任务关联；
- 当前进程关联；
- 软件磁盘占用；
- 卸载后残留扫描。

## P2

- Explorer 右键菜单；
- 系统还原点；
- 操作历史；
- 导出扫描报告。

---

# 36. 建议开发阶段

## 阶段一：扫描引擎

实现：

```text
RegistryScanner
AppxScanner
DirectoryScanner
ExeMetadataReader
SignatureReader
ProcessScanner
```

---

## 阶段二：应用归属引擎

实现：

```text
ApplicationResolver
DirectoryMatcher
PublisherMatcher
ExecutableMatcher
```

把不同来源数据合并。

---

## 阶段三：应用总览

完成：

```text
应用列表
筛选
搜索
详情
```

---

## 阶段四：卸载

实现：

```text
UninstallString
QuietUninstall
MSI
AppX
uninstall.exe
```

---

## 阶段五：残留检测

实现：

```text
文件夹
启动项
服务
计划任务
软件配置
```

---

# 37. 推荐项目结构

```text
WinAppInspector
│
├─ WinAppInspector.UI
│   ├─ Views
│   ├─ ViewModels
│   └─ Controls
│
├─ WinAppInspector.Core
│   ├─ Models
│   ├─ Services
│   └─ Rules
│
├─ WinAppInspector.Scanners
│   ├─ RegistryScanner
│   ├─ DirectoryScanner
│   ├─ AppxScanner
│   ├─ ProcessScanner
│   ├─ ServiceScanner
│   ├─ StartupScanner
│   └─ TaskScanner
│
├─ WinAppInspector.Analysis
│   ├─ AppResolver
│   ├─ RiskClassifier
│   └─ ResidueDetector
│
└─ WinAppInspector.Actions
    ├─ UninstallManager
    ├─ CleanupManager
    └─ RestorePointManager
```

---

# 38. 核心数据模型示例

```csharp
ApplicationEntity
{
    Id
    Name
    Version
    Publisher
    AppType
    InstallLocation
    MainExecutable
    IconPath
    UninstallCommand
    QuietUninstallCommand
    Signature
    IsRunning
    IsMicrosoft
    RiskLevel
    DetectionConfidence

    Directories[]
    Processes[]
    Services[]
    StartupItems[]
    ScheduledTasks[]
    RegistryEntries[]
}
```

---

# 39. 软件识别示例

例如发现：

```text
C:\Users\User\AppData\Local\Foo
```

扫描：

```text
Foo.exe
```

读取：

```text
ProductName: Foo Desktop
CompanyName: Foo Technologies
```

数字签名：

```text
Foo Technologies Ltd.
```

启动项：

```text
Foo -> Foo.exe
```

注册表：

```text
不存在 Uninstall
```

当前进程：

```text
Foo.exe 正在运行
```

最终生成：

```text
软件名称：
Foo Desktop

Publisher：
Foo Technologies Ltd.

类型：
用户级第三方应用

卸载方式：
未发现

状态：
正在运行

判断可信度：
高
```

而不是仅显示：

```text
Foo
```

---

# 40. 清理示例

目标程序已经卸载。

发现：

```text
C:\Users\User\AppData\Local\Foo
```

分析：

```text
主 EXE：不存在
卸载项：不存在
启动项：不存在
服务：不存在
计划任务：不存在
最近修改：8个月前
目录内容：Cache + Logs
```

结果：

```text
判断：
疑似卸载残留

建议：
可以考虑清理。

删除范围：
仅当前目录。
```

---

# 41. 风险示例

发现：

```text
C:\Program Files\Microsoft\EdgeWebView
```

系统检测：

```text
Publisher：
Microsoft Corporation

多个第三方应用引用 WebView2 Runtime
```

结果：

```text
类型：
共享运行组件

建议：
保留

不提供：
直接删除目录
```

---

# 42. 软件详情中的“为什么”

每个判断必须允许用户查看理由。

例如：

```text
为什么判定为残留？

✓ 未发现应用卸载记录
✓ 未发现主程序
✓ 无正在运行进程
✓ 无 Windows 服务
✓ 无启动项
✓ 最后更新时间：2025-11-21
```

这样比简单显示：

```text
可删除
```

更加可信。

---

# 43. 搜索方式

用户可以输入：

```text
Bing
Microsoft
Tencent
WeChat
abc.exe
AppData\Local\xxx
```

系统同时搜索：

```text
软件名
Publisher
ProductName
EXE
路径
注册表
服务名
计划任务
```

---

# 44. 导出报告

后续支持：

```text
CSV
JSON
HTML
```

至少包括：

```text
软件
版本
Publisher
类型
安装目录
状态
是否运行
卸载方式
磁盘占用
```

方便用户重装前备份软件清单。

---

# 45. 验收标准

V1.0 达到以下条件即可认为可用。

## 软件发现

能够发现：

- Windows 注册应用；
- 当前用户安装应用；
- AppData 中独立程序；
- Store 应用；
- 部分绿色应用。

## 软件判断

对于主流正常程序能够正确展示：

```text
名称
Publisher
版本
路径
卸载方式
```

## 安全

必须保证：

- 不主动删除文件；
- 不自动删除注册表；
- 不自动卸载；
- 系统组件默认不可清理；
- 删除动作需要确认。

## 卸载

标准软件：

```text
能正确调用其官方卸载程序。
```

## 残留

卸载完成后：

```text
能够重新扫描并显示明确残留。
```

---

# 46. 后续 V2 可考虑

V2 再增加：

- 批量卸载；
- 软件安装时间线；
- 文件变化监控；
- 新软件安装提醒；
- 软件自动启动影响；
- 软件网络访问；
- 软件更新组件识别；
- 浏览器扩展盘点；
- Shell Extension 盘点；
- Context Menu Handler 分析；
- 文件关联分析；
- 进程树分析；
- 可信软件数据库；
- VirusTotal 等外部安全服务接入（可选）。

---

# 47. 一句话产品定义

> **WinApp Inspector 不是“垃圾清理工具”，而是一款帮助用户看清 Windows 电脑里“到底装了什么、这些文件属于谁、应该怎么卸载”的程序发现与卸载分析工具。**

---

# 48. V1.0 最终边界

第一期只聚焦：

```text
发现程序
↓
识别程序
↓
解释依据
↓
查看详情
↓
调用官方卸载
↓
识别明确残留
↓
用户确认后清理
```

不向“万能电脑管家”扩张。

这是整个项目最重要的边界。
