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
    public static string Cell(string value)
    {
        // 软件名称和自由填写的备注都可能包含公式；前导空白不能绕过保护。
        var first=value.TrimStart();
        if(value.Length>0 && ("\t\r\n".Contains(value[0]) || first.Length>0 && "=+-@".Contains(first[0]))) value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    public static string Reason(string reason) => reason switch {
        "WindowClosed"=>"窗口关闭", "Disabled"=>"停用监控", "Removed"=>"移出列表", "TrackerExited"=>"计时器退出",
        "Interrupted"=>"异常恢复", "Edited"=>"配置更改", "SystemShutdown"=>"系统注销或关机", _=>reason };
    public static void Export(string path, IEnumerable<UsageSession> sessions, bool daily, DateOnly from, DateOnly to, string? appId)
    {
        var history=new UsageHistory(sessions,appId);
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        void Row(params string[] cells) => writer.WriteLine(string.Join(",",cells.Select(Cell)));
        if(daily)
        {
            Row("日期","软件ID","软件名称","计时类型","有效时长","会话数","总时长","备注");
            foreach(var s in history.Daily(from,to).OrderBy(s=>s.Day))
                Row(s.Day,s.AppId,s.Name,"窗口打开时长",Duration(s.Seconds),s.SessionCount.ToString(CultureInfo.InvariantCulture),Duration(s.TotalSeconds),s.Note);
        }
        else
        {
            Row("会话ID","软件ID","软件名称","启动路径","监控路径列表","计时类型","开始时间","结束时间","时区","有效时长","已确认暂停时长","状态","结束原因","异常恢复","总时长","备注");
            foreach(var s in history.Details(from,to).OrderBy(s=>s.Start))
                Row(s.Id,s.AppId,s.Name,s.LaunchPath,string.Join(" | ",s.MonitorPaths),"窗口打开时长",s.Start.ToString("o"),s.End?.ToString("o")??"",s.TimeZoneId,Duration(s.Seconds),s.PausedSeconds.HasValue?Duration(s.PausedSeconds.Value):"",s.End.HasValue?"已结束":"进行中",Reason(s.Reason),s.Recovered?"是":"否",Duration(history.Totals[s.AppId]),s.Note);
        }
    }
    public static void ExportLifetime(string path, IEnumerable<UsageSession> sessions, string? appId)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));
        void Row(params string[] cells) => writer.WriteLine(string.Join(",",cells.Select(Cell)));
        Row("软件ID","软件名称","计时类型","首次记录时间","最近记录时间","会话数","总时长","备注");
        // 不接受日期参数，确保累计导出包含已移出软件和仍在进行的会话。
        foreach(var s in new UsageHistory(sessions,appId).Lifetime())
            Row(s.AppId,s.Name,"窗口打开时长",s.FirstRecorded.ToString("o"),s.LastRecorded.ToString("o"),s.SessionCount.ToString(CultureInfo.InvariantCulture),Duration(s.Seconds),s.Note);
    }
}
