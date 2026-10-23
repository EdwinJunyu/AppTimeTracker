using System.Globalization;
using System.Text;

namespace AppTimeTracker;

public static class Csv
{
    // 仅显示完整秒，小时不按天取模，因此累计超过 24 小时仍可直接读数。
    public static string Duration(double seconds)
    {
        var wholeSeconds = (long)seconds;
        return string.Create(CultureInfo.InvariantCulture, $"{wholeSeconds/3600:D2}:{wholeSeconds/60%60:D2}:{wholeSeconds%60:D2}");
    }
    public static string Number(double seconds) => seconds.ToString("F2",CultureInfo.InvariantCulture);
    public static string Cell(string value)
    {
        // 防止用户填写的软件名称被 Excel 当成公式执行。
        if(value.Length>0 && "=+-@\t\r".Contains(value[0])) value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    public static string Reason(string reason) => reason switch {
        "WindowClosed"=>"窗口关闭", "Disabled"=>"停用监控", "Removed"=>"移出列表", "TrackerExited"=>"计时器退出",
        "Interrupted"=>"异常恢复", "Edited"=>"配置更改", "SystemShutdown"=>"系统注销或关机", _=>reason };
    // 数据库与正在计时的内存快照可能包含同一会话；只保留最新观察版本，时间相同时以后者为准。
    public static List<UsageSession> Latest(IEnumerable<UsageSession> sessions) => sessions
        .GroupBy(s => s.Id).Select(g => g.OrderBy(s => s.LastObserved).Last()).ToList();

    public static List<(string AppId, string Name, DateTimeOffset FirstRecorded, DateTimeOffset LastRecorded, int SessionCount, double Seconds)> Lifetime(IEnumerable<UsageSession> sessions, string? appId = null) => Latest(sessions)
        .Where(s => appId == null || s.AppId == appId).GroupBy(s => s.AppId)
        .Select(g => (AppId: g.Key, Name: g.OrderBy(s => s.LastObserved).Last().Name,
            FirstRecorded: g.Min(s => s.Start), LastRecorded: g.Max(s => s.End ?? s.LastObserved),
            SessionCount: g.Count(), Seconds: g.Sum(s => s.Seconds)))
        .OrderByDescending(s => s.Seconds).ThenBy(s => s.AppId, StringComparer.Ordinal).ToList();

    public static void Export(string path, IEnumerable<UsageSession> sessions, bool daily, DateOnly from, DateOnly to, string? appId)
    {
        var rows=Latest(sessions).Where(s=>appId==null||s.AppId==appId).ToList();
        // 总时长来自该软件全部历史，不能在日期过滤后求和；明细和每日中的重复值均表示当前累计。
        var totals=rows.GroupBy(s=>s.AppId).ToDictionary(g=>g.Key,g=>g.Sum(s=>s.Seconds));
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        void Row(params string[] cells) => writer.WriteLine(string.Join(",",cells.Select(Cell)));
        if(daily)
        {
            Row("日期","软件ID","软件名称","计时类型","有效时长","会话数","总时长");
            var groups=rows.SelectMany(s=>s.Segments.Select(p=>new {Session=s,Segment=p}))
                .Where(x=>DateOnly.ParseExact(x.Segment.Day,"yyyy-MM-dd",CultureInfo.InvariantCulture)>=from && DateOnly.ParseExact(x.Segment.Day,"yyyy-MM-dd",CultureInfo.InvariantCulture)<=to)
                .GroupBy(x=>new {x.Segment.Day,x.Session.AppId}).OrderBy(x=>x.Key.Day);
            foreach(var g in groups) { var seconds=g.Sum(x=>x.Segment.Seconds); Row(g.Key.Day,g.Key.AppId,g.OrderBy(x=>x.Session.LastObserved).Last().Session.Name,"窗口打开时长",Duration(seconds),g.Select(x=>x.Session.Id).Distinct().Count().ToString(CultureInfo.InvariantCulture),Duration(totals[g.Key.AppId])); }
        }
        else
        {
            Row("会话ID","软件ID","软件名称","启动路径","监控路径列表","计时类型","开始时间","结束时间","时区","有效时长","已确认暂停时长","状态","结束原因","异常恢复","总时长");
            foreach(var s in Filter(rows,from,to).OrderBy(s=>s.Start))
                Row(s.Id,s.AppId,s.Name,s.LaunchPath,string.Join(" | ",s.MonitorPaths),"窗口打开时长",s.Start.ToString("o"),s.End?.ToString("o")??"",s.TimeZoneId,Duration(s.Seconds),s.PausedSeconds.HasValue?Duration(s.PausedSeconds.Value):"",s.End.HasValue?"已结束":"进行中",Reason(s.Reason),s.Recovered?"是":"否",Duration(totals[s.AppId]));
        }
    }
    public static void ExportLifetime(string path, IEnumerable<UsageSession> sessions, string? appId)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        void Row(params string[] cells) => writer.WriteLine(string.Join(",",cells.Select(Cell)));
        Row("软件ID","软件名称","计时类型","首次记录时间","最近记录时间","会话数","总时长");
        // 不接受日期参数，确保累计导出包含已移出软件和仍在进行的会话。
        foreach(var s in Lifetime(sessions,appId))
            Row(s.AppId,s.Name,"窗口打开时长",s.FirstRecorded.ToString("o"),s.LastRecorded.ToString("o"),s.SessionCount.ToString(CultureInfo.InvariantCulture),Duration(s.Seconds));
    }
    // 明细返回与日期范围重叠的完整会话；每日汇总仅返回选定日期的片段。
    public static IEnumerable<UsageSession> Filter(IEnumerable<UsageSession> sessions, DateOnly from, DateOnly to) => sessions.Where(s=>
    {
        bool Includes(DateOnly day) => day >= from && day <= to;
        // 有效片段的归属日期已经在计时时确定；即使墙钟回拨，也不能漏掉这些记录。
        if (s.Segments.Any(p => Includes(DateOnly.ParseExact(p.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture)))) return true;
        var end = s.End ?? s.LastObserved;
        var firstDay = DateOnly.FromDateTime(s.Start.Date);
        var lastDay = DateOnly.FromDateTime(end.Date);
        // 正常会话保留原有重叠语义，因此没有有效片段的暂停日、零秒会话仍可查看。
        if (end >= s.Start) return firstDay <= to && lastDay >= from;
        // 倒序起止来自墙钟调整，不交换它们来虚构连续日期；仅保留已知端点与上面的有效日期。
        return Includes(firstDay) || Includes(lastDay);
    });
}
