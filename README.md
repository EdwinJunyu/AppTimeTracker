# AppTimeTracker · 软件时钟

中文 Windows x64 桌面计时器，自动记录指定软件的窗口打开时长，并为每次计时保存工作备注。当前版本为 **1.1.4**。

提供免安装版和小型版，功能、界面与数据格式相同。版本更新见 [CHANGELOG.md](CHANGELOG.md)。本仓库保存源码、发布配置和回归测试，二进制发布产物不提交到源码仓库；可按下方命令自行构建。

## 功能概览

- 按 EXE 完整路径监控软件窗口，显示本次、今日和累计时长。
- 同一软件多个窗口只累计一次，不同软件可同时计时。
- 锁屏、休眠时暂停，恢复后继续；切换软件或普通键鼠无操作不会暂停。
- 每次计时可填写、修改或清空独立备注，支持中文和多行。
- 提供会话明细、每日汇总和累计汇总，以及 UTF-8 BOM CSV 导出。
- 关闭主窗口后继续在托盘运行，支持当前用户登录后自动启动。
- 使用本地 SQLite 保存配置和历史，支持异常退出恢复及旧数据兼容读取。

## 选择版本

| 版本 | 运行条件 | 解压后大小 | ZIP 大小 |
|---|---|---:|---:|
| 免安装版 `Portable` | 自带 .NET 10 桌面运行环境 | 约 119.37 MiB | 约 48.52 MiB |
| 小型版 `Small` | 预装 .NET 10 Windows Desktop Runtime x64 | 约 2.26 MiB | 约 1.17 MiB |

以上为 1.1.4 发布产物实测值，包含使用说明和更新记录；大小会随 SDK、运行时及压缩设置变化。小型版的体积未计入另外安装的运行时。

小型版需要的是 **Windows Desktop Runtime x64**；仅安装 ASP.NET Core Runtime 不够。可从 [.NET 10 官方下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0) 获取桌面运行时。使用发布包无需安装 .NET SDK 或 Python。

两种版本都应完整解压到长期保留的位置，并保留同目录的 DLL 等文件，不能只复制 `AppTimeTracker.exe`。数据存放在当前用户目录，移动程序目录不会迁移数据库。

## 快速开始

