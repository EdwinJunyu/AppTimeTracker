using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using AppTimeTracker;

var tests = new (string Name, Action Run)[]
{
    ("旧记录读取为空备注", LegacyJson),
    ("备注及区间快照独立", Snapshot),
    ("备注持久化、修改和清空", StoreNotes),
    ("异常恢复保留备注", RecoverNotes),
    ("连续计时不串备注", TrackerNotes),
    ("明细 CSV 中文、多行与转义", DetailCsv),
    ("备注公式保护", FormulaCsv),
    ("当前会话最新备注与软件筛选", LatestCsv),
    ("每日汇总不合并备注且累计正确", DailyCsv),
    ("累计汇总不合并备注且累计正确", LifetimeCsv),
    ("同一会话多区间只显示一次备注", SingleSessionSummary),
    ("跨日记录保持日期筛选语义", DateFilter),
    ("时钟回拨后选择实际最近会话", RecentAfterClockRollback),
    ("相同时间按创建顺序选择且软件独立", RecentWithEqualTimes),
    ("创建顺序持久化并在重启后继续", RecentAfterRestart),
    ("旧数据库按入库顺序兼容且编辑不改顺序", LegacyCreationOrder)
};
var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures} passed, {failures} failed, {tests.Length} executed");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected [{expected}], actual [{actual}]");
}
static UsageSession Session(string id = "s1", string appId = "a1", string note = "写方案", int seconds = 60, int day = 23)
{
    var start = new DateTimeOffset(2026, 10, day, 10, 0, 0, TimeSpan.Zero);
    return new UsageSession { Id = id, AppId = appId, Name = "软件 " + appId, Note = note,
        Start = start, End = start.AddSeconds(seconds), LastObserved = start.AddSeconds(seconds), TimeZoneId = "UTC",
        Segments = [new TimeSegment { Start = start, End = start.AddSeconds(seconds), Day = start.ToString("yyyy-MM-dd"), Seconds = seconds }] };
}
static void Temp(Action<string> action)
{
    var dir = Path.Combine(Path.GetTempPath(), "apptimetracker-regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try { action(dir); }
    finally { Directory.Delete(dir, true); }
}
static DateOnly Day(int day = 23) => new(2026, 10, day);
static void LegacyJson()
{
    var legacy = JsonSerializer.Deserialize<UsageSession>("{\"Id\":\"old\",\"AppId\":\"a1\",\"Name\":\"旧记录\"}")!;
    Equal("", legacy.Note);
    Equal("", legacy.Snapshot().Note);
}
static void Snapshot()
{
    var current = Session(); current.CreationOrder = 17; var snapshot = current.Snapshot();
    current.Note = "修改代码"; current.Segments[0].Seconds = 120;
    Equal("写方案", snapshot.Note); Equal(60d, snapshot.Seconds);
    Equal(17L, snapshot.CreationOrder);
    snapshot.Note = "另一份快照";
    Equal("修改代码", current.Note);
}
static void StoreNotes() => Temp(dir =>
{
    var path = Path.Combine(dir, "usage.db");
    var first = Session(note: "初稿\n引号\"、逗号,以及 ' SQL 文本");
    var second = Session("s2", note: "修改代码");
    using (var store = new Store(path)) store.SaveSessions([first, second]);
    using (var store = new Store(path))
    {
        var saved = store.Sessions().ToDictionary(s => s.Id);
        Equal(first.Note, saved[first.Id].Note); Equal(second.Note, saved[second.Id].Note);
        saved[first.Id].Note = "已完成评审"; store.SaveSessions([saved[first.Id]]);
    }
    using (var store = new Store(path))
    {
        var saved = store.Sessions().ToDictionary(s => s.Id);
        Equal("已完成评审", saved[first.Id].Note); Equal("修改代码", saved[second.Id].Note);
        Equal(60d, saved[first.Id].Seconds);
        saved[first.Id].Note = ""; store.SaveSessions([saved[first.Id]]);
    }
    using (var store = new Store(path))
    {
        var saved = store.Sessions().ToDictionary(s => s.Id);
        Equal("", saved[first.Id].Note); Equal("修改代码", saved[second.Id].Note);
    }
});
static void RecoverNotes() => Temp(dir =>
{
    var path = Path.Combine(dir, "usage.db"); var session = Session(); session.End = null;
    using (var store = new Store(path)) store.SaveSessions([session]);
    using (var store = new Store(path))
    {
        store.Recover(); var recovered = store.Sessions().Single();
        Equal("写方案", recovered.Note); Equal(session.LastObserved, recovered.End!.Value);
        Equal(true, recovered.Recovered); Equal("Interrupted", recovered.Reason); Equal(60d, recovered.Seconds);
    }
});
static void TrackerNotes()
{
    var app = new TrackedApp { Id = "a", Name = "代码编辑器", MonitorPaths = ["editor.exe"] };
    var tracker = new Tracker(); var now = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    var open = new HashSet<string> { "editor.exe" }; var closed = new HashSet<string>();
    tracker.Observe([app], open, now, 0, false);
    var first = tracker.Active[app.Id]; first.Note = "修复问题";
    tracker.Observe([app], open, now.AddSeconds(1), 1, false);
    Equal("修复问题", tracker.Active[app.Id].Note);
    tracker.Observe([app], closed, now.AddSeconds(2), 2, false);
    Equal("修复问题", tracker.Pending[first.Id].Note); Equal(true, first.End.HasValue);
    tracker.Observe([app], open, now.AddSeconds(3), 3, false);
    var next = tracker.Active[app.Id]; Equal("", next.Note); Equal(false, first.Id == next.Id);
    next.Note = "编写文档"; tracker.CloseAll(now.AddSeconds(4), "TrackerExited");
    Equal("修复问题", tracker.Pending[first.Id].Note); Equal("编写文档", tracker.Pending[next.Id].Note);
}
static void DetailCsv() => Temp(dir =>
{
    var note = "评审方案，写文档\n第二行,\"引用\""; var session = Session(note: note); var path = Path.Combine(dir, "detail.csv");
    Csv.Export(path, [session], false, Day(), Day(), null);
    var bytes = File.ReadAllBytes(path); Equal(true, bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    var rows = ReadCsv(path); Equal(2, rows.Count); Equal("备注", rows[0][^1]); Equal(16, rows[0].Length);
    Equal(note, rows[1][^1]); Equal("00:01:00", rows[1][Index(rows, "总时长")]);
});
static void FormulaCsv() => Temp(dir =>
{
    var notes = new[] { "=1+1", "+SUM(A1:A2)", "-1+2", "@SUM(A1:A2)", "  =1+1", "\tdata", "\rdata", "\ndata" };
    var path = Path.Combine(dir, "formula.csv"); var sessions = notes.Select((note, i) => Session("s" + i, "a" + i, note)).ToList();
    Csv.Export(path, sessions, false, Day(), Day(), null);
    var rows = ReadCsv(path); Equal(notes.Length + 1, rows.Count);
    for (var i = 0; i < notes.Length; i++) Equal("'" + notes[i], rows[i + 1][^1]);
});
static void LatestCsv() => Temp(dir =>
{
    var old = Session(note: "旧备注"); var current = old.Snapshot(); current.Note = "进行中的最新备注"; current.End = null;
    var other = Session("s2", "a2", "其他软件"); var path = Path.Combine(dir, "latest.csv");
    Csv.Export(path, [old, current, other], false, Day(), Day(), null);
    var all = ReadCsv(path); Equal(3, all.Count); Equal("进行中的最新备注", all.Single(row => row[0] == "s1")[^1]);
    Equal("进行中", all.Single(row => row[0] == "s1")[Index(all, "状态")]);
    Csv.Export(path, [old, current, other], false, Day(), Day(), "a2");
    var filtered = ReadCsv(path); Equal(2, filtered.Count); Equal("其他软件", filtered[1][^1]);
});
static void DailyCsv() => Temp(dir =>
{
    var first = Session(note: "任务一", seconds: 60); var second = Session("s2", note: "任务二", seconds: 90);
    var older = Session("old", note: "范围外", seconds: 30, day: 22); var other = Session("s3", "a2", "单次任务", 15);
    var path = Path.Combine(dir, "daily.csv"); Csv.Export(path, [first, second, older, other], true, Day(), Day(), null);
    var rows = ReadCsv(path); Equal(3, rows.Count); Equal("备注", rows[0][^1]);
    var combined = rows.Single(r => r[1] == "a1"); Equal("", combined[^1]); Equal("00:02:30", combined[Index(rows, "有效时长")]);
    Equal("2", combined[Index(rows, "会话数")]); Equal("00:03:00", combined[Index(rows, "总时长")]);
    Equal("单次任务", rows.Single(r => r[1] == "a2")[^1]);
});
static void LifetimeCsv() => Temp(dir =>
{
    var sessions = new[] { Session(note: "任务一", seconds: 60), Session("s2", note: "任务二", seconds: 90), Session("s3", "a2", "单次任务", 15) };
    var path = Path.Combine(dir, "lifetime.csv"); Csv.ExportLifetime(path, sessions, null);
    var rows = ReadCsv(path); Equal(3, rows.Count); Equal("备注", rows[0][^1]);
    var combined = rows.Single(r => r[0] == "a1"); Equal("", combined[^1]); Equal("2", combined[Index(rows, "会话数")]); Equal("00:02:30", combined[Index(rows, "总时长")]);
    Equal("单次任务", rows.Single(r => r[0] == "a2")[^1]);
    Csv.ExportLifetime(path, sessions, "a2"); Equal(2, ReadCsv(path).Count);
});
static void SingleSessionSummary() => Temp(dir =>
{
    var session = Session(note: "同一任务"); session.Segments.Add(new TimeSegment { Start = session.Start.AddSeconds(120), End = session.Start.AddSeconds(150), Day = "2026-10-23", Seconds = 30 });
    var path = Path.Combine(dir, "single.csv"); Csv.Export(path, [session], true, Day(), Day(), null);
    var rows = ReadCsv(path); Equal("同一任务", rows[1][^1]); Equal("1", rows[1][Index(rows, "会话数")]); Equal("00:01:30", rows[1][Index(rows, "有效时长")]);
    var latest = session.Snapshot(); latest.Note = "最新修改";
    Equal("最新修改", Csv.SingleSessionNote([session, latest]));
});
static void DateFilter() => Temp(dir =>
{
    var session = Session(note: "跨日任务", day: 22); session.End = session.Start.AddDays(1); session.LastObserved = session.End.Value;
    session.Segments.Add(new TimeSegment { Start = session.Start.AddDays(1), End = session.Start.AddDays(1).AddSeconds(5), Day = "2026-10-23", Seconds = 5 });
    var path = Path.Combine(dir, "range.csv"); Csv.Export(path, [session], false, Day(), Day(), null);
    var rows = ReadCsv(path); Equal(2, rows.Count); Equal("跨日任务", rows[1][^1]); Equal("00:01:05", rows[1][Index(rows, "有效时长")]);
    Csv.Export(path, [session], true, Day(), Day(), null); rows = ReadCsv(path); Equal("00:00:05", rows[1][Index(rows, "有效时长")]); Equal("跨日任务", rows[1][^1]);
});
static (UsageSession Older, UsageSession Newer) RollbackSessions()
{
    var app = new TrackedApp { Id = "clock-app", MonitorPaths = ["editor.exe"] };
    var tracker = new Tracker(); var wall = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    var open = new HashSet<string> { "editor.exe" }; var closed = new HashSet<string>();
    tracker.Observe([app], open, wall, 0, false);
    var older = tracker.Active[app.Id]; older.Note = "之前的任务";
    tracker.Observe([app], closed, wall.AddSeconds(1), 1, false);
    tracker.Observe([app], open, wall.AddHours(-1), 2, false);
    var newer = tracker.Active[app.Id]; newer.Note = "刚结束的任务";
    tracker.Observe([app], closed, wall.AddHours(-1).AddSeconds(1), 3, false);
    Equal(0, tracker.Active.Count); Equal(true, newer.CreationOrder > older.CreationOrder);
    Equal(true, newer.Start < older.Start);
    return (older, newer);
}
static void RecentAfterClockRollback()
{
    var (older, newer) = RollbackSessions();
    // 同一个索引同时用于主页展示与编辑目标；不依赖输入顺序或墙钟。
    var selected = UsageSession.RecentByApp([newer, older])[newer.AppId];
    Equal(newer.Id, selected.Id); Equal("刚结束的任务", selected.Note);
    selected.Note = "补写刚结束的工作";
    Equal("补写刚结束的工作", newer.Note); Equal("之前的任务", older.Note);
}
static void RecentWithEqualTimes()
{
    var older = Session(); older.CreationOrder = 1;
    var newer = Session("s2"); newer.CreationOrder = 2;
    older.LastObserved = older.LastObserved.AddDays(1);
    var other = Session("s3", "a2", "其他软件"); other.CreationOrder = 3;
    var recent = UsageSession.RecentByApp([other, newer, older]);
    Equal(newer.Id, recent[older.AppId].Id); Equal(other.Id, recent[other.AppId].Id);
    Equal(0, UsageSession.RecentByApp([]).Count);
}
static void RecentAfterRestart() => Temp(dir =>
{
    var (older, newer) = RollbackSessions(); var path = Path.Combine(dir, "usage.db");
    // 逆序落盘也不能改变计时引擎分配的创建顺序。
    using (var store = new Store(path)) store.SaveSessions([newer.Snapshot(), older.Snapshot()]);
    UsageSession next;
    using (var store = new Store(path))
    {
        var rows = store.Sessions(); Equal(newer.Id, UsageSession.RecentByApp(rows)[newer.AppId].Id);
        var previous = rows.Single(s => s.Id == older.Id); previous.Note = "编辑更早的记录";
        store.SaveSessions([previous]);
        Equal(newer.Id, UsageSession.RecentByApp(store.Sessions())[newer.AppId].Id);
        var tracker = new Tracker(rows); var app = new TrackedApp { Id = older.AppId, MonitorPaths = ["editor.exe"] };
        tracker.Observe([app], new HashSet<string> { "editor.exe" }, older.Start.AddHours(-2), 0, false);
        next = tracker.Active[app.Id]; next.Note = "重启后的任务";
        Equal(true, next.CreationOrder > newer.CreationOrder); store.SaveSessions([next.Snapshot()]);
    }
    using (var store = new Store(path))
    {
        var recent = UsageSession.RecentByApp(store.Sessions())[older.AppId];
        Equal(next.Id, recent.Id); Equal("重启后的任务", recent.Note);
    }
});
static void LegacyCreationOrder() => Temp(dir =>
{
    var path = Path.Combine(dir, "usage.db"); var older = Session("legacy-older"); var newer = Session("legacy-newer");
    newer.Start = newer.Start.AddHours(-1); newer.LastObserved = newer.LastObserved.AddHours(-1); newer.End = newer.End?.AddHours(-1);
    using (var store = new Store(path)) { }
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
    {
        connection.Open();
        foreach (var row in new[] { older, newer })
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(row))!.AsObject(); json.Remove("CreationOrder");
            using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO Sessions(Id,Payload) VALUES($id,$payload)";
            command.Parameters.AddWithValue("$id", row.Id); command.Parameters.AddWithValue("$payload", json.ToJsonString()); command.ExecuteNonQuery();
        }
    }
    using (var store = new Store(path))
    {
        var rows = store.Sessions(); var first = rows.Single(s => s.Id == older.Id); var second = rows.Single(s => s.Id == newer.Id);
        Equal(true, first.CreationOrder > 0); Equal(true, second.CreationOrder > first.CreationOrder);
        Equal(newer.Id, UsageSession.RecentByApp(rows)[newer.AppId].Id);
        first.Note = "修改旧备注"; store.SaveSessions([first]);
    }
    using (var store = new Store(path))
    {
        var rows = store.Sessions(); Equal(newer.Id, UsageSession.RecentByApp(rows)[newer.AppId].Id);
        var tracker = new Tracker(rows); var app = new TrackedApp { Id = newer.AppId, MonitorPaths = ["editor.exe"] };
        tracker.Observe([app], new HashSet<string> { "editor.exe" }, newer.Start, 0, false);
        Equal(true, tracker.Active[app.Id].CreationOrder > rows.Max(s => s.CreationOrder));
    }
});
static int Index(List<string[]> rows, string name)
{
    var index = Array.IndexOf(rows[0], name);
    return index >= 0 ? index : throw new InvalidOperationException("Missing column " + name);
}
// Parse quoted CSV rather than splitting lines: notes can contain line breaks, commas and escaped quotes.
static List<string[]> ReadCsv(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8); var rows = new List<string[]>(); var fields = new List<string>(); var cell = new StringBuilder(); var quoted = false;
    for (var i = 0; i < text.Length; i++)
    {
        var ch = text[i];
        if (ch == '"')
        {
            if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
            else quoted = !quoted;
        }
        else if (!quoted && (ch == ',' || ch == '\r' || ch == '\n'))
        {
            fields.Add(cell.ToString()); cell.Clear();
            if (ch != ',') { rows.Add(fields.ToArray()); fields.Clear(); if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
        }
        else cell.Append(ch);
    }
    if (quoted) throw new InvalidOperationException("Unclosed CSV quote");
    if (cell.Length > 0 || fields.Count > 0) { fields.Add(cell.ToString()); rows.Add(fields.ToArray()); }
    if (rows.Any(row => row.Length != rows[0].Length)) throw new InvalidOperationException("Inconsistent CSV column count");
    return rows;
}
