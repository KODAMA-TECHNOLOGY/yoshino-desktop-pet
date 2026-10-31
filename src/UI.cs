using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using Forms=System.Windows.Forms;

namespace YoshinoPet {
public static class Theme {
    public static Brush Bg=new SolidColorBrush(Color.FromRgb(23,25,34)),Card=new SolidColorBrush(Color.FromRgb(34,37,49)),Ink=new SolidColorBrush(Color.FromRgb(236,238,245)),Muted=new SolidColorBrush(Color.FromRgb(160,169,190)),Accent=new SolidColorBrush(Color.FromRgb(112,88,166));
    public static TextBlock Text(string text,double size=13,Brush color=null) {return new TextBlock {Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)};}
    public static Button Button(string text,Action action) {var b=new Button {Content=text,Padding=new Thickness(12,6,12,6),Margin=new Thickness(3),Background=Card,Foreground=Ink,BorderBrush=new SolidColorBrush(Color.FromRgb(72,77,97)),Cursor=Cursors.Hand};b.Click+=(s,e)=>action();return b;}
    public static Border Box(UIElement child) {return new Border {Child=child,Background=Card,CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,5,0,5)};}
    public static TextBox Edit(string text="") {return new TextBox {Text=text??"",Background=Bg,Foreground=Ink,BorderBrush=Muted,Padding=new Thickness(8),Margin=new Thickness(0,5,0,10),FontSize=13};}
    public static Window Window(string title,int width,int height) {return new Window {Title=title,Width=width,Height=height,Background=Bg,Foreground=Ink,FontFamily=new FontFamily("Microsoft YaHei UI"),WindowStartupLocation=WindowStartupLocation.CenterScreen};}
    public static void Install(Application app) {
        const string xaml="<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Style TargetType='{x:Type TabItem}'><Setter Property='Foreground' Value='#ECEEF5'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TabItem}'><Border x:Name='tab' Background='#222531' BorderBrush='#484D61' BorderThickness='1' CornerRadius='5,5,0,0' Padding='16,8' Margin='0,0,3,0'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='#7058A6'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tab' Property='BorderBrush' Value='#A4ACBD'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></ResourceDictionary>";
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
    }
}

