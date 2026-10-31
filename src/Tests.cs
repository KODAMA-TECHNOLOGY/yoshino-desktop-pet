using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace YoshinoPet {
public static class TestRunner {
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    static readonly List<object> results=new List<object>();
    static void Check(string name,bool ok,string detail="") {results.Add(J.Obj("test",name,"passed",ok,"detail",detail));}
    static bool AllPassed() {return results.All(r=>J.Text(J.Get(r,"passed"))=="True");}
    static void Save(string path) {Paths.AtomicWrite(path,J.Encode(J.Obj("passed",AllPassed(),"checks",results.ToArray(),"capturedAt",DateTimeOffset.Now.ToString("o"))));}
    public static async Task<int> Run(string folder,bool integration) {
        folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);results.Clear();
        try {
            Check("idle maximized hides",VisibilityPolicy.ShouldHide(false,true,true,false,false,true,false,false));
            Check("active task overrides maximized",!VisibilityPolicy.ShouldHide(false,true,true,true,false,true,false,false));
            Check("unknown task keeps visible",!VisibilityPolicy.ShouldHide(false,true,true,false,true,true,false,false));
            Check("stale monitor keeps visible",!VisibilityPolicy.ShouldHide(false,true,true,false,false,false,false,false));
            Check("settings keeps visible",!VisibilityPolicy.ShouldHide(false,true,true,false,false,true,true,false));
            Check("completion grace keeps visible",!VisibilityPolicy.ShouldHide(false,true,true,false,false,true,false,true));
            Check("manual hide respected",VisibilityPolicy.ShouldHide(true,false,false,true,false,true,true,true));
            Check("restored foreground stays visible",!VisibilityPolicy.ShouldHide(false,true,false,false,false,true,false,false));
            Check("waiting is busy",new PetTask {State="waiting"}.Busy);
            var state=new RolloutState();state.Apply(Event("2026-10-19T10:00:00Z","task_started","A"));state.Apply(Event("2026-10-19T10:00:01Z","task_started","B"));state.Apply(Event("2026-10-19T10:00:02Z","task_complete","A"));
            Check("late old completion cannot finish new turn",state.Task.State=="running"&&state.Task.TurnId=="B");state.Apply(Event("2026-10-19T10:00:03Z","task_complete","B"));Check("matching completion finishes",state.Task.State=="completed");
            state.Apply(Event("2026-10-19T09:00:00Z","task_started","old"));Check("old tail rescan cannot revive completed turn",state.Task.State=="completed");
            var quota=new Quota {Raw=J.Obj("rateLimits",J.Obj("primary",J.Obj("usedPercent",45,"windowDurationMins",300),"secondary",J.Obj("usedPercent",10,"windowDurationMins",10080),"credits",J.Obj("balance","0")))};
            Check("quota remaining and credits distinct",quota.Compact.Contains("55%")&&quota.Compact.Contains("90%")&&quota.Compact.Contains("credits 0"));
            Check("missing quota is not zero",new Quota().Compact.Contains("读取中"));
            var now=DateTime.UtcNow;var activity=new ActivityBrief();var t=new PetTask {Id="a",State="running",TurnId="1",Summary="准备",LastUtc=now};
            activity.Observe(new[]{t},now);Check("initial history is silent",!activity.Visible(now));
            t.LastUtc=now.AddSeconds(1);activity.Observe(new[]{t},now.AddSeconds(1));Check("timestamp or token usage alone is silent",!activity.Visible(now.AddSeconds(1)));
            t.ActivityKey="tool1";t.Summary="使用工具";activity.Observe(new[]{t},now.AddSeconds(2));Check("new tool action shows a brief",activity.Visible(now.AddSeconds(2)));
            activity.Observe(new[]{t},now.AddSeconds(7));Check("unchanged running task cannot extend brief",!activity.Visible(now.AddSeconds(9)));
            t.ActivityKey="tool2";activity.Observe(new[]{t},now.AddSeconds(10));Check("repeated tool text still shows new action",activity.Visible(now.AddSeconds(10)));
            t.State="completed";activity.Observe(new[]{t},now.AddSeconds(11));Check("completion shows briefly",activity.Visible(now.AddSeconds(11)));
            activity.Observe(new[]{t},now.AddSeconds(18));Check("completed task disappears after six seconds",!activity.Visible(now.AddSeconds(18)));
            var old=new PetTask {Id="old",State="completed",LastUtc=now.AddDays(-1)};activity.Observe(new[]{t,old},now.AddSeconds(19));Check("newly discovered old history stays silent",!activity.Visible(now.AddSeconds(19)));
            var next=new PetTask {Id="b",State="running",LastUtc=now.AddSeconds(20)};activity.Observe(new[]{t,next},now.AddSeconds(20));Check("new running task announces itself",activity.Current.Id=="b"&&activity.Visible(now.AddSeconds(20)));
            var toolState=new RolloutState();toolState.Apply(J.Obj("timestamp",now.ToString("o"),"type","response_item","payload",J.Obj("type","function_call","name","exec_command","call_id","one")));
            Check("tool call is recognized as new action",toolState.Task.ActivityKey.Contains("one")&&toolState.Task.Actions.Any(x=>x.Contains("exec_command")));
            var cmd=ActionParser.Parse("exec_command",J.Encode(J.Obj("cmd","dotnet build")),"cmd");Check("direct command text is shown",cmd.Count==1&&cmd[0].Text=="dotnet build"&&cmd[0].Display.Contains("执行中"));
            string patch="*** Begin Patch\n*** Update File: C:/demo/Core.cs\n@@\n-old\n+one\n+two\n*** End Patch";
            var edit=ActionParser.Parse("apply_patch",patch,"edit");Check("patch shows filename and actual delta",edit.Count==1&&edit[0].Text=="Core.cs"&&edit[0].Added==2&&edit[0].Removed==1);
            string orchestration="text(await tools.exec_command({cmd:\"rg -n \\\"hello\\\" src\"}));text(await tools.apply_patch("+J.Encode(patch)+"));";
            var nested=ActionParser.Parse("exec",orchestration,"nested");Check("nested execution and patch are recognized",nested.Count==2&&nested[0].Text=="rg -n \"hello\" src"&&nested[1].Added==2);
            Check("nonzero command result is failed",ActionParser.Failed("Process exited with code 1")&&!ActionParser.Failed("Process exited with code 0"));
            var failedOutput=new[]{J.Obj("type","text","text",J.Encode(J.Obj("exit_code",1,"output","编译失败")))};Check("structured nested result preserves failures",ActionParser.Failed(failedOutput));
            var call=J.Obj("timestamp",now.ToString("o"),"type","response_item","payload",J.Obj("type","function_call","name","exec_command","call_id","cmd","arguments",J.Encode(J.Obj("cmd","dotnet build"))));
            var output=J.Obj("timestamp",now.AddSeconds(1).ToString("o"),"type","response_item","payload",J.Obj("type","function_call_output","call_id","cmd","output","Process exited with code 0"));
            var tracking=new RolloutState();tracking.Apply(call);tracking.Apply(output);Check("returned command updates to executed",tracking.Task.Actions.Single().Contains("已执行 dotnet build"));
            string key=tracking.Task.ActivityKey;tracking.Apply(call);tracking.Apply(output);Check("rescanning tool records does not duplicate actions",tracking.Task.Actions.Length==1&&tracking.Task.ActivityKey==key);
            var modern=new RolloutState {UsesTurnApi=true};modern.Apply(Event(now.AddSeconds(-10).ToString("o"),"turn_aborted","old"));
            var commandItem=J.Obj("type","CommandExecution","id","modern-command","command",new[]{"pwsh.exe","-Command","dotnet build"},"status","completed","exit_code",0);
            var modernCommand=ItemRecord(now,"live",commandItem);modern.Apply(modernCommand);
            Check("typed item tail recovers a live turn without its start marker",modern.Task.State=="running"&&modern.Task.TurnId=="live"&&!modern.UsesTurnApi);
            Check("typed command shows actual command and completion",modern.Task.Actions.Single().Contains("已执行 dotnet build"));
            modern.Apply(modernCommand);Check("typed item rescan is silent and deduplicated",modern.Task.Actions.Length==1);
            modern.Apply(ItemRecord(now.AddSeconds(1),"live",J.Obj("type","FileChange","id","modern-edit","status","completed","changes",J.Obj("C:/demo/New.cs",J.Obj("unified_diff","--- a/New.cs\n+++ b/New.cs\n@@\n-old\n+one\n+two\n")))));
            Check("typed file change reports path and real line delta",modern.Task.Actions.Last().Contains("已编辑 New.cs  +2 -1"));
            modern.Apply(ItemRecord(now.AddSeconds(2),"live",J.Obj("type","AgentMessage","id","modern-message","phase","commentary","content",new[]{J.Obj("text","正在检查当前任务")})));
            Check("typed commentary updates the brief",modern.Task.Summary=="正在检查当前任务");
            modern.Apply(Event(now.AddSeconds(3).ToString("o"),"turn_completed","live"));modern.Apply(modernCommand);
            Check("old typed item cannot revive a completed turn",modern.Task.State=="completed");
            var reasoning=new RolloutState();reasoning.Apply(ItemRecord(now,"private",J.Obj("type","Reasoning","id","reason","raw_content",new[]{"private reasoning"})));
            Check("reasoning establishes liveness without exposing its content",reasoning.Task.State=="running"&&reasoning.Task.Summary.Length==0&&reasoning.Task.Actions.Length==0);
            string activeRollout=Environment.GetEnvironmentVariable("YOSHINO_EXPECT_ACTIVE_ROLLOUT");
            if(!string.IsNullOrEmpty(activeRollout)) {
                var actual=new RolloutState();actual.Task.Rollout=activeRollout;actual.Read();
                Check("LOCAL current rollout is running",actual.Task.State=="running","observed="+actual.Task.State);
                Check("LOCAL current typed actions are readable",actual.Task.Actions.Length>0,"actions="+actual.Task.Actions.Length);
                Paths.AtomicWrite(Path.Combine(folder,"live-rollout.json"),J.Encode(J.Obj("state",actual.Task.State,"turnId",actual.Task.TurnId,"actions",actual.Task.Actions,"lastUtc",actual.Task.LastUtc.ToString("o"))));
            }
            var json=J.Encode(J.Obj("text","芳乃 中文😀","path",@"C:\含 空格\截图.png"));Check("UTF8 JSON roundtrip",J.S(J.Parse(json),"text")=="芳乃 中文😀");
            Check("deep link safely encodes prompt",Native.NewLink("hello&other=bad",folder).Contains("hello%26other%3Dbad"));
            using(var bmp=new System.Drawing.Bitmap(Paths.Atlas))Check("73-frame v2 atlas available",bmp.Width==1536&&bmp.Height==2288);
            var art=SpriteArt.Load();Check("HD art is selected",art.HighResolution);
            Check("all 12 HD key drawings exceed 480 pixels",art.Keys.Length==12&&art.Keys.All(k=>k.PixelWidth==576&&k.PixelHeight==624));
            Check("all existing animation slots remain valid",art.Rows.Select(r=>r.Length).SequenceEqual(new[]{6,8,8,4,5,8,6,6,6,8,8}));
            Check("HD alpha bounds retain transparent margins",art.Keys.Select(SpriteArt.AlphaBounds).All(b=>b.X>=8&&b.Y>=8&&b.X+b.Width<=568&&b.Y+b.Height<=616));
            var look=new HeadLook(art.Keys[0]);
            Check("cursor direction covers all eight head poses",new[]{HeadLook.Direction(-100,-100,18),HeadLook.Direction(0,-100,18),HeadLook.Direction(100,-100,18),HeadLook.Direction(-100,0,18),HeadLook.Direction(0,0,18),HeadLook.Direction(100,0,18),HeadLook.Direction(-100,100,18),HeadLook.Direction(0,100,18),HeadLook.Direction(100,100,18)}.SequenceEqual(Enumerable.Range(0,9)));
            Check("distant cursor still points the head",HeadLook.Direction(-2000,-1000,18)==0&&HeadLook.Direction(2000,1000,18)==8);
            Check("direction output is complete with transparent margins",look.Frames.All(k=>k.PixelWidth==576&&k.PixelHeight==624)&&look.Frames.Select(SpriteArt.AlphaBounds).All(b=>b.X>=8&&b.Y>=8&&b.X+b.Width<=568&&b.Y+b.Height<=616));
            Check("generated head direction atlas is loaded",look.UsesDirectionalAtlas,"native cell "+look.SourceCellWidth+"x"+look.SourceCellHeight);
            Check("native direction source cells exceed 480 pixels",look.SourceCellWidth>=480&&look.SourceCellHeight>=480);
            Check("complete drawings change with direction",Enumerable.Range(0,9).Where(i=>i!=4).All(i=>!SamePixels(look.Frame(i),look.Frame(4),0,SpriteArt.FrameHeight)));
            Check("direction drawings retain their own lower body",Enumerable.Range(0,9).Where(i=>i!=4).All(i=>!SamePixels(look.Frame(i),art.Keys[0],350,SpriteArt.FrameHeight)));
            var mockSettings=new Settings {CodexPath=Process.GetCurrentProcess().MainModule.FileName};
            using(var fake=new CodexClient()) {
                int notifications=0;var requestSeen=new TaskCompletionSource<bool>();fake.Notification+=(m,p)=>notifications++;
                fake.ServerRequest+=async msg=>{await fake.Reply(J.Get(msg,"id"),J.Obj("decision","decline"));requestSeen.TrySetResult(true);};
                await fake.Start(mockSettings,"--fake-server");
                var slow=fake.Call("test/slow",J.Obj("text","慢请求"));var fast=fake.Call("test/echo",J.Obj("text","芳乃"));
                var f=await fast;var s=await slow;Check("out-of-order RPCs retain ownership",J.S(f,"text")=="芳乃"&&J.S(s,"text")=="慢请求");
                bool timeout=false;try {await fake.Call("test/hang",J.Obj(),90);}catch(TimeoutException){timeout=true;}Check("RPC timeout completes instead of hanging",timeout);
                var after=await fake.Call("test/echo",J.Obj("text","仍可调用"));Check("timeout does not damage later RPC",J.S(after,"text")=="仍可调用");
                await fake.Call("test/request",J.Obj());await Task.WhenAny(requestSeen.Task,Task.Delay(2000));Check("server requests routed separately",requestSeen.Task.IsCompleted);
                await fake.Call("test/notify",J.Obj());await Task.Delay(80);Check("notifications do not consume RPC responses",notifications>0);
                fake.Dispose();await fake.Start(mockSettings,"--fake-server");var reconnect=await fake.Call("test/echo",J.Obj("text","重连"));Check("old reader cannot close reconnected client",J.S(reconnect,"text")=="重连");
            }
            if(integration){Save(Path.Combine(folder,"unit-results.json"));await Live(folder);}
        }catch(Exception e){Check("unexpected exception",false,e.ToString());}
        Save(Path.Combine(folder,integration?"integration-results.json":"unit-results.json"));return AllPassed()?0:1;
    }
    static object Event(string time,string type,string turn) {return J.Obj("timestamp",time,"type","event_msg","payload",J.Obj("type",type,"turn_id",turn));}
    static object ItemRecord(DateTime time,string turn,object item) {return J.Obj("timestamp",time.ToString("o"),"type","event_msg","payload",J.Obj("type","item_completed","turn_id",turn,"item",item));}
    static async Task Live(string folder) {
        using(var client=new CodexClient()) {
            await client.Start(Settings.Load());var account=await client.Call("account/read",J.Obj("refreshToken",false));Check("LIVE existing login",J.Get(account,"account")!=null);
            try {var limits=await client.Call("account/rateLimits/read",J.Obj(),20000);Check("LIVE quota API",J.Get(limits,"rateLimits")!=null);}
            catch(Exception e){Check("LIVE quota API",false,e.Message);}
            var monitor=new SessionMonitor();var tasks=await monitor.Poll(client);Check("LIVE root task discovery",tasks.Count>0,"root count "+tasks.Count);Check("LIVE task state read",tasks.Any(t=>t.State!="unknown"));
            Check("LIVE local action records parsed",tasks.Any(t=>t.Actions.Length>0),"tasks with actions "+tasks.Count(t=>t.Actions.Length>0));
            string expected=Environment.GetEnvironmentVariable("YOSHINO_EXPECT_ACTIVE_THREAD");if(!string.IsNullOrEmpty(expected))Check("LIVE current active chat is running",tasks.Any(t=>t.Id==expected&&t.State=="running"),"observed="+(tasks.FirstOrDefault(t=>t.Id==expected)??new PetTask()).State);
            Paths.AtomicWrite(Path.Combine(folder,"monitor-diagnostic.json"),J.Encode(tasks.Select(t=>J.Obj("id",t.Id,"path",t.Rollout,"state",t.State,"actions",t.Actions.Length)).ToArray()));
        }
    }
    public static int Ui(string folder) {
        folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);results.Clear();var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Theme.Install(app);
        Companion c=null;Window fixture=null;var prior=Native.GetForegroundWindow();Native.POINT priorCursor;Native.GetCursorPos(out priorCursor);
        app.Startup+=async(s,e)=>{
            try {
                var settings=Settings.Load();settings.AutoHide=false;settings.ShowQuota=true;settings.ShowBrief=true;c=new Companion(settings);c.Start(true);await Task.Delay(120);
                Check("only pet, brief and settings windows exist",app.Windows.Count==3&&app.Windows.Cast<Window>().All(w=>w is PetWindow||w is BriefWindow||w is SettingsWindow));
                c.Brief.UpdateHover(false);Check("no activity and no hover stays hidden",!c.Brief.IsVisible);
                c.Brief.UpdateHover(true);Check("hover shows quota",c.Brief.IsVisible&&c.Brief.ShowingQuota&&!c.Brief.ShowingActivity);Snapshot(c.Brief,Path.Combine(folder,"hover-quota.png"));
                var bubble=(System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)c.Brief.Content).Child;
                Check("quota-only bubble has no visible title",bubble.Children.OfType<System.Windows.Controls.TextBlock>().Count(b=>b.Visibility==Visibility.Visible)==1&&!bubble.Children.OfType<System.Windows.Controls.TextBlock>().Any(b=>b.Visibility==Visibility.Visible&&b.Text.Contains("芳乃")));
                c.Brief.UpdateHover(false);Check("leaving pet hides quota",!c.Brief.IsVisible);
                var t=new PetTask {Id="fixture",State="running",Name="动作显示样例",Summary="使用工具：测试",LastUtc=DateTime.UtcNow,Actions=new[]{"已编辑 Core.cs  +17 -5","已执行 dotnet build","执行中 运行界面验证"}};c.Activity.Observe(new[]{t},DateTime.UtcNow);t.ActivityKey="new";c.Activity.Observe(new[]{t},DateTime.UtcNow);
                c.Brief.UpdateHover(false);Check("new activity shows without quota",c.Brief.IsVisible&&c.Brief.ShowingActivity&&!c.Brief.ShowingQuota);Snapshot(c.Brief,Path.Combine(folder,"activity-brief.png"));
                Check("task activity keeps its heading",bubble.Children.OfType<System.Windows.Controls.TextBlock>().Any(b=>b.Visibility==Visibility.Visible&&b.Text.Contains("动作显示样例")));
                c.Activity.Until=DateTime.UtcNow.AddSeconds(-1);c.Brief.UpdateHover(false);Check("expired activity hides while task runs",!c.Brief.IsVisible);
                c.OpenSettings();await Task.Delay(100);Snapshot(c.Panel,Path.Combine(folder,"settings.png"));c.Panel.Close();Check("settings closes to tray",!c.Panel.IsVisible);
                c.ManualHidden=false;c.Toggle();Check("manual hide",!c.Pet.IsVisible);c.Wake();Check("wake",c.Pet.IsVisible);Snapshot(c.Pet,Path.Combine(folder,"pet.png"));
                if(SystemParameters.ClientAreaAnimation) {
                    c.Tasks=new List<PetTask>{t};var area=SystemParameters.WorkArea;c.Pet.Left=area.Left+area.Width/2-c.Pet.Width/2;c.Pet.Top=area.Top+area.Height/2-c.Pet.Height/2;
                    var center=c.Pet.PointToScreen(new Point(c.Pet.Width/2,c.Pet.Height*0.35));var expectedLook=new HeadLook(SpriteArt.Load().Keys[0]);bool whole=true,allDirections=true;
                    for(int i=0;i<9;i++)if(i!=4) {
                        int dx=i%3-1,dy=i/3-1;bool moved=SetCursorPos((int)center.X+dx*230,(int)center.Y+dy*230);await Task.Delay(180);
                        allDirections&=moved&&c.Pet.GazeDirection==i;
                        Snapshot(c.Pet,Path.Combine(folder,"look-"+i+".png"));
                        whole&=SamePixels(c.Pet.CurrentFrame,expectedLook.Frame(i),0,SpriteArt.FrameHeight);
                    }
                    Check("real cursor drives every head direction during a running task",allDirections);
                    Check("running-task gaze uses each intact full-body drawing",whole);
                    SetCursorPos((int)center.X-600,(int)center.Y-300);await Task.Delay(160);Check("real cursor beyond old 450 pixel cutoff still tracks",c.Pet.GazeDirection==0);
                    await Task.Delay(3200);Check("head returns to task pose after cursor rests",c.Pet.GazeDirection==4);
                    c.Tasks.Clear();
                }
                // Capture and paste only software-owned content. This test never opens Codex or sends a model request.
                bool gotImage=false;var panel=new System.Windows.Controls.StackPanel {Margin=new Thickness(40)};
                panel.Children.Add(new System.Windows.Controls.TextBlock {Text="芳乃截图粘贴测试 YOSHINO-2026",FontSize=28,Foreground=Brushes.White});
                var input=new System.Windows.Controls.TextBox {Margin=new Thickness(0,40,0,0),Height=100,AcceptsReturn=true};panel.Children.Add(input);
                DataObject.AddPastingHandler(input,(sender,args)=>{gotImage=args.DataObject.GetDataPresent(DataFormats.Bitmap)||args.DataObject.GetDataPresent("PNG");args.CancelCommand();});
                input.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Paste,(sender,args)=>{gotImage=Clipboard.ContainsImage();args.Handled=true;},(sender,args)=>{args.CanExecute=true;args.Handled=true;}));
                fixture=new Window {Title="芳乃受控截图与粘贴测试",Width=800,Height=500,Background=new SolidColorBrush(Color.FromRgb(56,61,86)),Content=panel};fixture.Show();fixture.Activate();input.Focus();await Task.Delay(400);
                var handle=new WindowInteropHelper(fixture).Handle;var image=Native.Capture(handle);bool controlled;
                using(var bmp=new System.Drawing.Bitmap(image)){var p=bmp.GetPixel(bmp.Width-50,bmp.Height-50);controlled=p.R==56&&p.G==61&&p.B==86;}
                Check("window capture is controlled fixture",controlled);if(!controlled)throw new Exception("fixture screenshot was not verified");
                File.Copy(image,Path.Combine(folder,"captured-fixture.png"),true);await Native.CopyImage(image);Check("clipboard contains an image",Clipboard.ContainsImage());
                Check("fixture is not mistaken for Codex",!Native.IsCodexWindow(handle));
                Check("wrong foreground target refuses paste",!Native.PasteToForeground(new WindowInteropHelper(c.Pet).Handle));
                fixture.Activate();Native.SetForegroundWindow(handle);input.Focus();await Task.Delay(300);bool foreground=Native.GetForegroundWindow()==handle;bool injected=Native.PasteToForeground(handle);await Task.Delay(250);
                Check("native paste delivers or refuses lost foreground",foreground?(injected&&gotImage):(!injected&&!gotImage),"fixture foreground="+foreground+", injected="+injected+", image received="+gotImage);
            }catch(Exception ex){Check("UI exception",false,ex.ToString());}
            finally {if(fixture!=null)fixture.Close();SetCursorPos(priorCursor.X,priorCursor.Y);if(prior!=IntPtr.Zero)Native.SetForegroundWindow(prior);Save(Path.Combine(folder,"ui-results.json"));if(c!=null)c.ShutdownTest();else app.Shutdown();}
        };app.Run();return AllPassed()?0:1;
    }
    static bool SamePixels(BitmapSource a,BitmapSource b,int startY,int endY) {
        if(a.PixelWidth!=b.PixelWidth||a.PixelHeight!=b.PixelHeight)return false;int w=a.PixelWidth,n=(endY-startY)*w*4;var x=new byte[n];var y=new byte[n];var rect=new Int32Rect(0,startY,w,endY-startY);
        new FormatConvertedBitmap(a,PixelFormats.Pbgra32,null,0).CopyPixels(rect,x,w*4,0);new FormatConvertedBitmap(b,PixelFormats.Pbgra32,null,0).CopyPixels(rect,y,w*4,0);return x.SequenceEqual(y);
    }
    static BitmapSource Snapshot(Window window,string path) {
        window.UpdateLayout();var dpi=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;int w=Math.Max(1,(int)(window.ActualWidth*dpi.M11)),h=Math.Max(1,(int)(window.ActualHeight*dpi.M22));
        var bitmap=new RenderTargetBitmap(w,h,96*dpi.M11,96*dpi.M22,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))encoder.Save(f);return bitmap;
    }
    static readonly object outputGate=new object();
    static void Write(object o) {lock(outputGate)Console.WriteLine(J.Encode(o));}
    public static int FakeServer() {
        Console.SetIn(new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false)));Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});
        string line;while((line=Console.ReadLine())!=null)try {
            var message=J.Parse(line);var id=J.Get(message,"id");var method=J.S(message,"method");var p=J.Get(message,"params");if(method.Length==0||id==null)continue;
            if(method=="test/hang")continue;
            if(method=="test/slow") {var capture=id;var data=p;System.Threading.Tasks.Task.Run(async()=>{await Task.Delay(220);Write(J.Obj("id",capture,"result",data));});continue;}
            if(method=="test/request")Write(J.Obj("id","approval-1","method","item/commandExecution/requestApproval","params",J.Obj("threadId","fake-thread","command","echo test")));
            if(method=="test/notify")Write(J.Obj("method","turn/completed","params",J.Obj("threadId","fake-thread","turn",J.Obj("id","t1","status","completed"))));
            Write(J.Obj("id",id,"result",method=="initialize"?J.Obj("codexHome","fake-home"):p??J.Obj()));
        }catch {}return 0;
    }
}
}
