using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AppTimeTracker;

public sealed class Store : IDisposable
{
    private readonly SqliteConnection connection;
    // SQLite 连接不并发使用；后台保存与偶发的配置操作共用这一把锁。
    private readonly object gate=new();
    public Store(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var existed = File.Exists(path);
        // 只允许首次创建；已有文件损坏时不回退到新数据库。
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = existed ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 1, Pooling = false }.ToString());
        try {
        connection.Open();
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        if ((string?)check.ExecuteScalar() != "ok") throw new IOException("数据库完整性检查失败，请保留文件并从备份恢复。");
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS Apps(Id TEXT PRIMARY KEY, Payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Sessions(Id TEXT PRIMARY KEY, Payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS Segments(SessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL, Payload TEXT NOT NULL, PRIMARY KEY(SessionId,Ordinal)); CREATE TABLE IF NOT EXISTS Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);");
        } catch { connection.Dispose(); throw; }
    }
    private void Exec(string sql) { using var c = connection.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    public List<TrackedApp> Apps()
    {
        lock(gate)
        {
            using var c = connection.CreateCommand(); c.CommandText = "SELECT Payload FROM Apps ORDER BY rowid";
            using var r = c.ExecuteReader(); var result = new List<TrackedApp>();
            while (r.Read()) result.Add(JsonSerializer.Deserialize<TrackedApp>(r.GetString(0))!);
            return result;
        }
    }
    public void SaveApp(TrackedApp app)
    {
        lock(gate)
        {
            using var c = connection.CreateCommand(); c.CommandText = "INSERT INTO Apps VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload";
            c.Parameters.AddWithValue("$id", app.Id); c.Parameters.AddWithValue("$json", JsonSerializer.Serialize(app)); c.ExecuteNonQuery();
        }
    }
    public string? Setting(string key)
    {
        lock(gate)
        {
            using var c = connection.CreateCommand(); c.CommandText = "SELECT Value FROM Settings WHERE Key=$key"; c.Parameters.AddWithValue("$key",key);
            return (string?)c.ExecuteScalar();
        }
    }
    public void SetSetting(string key, string value)
    {
        lock(gate)
        {
            using var c = connection.CreateCommand(); c.CommandText = "INSERT INTO Settings VALUES($k,$v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
            c.Parameters.AddWithValue("$k",key); c.Parameters.AddWithValue("$v",value); c.ExecuteNonQuery();
        }
    }
    public List<UsageSession> Sessions()
    {
        lock(gate)
        {
            var result = new Dictionary<string,UsageSession>();
            using (var c = connection.CreateCommand())
            {
                c.CommandText = "SELECT rowid,Id,Payload FROM Sessions ORDER BY rowid"; using var r = c.ExecuteReader();
                while(r.Read())
                {
                    var session=JsonSerializer.Deserialize<UsageSession>(r.GetString(2))!;
                    // 旧数据没有创建序号，沿用稳定的入库顺序；更新备注不会改变 rowid。
                    if(session.CreationOrder==0)session.CreationOrder=r.GetInt64(0);
                    result.Add(r.GetString(1),session);
                }
            }
            using (var c = connection.CreateCommand())
            {
                c.CommandText = "SELECT SessionId,Payload FROM Segments ORDER BY SessionId,Ordinal"; using var r = c.ExecuteReader();
                while(r.Read()) result[r.GetString(0)].Segments.Add(JsonSerializer.Deserialize<TimeSegment>(r.GetString(1))!);
            }
            return result.Values.ToList();
        }
    }
    public void SaveSessions(IEnumerable<UsageSession> sessions)
    {
        lock(gate)
        {
            // 完整快照仍在同一事务提交；只改动发生变化的区间，避免反复删除、重写整段历史。
            // 比较数据库中的内容而非内存缓存，因此事务失败、重启、缩短快照后重试仍然正确。
            using var tx = connection.BeginTransaction();
            using var session=connection.CreateCommand();session.Transaction=tx;
            session.CommandText="INSERT INTO Sessions VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Payload=excluded.Payload WHERE Sessions.Payload<>excluded.Payload";
            var sessionId=session.Parameters.Add("$id",SqliteType.Text);var sessionJson=session.Parameters.Add("$json",SqliteType.Text);
            using var segment=connection.CreateCommand();segment.Transaction=tx;
            segment.CommandText="INSERT INTO Segments VALUES($id,$i,$json) ON CONFLICT(SessionId,Ordinal) DO UPDATE SET Payload=excluded.Payload WHERE Segments.Payload<>excluded.Payload";
            var segmentId=segment.Parameters.Add("$id",SqliteType.Text);var ordinal=segment.Parameters.Add("$i",SqliteType.Integer);var segmentJson=segment.Parameters.Add("$json",SqliteType.Text);
            using var trim=connection.CreateCommand();trim.Transaction=tx;
            trim.CommandText="DELETE FROM Segments WHERE SessionId=$id AND Ordinal >= $count";
            var trimId=trim.Parameters.Add("$id",SqliteType.Text);var count=trim.Parameters.Add("$count",SqliteType.Integer);
            foreach(var s in sessions)
            {
                sessionId.Value=s.Id;sessionJson.Value=JsonSerializer.Serialize(s);session.ExecuteNonQuery();
                segmentId.Value=s.Id;
                for(var i=0;i<s.Segments.Count;i++)
                {ordinal.Value=i;segmentJson.Value=JsonSerializer.Serialize(s.Segments[i]);segment.ExecuteNonQuery();}
                trimId.Value=s.Id;count.Value=s.Segments.Count;trim.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }
    public void Recover()
    {
        lock(gate)
        {
            var unfinished=Sessions().Where(s=>s.End==null).ToList();
            foreach(var s in unfinished) { s.End=s.LastObserved; s.Reason="Interrupted"; s.Recovered=true; s.PausedSeconds=null; }
            if(unfinished.Count>0) SaveSessions(unfinished);
        }
    }
    public void Dispose() { lock(gate) connection.Dispose(); }
}