public sealed class Companion {
    public readonly Settings Settings;public readonly CodexClient Client=new CodexClient();public readonly SessionMonitor Monitor=new SessionMonitor();
    public readonly Quota Quota=new Quota();public readonly ActivityBrief Activity=new ActivityBrief();
    public List<PetTask> Tasks=new List<PetTask>();public string Connection="正在连接 Codex…";public bool Closing,Connecting,ManualHidden;
    public PetWindow Pet;public SettingsWindow Panel;public BriefWindow Brief;Forms.NotifyIcon tray;
    readonly Dispatcher ui=Dispatcher.CurrentDispatcher;readonly DispatcherTimer pollTimer=new DispatcherTimer(),quotaTimer=new DispatcherTimer();
    bool polling,handoff,quotaReading;DateTime grace=DateTime.UtcNow.AddSeconds(6);public IntPtr LastOtherWindow;
    public Companion(Settings settings) {
        Settings=settings;
        Client.Notification+=(method,p)=>Post(()=>{if(method=="account/rateLimits/updated"){Quota.Raw=p;Quota.UpdatedUtc=DateTime.UtcNow;Quota.Error="";}});
        Client.ServerRequest+=async msg=>{try {await Client.ReplyError(J.Get(msg,"id"),"请在 Codex 窗口处理交互。");}catch {}};
        Client.Disconnected+=message=>Post(()=>{Connection="Codex 已断开："+J.Short(message,100);if(Panel!=null)Panel.RefreshConnection();});
    }
    public void Start(bool noConnect=false) {
        Pet=new PetWindow(this);Brief=new BriefWindow(this);Panel=new SettingsWindow(this);CreateTray();Pet.Show();
        pollTimer.Interval=TimeSpan.FromSeconds(1);pollTimer.Tick+=async(s,e)=>await Poll();pollTimer.Start();
        quotaTimer.Interval=TimeSpan.FromSeconds(60);quotaTimer.Tick+=async(s,e)=>await RefreshQuota();quotaTimer.Start();
        if(!noConnect)Connect();
    }
    void Post(Action a) {if(!Closing)ui.BeginInvoke(a);}
    public async void Connect() {
        if(Connecting||Client.Connected)return;Connecting=true;Connection="正在连接 Codex…";Panel.RefreshConnection();
        try {await Client.Start(Settings);var account=await Client.Call("account/read",J.Obj("refreshToken",false));
            Connection=J.Get(account,"account")==null?"Codex 尚未登录；请先登录 Codex":"已连接 Codex · "+J.S(account,"account","planType");
            await Poll();await RefreshQuota();
        }catch(Exception e) {Connection="连接失败："+e.Message;Paths.Log(Connection);Client.Dispose();}
        finally {Connecting=false;if(!Closing)Panel.RefreshConnection();}
    }
    public async Task RefreshQuota() {
        if(!Client.Connected||quotaReading)return;quotaReading=true;
        try {Quota.Raw=await Client.Call("account/rateLimits/read",J.Obj(),20000);Quota.UpdatedUtc=DateTime.UtcNow;Quota.Error="";}
        catch(Exception e){Quota.Error=e.Message;Paths.Log("额度刷新: "+e.Message);}
        finally {quotaReading=false;}
    }
    public async Task Poll() {
        if(polling||!Client.Connected)return;polling=true;
        try {var tasks=await Monitor.Poll(Client);Tasks=tasks.Where(t=>!Settings.IgnoredThreads.Contains(t.Id)).OrderByDescending(t=>t.LastUtc).ToList();Activity.Observe(Tasks,DateTime.UtcNow);}
        catch(Exception e){Monitor.Error=e.Message;Paths.Log("任务扫描: "+e.Message);}
        finally {polling=false;}
    }
    public bool Busy {get {return Tasks.Any(t=>t.Busy);}}
    public bool HandoffBusy {get {return handoff;}}
    public bool Unknown {get {return Tasks.Any(t=>t.State=="unknown");}}
    public bool Fresh {get {return (DateTime.UtcNow-Monitor.LastSuccess).TotalSeconds<20&&Monitor.Error.Length==0;}}
    public PetTask Featured {get {return Tasks.Where(t=>t.Busy).OrderByDescending(t=>t.State=="waiting").ThenByDescending(t=>t.LastUtc).FirstOrDefault()??Tasks.FirstOrDefault();}}
    public bool Grace {get {return DateTime.UtcNow<grace||(Settings.ShowBrief&&Activity.Visible(DateTime.UtcNow));}}
    public void Wake() {ManualHidden=false;grace=DateTime.UtcNow.AddSeconds(12);Pet.Show();}
    public void Quick() {if(handoff)return;try {Directory.CreateDirectory(Settings.Workspace);Native.Open(Native.NewLink("",Settings.Workspace));}catch(Exception e){ShowError(e.Message);}}
    public void OpenSettings() {Wake();Panel.RefreshConnection();Panel.WindowState=WindowState.Normal;Panel.Show();Panel.Activate();}
    public void Toggle() {if(ManualHidden){Wake();}else {ManualHidden=true;Pet.Hide();Brief.Hide();}}
    public void Capture() {
        var h=Native.GetForegroundWindow();if(Native.IsOurs(h)||h==IntPtr.Zero)h=LastOtherWindow;CaptureTarget(h);
    }
    internal async void CaptureTarget(IntPtr h) {
        if(handoff)return;handoff=true;
        try {
            Pet.Hide();Brief.Hide();Panel.Hide();await Task.Delay(120);
            var image=Native.Capture(h);await Native.CopyImage(image);
            Directory.CreateDirectory(Settings.Workspace);Native.Open(Native.NewLink("",Settings.Workspace));
            bool pasted=await Native.PasteIntoCodex();
            if(!pasted)ShowError("截图已复制。Codex 未及时获得焦点，请在新 Work 输入框按 Ctrl+V 粘贴。");
        }catch(Exception e){ShowError(e.Message);}
        finally {handoff=false;Wake();}
    }
    void CreateTray() {
        tray=new Forms.NotifyIcon {Text="芳乃 · Codex 桌宠",Visible=true,Icon=MakeIcon()};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("新的 Work",null,(s,e)=>Quick());menu.Items.Add("截图并新建 Work",null,(s,e)=>Capture());
        menu.Items.Add("显示 / 隐藏芳乃",null,(s,e)=>Toggle());menu.Items.Add("设置",null,(s,e)=>OpenSettings());menu.Items.Add("退出",null,(s,e)=>Quit());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Quick();
    }
    static System.Drawing.Icon MakeIcon() {try {using(var atlas=new System.Drawing.Bitmap(Paths.Atlas))using(var icon=new System.Drawing.Bitmap(64,64))using(var g=System.Drawing.Graphics.FromImage(icon)){g.DrawImage(atlas,new System.Drawing.Rectangle(0,0,64,64),new System.Drawing.Rectangle(0,0,192,208),System.Drawing.GraphicsUnit.Pixel);var h=icon.GetHicon();try{return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();}finally{Native.DestroyIcon(h);}}}catch{return System.Drawing.SystemIcons.Application;}}
    public void ShowError(string text) {Paths.Log(text);if(tray!=null){tray.BalloonTipTitle="芳乃";tray.BalloonTipText=J.Short(text,220);tray.ShowBalloonTip(5000);}}
    public void SaveSettings() {Settings.Save();Pet.SetSize();Pet.RegisterKeys();}
    public void Quit() {Settings.Left=Pet.Left;Settings.Top=Pet.Top;Settings.Save();ShutdownTest();}
    public void ShutdownTest() {Closing=true;pollTimer.Stop();quotaTimer.Stop();if(tray!=null)tray.Dispose();Client.Dispose();Application.Current.Shutdown();}
}
public sealed class PetWindow : Window {
    readonly Companion app;readonly Image image=new Image();readonly SpriteArt art;readonly BitmapSource[][] rows;
    readonly ScaleTransform poseScale=new ScaleTransform(1,1);readonly RotateTransform poseTilt=new RotateTransform();readonly TranslateTransform poseMove=new TranslateTransform();readonly HeadLook headLook;
    Native.POINT lastCursor;bool cursorSeen;DateTime cursorMoved=DateTime.MinValue;
    public int GazeDirection {get;private set;}internal BitmapSource CurrentFrame {get {return image.Source as BitmapSource;}}
    readonly DispatcherTimer timer=new DispatcherTimer();HwndSource source;int frame,currentRow,lastRow=-1;DateTime nextFrame=DateTime.MinValue;
    Point down;bool dragging;readonly int[][] durations={new[]{280,110,110,140,140,320},Enumerable.Repeat(120,8).ToArray(),Enumerable.Repeat(120,8).ToArray(),new[]{140,140,140,280},new[]{140,140,140,140,280},Enumerable.Repeat(140,8).ToArray(),Enumerable.Repeat(150,6).ToArray(),Enumerable.Repeat(120,6).ToArray(),Enumerable.Repeat(150,6).ToArray()};
    public PetWindow(Companion a) {
        app=a;Title="芳乃桌宠";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;ResizeMode=ResizeMode.NoResize;Content=image;
        art=SpriteArt.Load();rows=art.Rows;GazeDirection=4;if(art.HighResolution)headLook=new HeadLook(art.Keys[0]);
        if(art.HighResolution)durations[0]=new[]{2600,120,80,110,90,600};
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);UseLayoutRounding=true;image.SnapsToDevicePixels=true;
        var transforms=new TransformGroup();transforms.Children.Add(poseScale);transforms.Children.Add(poseTilt);transforms.Children.Add(poseMove);image.RenderTransform=transforms;image.RenderTransformOrigin=new Point(0.5,0.95);
        image.Stretch=Stretch.Fill;image.Source=rows[0][0];Icon=rows[0][0];SetSize();
        var area=SystemParameters.WorkArea;Left=a.Settings.Left<0?area.Right-Width-25:a.Settings.Left;Top=a.Settings.Top<0?area.Bottom-Height-15:a.Settings.Top;Clamp();
        SourceInitialized+=(s,e)=>{source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook(Hook);RegisterKeys();};
        MouseLeftButtonDown+=(s,e)=>{down=e.GetPosition(this);dragging=false;};
        MouseMove+=(s,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&!dragging&&(e.GetPosition(this)-down).Length>5){dragging=true;currentRow=Mouse.GetPosition(this).X>down.X?1:2;try {DragMove();}catch {}currentRow=0;Clamp();app.Settings.Left=Left;app.Settings.Top=Top;app.Settings.Save();}};
        MouseLeftButtonUp+=(s,e)=>{if(!dragging)app.Quick();dragging=false;};
        MouseRightButtonUp+=(s,e)=>{var m=new ContextMenu();foreach(var pair in new[]{new KeyValuePair<string,Action>("新的 Work",app.Quick),new KeyValuePair<string,Action>("截图",app.Capture),new KeyValuePair<string,Action>("隐藏",app.Toggle),new KeyValuePair<string,Action>("设置",app.OpenSettings)}){var item=new MenuItem {Header=pair.Key};var action=pair.Value;item.Click+=(x,y)=>action();m.Items.Add(item);}m.IsOpen=true;};
        timer.Interval=TimeSpan.FromMilliseconds(65);timer.Tick+=(s,e)=>Tick();timer.Start();Closing+=(s,e)=>{for(int i=1;i<=3;i++)Native.UnregisterHotKey(new WindowInteropHelper(this).Handle,i);};
    }
    public void SetSize() {Width=app.Settings.PetSize;Height=Width*208.0/192;}
    public void RegisterKeys() {
        if(source==null)return;var handle=new WindowInteropHelper(this).Handle;for(int i=1;i<=3;i++)Native.UnregisterHotKey(handle,i);
        var keys=new[]{app.Settings.QuickHotkey,app.Settings.CaptureHotkey,app.Settings.ToggleHotkey};var failed=new List<string>();
        for(int i=0;i<keys.Length;i++)if(!Native.RegisterHotKey(handle,i+1,0x4000|0x0001|0x0002,(uint)keys[i]))failed.Add(i==0?"快速输入":i==1?"截图":"显隐");
        if(failed.Count>0)app.ShowError("快捷键被占用："+string.Join("、",failed)+"。可从托盘操作或在设置更换键位。");
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) {if(msg==Program.WakeMessage){handled=true;app.Quick();}if(msg==0x0312){handled=true;int id=w.ToInt32();if(id==1)app.Quick();else if(id==2)app.Capture();else app.Toggle();}return IntPtr.Zero;}
    void Clamp() {var screens=Forms.Screen.AllScreens;var h=new WindowInteropHelper(this).Handle;var dpi=PresentationSource.FromVisual(this);double scale=dpi==null?1:dpi.CompositionTarget.TransformToDevice.M11;
        var screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)(Left*scale),(int)(Top*scale))).WorkingArea;Left=Math.Max(screen.Left/scale,Math.Min(Left,screen.Right/scale-Width));Top=Math.Max(screen.Top/scale,Math.Min(Top,screen.Bottom/scale-Height));}
    void Tick() {
        var fore=Native.GetForegroundWindow();if(fore!=IntPtr.Zero&&!Native.IsOurs(fore))app.LastOtherWindow=fore;
        if(app.HandoffBusy){if(IsVisible)Hide();if(app.Brief.IsVisible)app.Brief.Hide();return;}
        bool hide=VisibilityPolicy.ShouldHide(app.ManualHidden,app.Settings.AutoHide,Native.Maximized(app.LastOtherWindow),app.Busy,app.Unknown,app.Fresh,app.Panel.IsVisible&&app.Panel.WindowState!=WindowState.Minimized,app.Grace);
        if(hide){if(IsVisible)Hide();if(app.Brief.IsVisible)app.Brief.Hide();return;}if(!IsVisible)Show();
        var task=app.Featured;int row=dragging?currentRow:task!=null&&task.State=="waiting"?6:task!=null&&task.State=="running"?7:task!=null&&task.CompletedUtc>DateTime.UtcNow.AddSeconds(-10)?(task.State=="failed"?5:8):0;
        bool looking=Look(row);
        if(looking){lastRow=-1;ResetMotion();}
        else {
            if(row==0&&!SystemParameters.ClientAreaAnimation)image.Source=rows[0][0];
            else {if(lastRow!=row){frame=0;lastRow=row;nextFrame=DateTime.MinValue;}if(DateTime.UtcNow>=nextFrame){image.Source=rows[row][frame];nextFrame=DateTime.UtcNow.AddMilliseconds(durations[row][frame]);frame=(frame+1)%rows[row].Length;}}
            if(art.HighResolution)SmoothMotion(row);
        }
        app.Brief.Update();
    }
    void ResetMotion() {poseScale.ScaleX=poseScale.ScaleY=1;poseTilt.Angle=0;poseMove.X=poseMove.Y=0;}
    void SmoothMotion(int row) {
        if(!SystemParameters.ClientAreaAnimation||row==0){ResetMotion();return;}
        double seconds=DateTime.UtcNow.TimeOfDay.TotalSeconds,breath=Math.Sin(seconds*2.2),step=Math.Sin(seconds*8);
        poseScale.ScaleY=1+breath*0.006;poseTilt.Angle=0;poseMove.X=0;poseMove.Y=breath*0.65;
        if(row==1||row==2){poseTilt.Angle+=step*1.5;poseMove.Y-=Math.Abs(step)*2.2;}
        else if(row==3){poseTilt.Angle+=Math.Sin(seconds*5)*1.5;}
        else if(row==4){poseMove.Y-=Math.Abs(Math.Sin(seconds*3))*7;}
        else if(row==5){poseTilt.Angle+=Math.Sin(seconds*4)*0.8;}
        else if(row==6){poseTilt.Angle+=Math.Sin(seconds*1.5)*0.8;}
        else if(row==7){poseMove.Y+=Math.Sin(seconds*3)*0.7;}
        else if(row==8){poseTilt.Angle+=Math.Sin(seconds*3)*1;poseMove.Y-=Math.Abs(Math.Sin(seconds*3))*1.8;}
    }
    bool Look(int row) {
        Native.POINT p;if(!Native.GetCursorPos(out p)||dragging)return false;
        if(!cursorSeen||Math.Abs(p.X-lastCursor.X)+Math.Abs(p.Y-lastCursor.Y)>2){cursorMoved=DateTime.UtcNow;lastCursor=p;cursorSeen=true;}
        GazeDirection=4;if(!SystemParameters.ClientAreaAnimation||DateTime.UtcNow-cursorMoved>TimeSpan.FromSeconds(3))return false;
        var center=PointToScreen(new Point(ActualWidth/2,ActualHeight*0.35));double dx=p.X-center.X,dy=p.Y-center.Y;
        int direction=HeadLook.Direction(dx,dy,18*ActualWidth/210);if(direction==4)return false;
        GazeDirection=direction;
        if(headLook!=null)image.Source=headLook.Frame(direction);
        else if(row==0){double deg=(Math.Atan2(dx,-dy)*180/Math.PI+360)%360;int index=((int)Math.Round(deg/22.5))%16;image.Source=rows[9+index/8][index%8];}
        else return false;
        return true;
    }
}
public sealed class BriefWindow : Window {
    readonly Companion app;readonly TextBlock heading=Theme.Text("芳乃",12),body=Theme.Text("",12),quota=Theme.Text("",11,Theme.Muted);
    public bool ShowingQuota {get;private set;}public bool ShowingActivity {get;private set;}
    public BriefWindow(Companion a) {app=a;Title="芳乃提示";Width=310;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        var stack=new StackPanel();stack.Children.Add(heading);stack.Children.Add(body);stack.Children.Add(quota);Content=new Border {Background=new SolidColorBrush(Color.FromArgb(240,28,30,40)),CornerRadius=new CornerRadius(12),Padding=new Thickness(12),Child=stack};
        MouseLeftButtonUp+=(s,e)=>{var t=app.Activity.Current;if(ShowingActivity&&t!=null)Native.Open(Native.ThreadLink(t.Id));};
        a.Pet.MouseEnter+=async(s,e)=>{if(a.Settings.ShowQuota&&(DateTime.UtcNow-a.Quota.UpdatedUtc).TotalSeconds>15)await a.RefreshQuota();};
    }
    public void Update() {UpdateHover(app.Pet.IsMouseOver||(IsVisible&&IsMouseOver));}
    internal void UpdateHover(bool hover) {
        ShowingActivity=app.Settings.ShowBrief&&app.Activity.Visible(DateTime.UtcNow);ShowingQuota=app.Settings.ShowQuota&&hover;
        if(!ShowingActivity&&!ShowingQuota){Hide();return;}
        var t=app.Activity.Current;heading.Text=ShowingActivity?t.StateLabel+" · "+J.Short(t.Name,34):"";heading.Visibility=ShowingActivity?Visibility.Visible:Visibility.Collapsed;
        body.Text=ShowingActivity?(t.Actions.Length>0?string.Join("\n",t.Actions):J.Short(t.Summary.Length>0?t.Summary:t.StateLabel,150)):"";body.Visibility=ShowingActivity?Visibility.Visible:Visibility.Collapsed;
        quota.Text=app.Quota.Raw==null?app.Quota.Compact:app.Quota.Details;quota.Visibility=ShowingQuota?Visibility.Visible:Visibility.Collapsed;
        if(!IsVisible)Show();UpdateLayout();var p=app.Pet;var area=Forms.Screen.FromHandle(new WindowInteropHelper(p).Handle).WorkingArea;var matrix=PresentationSource.FromVisual(p).CompositionTarget.TransformToDevice;double s=matrix.M11;
        Left=Math.Max(area.Left/s,Math.Min(p.Left+p.Width/2-Width/2,area.Right/s-Width));Top=p.Top-ActualHeight-8;if(Top<area.Top/s)Top=Math.Min(p.Top+p.Height+5,area.Bottom/s-ActualHeight);
    }
}
public sealed class SettingsWindow : Window {
    readonly Companion app;readonly TextBlock connection=Theme.Text("",12,Theme.Muted),notice=Theme.Text("",12);
    public SettingsWindow(Companion a) {
        app=a;Title="芳乃 · 设置";Width=480;Height=670;Background=Theme.Bg;Foreground=Theme.Ink;FontFamily=new FontFamily("Microsoft YaHei UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var stack=new StackPanel {Margin=new Thickness(22)};stack.Children.Add(Theme.Text("芳乃 · 设置",22));stack.Children.Add(connection);
        CheckBox auto=Check("空闲且前台窗口最大化时自动隐藏",a.Settings.AutoHide),brief=Check("有新任务动作时显示 6 秒简报",a.Settings.ShowBrief),quota=Check("鼠标悬停时显示额度与 credits",a.Settings.ShowQuota);
        stack.Children.Add(auto);stack.Children.Add(brief);stack.Children.Add(quota);
        stack.Children.Add(Theme.Text("桌宠大小"));var size=new Slider {Minimum=120,Maximum=380,Value=a.Settings.PetSize,TickFrequency=10,IsSnapToTickEnabled=true,Margin=new Thickness(0,5,0,12)};stack.Children.Add(size);
        stack.Children.Add(Theme.Text("新 Work 工作目录"));var cwd=Theme.Edit(a.Settings.Workspace);stack.Children.Add(cwd);stack.Children.Add(Theme.Button("选择工作目录",()=>{using(var f=new Forms.FolderBrowserDialog()){f.SelectedPath=cwd.Text;if(f.ShowDialog()==Forms.DialogResult.OK)cwd.Text=f.SelectedPath;}}));
        stack.Children.Add(Theme.Text("Codex CLI（用于读取状态与额度，留空自动查找）"));var cli=Theme.Edit(a.Settings.CodexPath);stack.Children.Add(cli);
        stack.Children.Add(Theme.Text("快捷键（Ctrl+Alt+以下键）",15));
        TextBox quick=Theme.Edit(KeyName(a.Settings.QuickHotkey)),capture=Theme.Edit(KeyName(a.Settings.CaptureHotkey)),toggle=Theme.Edit(KeyName(a.Settings.ToggleHotkey));
        stack.Children.Add(Theme.Text("新 Work"));stack.Children.Add(quick);stack.Children.Add(Theme.Text("截图并新建 Work"));stack.Children.Add(capture);stack.Children.Add(Theme.Text("显示 / 隐藏"));stack.Children.Add(toggle);
        var startup=Check("登录 Windows 时启动",a.Settings.StartWithWindows);stack.Children.Add(startup);
        stack.Children.Add(Theme.Button("保存设置",()=>{try {
            if(!Path.IsPathRooted(cwd.Text))throw new Exception("工作目录需要绝对路径。");
            int q=ParseKey(quick.Text),c=ParseKey(capture.Text),t=ParseKey(toggle.Text);if(q==c||q==t||c==t)throw new Exception("三个快捷键应使用不同键位。");
            a.Settings.AutoHide=auto.IsChecked==true;a.Settings.ShowBrief=brief.IsChecked==true;a.Settings.ShowQuota=quota.IsChecked==true;a.Settings.PetSize=(int)size.Value;a.Settings.Workspace=Path.GetFullPath(cwd.Text);a.Settings.CodexPath=cli.Text.Trim();a.Settings.QuickHotkey=q;a.Settings.CaptureHotkey=c;a.Settings.ToggleHotkey=t;a.Settings.StartWithWindows=startup.IsChecked==true;
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(a.Settings.StartWithWindows)key.SetValue("YoshinoPet","\""+System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName+"\"");else key.DeleteValue("YoshinoPet",false);}
            a.SaveSettings();notice.Text="设置已保存。CLI 路径修改后请重启桌宠。";
        }catch(Exception e){notice.Text=e.Message;}}));
        stack.Children.Add(Theme.Button("重新连接 / 刷新",async()=>{if(!a.Client.Connected)a.Connect();else await a.RefreshQuota();}));stack.Children.Add(notice);
        Content=new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Closing+=(s,e)=>{if(!app.Closing){e.Cancel=true;Hide();}};
    }
    public void RefreshConnection() {connection.Text=app.Connection;}
    static CheckBox Check(string text,bool value) {return new CheckBox {Content=text,IsChecked=value,Foreground=Theme.Ink,Margin=new Thickness(0,7,0,7)};}
    static string KeyName(int key) {return key==0x20?"Space":((Forms.Keys)key).ToString();}
    static int ParseKey(string text) {Forms.Keys key;if(!Enum.TryParse<Forms.Keys>(text.Trim(),true,out key)||(int)key<1||(int)key>255)throw new Exception("快捷键请输入 Space、S、Y 或 F1 等单个键名。");return (int)key;}
}
}
