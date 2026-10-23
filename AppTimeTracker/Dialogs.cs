using System.Drawing.Drawing2D;

namespace AppTimeTracker;

// 共享颜色和原生控件外观；毛玻璃只改变绘制层，键盘、滚动与系统选择器仍由 Windows 管理。
public static class Ui
{
    public static readonly Color Ink=Color.FromArgb(36,52,75);
    public static readonly Color MutedInk=Color.FromArgb(86,103,124);
    public static readonly Color Accent=Color.FromArgb(65,105,212);
    public static readonly Color Paper=Color.FromArgb(247,249,252);
    public static readonly Color Line=Color.FromArgb(222,230,241);
    public static readonly Color Success=Color.FromArgb(44,105,84);
    public static readonly Font Body=new("Microsoft YaHei UI",10);
    public static readonly Font Data=new("Consolas",10);

    public static void Style(Form form)
    {
        form.Font=Body;form.AutoScaleMode=AutoScaleMode.Dpi;
        form.BackColor=SystemInformation.HighContrast?SystemColors.Window:Paper;
        form.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ink;
        form.StartPosition=FormStartPosition.CenterScreen;
        form.Resize+=(_,_)=>form.Invalidate(true);
        form.Paint+=(_,e)=>
        {
            if(SystemInformation.HighContrast||form.ClientSize.Width<=0||form.ClientSize.Height<=0)return;
            // 低对比的冰蓝、淡紫背景光晕保留明亮阅读表面，不加入 3D 装饰。
            var bounds=form.ClientRectangle;
            using var wash=new LinearGradientBrush(bounds,Color.FromArgb(239,246,254),Paper,35);
            e.Graphics.FillRectangle(wash,bounds);
            PaintGlow(e.Graphics,new Rectangle(bounds.Width/2,bounds.Height/5,bounds.Width,bounds.Height),Color.FromArgb(60,232,225,249));
            PaintGlow(e.Graphics,new Rectangle(-bounds.Width/2,-bounds.Height/2,bounds.Width,bounds.Height),Color.FromArgb(70,222,238,255));
        };
        WindowsIntegration.ApplyWindowMaterial(form);
    }
    private static void PaintGlow(Graphics graphics,Rectangle bounds,Color center)
    {
        if(bounds.Width<=0||bounds.Height<=0)return;
        using var path=new GraphicsPath();path.AddEllipse(bounds);
        using var glow=new PathGradientBrush(path) { CenterColor=center,SurroundColors=[Color.FromArgb(0,center)] };
        graphics.FillPath(glow,path);
    }
    public static Panel Surface(int radius=18)=>new GlassSurface(radius) { Padding=new Padding(12),Margin=Padding.Empty };
    public static Label Title(string text,float size=24)=>new() { Text=text,Font=new Font(Body.FontFamily,size,FontStyle.Regular),AutoSize=true,BackColor=Color.Transparent,Margin=Padding.Empty };
    public static Label Muted(string text)=>new() { Text=text,AutoSize=true,ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:MutedInk,BackColor=Color.Transparent };
    public static Button Button(string text,EventHandler click)
    {
        var button=new GlassButton { Text=text,Font=Body,AutoSize=true,MinimumSize=new Size(96,44),Margin=new Padding(4),Padding=new Padding(14,5,14,5),AccessibleName=text };
        button.Click+=click;return button;
    }
    public static Button PrimaryButton(string text,EventHandler click)
    {
        var button=Button(text,click);((GlassButton)button).Primary=true;return button;
    }
    public static void Select(Button button,bool selected)
    {
        if(button is GlassButton glass){glass.Selected=selected;glass.AccessibleDescription=selected?"当前视图":null;glass.Invalidate();}
    }
    public static DataGridView Grid()
    {
        var contrast=SystemInformation.HighContrast;
        var grid=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
            MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible=false,BackgroundColor=SystemColors.Window,BorderStyle=BorderStyle.None,AutoGenerateColumns=false,
            AllowUserToResizeRows=false,EnableHeadersVisualStyles=contrast,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor=contrast?SystemColors.ControlDark:Color.FromArgb(237,241,247),ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeight=42,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,RowTemplate={Height=46} };
        if(!contrast)
        {
            grid.DefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.White,ForeColor=Ink,SelectionBackColor=Color.FromArgb(231,240,255),SelectionForeColor=Ink,Padding=new Padding(12,6,12,6) };
            grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(252,253,255);
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(247,250,254),ForeColor=MutedInk,SelectionBackColor=Color.FromArgb(247,250,254),SelectionForeColor=MutedInk,Padding=new Padding(12,6,12,6),Font=Body };
        }
        return grid;
    }
    public static void Column(DataGridView grid,string title,int weight=100)
    {
        var numeric=title.Contains("时长")||title=="会话数";
        var column=new DataGridViewTextBoxColumn { HeaderText=title,FillWeight=weight,MinimumWidth=numeric?108:100,SortMode=DataGridViewColumnSortMode.NotSortable };
        if(numeric){column.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;column.DefaultCellStyle.Font=Data;column.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;}
        grid.Columns.Add(column);
    }
    public static void Error(IWin32Window owner,Exception ex)=>MessageBox.Show(owner,ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    private static GraphicsPath Rounded(RectangleF bounds,float radius)
    {
        var path=new GraphicsPath();var diameter=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
        if(diameter<=0){path.AddRectangle(bounds);return path;}
        path.AddArc(bounds.Left,bounds.Top,diameter,diameter,180,90);path.AddArc(bounds.Right-diameter,bounds.Top,diameter,diameter,270,90);
        path.AddArc(bounds.Right-diameter,bounds.Bottom-diameter,diameter,diameter,0,90);path.AddArc(bounds.Left,bounds.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
    private sealed class GlassSurface : Panel
    {
        private readonly int radius;
        public GlassSurface(int radius)
        {
            this.radius=radius;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.ResizeRedraw,true);
            BackColor=Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Width<2||Height<2)return;
            if(SystemInformation.HighContrast){e.Graphics.Clear(SystemColors.Window);ControlPaint.DrawBorder(e.Graphics,ClientRectangle,SystemColors.WindowText,ButtonBorderStyle.Solid);return;}
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var shape=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),radius*DeviceDpi/96f);
            using var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(248,255,255,255),Color.FromArgb(235,255,255,255),70);
            using var line=new Pen(Color.FromArgb(210,224,233,246));e.Graphics.FillPath(fill,shape);e.Graphics.DrawPath(line,shape);
        }
    }
    private sealed class GlassButton : Button
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Primary { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected { get; set; }
        private bool hovered,pressed;
        public GlassButton()
        {
            if(SystemInformation.HighContrast){FlatStyle=FlatStyle.System;UseVisualStyleBackColor=true;Cursor=Cursors.Hand;return;}
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;BackColor=Color.Transparent;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;
            // ButtonBase 默认带 Opaque，正常 WM_PAINT 会因此跳过父级背景绘制。
            // 圆角只填充中间区域，必须关闭此标记，避免四角留下双缓冲中的黑色像素。
            SetStyle(ControlStyles.Opaque,false);
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){pressed=true;Invalidate();}base.OnKeyDown(e);}
        protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            // 高对比模式直接使用原生按钮，避免主题颜色覆盖系统偏好。
            if(SystemInformation.HighContrast){base.OnPaint(e);return;}
            if(Width<2||Height<2)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var fill=Primary?Accent:Selected?Color.FromArgb(231,239,255):Color.FromArgb(246,250,255);
            if(hovered&&Enabled)fill=Primary?Color.FromArgb(53,91,192):Color.FromArgb(231,240,255);
            if(pressed&&Enabled)fill=Primary?Color.FromArgb(43,78,175):Color.FromArgb(218,231,252);
            if(!Enabled)fill=Color.FromArgb(241,244,249);
            var foreground=!Enabled?Color.FromArgb(126,140,158):Primary?Color.White:Selected?Accent:Ink;
            using var shape=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),10*DeviceDpi/96f);
            using var brush=new SolidBrush(fill);using var border=new Pen(Primary&&Enabled?fill:Line);e.Graphics.FillPath(brush,shape);e.Graphics.DrawPath(border,shape);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),foreground,fill);
        }
    }
}

