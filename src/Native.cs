using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace YoshinoPet {
public static class Native {
    [StructLayout(LayoutKind.Sequential)]public struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)]public struct POINT { public int X,Y; }
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")]public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")]static extern int GetClassName(IntPtr h,StringBuilder text,int max);
    [DllImport("user32.dll")]public static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
    [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,int message,IntPtr w,IntPtr l);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)]struct INPUT {public uint type;public INPUTDATA data;}
    [StructLayout(LayoutKind.Explicit)]struct INPUTDATA {
        [FieldOffset(0)]public KEYBDINPUT key;[FieldOffset(0)]public MOUSEINPUT mouse;
    }
    [StructLayout(LayoutKind.Sequential)]struct KEYBDINPUT {public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
    [StructLayout(LayoutKind.Sequential)]struct MOUSEINPUT {public int x,y;public uint data,flags,time;public UIntPtr extra;}
    public static void Dpi() {try {SetProcessDPIAware();}catch {}}
    public static bool IsOurs(IntPtr h) {uint pid;GetWindowThreadProcessId(h,out pid);return pid==(uint)Process.GetCurrentProcess().Id;}
    public static bool Maximized(IntPtr h) {
        if(h==IntPtr.Zero||IsOurs(h))return false;
        var cn=new StringBuilder(256);GetClassName(h,cn,256);if(cn.ToString()=="Progman"||cn.ToString()=="WorkerW"||cn.ToString()=="Shell_TrayWnd")return false;
        if(IsZoomed(h))return true;RECT r;if(!GetWindowRect(h,out r))return false;
        var b=System.Windows.Forms.Screen.FromHandle(h).Bounds;return r.Left<=b.Left+2&&r.Top<=b.Top+2&&r.Right>=b.Right-2&&r.Bottom>=b.Bottom-2;
    }
    public static string Capture(IntPtr h) {
        RECT r;if(h==IntPtr.Zero||!GetWindowRect(h,out r)||r.Right<=r.Left||r.Bottom<=r.Top)throw new Exception("没有可截图的前台窗口。");
        int w=r.Right-r.Left,ht=r.Bottom-r.Top;
        if(IsIconic(h))throw new Exception("目标窗口已最小化，请先恢复窗口再截图。");
        if(w<=0||ht<=0||w>16000||ht>16000||(long)w*ht>50000000)throw new Exception("窗口尺寸无效或过大，请调整目标窗口后再截图。");
        string path=Path.Combine(Paths.Data,"captures",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png");
        using(var bmp=new Bitmap(w,ht,PixelFormat.Format32bppRgb)) {
            bool ok=false;using(var g=Graphics.FromImage(bmp)) {g.Clear(System.Drawing.Color.Black);var dc=g.GetHdc();try {ok=PrintWindow(h,dc,2);}finally {g.ReleaseHdc(dc);}}
            if(!ok||Black(bmp)) {
                // Screen-coordinate fallback is permitted only while this exact target remains foreground.
                // If focus changed, fail explicitly rather than capture another application.
                if(GetForegroundWindow()!=h)throw new Exception("目标窗口已不在前台，且无法按窗口抓取。请重新置前台后截图。");
                using(var g=Graphics.FromImage(bmp))g.CopyFromScreen(r.Left,r.Top,0,0,new System.Drawing.Size(w,ht),CopyPixelOperation.SourceCopy);
                if(GetForegroundWindow()!=h)throw new Exception("截图期间前台窗口发生变化，请重试。");
            }
            bmp.Save(path,ImageFormat.Png);
        }
        return path;
    }
    static bool Black(Bitmap image) {for(int y=1;y<6;y++)for(int x=1;x<6;x++){var p=image.GetPixel(image.Width*x/6,image.Height*y/6);if(p.R+p.G+p.B>18)return false;}return true;}
    public static async Task CopyImage(string path) {
        // Supply both bitmap and PNG formats; keep the clipboard alive after this process exits.
        byte[] png=File.ReadAllBytes(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=new MemoryStream(png);bitmap.EndInit();bitmap.Freeze();
        var data=new DataObject();data.SetImage(bitmap);data.SetData("PNG",new MemoryStream(png));
        for(int retry=0;;retry++){try {Clipboard.SetDataObject(data,true);return;}catch(COMException){if(retry>=9)throw;}await Task.Delay(60);}
    }
    public static bool IsCodexWindow(IntPtr window) {
        if(window==IntPtr.Zero)return false;
        try {uint pid;GetWindowThreadProcessId(window,out pid);using(var p=Process.GetProcessById((int)pid)) {
            string file=p.MainModule.FileName;
            return file.IndexOf("\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0&&string.Equals(Path.GetFileName(file),"ChatGPT.exe",StringComparison.OrdinalIgnoreCase);
        }}catch{return false;}
    }
    static bool ModifiersReleased() {return (GetAsyncKeyState(0x10)&0x8000)==0&&(GetAsyncKeyState(0x11)&0x8000)==0&&(GetAsyncKeyState(0x12)&0x8000)==0;}
    public static async Task<bool> PasteIntoCodex() {
        // URI activation opens a fresh native composer. Never send input to another foreground app.
        await Task.Delay(1200);IntPtr stable=IntPtr.Zero;DateTime since=DateTime.UtcNow;
        for(int i=0;i<55;i++) {
            var h=GetForegroundWindow();
            if(!IsCodexWindow(h)||!ModifiersReleased()){stable=IntPtr.Zero;since=DateTime.UtcNow;}
            else if(h!=stable){stable=h;since=DateTime.UtcNow;}
            else if((DateTime.UtcNow-since).TotalMilliseconds>=400)return PasteToForeground(h);
            await Task.Delay(100);
        }
        return false;
    }
    internal static bool PasteToForeground(IntPtr expected) {
        if(expected==IntPtr.Zero||GetForegroundWindow()!=expected||!ModifiersReleased())return false;
        var keys=new[]{Key(0x11,false),Key(0x56,false),Key(0x56,true),Key(0x11,true)};
        uint sent=SendInput((uint)keys.Length,keys,Marshal.SizeOf(typeof(INPUT)));
        if(sent!=keys.Length){SendInput(1,new[]{Key(0x11,true)},Marshal.SizeOf(typeof(INPUT)));return false;}return true;
    }
    static INPUT Key(ushort vk,bool up) {return new INPUT {type=1,data=new INPUTDATA {key=new KEYBDINPUT {vk=vk,flags=up?2U:0U}}};}
    public static void Open(string uri) {Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}
    public static string ThreadLink(string id) {return "codex://threads/"+Uri.EscapeDataString(id);}
    public static string NewLink(string prompt,string cwd) {return "codex://new?prompt="+Uri.EscapeDataString(prompt??"")+"&path="+Uri.EscapeDataString(Path.GetFullPath(cwd));}
}
}