1. 完整解压发布包；小型版先安装所需桌面运行时。自行构建的方法见[开发与构建](#开发与构建)。
2. 双击 `AppTimeTracker.exe`。
3. 将目标软件的 EXE 拖入窗口，自动登记并启动；也可点击“添加软件…”填写路径，再手动打开目标软件或点击“启动”。
4. 目标窗口出现后自动计时。选中软件，点击“编辑备注…”记录这段时间做了什么。
5. 关闭主窗口后，计时器仍在托盘监控。需要完全退出时，右键托盘选择“退出并保存”。

默认启用“登录 Windows 后自动启动到托盘”，可在主页取消勾选。这是当前用户登录后的自启动，未登录时不会运行。首次运行会创建当前用户数据库和登录启动配置，不需要管理员权限。

同一 Windows 账号跨登录会话只运行一个实例。重复启动会唤起已有界面；若计时器已在另一个登录会话运行，请先在那里退出。

从脚本启动到托盘时，可使用 `AppTimeTracker.exe --tray`。

## 计时规则

| 场景 | 处理方式 |
|---|---|
| 可见顶层窗口或最小化窗口 | 计时；切换到其他软件不暂停 |
| 同一软件多个窗口或实例 | 累计一次，最后一个窗口关闭时结束会话 |
| 不同软件同时打开 | 分别计时 |
| 隐藏窗口、工具窗口 | 不计时 |
| 其他虚拟桌面的窗口 | 继续计时；应用主动通过 DWM 隐藏的窗口不计时 |
| 锁屏、休眠 | 暂停，恢复后继续；UAC 安全桌面不视为锁屏 |
| 普通键鼠无操作 | 不暂停 |
| 计时器启动前已经打开的软件 | 从发现时开始计时，不追溯过去 |

程序每秒检查一次，极短窗口可能漏过。锁定状态按真实 Windows 会话查询并持续校正；取消关机或注销后继续监控。

有效时长由单调时钟累计，系统时间调整不会直接放大时长。超过三秒且原因未知的采样间隔会保守排除，暂停秒数标为未知。时间回拨时保留真实起止时间，因此结束时间可能早于开始时间；日期筛选保留已知有效区间，不把回拨跨过的未知日期视为有效日期。

### 路径与启动器

- 按规范化的 EXE 完整路径识别，不按同名进程猜测；普通路径、Windows 扩展盘符和扩展 UNC 路径使用一致的匹配规则。
- 使用启动器时，可在“编辑…”中保留启动路径，把实际软件 EXE 的完整路径逐行填入监控路径。
- 路径失效会提示；受保护进程无法读取路径时，不会退回按进程名匹配。不保证所有商店应用或特殊启动器都能识别。
- 停用或移出软件会保留历史；重新添加相同启动路径可恢复已移出的软件。
- 编辑时若启动路径与另一条记录冲突，会拒绝保存并保留原 ID；恢复旧记录请使用添加操作。

## 计时备注

每个软件的每次计时对应一条独立备注，可留空。开始新一次计时时，备注默认为空，不沿用上次内容。

| 入口 | 编辑对象 |
|---|---|
| 主页选中软件 →“编辑备注…” | 正在进行的计时；没有当前计时时，编辑最近一次记录 |
| 双击主页备注单元格 | 与主页“编辑备注…”相同 |
| “历史与导出”→“会话明细”→选中记录→“编辑备注…” | 指定的历史或进行中会话；也可双击该行 |

计时中、暂停或结束后均可编辑。主页标明“本次”或“最近一次”；最近一次按持久化创建顺序确定，系统时间回拨不会选错记录。尚无计时记录时，备注入口不可用。

备注支持中文、多行、修改及清空。点击“保存备注”后存入数据库；保存失败会保留输入供重试。取消或关闭编辑窗口不保存修改，有未保存内容时会提示是否放弃。

## 历史与 CSV 导出

每次打开“历史与导出”，默认选择**所有软件**，不继承主页选中项或上次的单软件筛选。明细与每日视图使用当前日期范围，累计视图包含全部历史。界面每页 50 条，CSV 导出包含全部符合条件的记录，不限于当前页。

| 视图 | 数据范围 | 备注规则 |
|---|---|---|
| 会话明细 | 与所选日期范围重叠的完整会话 | 每次计时显示自身备注 |
| 每日汇总 | 所选日期范围内的有效片段 | 一行只有一次计时时显示该备注，多次计时留空 |
| 累计汇总 | 每个软件的全部历史，不受日期筛选影响 | 一行只有一次计时时显示该备注，多次计时留空 |

汇总不会合并或任选某次备注。查看或修改每次备注，请切换到会话明细。历史预览与 CSV 共用 `UsageHistory` 的筛选、分组、累计及备注规则；界面按时间倒序展示明细，CSV 按时间正序导出。

### 字段与格式

| CSV 类型 | 字段 |
|---|---|
| 会话明细 | 会话 ID、软件 ID、软件名称、启动路径、监控路径列表、计时类型、开始时间、结束时间、时区、有效时长、已确认暂停时长、状态、结束原因、异常恢复、总时长、备注 |
| 每日汇总 | 日期、软件 ID、软件名称、计时类型、有效时长、会话数、总时长、备注 |
| 累计汇总 | 软件 ID、软件名称、计时类型、首次记录时间、最近记录时间、会话数、总时长、备注 |

- CSV 使用 UTF-8 BOM，转义引号、逗号和换行；可能被 Excel 当作公式的文本添加保护前缀。
- 时长统一显示为 `HH:mm:ss`，小时可超过 24，例如 `126:42:10`。按累计秒数取整显示，数据库保留原有精度。
- 时间戳包含偏移；进行中会话结束时间为空，未知暂停时长为空。
- **“总时长”始终表示该软件自首次记录以来的全部有效累计**，包含当前未结束会话，不含暂停时间，也不受日期范围限制。明细或每日多行可能重复此值，请勿将这一列再次相加。
- 停用或移出软件的历史仍参与统计；同名不同软件 ID 分别计算。
- 明细保留完整会话，每日只统计所选日期片段，截断日期范围时两者时长不一定相等。Excel 打开已导出的 CSV 不会阻塞数据库计时。

## 数据保存、备份与升级

数据库位置：

```text
%LocalAppData%\AppTimeTracker\usage.db
```

配置、会话、备注及有效区间存入 SQLite。界面累计包含尚未到周期保存点的增量。

- 每五秒及会话开始、恢复、结束时提交后台保存；暂停、退出和系统会话结束时等待后台任务并保存最新尾段。
- 后台写入独立快照，采样继续进行；保存失败会合并最新记录重试，不以旧快照覆盖新增时长或已保存备注。
- 隐藏到托盘或最小化时继续采样与保存，跳过不可见表格刷新。
- 异常退出恢复到最后成功保存的位置，不补计离线时间。正常情况下可能损失约五秒尾段，磁盘长期故障时可能更多；写入失败时请勿强制关闭。
- 已有数据库损坏时不会以空库覆盖，会报告启动失败。

**备份：** 先从托盘“退出并保存”，再复制整个数据目录。不要只复制运行中的 `usage.db`，未检查点数据可能位于 `usage.db-wal`。

**升级：** 先退出旧版，再启动新版。两种发布方式使用同一用户数据库，现有历史无需手动迁移；若旧版仍在运行，重复启动只会唤起旧版界面。

**移动程序目录：** 先退出，移动后运行新位置的 EXE，以更新登录自启动路径。

## 开发与构建

### 环境要求

- Windows x64 用于实际运行、界面和系统集成验证。
- .NET 10 SDK 用于构建；依赖源由 [NuGet.Config](NuGet.Config) 配置。
- Linux 上的 .NET 10 SDK 可交叉编译和发布 Windows 产物，不能直接运行本 WinForms 应用。

### Windows 开发

在仓库根目录执行：

```powershell
dotnet restore AppTimeTracker/AppTimeTracker.csproj --configfile NuGet.Config
dotnet build AppTimeTracker/AppTimeTracker.csproj -c Release --no-restore
dotnet run --project AppTimeTracker/AppTimeTracker.csproj -c Release --no-build
```

### 生成两种发布版本

```powershell
dotnet publish AppTimeTracker/AppTimeTracker.csproj -c Release -r win-x64 -p:PublishProfile=Portable
dotnet publish AppTimeTracker/AppTimeTracker.csproj -c Release -r win-x64 -p:PublishProfile=Small
```

| 配置 | 输出目录 |
|---|---|
| [Portable.pubxml](AppTimeTracker/Properties/PublishProfiles/Portable.pubxml) | `artifacts/AppTimeTracker-1.1.4-portable` |
| [Small.pubxml](AppTimeTracker/Properties/PublishProfiles/Small.pubxml) | `artifacts/AppTimeTracker-1.1.4-small` |

输出目录随项目版本号更新。发布配置生成文件夹；ZIP 需要对整个文件夹另行压缩。SDK 缓存、构建和发布产物不纳入源码版本控制。

### Linux 交叉构建

在上述 `restore`、`build`、`publish` 命令中添加 `-p:EnableWindowsTargeting=true`。例如：

```sh
dotnet restore AppTimeTracker/AppTimeTracker.csproj --configfile NuGet.Config -p:EnableWindowsTargeting=true
dotnet build AppTimeTracker/AppTimeTracker.csproj -c Release --no-restore -p:EnableWindowsTargeting=true
dotnet publish AppTimeTracker/AppTimeTracker.csproj -c Release -r win-x64 -p:PublishProfile=Portable -p:EnableWindowsTargeting=true
dotnet publish AppTimeTracker/AppTimeTracker.csproj -c Release -r win-x64 -p:PublishProfile=Small -p:EnableWindowsTargeting=true
```

### 核心回归测试

```sh
dotnet restore tests/AppTimeTracker.Regression/AppTimeTracker.Regression.csproj --configfile NuGet.Config
dotnet run --project tests/AppTimeTracker.Regression/AppTimeTracker.Regression.csproj -c Release --no-restore
```

当前包含 20 项测试，覆盖备注持久化、旧数据库兼容、会话顺序与时间回拨、日期筛选、共享查询与 CSV 一致性、转义及跨页导出。测试可在 Linux 和 Windows 运行，详细范围见 [测试说明](tests/AppTimeTracker.Regression/README.md)。

1.1.4 已通过 Release 构建、两种 Windows x64 发布、20 项核心回归测试及 52 组与 1.1.3 的 CSV 逐字节对比。52 组旧版对比为发布阶段执行的额外验证，不属于上述 20 项测试命令。

## 项目结构

```text
AppTimeTracker/
  Program.cs                 启动、单实例与当前用户数据目录
  MainForm.cs                主页、采样调度和后台保存
  Dialogs.cs                 共享 UI、软件编辑、备注编辑及历史窗口
  Tracker.cs                 计时引擎与会话创建顺序
  Models.cs                  软件、会话及有效区间模型
  UsageHistory.cs            历史与导出的共享查询和汇总
  Store.cs                   SQLite 存储、事务和异常恢复
  Csv.cs                     CSV 格式、转义及输出
  WindowsIntegration.cs      窗口扫描、会话状态、自启动与标题栏材料
  Properties/PublishProfiles/ 两种发布配置
tests/AppTimeTracker.Regression/  跨平台核心回归测试
```

界面视觉约定见 [DESIGN.md](DESIGN.md)，交互与数据展示规则见 [UX-CONTRACT.md](UX-CONTRACT.md)。

## 界面与已知限制

界面使用固定浅色的珍珠白磨砂表面、浅冰蓝选中态和圆角控件，保留 Windows 原生键盘操作、文件/日期选择器及高对比回退。Windows 11 支持时请求标题栏 Acrylic；正文不提供实时透视桌面，不支持相应标题栏材料时使用回退外观。操作系统仍需满足 .NET 10 的运行要求。

- 当前版本在 Linux 环境完成编译、核心测试与产物检查；Windows 实际启动、窗口/托盘交互、中文输入法及 DPI 尚未实测。
- 发布包未做代码签名。
- 当前传递依赖 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 包含 SQLite 3.49.1，会产生 NU1903 高危漏洞警告，对应 [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)。SQLite 3.50.2 已修复该引擎漏洞，项目尚未升级底层打包依赖；不能通过安装电脑上的其他 SQLite 版本修复随包引擎。