public sealed class AppEditor : Form
{
    private readonly TextBox name=new() { Dock=DockStyle.Top,AccessibleName="软件名称" };
    private readonly TextBox launch=new() { Dock=DockStyle.Top,AccessibleName="启动路径" };
    private readonly TextBox monitors=new() { Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,AccessibleName="监控路径，每行一个" };
    private readonly Label error=new() { Dock=DockStyle.Fill,ForeColor=Color.Firebrick,AutoSize=false,AccessibleName="输入错误",BackColor=Color.Transparent };
    private readonly TrackedApp? original;
    public TrackedApp? Result { get; private set; }
    public AppEditor(TrackedApp? app=null)
    {
        original=app;Ui.Style(this);Text=app==null?"添加软件":"编辑软件";Size=new Size(760,580);MinimumSize=new Size(650,530);
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=2,BackColor=Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,82));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var header=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty };
        header.Controls.Add(Ui.Title(Text));header.Controls.Add(Ui.Muted("添加启动程序，并选择需要记录的 EXE 窗口。"));
        var surface=Ui.Surface();surface.Dock=DockStyle.Fill;surface.Padding=new Padding(20);
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,BackColor=Color.Transparent,Margin=Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        Label FieldLabel(string text)=>new() { Text=text,AutoSize=true,Padding=new Padding(0,5,0,0),BackColor=Color.Transparent };
        foreach(var input in new[]{name,launch,monitors}){input.BorderStyle=BorderStyle.FixedSingle;input.Margin=new Padding(0,4,0,8);}
        layout.Controls.Add(FieldLabel("软件名称"),0,0);layout.Controls.Add(name,1,0);
        var pathPanel=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=Padding.Empty,BackColor=Color.Transparent };
        pathPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));pathPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,52));pathPanel.Controls.Add(launch,0,0);
        var browse=Ui.Button("浏览 EXE…",(_,_)=>
        {
            using var picker=new OpenFileDialog { Filter="可执行程序 (*.exe)|*.exe",CheckFileExists=true };
            if(picker.ShowDialog(this)==DialogResult.OK){launch.Text=picker.FileName;if(string.IsNullOrWhiteSpace(name.Text))name.Text=Path.GetFileNameWithoutExtension(picker.FileName);}
        });browse.Margin=Padding.Empty;pathPanel.Controls.Add(browse,0,1);
        layout.Controls.Add(FieldLabel("启动路径"),0,1);layout.Controls.Add(pathPanel,1,1);
        layout.Controls.Add(FieldLabel("监控路径"),0,2);layout.Controls.Add(monitors,1,2);
        var hint=Ui.Muted("每行一个完整 EXE 路径。留空时监控启动程序；\n使用启动器时，可填写实际软件的 EXE。");hint.Dock=DockStyle.Fill;hint.AutoSize=false;layout.Controls.Add(hint,1,3);layout.Controls.Add(error,1,4);
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,BackColor=Color.Transparent,Margin=Padding.Empty };
        var cancel=Ui.Button("取消",(_,_)=>Close());cancel.DialogResult=DialogResult.Cancel;
        var save=Ui.PrimaryButton("保存",(_,_)=>Save());actions.Controls.Add(cancel);actions.Controls.Add(save);layout.Controls.Add(actions,1,5);
        surface.Controls.Add(layout);root.Controls.Add(header,0,0);root.Controls.Add(surface,0,1);Controls.Add(root);AcceptButton=save;CancelButton=cancel;
        if(app!=null){name.Text=app.Name;launch.Text=app.LaunchPath;monitors.Text=string.Join(Environment.NewLine,app.MonitorPaths);}
    }
    private void Save()
    {
        try
        {
            if(string.IsNullOrWhiteSpace(name.Text)){name.Focus();throw new ArgumentException("请输入软件名称。");}
            var path=TrackedApp.Normalize(launch.Text);TrackedApp.ValidateExe(path);
            var paths=monitors.Lines.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(TrackedApp.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(paths.Count==0)paths.Add(path);foreach(var p in paths)TrackedApp.ValidateExe(p);
            Result=new TrackedApp { Id=original?.Id??Guid.NewGuid().ToString("N"),Name=name.Text.Trim(),LaunchPath=path,MonitorPaths=paths,Enabled=original?.Enabled??true };
            DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex){error.Text=ex.Message;}
    }
}

