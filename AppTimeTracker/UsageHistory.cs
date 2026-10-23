using System.Globalization;

namespace AppTimeTracker;

// 历史预览与 CSV 共用数据规则；各自只决定显示格式和行顺序。
public sealed class UsageHistory
{
    private readonly List<UsageSession> sessions;
    public IReadOnlyDictionary<string,double> Totals { get; }

    public UsageHistory(IEnumerable<UsageSession> sessions,string? appId=null)
    {
        this.sessions=Latest(sessions).Where(s=>appId==null||s.AppId==appId).ToList();
        // 在日期筛选前计算全部历史累计，明细与每日都引用相同的总时长。
        Totals=this.sessions.GroupBy(s=>s.AppId).ToDictionary(g=>g.Key,g=>g.Sum(s=>s.Seconds));
    }
    private static List<UsageSession> Latest(IEnumerable<UsageSession> sessions)=>sessions
        .GroupBy(s=>s.Id).Select(g=>g.OrderBy(s=>s.LastObserved).Last()).ToList();

    // 一行涉及多次计时时留空，不拼接或任选一条备注。
    public static string SingleSessionNote(IEnumerable<UsageSession> sessions)
    {
        var rows=Latest(sessions);return rows.Count==1?rows[0].Note:"";
    }
    public List<(string AppId,string Name,DateTimeOffset FirstRecorded,DateTimeOffset LastRecorded,int SessionCount,double Seconds,string Note)> Lifetime()=>sessions
        .GroupBy(s=>s.AppId).Select(g=>(AppId:g.Key,Name:g.OrderBy(s=>s.LastObserved).Last().Name,
            FirstRecorded:g.Min(s=>s.Start),LastRecorded:g.Max(s=>s.End??s.LastObserved),
            SessionCount:g.Count(),Seconds:Totals[g.Key],Note:SingleSessionNote(g)))
        .OrderByDescending(s=>s.Seconds).ThenBy(s=>s.AppId,StringComparer.Ordinal).ToList();

    public IEnumerable<(string Day,string AppId,string Name,double Seconds,int SessionCount,double TotalSeconds,string Note)> Daily(DateOnly from,DateOnly to)=>sessions
        .SelectMany(s=>s.Segments.Select(p=>new {Session=s,Segment=p}))
        .Where(x=>Includes(DateOnly.ParseExact(x.Segment.Day,"yyyy-MM-dd",CultureInfo.InvariantCulture),from,to))
        .GroupBy(x=>new {x.Segment.Day,x.Session.AppId})
        .Select(g=>(g.Key.Day,g.Key.AppId,g.OrderBy(x=>x.Session.LastObserved).Last().Session.Name,
            g.Sum(x=>x.Segment.Seconds),g.Select(x=>x.Session.Id).Distinct().Count(),Totals[g.Key.AppId],SingleSessionNote(g.Select(x=>x.Session))));

    // 明细返回重叠的完整会话；每日汇总只取所选日期片段。
    public IEnumerable<UsageSession> Details(DateOnly from,DateOnly to)=>sessions.Where(s=>
    {
        // 有效片段归属日期已经在计时时确定，墙钟回拨也不能漏掉这些记录。
        if(s.Segments.Any(p=>Includes(DateOnly.ParseExact(p.Day,"yyyy-MM-dd",CultureInfo.InvariantCulture),from,to)))return true;
        var end=s.End??s.LastObserved;
        var firstDay=DateOnly.FromDateTime(s.Start.Date);var lastDay=DateOnly.FromDateTime(end.Date);
        // 正常会话保留零秒会话和暂停日；倒序起止只保留已知端点，不虚构连续日期。
        return end>=s.Start?firstDay<=to&&lastDay>=from:Includes(firstDay,from,to)||Includes(lastDay,from,to);
    });
    private static bool Includes(DateOnly day,DateOnly from,DateOnly to)=>day>=from&&day<=to;
}
