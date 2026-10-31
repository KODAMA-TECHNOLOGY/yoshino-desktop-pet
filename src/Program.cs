using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace YoshinoPet {
public static class Program {
    public static readonly int WakeMessage=Native.RegisterWindowMessage("YoshinoPet.Show.v1");
    [STAThread]public static int Main(string[] args) {
        if(args.Length>0&&args[0]=="--fake-server")return TestRunner.FakeServer();
        Paths.Init();Native.Dpi();
        if(args.Length>1&&args[0]=="--export-art"){SpriteArt.Load().Export(Path.GetFullPath(args[1]));return 0;}
        if(args.Length>0&&args[0]=="--self-test")return TestRunner.Run(args.Length>1?args[1]:Path.Combine(Paths.Data,"tests"),false).GetAwaiter().GetResult();
        if(args.Length>0&&args[0]=="--integration-test")return TestRunner.Run(args.Length>1?args[1]:Path.Combine(Paths.Data,"tests"),true).GetAwaiter().GetResult();
        if(args.Length>1&&args[0]=="--window-fixture")return Fixture(args[1]);
        if(args.Length>0&&args[0]=="--ui-test")return TestRunner.Ui(args.Length>1?args[1]:Path.Combine(Paths.Data,"tests"));
        bool first;using(var mutex=new Mutex(true,"Local\\YoshinoPet.v1",out first)) {
            if(!first){Native.PostMessage(new IntPtr(0xffff),WakeMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Theme.Install(app);
            app.DispatcherUnhandledException+=(s,e)=>{Paths.Log(e.Exception.ToString());MessageBox.Show(e.Exception.Message,"芳乃桌宠");e.Handled=true;};
            try {if(!File.Exists(Paths.Atlas))throw new FileNotFoundException("找不到 assets/yoshino-v2.png，请完整解压软件文件夹。");var companion=new Companion(Settings.Load());app.Startup+=(s,e)=>companion.Start();app.Run();return 0;}
            catch(Exception e){Paths.Log(e.ToString());MessageBox.Show(e.Message,"芳乃桌宠启动失败");return 1;}
        }
    }
    static int Fixture(string file) {
        var app=new Application();var panel=new StackPanel {Margin=new Thickness(70)};panel.Children.Add(new TextBlock {Text="芳乃桌宠 · 窗口截图测试",FontSize=36,Foreground=Brushes.White});panel.Children.Add(new TextBlock {Text="这是软件自己的测试窗口。\n窗口编号 YOSHINO-2026\n没有其他应用内容。",FontSize=22,Foreground=Brushes.White,Margin=new Thickness(0,30,0,0)});
        var w=new Window {Title="芳乃测试窗口",WindowState=WindowState.Maximized,Background=new SolidColorBrush(Color.FromRgb(56,61,86)),Content=panel};
        w.ContentRendered+=(s,e)=>File.WriteAllText(file,new WindowInteropHelper(w).Handle.ToInt64().ToString());
        var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(25)};timer.Tick+=(s,e)=>w.Close();timer.Start();app.Run(w);return 0;
    }
}
}
