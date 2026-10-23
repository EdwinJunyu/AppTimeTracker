# 核心回归测试

从仓库根目录执行：

```sh
dotnet restore tests/AppTimeTracker.Regression/AppTimeTracker.Regression.csproj --configfile NuGet.Config
dotnet run --project tests/AppTimeTracker.Regression/AppTimeTracker.Regression.csproj -c Release --no-restore
```

测试直接编译生产代码的模型、计时引擎、SQLite 存储与 CSV 导出，在 Linux 或 Windows 的 .NET 10 SDK 上运行。每项结果和执行总数写入控制台，任一失败返回非零退出码；每次使用独立临时目录，完成后清理。

覆盖旧数据兼容、备注快照、数据库重启后持久化及清空、异常恢复、同一软件多次计时隔离、CSV 实际内容与转义、公式防护、最新会话版本、软件与日期筛选、单次/多次汇总备注规则及原有时长累计。

同时验证系统时间回拨、相同时间戳、逆序落盘、编辑较早记录及重启后，主页仍按会话创建顺序选择正确的最近一次备注；旧数据库按稳定入库顺序兼容读取。

本测试不包含 Windows Forms 的窗口交互、主页选择、输入法与 DPI 检查；这些仍需在 Windows 验证。
