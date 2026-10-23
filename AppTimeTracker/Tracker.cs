namespace AppTimeTracker;

// 引擎不访问窗口或磁盘：测试可以直接提供时钟和窗口快照。
public sealed class Tracker
{
    public Dictionary<string, UsageSession> Active { get; } = [];
    public Dictionary<string, UsageSession> Pending { get; } = [];
    private DateTimeOffset? previousWall;
    private double previousTick;
    private bool wasPaused;
    private bool newSegment;
    private long nextCreationOrder;
    public bool Changed { get; private set; }

    public Tracker(IEnumerable<UsageSession>? history=null)
    {
        if(history!=null)nextCreationOrder=history.Select(s=>s.CreationOrder).DefaultIfEmpty().Max();
    }

    public void Observe(IEnumerable<TrackedApp> apps, ISet<string> paths, DateTimeOffset wall, double tick, bool paused)
    {
        Changed = false;
        var delta = previousWall.HasValue ? Math.Max(0, tick - previousTick) : 0;
        // 系统通知遗漏或调试长暂停时，不将未知间隔误记成使用时间。
        var gap = delta > 3 && !wasPaused;
        foreach (var session in Active.Values)
        {
            if (wasPaused) session.PausedSeconds += delta;
            else if (!gap && previousWall.HasValue) AddTime(session, previousWall.Value, wall, delta);
            else if (gap) session.PausedSeconds = null;
            session.LastObserved = wall;
            Pending[session.Id] = session;
        }
        if (paused != wasPaused || gap) { Changed = true; newSegment = true; }
        foreach (var app in apps)
        {
            var exists = app.Enabled && !app.Removed && app.MonitorPaths.Any(paths.Contains);
            // 锁屏期间不能可靠枚举交互桌面；已打开的会话保留，解锁后再确认。
            if (paused && app.Enabled && !app.Removed) continue;
            if (!exists && Active.Remove(app.Id, out var ended))
            {
                ended.End = wall;
                ended.Reason = app.Removed ? "Removed" : !app.Enabled ? "Disabled" : "WindowClosed";
                Pending[ended.Id] = ended;
                Changed = true;
            }
            else if (exists && !Active.ContainsKey(app.Id))
            {
                var session = new UsageSession { CreationOrder = checked(++nextCreationOrder), AppId = app.Id, Name = app.Name, LaunchPath = app.LaunchPath,
                    MonitorPaths = [.. app.MonitorPaths], Start = wall, LastObserved = wall };
                Active[app.Id] = session;
                Pending[session.Id] = session;
                Changed = true;
            }
        }
        previousWall = wall; previousTick = tick; wasPaused = paused;
    }

    private void AddTime(UsageSession session, DateTimeOffset start, DateTimeOffset end, double seconds)
    {
        if (seconds <= 0) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(session.TimeZoneId);
        var elapsed = (end - start).TotalSeconds;
        // 墙钟跳变只影响显示，不放大/缩小单调时钟的计时结果。
        if (elapsed <= 0 || Math.Abs(elapsed - seconds) > 2) end = start.AddSeconds(seconds);
        var total = (end - start).TotalSeconds;
        var cursor = start;
        while (cursor < end)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, zone);
            var nextDate = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
            var midnight = new DateTimeOffset(nextDate, zone.GetUtcOffset(nextDate));
            var partEnd = midnight < end ? midnight : end;
            var amount = seconds * (partEnd - cursor).TotalSeconds / total;
            var day = local.ToString("yyyy-MM-dd");
            var last = session.Segments.LastOrDefault();
            if (!newSegment && last != null && last.Day == day && last.End == cursor)
            { last.End = partEnd; last.Seconds += amount; }
            else session.Segments.Add(new TimeSegment { Start = cursor, End = partEnd, Day = day, Seconds = amount });
            cursor = partEnd;
        }
        newSegment = false;
    }

    public void CloseAll(DateTimeOffset wall, string reason)
    {
        foreach (var session in Active.Values)
        { session.End = wall; session.Reason = reason; Pending[session.Id] = session; }
        Active.Clear(); Changed = true;
    }
}