public sealed class SessionNoteEditor : Form
{
    private readonly TextBox note=new() { Dock=DockStyle.Fill,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,MaxLength=0,AccessibleName="备注" };
    private readonly Label error=Ui.Muted("");
    public SessionNoteEditor(UsageSession session,Action<string,string> saveNote)
    {
        Ui.Style(this);Text="编辑计时备注";Size=new Size(720,540);MinimumSize=new Size(520,420);StartPosition=FormStartPosition.CenterParent;
        var original=session.Note;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24),BackColor=Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,120));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,58));root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        var header=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty };
        header.Controls.Add(Ui.Title("计时备注",20));
        var context=Ui.Muted(session.Name+" · 开始于 "+session.Start.ToString("yyyy-MM-dd HH:mm:ss zzz"));context.AutoSize=false;context.Height=48;context.Margin=Padding.Empty;context.AutoEllipsis=true;header.Controls.Add(context);
        var hint=Ui.Muted("记录这段时间做了什么，可留空。点击“保存备注”后生效。");hint.AutoSize=false;hint.Height=36;hint.Margin=Padding.Empty;header.Controls.Add(hint);
        header.SizeChanged+=(_,_)=>{context.Width=Math.Max(0,header.ClientSize.Width);hint.Width=context.Width;};
        var surface=Ui.Surface();surface.Dock=DockStyle.Fill;surface.Controls.Add(note);
        note.Text=original;note.TabIndex=0;
        error.Dock=DockStyle.Fill;error.AutoSize=false;error.Padding=new Padding(0,6,0,0);error.AccessibleName="备注保存状态";
        error.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Color.Firebrick;
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty };
        var cancel=Ui.Button("取消",(_,_)=>Close());CancelButton=cancel;
        var save=Ui.PrimaryButton("保存备注",(_,_)=>
        {
            try { saveNote(session.Id,note.Text);DialogResult=DialogResult.OK;Close(); }
            catch(Exception ex){error.Text="备注未保存，请重试："+ex.Message;note.Focus();}
        });
        actions.Controls.Add(cancel);actions.Controls.Add(save);
        root.Controls.Add(header,0,0);root.Controls.Add(surface,0,1);root.Controls.Add(error,0,2);root.Controls.Add(actions,0,3);Controls.Add(root);
        // 不设置回车提交：原生多行输入保留换行和中文输入法的确认键。
        Shown+=(_,_)=>note.Focus();
        FormClosing+=(_,e)=>
        {
            if(DialogResult==DialogResult.OK||note.Text==original)return;
            if(MessageBox.Show(this,"放弃尚未保存的备注修改？","放弃修改",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)e.Cancel=true;
        };
    }
}

