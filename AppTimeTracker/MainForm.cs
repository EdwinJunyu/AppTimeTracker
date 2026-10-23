using System.Diagnostics;
using Microsoft.Win32;

namespace AppTimeTracker;

public sealed class MainForm : Form
{
    private readonly Store store;
    private readonly List<TrackedApp> apps;
    private readonly Tracker tracker;
    private readonly Dictionary<string,UsageSession> history;
    private Dictionary<string,UsageSession> recentSessions=[];
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer timer=new() { Interval=1000 };
    private readonly NotifyIcon tray=new() { Icon=SystemIcons.Application,Text="AppTimeTracker",Visible=true };
    private readonly EventWaitHandle wake;
    private readonly Func<bool?> readSessionLocked;
    private readonly DataGridView grid=Ui.Grid();
    private readonly Label status=new() { Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AccessibleName="监控与保存状态" };
    private readonly Label overview=new() { Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft };
    private readonly CheckBox autostart=new() { Text="登录 Windows 后自动启动到托盘",AutoSize=true,AccessibleName="登录 Windows 后自动启动到托盘" };
    private readonly Label selectionCaption=Ui.Muted("选择一个软件");
    private readonly List<Button> selectionButtons=[];
    private Button? toggleButton;
    private Button? noteButton;
    private bool locked,suspended,quit,exiting,starting=true,changingAutostart;
    private readonly bool startInTray,systemIntegration;
    private double lastSave,saveStarted;
    private Task? saveTask;
    private UsageSession[] savingSessions=[];
    private bool saveRequested;
    private string? saveError;
    private WindowSnapshot lastScan=new(new HashSet<string>(StringComparer.OrdinalIgnoreCase),0);
    public MainForm(Store store,bool startInTray,EventWaitHandle wake,bool systemIntegration=true,Func<bool?>? sessionState=null)
    {
        this.store=store;this.wake=wake;this.startInTray=startInTray;this.systemIntegration=systemIntegration;apps=store.Apps();history=store.Sessions().ToDictionary(s=>s.Id);
        tracker=new Tracker(history.Values);
        readSessionLocked=sessionState??(systemIntegration?WindowsIntegration.SessionLocked:()=>null);
        // 旧配置也应用统一路径规则，避免更新后仍无法识别带扩展前缀的记录。
        foreach(var app in apps) NormalizePaths(app);
        Ui.Style(this);Text="AppTimeTracker · 软件时钟";Size=new Size(1120,800);MinimumSize=new Size(900,650);
        // 结构沿用已确认预览：导航、标题、添加入口、软件表格、选中项操作和托盘设置。
        // 容器绘制父级柔光背景，表格保留清晰白底；不使用窗体整体透明度影响文字。
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,Padding=new Padding(28,18,28,18),ColumnCount=1,RowCount=8 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach(var height in new[]{60,56}) root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var height in new[]{64,36,38}) root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        void OpenHistory(){Safe(()=>{using var dialog=new HistoryForm(Snapshot,apps,SaveNote);dialog.ShowDialog(this);RefreshGrid();});}
        var navigation=new FlowLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,WrapContents=false,Margin=Padding.Empty };
        var activeTab=Ui.Muted("软件监控");activeTab.ForeColor=SystemInformation.HighContrast?SystemColors.HotTrack:Ui.Accent;activeTab.Padding=new Padding(4,8,18,0);
        navigation.Controls.Add(activeTab);
        var historyButton=Ui.Button("历史与导出",(_,_)=>OpenHistory());historyButton.Margin=new Padding(4,0,0,0);
        navigation.Controls.Add(historyButton);root.Controls.Add(navigation,0,0);
        // 标题按字体的实际首选高度排版；固定 42px 会裁掉部分中文字体的下沿。
        // 说明文字与标题、监控摘要分别留出 12/8 个逻辑像素，随系统 DPI 一起缩放。
        var hero=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Color.Transparent,ColumnCount=1,RowCount=3,Padding=new Padding(2,12,0,12),Margin=Padding.Empty };
        for(var i=0;i<3;i++)hero.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading=Ui.Title("软件时钟",24);var description=Ui.Muted("自动记录软件运行时间");description.Margin=new Padding(0,12,0,0);
        hero.Controls.Add(heading,0,0);hero.Controls.Add(description,0,1);
        overview.AutoSize=true;overview.Dock=DockStyle.Top;overview.Margin=new Padding(0,8,0,0);
        overview.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ui.Success;hero.Controls.Add(overview,0,2);root.Controls.Add(hero,0,1);
        hero.Layout+=(_,_)=>
        {
            // 运行时创建的控件可能在句柄建立后才确定显示器 DPI，按实际 DPI 校正留白。
            // 仅在数值变化时赋值，避免布局事件互相触发造成循环。
            var descriptionGap=(int)Math.Round(12*DeviceDpi/96d);var summaryGap=(int)Math.Round(8*DeviceDpi/96d);
            if(description.Margin.Top!=descriptionGap)description.Margin=new Padding(0,descriptionGap,0,0);
            if(overview.Margin.Top!=summaryGap)overview.Margin=new Padding(0,summaryGap,0,0);
        };
        var toolbar=new TableLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var listTitle=Ui.Title("我的软件",12);listTitle.AutoSize=false;listTitle.Dock=DockStyle.Fill;listTitle.TextAlign=ContentAlignment.MiddleLeft;toolbar.Controls.Add(listTitle,0,0);
        var primaryActions=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,BackColor=Color.Transparent,WrapContents=false,Margin=Padding.Empty };
        primaryActions.Controls.Add(Ui.Button("导出记录",(_,_)=>OpenHistory()));primaryActions.Controls.Add(Ui.PrimaryButton("添加软件…",(_,_)=>Edit(null)));
        toolbar.Controls.Add(primaryActions,1,0);root.Controls.Add(toolbar,0,2);
        var dropSurface=Ui.Surface(12);dropSurface.Dock=DockStyle.Fill;dropSurface.Margin=new Padding(0,3,0,10);
        var drop=new Label { Text="  将 EXE 拖入窗口，添加并启动软件   ·   也可点击“添加软件”",Dock=DockStyle.Fill,BackColor=Color.Transparent,ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ui.MutedInk,TextAlign=ContentAlignment.MiddleCenter,AllowDrop=true,AccessibleName="拖入软件 EXE，或使用添加软件按钮" };
        dropSurface.Controls.Add(drop);root.Controls.Add(dropSurface,0,3);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,AutoScroll=true,WrapContents=false,Padding=new Padding(0,8,0,0),Margin=Padding.Empty };
        selectionCaption.Padding=new Padding(4,10,14,0);actions.Controls.Add(selectionCaption);
        void SelectedButton(string text,Action<TrackedApp> action)
        { var b=Ui.Button(text,(_,_)=>{ var a=Selected();if(a!=null) Safe(()=>action(a)); }); selectionButtons.Add(b);actions.Controls.Add(b); }
        SelectedButton("启动",Launch);SelectedButton("编辑…",Edit);
        SelectedButton("编辑备注…",EditNote);noteButton=selectionButtons[^1];
        SelectedButton("停用监控",a=>{var copy=Copy(a);copy.Enabled=!copy.Enabled;Commit(copy);});toggleButton=selectionButtons[^1];
        SelectedButton("移出列表",a=>{
            if(MessageBox.Show(this,$"将“{a.Name}”移出监控列表？\n历史记录与累计时间会保留。重新添加相同路径可恢复。","移出列表",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;
            var copy=Copy(a);copy.Removed=true;Commit(copy);
        });
        root.Controls.Add(actions,0,5);
        foreach(var title in new[]{"软件","状态","本次时长","今日时长","累计时长","计时备注"})Ui.Column(grid,title);
        grid.Columns[0].FillWeight=170;grid.Columns[1].FillWeight=115;
        grid.Columns[5].FillWeight=200;grid.Columns[5].MinimumWidth=220;
        for(var i=2;i<5;i++){grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;}
        grid.SelectionChanged+=(_,_)=>UpdateButtons();
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0&&Selected() is {} a)Safe(()=>{if(e.ColumnIndex==5)EditNote(a);else Launch(a);});};
        var tableSurface=Ui.Surface();tableSurface.Dock=DockStyle.Fill;tableSurface.Margin=new Padding(0);tableSurface.Padding=new Padding(10);
        tableSurface.Controls.Add(grid);root.Controls.Add(tableSurface,0,4);
        autostart.BackColor=Color.Transparent;autostart.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ui.MutedInk;
        root.Controls.Add(autostart,0,6);status.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ui.MutedInk;
        status.Text="正在监控 · 关闭此窗口后继续在托盘运行";root.Controls.Add(status,0,7);Controls.Add(root);
        AllowDrop=true;
        DragEventHandler enter=(_,e)=>e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;
        DragEventHandler dropped=(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] files) foreach(var file in files) Safe(()=>AddDropped(file));};
        DragEnter+=enter;DragDrop+=dropped;drop.DragEnter+=enter;drop.DragDrop+=dropped;
        var menu=new ContextMenuStrip();menu.Items.Add("显示界面",null,(_,_)=>ShowWindow());menu.Items.Add("退出并保存",null,(_,_)=>RequestExit());tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>ShowWindow();
        autostart.Checked=store.Setting("Autostart")!="false";
        autostart.CheckedChanged+=(_,_)=>{if(!starting&&!changingAutostart)UpdateAutostart();};
        timer.Tick+=(_,_)=>{if(wake.WaitOne(0))ShowWindow();Poll();};
        if(systemIntegration){SystemEvents.SessionSwitch+=SessionSwitch;SystemEvents.PowerModeChanged+=PowerChanged;}
        FormClosing+=OnClosing;
        RefreshGrid();
    }
    protected override void SetVisibleCore(bool value)
    {
        if(starting&&value)
        {
            // 首次显示之前创建句柄并启动监控，托盘启动不会闪出主窗口。
            starting=false;
            _=Handle;
            if(systemIntegration)
            {
                Safe(()=>{WindowsIntegration.SetAutostart(autostart.Checked);store.SetSetting("Autostart",autostart.Checked?"true":"false");});
                Poll();timer.Start();
            }
            if(startInTray)value=false;
        }
        base.SetVisibleCore(value);
        if(value)RefreshGrid();
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x0011) // WM_QUERYENDSESSION：系统尚未确定是否真的结束会话。
        {
            Poll();Flush();message.Result=(nint)1;return;
        }
        if(message.Msg==0x0016) // WM_ENDSESSION：只有 TRUE 才允许结束计时和退出。
        {
            if(message.WParam!=0)
            {
                Poll();tracker.CloseAll(DateTimeOffset.Now,"SystemShutdown");Flush();timer.Stop();quit=true;
                // 查询阶段没有交给 WinForms 改变内部关闭状态，这里明确关闭窗口。
                Close();
            }
            message.Result=0;return;
        }
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) {timer.Stop();CompleteSave(true);timer.Dispose();SystemEvents.SessionSwitch-=SessionSwitch;SystemEvents.PowerModeChanged-=PowerChanged;tray.Visible=false;tray.Dispose();}
        base.Dispose(disposing);
    }
    private static TrackedApp Copy(TrackedApp a)=>new(){Id=a.Id,Name=a.Name,LaunchPath=a.LaunchPath,MonitorPaths=[..a.MonitorPaths],Enabled=a.Enabled,Removed=a.Removed};
    private TrackedApp? Selected()=>grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as TrackedApp:null;
    private void UpdateButtons()
    {
        var selected=Selected();foreach(var b in selectionButtons)b.Enabled=selected!=null&&!exiting;
        selectionCaption.Text=selected==null?"选择一个软件":"已选择 "+selected.Name;
        if(toggleButton!=null){toggleButton.Text=selected?.Enabled==false?"启用监控":"停用监控";toggleButton.AccessibleName=toggleButton.Text;}
        if(noteButton!=null)
        {
            noteButton.Enabled=selected!=null&&!exiting&&NoteSession(selected.Id)!=null;
            noteButton.AccessibleDescription="编辑本次计时的备注；尚未开始新计时时，可编辑最近一次计时的备注。更早记录请打开历史与导出。";
        }
    }
    private void UpdateAutostart()
    {
        var previous=store.Setting("Autostart")!="false";
        try
        {
            if(systemIntegration)WindowsIntegration.SetAutostart(autostart.Checked);
            store.SetSetting("Autostart",autostart.Checked?"true":"false");
        }
        catch(Exception ex)
        {
            // 保存失败时恢复复选框和系统设置，不能把尚未保存的状态显示成成功。
            changingAutostart=true;autostart.Checked=previous;changingAutostart=false;
            if(systemIntegration)try{WindowsIntegration.SetAutostart(previous);}catch{ /* 保留首次失败原因供用户处理。 */ }
            Ui.Error(this,ex);
        }
    }
    private void Safe(Action action){try{action();}catch(Exception ex){Ui.Error(this,ex);}}
    private void ShowWindow(){Show();WindowState=FormWindowState.Normal;RefreshGrid();Activate();}
    private void SessionSwitch(object sender,SessionSwitchEventArgs e)
    {
        if(IsDisposed||!IsHandleCreated)return;
        BeginInvoke(()=>{if(e.Reason==SessionSwitchReason.SessionLock)locked=true;else if(e.Reason==SessionSwitchReason.SessionUnlock)locked=false;Poll();Flush();});
    }
    private void PowerChanged(object sender,PowerModeChangedEventArgs e)
    {
        if(IsDisposed||!IsHandleCreated)return;
        BeginInvoke(()=>{if(e.Mode==PowerModes.Suspend)suspended=true;else if(e.Mode==PowerModes.Resume)suspended=false;Poll();Flush();});
    }
    private void Poll()
    {
        if(exiting){if(Flush()){quit=true;Close();}return;}
        try
        {
            // 权限不足或查询失败只代表未知；已知结果每次采样重新校正，避免永久卡住暂停。
            if(readSessionLocked() is bool sessionLocked)locked=sessionLocked;
            var paused=locked||suspended;
            if(!paused)lastScan=WindowsIntegration.Scan();
            tracker.Observe(apps,lastScan.Paths,DateTimeOffset.Now,clock.Elapsed.TotalSeconds,paused);
            saveRequested|=tracker.Changed;
            CompleteSave(false);
            if(saveRequested||clock.Elapsed.TotalSeconds-lastSave>=5)StartSave();
            status.Text=saveError??(paused?"计时已暂停 · 解锁或唤醒后自动继续":lastScan.UnreadableWindows>0?$"正在监控 · {lastScan.UnreadableWindows} 个窗口无法读取路径，受保护的软件可能无法识别":"正在监控 · 关闭此窗口后继续在托盘运行");
            status.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:saveError!=null?Color.Firebrick:Ui.MutedInk;
            // 托盘和最小化期间继续采样与保存，省去不可见表格的历史汇总、路径访问与绘制。
            if(Visible&&WindowState!=FormWindowState.Minimized)RefreshGrid();
        }
        catch(Exception ex){status.Text="监控失败，将自动重试："+ex.Message;status.ForeColor=Color.Firebrick;}
    }
    private void StartSave()
    {
        if(saveTask!=null)return;
        if(tracker.Pending.Count==0){lastSave=clock.Elapsed.TotalSeconds;saveRequested=false;return;}
        savingSessions=tracker.Pending.Values.Select(s=>s.Snapshot()).ToArray();
        // 主线程保留最新对象供显示、导出；后台只读深拷贝，不会与下一秒的累计竞争。
        foreach(var pair in tracker.Pending)history[pair.Key]=pair.Value;
        tracker.Pending.Clear();saveStarted=clock.Elapsed.TotalSeconds;saveRequested=false;
        var batch=savingSessions;
        saveTask=Task.Run(()=>store.SaveSessions(batch));
    }
    private bool CompleteSave(bool wait)
    {
        if(saveTask==null)return true;
        if(!wait&&!saveTask.IsCompleted)return false;
        try
        {
            saveTask.GetAwaiter().GetResult();lastSave=saveStarted;saveError=null;return true;
        }
        catch(Exception ex)
        {
            // 失败快照必须重新待写；已存在的更新版本优先，避免旧快照覆盖新累计或结束状态。
            foreach(var s in savingSessions)
                if(!tracker.Pending.ContainsKey(s.Id))tracker.Pending[s.Id]=history.GetValueOrDefault(s.Id,s);
            saveRequested=true;saveError="保存失败，正在保留记录并重试。请勿强制关闭："+ex.Message;
            status.Text=saveError;return false;
        }
        finally {saveTask=null;savingSessions=[];}
    }
    private bool Flush()
    {
        // 退出、休眠与系统会话结束必须先收齐后台写入，再同步保存最后一段，不能提前清空待写数据。
        CompleteSave(true);
        try
        {
            if(tracker.Pending.Count>0)store.SaveSessions(tracker.Pending.Values);
            foreach(var pair in tracker.Pending)history[pair.Key]=pair.Value;
            tracker.Pending.Clear();lastSave=clock.Elapsed.TotalSeconds;saveRequested=false;saveError=null;return true;
        }
        catch(Exception ex){saveError="保存失败，正在保留记录并重试。请勿强制关闭："+ex.Message;status.Text=saveError;return false;}
    }
    public List<UsageSession> Snapshot()
    {
        var all=new Dictionary<string,UsageSession>(history);
        foreach(var pair in tracker.Pending)all[pair.Key]=pair.Value;
        foreach(var s in tracker.Active.Values)all[s.Id]=s;
        return all.Values.ToList();
    }
    private UsageSession? NoteSession(string appId)=>tracker.Active.GetValueOrDefault(appId)??recentSessions.GetValueOrDefault(appId);
    private void EditNote(TrackedApp app)
    {
        if(NoteSession(app.Id) is not {} session)return;
        using var editor=new SessionNoteEditor(session,SaveNote);
        editor.ShowDialog(this);RefreshGrid();
    }
    private void SaveNote(string sessionId,string note)
    {
        // 先收齐旧的后台快照，再保存最新会话，避免旧备注或累计覆盖本次编辑。
        CompleteSave(true);
        var session=Snapshot().FirstOrDefault(s=>s.Id==sessionId)??throw new InvalidOperationException("找不到这次计时记录，请关闭编辑窗口并刷新后重试。");
        var updated=session.Snapshot();updated.Note=note;
        // 保存成功后才更新界面与待写对象；失败时编辑窗口保留输入，可直接重试。
        store.SaveSessions([updated]);
        session.Note=note;history[session.Id]=session;
        if(tracker.Pending.ContainsKey(session.Id))tracker.Pending[session.Id]=session;
        RefreshGrid();
        status.Text="备注已保存。";
    }
    private void RefreshGrid()
    {
        var selected=Selected()?.Id;
        var visible=apps.Where(a=>!a.Removed).ToList();
        var today=DateTime.Now.ToString("yyyy-MM-dd");
        var totals=new Dictionary<string,(double Today,double Total)>();
        var sessions=Snapshot();recentSessions=UsageSession.RecentByApp(sessions);
        // 历史只遍历一次，避免每一行软件都重新过滤全部会话；亚秒数先相加再格式化。
        foreach(var session in sessions)
        {
            var sum=totals.GetValueOrDefault(session.AppId);
            sum.Total+=session.Seconds;
            foreach(var segment in session.Segments)if(segment.Day==today)sum.Today+=segment.Seconds;
            totals[session.AppId]=sum;
        }
        var pathValidity=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        bool Exists(string path)
        {if(!pathValidity.TryGetValue(path,out var valid)){valid=File.Exists(path);pathValidity[path]=valid;}return valid;}
        // 保留行对象和选择，避免每秒刷新使键盘焦点、滚动位置跳动。
        if(grid.Rows.Count!=visible.Count||visible.Where((a,i)=>grid.Rows[i].Tag!=a).Any())
        {grid.Rows.Clear();foreach(var a in visible){var i=grid.Rows.Add();grid.Rows[i].Tag=a;}}
        foreach(DataGridViewRow row in grid.Rows)
        {
            var a=(TrackedApp)row.Tag!;var current=tracker.Active.GetValueOrDefault(a.Id);
            var valid=Exists(a.LaunchPath)&&a.MonitorPaths.All(Exists);
            var state=!a.Enabled?"已停用":!valid?"路径失效，请编辑":current!=null?(locked||suspended?"已暂停":"计时中"):"等待窗口";
            var sum=totals.GetValueOrDefault(a.Id);
            var noteSession=NoteSession(a.Id);
            var note=noteSession==null?"尚无计时记录":(current!=null?"本次：":"最近一次：")+(string.IsNullOrEmpty(noteSession.Note)?"未填写":noteSession.Note);
            object[] values=[a.Name,state,Csv.Duration(current?.Seconds??0),Csv.Duration(sum.Today),Csv.Duration(sum.Total),note];
            for(var i=0;i<values.Length;i++)if(!Equals(row.Cells[i].Value,values[i]))row.Cells[i].Value=values[i];
            if(a.Id==selected)row.Selected=true;
        }
        overview.Text=visible.Count==0?"从一个软件开始：拖入 EXE 或添加路径。":$"{visible.Count} 个软件 · {tracker.Active.Count} 个打开中 · 同一软件多窗口只累计一次";
        UpdateButtons();
    }
    private void Edit(TrackedApp? app)
    {
        using var editor=new AppEditor(app);
        if(editor.ShowDialog(this)==DialogResult.OK&&editor.Result is {} result)Safe(()=>Commit(result));
    }
    private void Commit(TrackedApp app)
    {
        NormalizePaths(app);
        var duplicate=apps.FirstOrDefault(a=>a.Id!=app.Id&&string.Equals(a.LaunchPath,app.LaunchPath,StringComparison.OrdinalIgnoreCase));
        if(duplicate!=null)
        {
            // 编辑必须保留原软件身份；只有新增操作可以恢复已移出的同路径记录。
            if(apps.Any(a=>a.Id==app.Id))throw new ArgumentException($"启动路径已属于“{duplicate.Name}”。编辑未保存，请使用不同路径，或重新添加该软件以恢复旧记录。");
            if(duplicate.Removed){app.Id=duplicate.Id;app.Removed=false;}
            else {Select(duplicate.Id);MessageBox.Show(this,"该路径已经登记，已选中已有软件。","已存在",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        }
        Poll();
        store.SaveApp(app);
        if(tracker.Active.Remove(app.Id,out var session)) {session.End=DateTimeOffset.Now;session.Reason=app.Removed?"Removed":!app.Enabled?"Disabled":"Edited";tracker.Pending[session.Id]=session;}
        var index=apps.FindIndex(a=>a.Id==app.Id);if(index<0)apps.Add(app);else apps[index]=app;
        Flush();Poll();Select(app.Id);
    }
    private static void NormalizePaths(TrackedApp app)
    {
        app.LaunchPath=TrackedApp.Normalize(app.LaunchPath);
        app.MonitorPaths=app.MonitorPaths.Select(TrackedApp.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private void Select(string id){foreach(DataGridViewRow row in grid.Rows)row.Selected=((TrackedApp)row.Tag!).Id==id;}
    private void AddDropped(string file)
    {
        var path=TrackedApp.Normalize(file);TrackedApp.ValidateExe(path);
        var app=apps.FirstOrDefault(a=>string.Equals(a.LaunchPath,path,StringComparison.OrdinalIgnoreCase));
        if(app==null){app=new TrackedApp{Name=Path.GetFileNameWithoutExtension(path),LaunchPath=path,MonitorPaths=[path]};Commit(app);}
        else if(app.Removed||!app.Enabled){app=Copy(app);app.Removed=false;app.Enabled=true;Commit(app);}
        Select(app.Id);Launch(app);
    }
    private void Launch(TrackedApp app){WindowsIntegration.Launch(app);status.Text="已请求启动 "+app.Name+"，窗口出现后开始计时。";}
    private void RequestExit(){Poll();tracker.CloseAll(DateTimeOffset.Now,"TrackerExited");exiting=true;if(Flush()){quit=true;Close();}else ShowWindow();}
    private void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(quit)return;
        if(e.CloseReason==CloseReason.TaskManagerClosing)
        {Poll();tracker.CloseAll(DateTimeOffset.Now,"SystemShutdown");Flush();quit=true;return;}
        e.Cancel=true;Hide();
    }
}
