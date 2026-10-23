using System.Text.Json.Serialization;

namespace AppTimeTracker;

public sealed class TrackedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string LaunchPath { get; set; } = "";
    public List<string> MonitorPaths { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public bool Removed { get; set; }
    public override string ToString() => Name;
    public static string Normalize(string path)
    {
        var expanded=Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        // 进程查询通常返回普通 Win32 路径，配置却可能使用扩展路径前缀。
        // 先统一本地盘符和 UNC 的表示，再解析 . / ..；.NET 10 仍支持超过 MAX_PATH 的路径，不能截断。
        if(expanded.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) expanded=@"\\"+expanded[8..];
        else if(expanded.StartsWith(@"\\?\",StringComparison.Ordinal)&&expanded.Length>=7&&char.IsAsciiLetter(expanded[4])&&expanded[5]==':'&&expanded[6]=='\\') expanded=expanded[4..];
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }
    public static void ValidateExe(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("请选择存在的 EXE 文件，并填写完整路径。");
    }
}

public sealed class TimeSegment
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string Day { get; set; } = "";
    public double Seconds { get; set; }
}

public sealed class UsageSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long CreationOrder { get; set; }
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string LaunchPath { get; set; } = "";
    public List<string> MonitorPaths { get; set; } = [];
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public DateTimeOffset LastObserved { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public double? PausedSeconds { get; set; } = 0;
    public string Reason { get; set; } = "";
    public bool Recovered { get; set; }
    [JsonIgnore] public List<TimeSegment> Segments { get; set; } = [];
    [JsonIgnore] public double Seconds => Segments.Sum(x => x.Seconds);

    // 后台写入必须持有独立快照；采样线程仍可继续更新原会话，不能共享可变区间。
    public UsageSession Snapshot() => new()
    {
        Id=Id,CreationOrder=CreationOrder,AppId=AppId,Name=Name,LaunchPath=LaunchPath,MonitorPaths=[..MonitorPaths],
        Start=Start,End=End,LastObserved=LastObserved,TimeZoneId=TimeZoneId,
        PausedSeconds=PausedSeconds,Reason=Reason,Recovered=Recovered,Note=Note,
        Segments=Segments.Select(p=>new TimeSegment {Start=p.Start,End=p.End,Day=p.Day,Seconds=p.Seconds}).ToList()
    };

    // 墙钟可回拨；主页显示与编辑共用创建顺序索引，不按开始时间猜测最近一次。
    public static Dictionary<string,UsageSession> RecentByApp(IEnumerable<UsageSession> sessions)
    {
        var recent=new Dictionary<string,UsageSession>();
        foreach(var session in sessions)
            if(!recent.TryGetValue(session.AppId,out var previous)||session.CreationOrder>=previous.CreationOrder)
                recent[session.AppId]=session;
        return recent;
    }
}
