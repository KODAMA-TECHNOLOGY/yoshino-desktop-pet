using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace YoshinoPet {
public static class J {
    public static Dictionary<string,object> Obj(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]]=pairs[i+1];return d; }
    public static object Get(object value,string key) { var d=value as IDictionary<string,object>;object v;return d!=null&&d.TryGetValue(key,out v)?v:null; }
    public static object At(object value,params string[] keys) { foreach(var key in keys)value=Get(value,key);return value; }
    public static string Text(object value) { return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture); }
    public static string S(object value,params string[] keys) { return Text(At(value,keys)); }
    public static object[] Items(object value) { var a=value as IEnumerable;return a==null||value is string?new object[0]:a.Cast<object>().ToArray(); }
    public static double Number(object value,double fallback=0) { double x;return double.TryParse(Text(value),NumberStyles.Any,CultureInfo.InvariantCulture,out x)?x:fallback; }
    public static JavaScriptSerializer Codec() { return new JavaScriptSerializer { MaxJsonLength=32*1024*1024, RecursionLimit=128 }; }
    public static string Encode(object value) { return Codec().Serialize(value); }
    public static object Parse(string text) { return Codec().DeserializeObject(text); }
    public static string Short(string text,int max) { text=(text??"").Replace("\r"," ").Replace("\n"," ");return text.Length>max?text.Substring(0,max)+"…":text; }
}
public sealed class Settings {
    public string CodexPath {get;set;} public string CodexHome {get;set;} public string Workspace {get;set;}
    public bool AutoHide {get;set;} public bool ShowQuota {get;set;} public bool ShowBrief {get;set;}
    public int PetSize {get;set;} public double Left {get;set;} public double Top {get;set;}
    public string Sandbox {get;set;} public string Model {get;set;} public bool StartWithWindows {get;set;}
    public List<string> IgnoredThreads {get;set;}
    public int QuickHotkey {get;set;} public int CaptureHotkey {get;set;} public int ToggleHotkey {get;set;}
    public Settings() {
        CodexPath="";CodexHome=Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        Workspace=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Codex","芳乃Work");
        AutoHide=true;ShowQuota=true;ShowBrief=true;PetSize=210;Left=-1;Top=-1;Sandbox="workspace-write";Model="";
        IgnoredThreads=new List<string>();QuickHotkey=0x20;CaptureHotkey=0x53;ToggleHotkey=0x59;
    }
    public static Settings Load() { try { var s=J.Codec().Deserialize<Settings>(File.ReadAllText(Paths.Config));s.IgnoredThreads=s.IgnoredThreads??new List<string>();s.PetSize=Math.Max(120,Math.Min(380,s.PetSize));return s; } catch { return new Settings(); } }
    public void Save() { Paths.AtomicWrite(Paths.Config,J.Encode(this)); }
}
public static class Paths {
    public static readonly string Base=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"YoshinoPet");
    public static string Config {get {return Path.Combine(Data,"settings.json");}}
    public static string Atlas {get {return Path.Combine(Base,"assets","yoshino-v2.png");}}
    public static void Init() { Directory.CreateDirectory(Data);Directory.CreateDirectory(Path.Combine(Data,"captures")); }
    public static void AtomicWrite(string path,string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));var tmp=path+".tmp";File.WriteAllText(tmp,text,new System.Text.UTF8Encoding(false));
        if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
    }
    public static void Log(string message) { try { File.AppendAllText(Path.Combine(Data,"app.log"),DateTimeOffset.Now.ToString("o")+" "+J.Short(message,1500)+Environment.NewLine); } catch {} }
    public static string FindCodex(string selected) {
        if(!string.IsNullOrWhiteSpace(selected)&&File.Exists(selected))return Path.GetFullPath(selected);
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) { try {var p=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(p))return p;}catch {} }
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root)) {var files=Directory.GetFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc);if(files.Any())return files.First();}
        throw new FileNotFoundException("未找到 codex.exe，请在设置中选择 Codex CLI。");
    }
}
public sealed class PetTask {
    public string Id="",Name="",Cwd="",Rollout="",State="unknown",Summary="",TurnId="",ActivityKey="";
    public DateTime LastUtc=DateTime.MinValue;public DateTime CompletedUtc=DateTime.MinValue;public bool Managed;
    public string[] Actions=new string[0];
    public bool Busy {get {return State=="running"||State=="waiting";}}
    public string StateLabel {get {switch(State){case "running":return "运行中";case "waiting":return "等待输入";case "completed":return "已完成";case "failed":return "已中断 / 失败";case "idle":return "空闲";default:return "状态未确认";}}}
    public PetTask Clone() {var copy=(PetTask)MemberwiseClone();copy.Actions=(string[])Actions.Clone();return copy;}
    public override string ToString() {return "["+StateLabel+"] "+Name;}
}
public sealed class ActivityBrief {
    readonly Dictionary<string,string> signatures=new Dictionary<string,string>();bool primed;
    public PetTask Current;public DateTime Until=DateTime.MinValue;
    public bool Visible(DateTime now) {return Current!=null&&now<Until;}
    public void Observe(IEnumerable<PetTask> tasks,DateTime now) {
        PetTask changed=null;
        foreach(var t in tasks) {
            string signature=J.Encode(new[]{t.State,t.TurnId,t.Summary,t.ActivityKey,string.Join("\n",t.Actions)}),old;
            bool known=signatures.TryGetValue(t.Id,out old);
            if(primed&&((known&&old!=signature)||(!known&&(t.Busy||t.LastUtc>=now.AddSeconds(-10)))))
                if(changed==null||t.LastUtc>changed.LastUtc)changed=t;
            signatures[t.Id]=signature;
        }
        primed=true;
        if(changed!=null){Current=changed.Clone();Until=now.AddSeconds(6);}
    }
}
public sealed class Quota {
    public object Raw;public DateTime UpdatedUtc;public string Error="";
    public string Compact {get { if(Raw==null)return "额度："+(Error.Length>0?"暂不可用":"读取中");var r=Pick();return Window(J.Get(r,"primary"))+" · "+Window(J.Get(r,"secondary"))+" · credits "+Credit(r); }}
    public string Details {get { if(Raw==null)return Error;var r=Pick();return Compact+Environment.NewLine+Reset(J.Get(r,"primary"))+Environment.NewLine+Reset(J.Get(r,"secondary"))+Environment.NewLine+"更新 "+UpdatedUtc.ToLocalTime().ToString("HH:mm:ss")+(Error.Length>0?"（刷新失败，显示缓存）":""); }}
    object Pick() {return J.At(Raw,"rateLimitsByLimitId","codex")??J.Get(Raw,"rateLimits")??Raw;}
    static string Window(object w) {if(w==null)return "未知";int mins=(int)J.Number(J.Get(w,"windowDurationMins"));string label=mins==300?"5小时":mins==10080?"每周":mins+"分钟";var used=J.Get(w,"usedPercent");return label+" "+(used==null?"未知":Math.Max(0,Math.Min(100,100-J.Number(used))).ToString("0.#")+"%");}
    static string Credit(object r) {var c=J.Get(r,"credits");if(c==null)return "未知";if(J.Text(J.Get(c,"unlimited"))=="True")return "不限";var b=J.Get(c,"balance");return b==null?"未知":J.Text(b);}
    static string Reset(object w) {var x=J.Get(w,"resetsAt");if(x==null)return "重置时间未知";try{return "重置："+new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(J.Number(x)).ToLocalTime().ToString("MM-dd HH:mm");}catch{return "重置时间未知";}}
}
public static class VisibilityPolicy {
    public static bool ShouldHide(bool manualHidden,bool autoHide,bool maximized,bool busy,bool unknown,bool monitorFresh,bool panelOpen,bool grace) {
        if(manualHidden)return true;return autoHide&&maximized&&!busy&&!unknown&&monitorFresh&&!panelOpen&&!grace;
    }
}
}
