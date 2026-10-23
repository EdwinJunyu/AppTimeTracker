using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Buffers;
using Microsoft.Win32;

namespace AppTimeTracker;

public sealed record WindowSnapshot(HashSet<string> Paths, int UnreadableWindows);

public static class WindowsIntegration
{
    private delegate bool WindowCallback(nint window,nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(nint window,int index);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryFullProcessImageName(nint process,uint flags,[Out] char[] path,ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window,uint attribute,out uint value,uint size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window,uint attribute,ref int value,uint size);
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool WTSQuerySessionInformation(nint server,uint session,int informationClass,out nint buffer,out uint bytesReturned);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint memory);

    // 对应微软 wtsapi32.h 的 WTSINFOEX_LEVEL1_W。固定字符串和 LARGE_INTEGER 的
    // 自然对齐都必须保留，外层联合体因此按 8 字节对齐，不能把 SessionFlags 当成紧挨 Level。
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct WtsInfoExLevel1
    {
        public uint SessionId;
        public int SessionState;
        public int SessionFlags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=33)] public string WinStationName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=21)] public string UserName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=18)] public string DomainName;
        public long LogonTime,ConnectTime,DisconnectTime,LastInputTime,CurrentTime;
        public uint IncomingBytes,OutgoingBytes,IncomingFrames,OutgoingFrames,IncomingCompressedBytes,OutgoingCompressedBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WtsInfoEx
    {
        public uint Level;
        // WTSINFOEX_LEVEL_W 联合体只有 Level1 这一项，与直接包含该结构具有相同布局。
        public WtsInfoExLevel1 Data;
    }

    public static bool? SessionLocked()
    {
        // 查询调用进程所在的真实 Windows 会话；UAC 安全桌面不等于用户锁屏。
        // 无权限、服务不可用或未知标记只表示无法确认，由调用者保留最近的已知状态并稍后重试。
        nint buffer=0;
        try
        {
            const uint currentSession=0xffffffff;
            const int sessionInfoEx=25;
            if(!WTSQuerySessionInformation(0,currentSession,sessionInfoEx,out buffer,out var bytes)||buffer==0||bytes<Marshal.SizeOf<WtsInfoEx>()) return null;
            var info=Marshal.PtrToStructure<WtsInfoEx>(buffer);
            if(info.Level!=1) return null;
            return info.Data.SessionFlags switch { 0=>true,1=>false,_=>null };
        }
        finally { if(buffer!=0) WTSFreeMemory(buffer); }
    }
    public static WindowSnapshot Scan()
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<uint>(); var unreadable=0;
        // 整轮扫描共用池化缓冲区，保留长路径容量；不缓存 PID，避免进程 ID 重用造成误计。
        var buffer=ArrayPool<char>.Shared.Rent(32768);
        try
        {
            // 最小化窗口仍有 WS_VISIBLE；工具窗口不作为用户打开的软件窗口。
            if(!EnumWindows((window,_)=>{
                if(!IsWindowVisible(window)||(GetWindowLong(window,-20)&0x80)!=0) return true;
                // 应用主动 cloak 或从所有者继承 cloak 时，窗口已被应用隐藏，不能继续计时。
                // 仅 SHELL cloak（例如窗口位于另一个虚拟桌面）仍算打开，保持跨桌面计时。
                if(DwmGetWindowAttribute(window,14,out var cloaked,sizeof(uint))==0&&(cloaked&0x5)!=0) return true;
                GetWindowThreadProcessId(window,out var pid);
                if(!seen.Add(pid)) return true;
                var process=OpenProcess(0x1000,false,pid);
                if(process==0) { unreadable++; return true; }
                try
                {
                    uint length=32768;
                    if(QueryFullProcessImageName(process,0,buffer,ref length)) paths.Add(TrackedApp.Normalize(new string(buffer,0,(int)length)));
                    else unreadable++;
                }
                finally { CloseHandle(process); }
                return true;
            },0)) throw new IOException("无法读取桌面窗口，请稍后重试。");
            return new WindowSnapshot(paths,unreadable);
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }
    public static void Launch(TrackedApp app)
    {
        TrackedApp.ValidateExe(app.LaunchPath);
        // 路径作为 FileName 传入，不拼接 shell 命令，支持空格和中文目录。
        Process.Start(new ProcessStartInfo(app.LaunchPath) { UseShellExecute=true, WorkingDirectory=Path.GetDirectoryName(app.LaunchPath)! });
    }
    public static void ApplyWindowMaterial(Form form)
    {
        void Apply()
        {
            if(SystemInformation.HighContrast||!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
            // 使用公开的 DWM 属性；强制亮色原生标题栏，与已确认的白色主题一致。
            int light=0;_=DwmSetWindowAttribute(form.Handle,20,ref light,sizeof(int));
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22621))
            {
                // Desktop Acrylic 只影响系统可绘制的背景/标题栏；WinForms 白色正文会遮挡它。
                // 请求失败时保留共享 Ui 的白色磨砂视觉，不降低整窗透明度或影响文字和点击。
                int acrylic=3;_=DwmSetWindowAttribute(form.Handle,38,ref acrylic,sizeof(int));
            }
        }
        form.HandleCreated+=(_,_)=>Apply();
        if(form.IsHandleCreated)Apply();
    }
    public static void SetAutostart(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);
        // 开发时也可通过 dotnet DLL 启动；登录命令必须保留实际入口程序集。
        var host=Environment.ProcessPath!;
        var entry=typeof(WindowsIntegration).Assembly.Location;
        var command=string.Equals(Path.GetFileNameWithoutExtension(host),"dotnet",StringComparison.OrdinalIgnoreCase)
            ?$"\"{host}\" \"{entry}\" --tray":$"\"{host}\" --tray";
        if(enabled) key.SetValue("AppTimeTracker",command);
        else key.DeleteValue("AppTimeTracker",false);
    }
}