public sealed class HistoryForm : Form
{
    private readonly Func<List<UsageSession>> snapshot;
    private readonly Action<string,string> saveNote;
    private readonly ComboBox apps=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=195,AccessibleName="筛选软件" };
    private readonly DateTimePicker from=new() { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Width=135,Value=DateTime.Today.AddDays(-30),AccessibleName="起始日期" };
    private readonly DateTimePicker to=new() { Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Width=135,AccessibleName="结束日期" };
    private readonly DataGridView grid=Ui.Grid();
    private readonly Label pageLabel=new() { AutoSize=false,Width=360,Height=48,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(8,0,4,0),BackColor=Color.Transparent,AccessibleName="历史分页状态" };
    private readonly Label rangeNote=Ui.Muted("");
    private readonly Label empty=Ui.Muted("");
    private readonly FlowLayoutPanel dateRange=new() { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty };
    private readonly Label allRange=Ui.Muted("全部历史 · 从首次记录至今");
    private readonly Dictionary<string,Button> modes=[];
    private readonly Button export;
    private readonly Button previous;
    private readonly Button next;
    private readonly Button editNote;
    private string mode="detail";
    private int page;
    private bool ready;
    public HistoryForm(Func<List<UsageSession>> snapshot,List<TrackedApp> allApps,Action<string,string> saveNote)
    {
        this.snapshot=snapshot;this.saveNote=saveNote;Ui.Style(this);Text="历史与导出";Size=new Size(1120,760);MinimumSize=new Size(960,660);
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(24),BackColor=Color.Transparent };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,88));root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,44));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        var header=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty };
        header.Controls.Add(Ui.Title("历史与导出"));header.Controls.Add(Ui.Muted("查看每次使用情况"));
        var segments=new FlowLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,WrapContents=false,Margin=Padding.Empty };
        foreach(var item in new[]{("detail","会话明细"),("daily","每日汇总"),("lifetime","累计汇总")})
        {
            var key=item.Item1;var button=Ui.Button(item.Item2,(_,_)=>SetMode(key));modes.Add(key,button);segments.Controls.Add(button);
        }
        // 已移出配置的软件仍可按历史 ID 筛选；现有配置名称优先，软件名称相同也不合并 ID。
        var options=allApps.GroupBy(a=>a.Id).Select(g=>g.First()).ToList();
        foreach(var row in new UsageHistory(snapshot()).Lifetime())if(options.All(a=>a.Id!=row.AppId))options.Add(new TrackedApp { Id=row.AppId,Name=row.Name,Removed=true });
        // 每次打开都从所有软件开始，不继承主页选中项或上次筛选。
        apps.Items.Add("所有软件");foreach(var app in options)apps.Items.Add(app);apps.SelectedIndex=0;
        var filters=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,BackColor=Color.Transparent };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,210));
        // 必须显式限制筛选行。默认 AutoSize 行会被内部流式容器的首选高度撑大，使导出按钮越过表格边界。
        filters.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var fields=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true,BackColor=Color.Transparent,Margin=Padding.Empty };
        Control Field(string title,Control input)
        {
            var field=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,16,0),BackColor=Color.Transparent };
            var label=Ui.Muted(title);label.Margin=new Padding(0,0,0,6);input.Margin=Padding.Empty;field.Controls.Add(label);field.Controls.Add(input);return field;
        }
        fields.Controls.Add(Field("软件",apps));dateRange.Controls.Add(Field("开始日期",from));dateRange.Controls.Add(Field("结束日期",to));fields.Controls.Add(dateRange);
        allRange.Padding=new Padding(0,27,0,0);allRange.Visible=false;fields.Controls.Add(allRange);
        export=Ui.PrimaryButton("导出会话明细 CSV",(_,_)=>ExportCurrent());export.Anchor=AnchorStyles.Right|AnchorStyles.Top;export.Margin=new Padding(0,20,0,0);
        filters.Controls.Add(fields,0,0);filters.Controls.Add(export,1,0);
        var table=Ui.Surface();table.Dock=DockStyle.Fill;table.Padding=new Padding(10);table.Controls.Add(grid);
        empty.AutoSize=false;empty.TextAlign=ContentAlignment.MiddleCenter;empty.AccessibleName="历史记录状态";table.Controls.Add(empty);
        // 空状态覆盖数据区域，表头保持原位；筛选错误不会让窗口内容跳动。
        void PlaceEmpty()=>empty.SetBounds(grid.Left+12,grid.Top+grid.ColumnHeadersHeight+12,Math.Max(0,grid.Width-24),Math.Max(0,grid.Height-grid.ColumnHeadersHeight-24));
        table.Resize+=(_,_)=>PlaceEmpty();PlaceEmpty();
        rangeNote.Dock=DockStyle.Fill;rangeNote.AutoSize=false;rangeNote.Padding=new Padding(2,12,0,0);
        var paging=new FlowLayoutPanel { Dock=DockStyle.Fill,BackColor=Color.Transparent,WrapContents=false,Margin=Padding.Empty };
        previous=Ui.Button("上一页",(_,_)=>{page=Math.Max(0,page-1);RefreshRows();});next=Ui.Button("下一页",(_,_)=>{page++;RefreshRows();});
        var refresh=Ui.Button("刷新记录",(_,_)=>{page=0;RefreshRows();});
        editNote=Ui.Button("编辑备注…",(_,_)=>EditNote());paging.Controls.AddRange([previous,next,refresh,editNote,pageLabel]);
        grid.SelectionChanged+=(_,_)=>UpdateNoteButton();
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0&&mode=="detail")EditNote();};
        root.Controls.Add(header,0,0);root.Controls.Add(segments,0,1);root.Controls.Add(filters,0,2);root.Controls.Add(table,0,3);root.Controls.Add(rangeNote,0,4);root.Controls.Add(paging,0,5);Controls.Add(root);
        apps.SelectedIndexChanged+=(_,_)=>FiltersChanged();from.ValueChanged+=(_,_)=>FiltersChanged();to.ValueChanged+=(_,_)=>FiltersChanged();
        ready=true;SetMode("detail");
    }
    private void FiltersChanged(){if(!ready)return;page=0;RefreshRows();}
    private void SetMode(string value)
    {
        if(value is not ("detail" or "daily" or "lifetime"))throw new ArgumentException("未知记录类型。",nameof(value));
        mode=value;page=0;foreach(var item in modes)Ui.Select(item.Value,item.Key==mode);
        dateRange.Visible=mode!="lifetime";from.Enabled=mode!="lifetime";to.Enabled=mode!="lifetime";allRange.Visible=mode=="lifetime";
        export.Text="导出"+(mode=="lifetime"?"累计汇总":mode=="daily"?"每日汇总":"会话明细")+" CSV";export.AccessibleName=export.Text;
        grid.Columns.Clear();
        var titles=mode=="lifetime"?new[]{"软件","首次记录","最近记录","会话数","总时长","备注"}:mode=="daily"?new[]{"日期","软件","有效时长","会话数","总时长","备注"}:new[]{"软件","开始时间","结束时间","有效时长","总时长","状态 / 结束原因","备注"};
        foreach(var title in titles)Ui.Column(grid,title,title.Contains("时间")||title.Contains("记录")?145:100);
        foreach(DataGridViewColumn column in grid.Columns)if(column.HeaderText.Contains("时间")||column.HeaderText.Contains("记录"))column.MinimumWidth=mode=="detail"?220:175;
        grid.Columns[^1].MinimumWidth=200;grid.Columns[^1].FillWeight=180;
        RefreshRows();
    }
    private void UpdateNoteButton()
    {
        editNote.Enabled=mode=="detail"&&grid.SelectedRows.Count>0&&grid.SelectedRows[0].Tag is string;
        editNote.AccessibleDescription=mode=="detail"?"编辑选中计时记录的备注。":"请切换到会话明细，选择一次计时后编辑备注。";
    }
    private void EditNote()
    {
        if(mode!="detail"||grid.SelectedRows.Count==0||grid.SelectedRows[0].Tag is not string id)return;
        var session=snapshot().FirstOrDefault(s=>s.Id==id);if(session==null){RefreshRows();return;}
        using var editor=new SessionNoteEditor(session,saveNote);
        if(editor.ShowDialog(this)==DialogResult.OK){RefreshRows(id);pageLabel.Text+=" · 备注已保存";}
    }
    private void ValidateDates()
    {
        if(from.Value.Date>to.Value.Date)throw new ArgumentException("结束日期不能早于开始日期，请调整日期范围。");
    }
    private void RefreshRows(string? selectedId=null)
    {
        try
        {
            if(mode!="lifetime")ValidateDates();
            // 预览和导出均按软件 ID 累计，日期只影响明细或每日片段，不改变总时长。
            var history=new UsageHistory(snapshot(),(apps.SelectedItem as TrackedApp)?.Id);var display=new List<object[]>();var sessionIds=new List<string>();
            if(mode=="lifetime")
                foreach(var row in history.Lifetime())display.Add([row.Name,row.FirstRecorded.ToString("yyyy-MM-dd HH:mm:ss"),row.LastRecorded.ToString("yyyy-MM-dd HH:mm:ss"),row.SessionCount,Csv.Duration(row.Seconds),row.Note]);
            else if(mode=="daily")
            {
                foreach(var row in history.Daily(DateOnly.FromDateTime(from.Value),DateOnly.FromDateTime(to.Value)).OrderByDescending(x=>x.Day).ThenBy(x=>x.AppId))
                    display.Add([row.Day,row.Name,Csv.Duration(row.Seconds),row.SessionCount,Csv.Duration(row.TotalSeconds),row.Note]);
            }
            else
                foreach(var s in history.Details(DateOnly.FromDateTime(from.Value),DateOnly.FromDateTime(to.Value)).OrderByDescending(s=>s.Start))
                { sessionIds.Add(s.Id);display.Add([s.Name,s.Start.ToString("yyyy-MM-dd HH:mm:ss zzz"),s.End?.ToString("yyyy-MM-dd HH:mm:ss zzz")??"进行中",Csv.Duration(s.Seconds),Csv.Duration(history.Totals[s.AppId]),s.End==null?"进行中":Csv.Reason(s.Reason),s.Note]); }
            page=Math.Clamp(page,0,Math.Max(0,(display.Count-1)/50));grid.Rows.Clear();
            foreach(var i in Enumerable.Range(page*50,Math.Min(50,display.Count-page*50)))
            {
                var index=grid.Rows.Add(display[i]);
                if(mode=="detail"){grid.Rows[index].Tag=sessionIds[i];if(sessionIds[i]==selectedId){grid.CurrentCell=grid.Rows[index].Cells[0];grid.Rows[index].Selected=true;}}
            }
            UpdateNoteButton();
            empty.Text=mode=="lifetime"?"还没有累计记录。添加软件并开始使用后，记录会显示在这里。":"此范围没有记录，请调整日期或软件筛选。";empty.Visible=display.Count==0;if(empty.Visible)empty.BringToFront();
            previous.Enabled=page>0;next.Enabled=(page+1)*50<display.Count;export.Enabled=true;
            pageLabel.Text=display.Count==0?"共 0 条记录":$"第 {page+1} / {(display.Count+49)/50} 页 · 共 {display.Count} 条（每页 50 条）";
            rangeNote.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Ui.MutedInk;
            rangeNote.Text=mode=="detail"?"总时长为软件全部有效累计。选择记录后可编辑备注，CSV 同步导出。":
                (mode=="lifetime"?"累计包含全部历史。":"总时长为全部有效累计。")+"多次计时的汇总行备注留空，请到“会话明细”查看或编辑。";
        }
        catch(Exception ex)
        {
            // 日期变更即时查询，用固定的内联区域提示错误，保留输入，不弹出重复对话框。
            grid.Rows.Clear();empty.Text=ex.Message;empty.Visible=true;empty.BringToFront();pageLabel.Text="请调整筛选条件后重试";
            rangeNote.Text=ex.Message;rangeNote.ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Color.Firebrick;
            previous.Enabled=false;next.Enabled=false;export.Enabled=false;
            UpdateNoteButton();
        }
    }
    private void ExportCurrent()
    {
        if(mode!="lifetime"&&from.Value.Date>to.Value.Date){RefreshRows();return;}
        try
        {
            var name=mode=="lifetime"?"累计汇总":mode=="daily"?"每日汇总":"会话明细";
            using var dialog=new SaveFileDialog { Filter="CSV 文件 (*.csv)|*.csv",FileName=name+".csv",OverwritePrompt=true };
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;var id=(apps.SelectedItem as TrackedApp)?.Id;
            if(mode=="lifetime")Csv.ExportLifetime(dialog.FileName,snapshot(),id);
            else Csv.Export(dialog.FileName,snapshot(),mode=="daily",DateOnly.FromDateTime(from.Value),DateOnly.FromDateTime(to.Value),id);
            pageLabel.Text="导出完成："+dialog.FileName;
        }
        catch(Exception ex){Ui.Error(this,ex);}
    }
}
