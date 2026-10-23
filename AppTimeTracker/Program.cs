using System.Globalization;

namespace AppTimeTracker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("zh-CN");
        ApplicationConfiguration.Initialize();
        var user=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        try
        {
            using var instance=new TrackerInstance(user);
            if(!instance.TryAcquire()) { instance.Wake.Set();return; }
            var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AppTimeTracker");
            using var store=new Store(Path.Combine(data,"usage.db"));
            store.Recover();
            using var form=new MainForm(store,args.Contains("--tray"),instance.Wake);
            Application.Run(form);
        }
        catch(Exception ex)
        {
            MessageBox.Show("无法启动计时器。现有数据不会被覆盖。\n\n"+ex.Message,"AppTimeTracker — 启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}

// 同一用户的数据库跨 Windows 登录会话共享，因此互斥与唤醒事件也必须跨会话。
// Global 命名的 Mutex/Event 不需要创建全局文件映射所要求的额外权限。
public sealed class TrackerInstance : IDisposable
{
    private readonly Mutex mutex;
    private bool owned;
    public EventWaitHandle Wake { get; }
    public TrackerInstance(string user)
    {
        mutex=new Mutex(false,@"Global\AppTimeTracker-"+user);
        try { Wake=new EventWaitHandle(false,EventResetMode.AutoReset,@"Global\AppTimeTracker-Wake-"+user); }
        catch { mutex.Dispose();throw; }
    }
    public bool TryAcquire()
    {
        if(owned)return true;
        try { owned=mutex.WaitOne(0); } catch(AbandonedMutexException) { owned=true; }
        return owned;
    }
    public void Dispose()
    {
        if(owned){mutex.ReleaseMutex();owned=false;}
        Wake.Dispose();mutex.Dispose();
    }
}
