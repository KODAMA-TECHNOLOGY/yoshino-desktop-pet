using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YoshinoPet {
public sealed class CodexClient : IDisposable {
    Process process;StreamWriter input;long seq;int generation;readonly object gate=new object();readonly SemaphoreSlim writer=new SemaphoreSlim(1,1);
    readonly Dictionary<string,TaskCompletionSource<object>> pending=new Dictionary<string,TaskCompletionSource<object>>();
    public event Action<string,object> Notification;public event Action<object> ServerRequest;public event Action<string> Disconnected;
    public bool Connected {get;private set;} public string CodexHome {get;private set;}
    public async Task Start(Settings settings,string arguments="app-server") {
        if(process!=null)throw new InvalidOperationException("连接正在使用，请先断开。");
        var info=new ProcessStartInfo(Paths.FindCodex(settings.CodexPath),arguments) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false) };
        info.EnvironmentVariables["CODEX_HOME"]=settings.CodexHome;
        process=new Process {StartInfo=info,EnableRaisingEvents=true};process.Start();
        input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false)) {AutoFlush=true,NewLine="\n"};
        int ownGeneration=++generation;ReadLoop(process,ownGeneration);DrainErrors(process);
        try {var r=await Call("initialize",J.Obj("clientInfo",J.Obj("name","yoshino_pet","title","芳乃桌宠","version","1.3.1"),"capabilities",J.Obj("experimentalApi",true)),45000);CodexHome=J.S(r,"codexHome");await Send(J.Obj("method","initialized","params",J.Obj()));if(process==null||process.HasExited)throw new IOException("Codex 在连接期间退出。");Connected=true;}
        catch {Dispose();throw;}
    }
    async void ReadLoop(Process instance,int ownGeneration) {
        string failure="Codex 连接已关闭";
        try {string line;while((line=await instance.StandardOutput.ReadLineAsync().ConfigureAwait(false))!=null) {
            if(ownGeneration!=generation)break;
            object msg;try {msg=J.Parse(line);}catch {continue;}
            var method=J.S(msg,"method");var id=J.Get(msg,"id");
            if(method.Length>0) {if(id!=null){var h=ServerRequest;if(h!=null)h(msg);else await ReplyError(id,"此客户端尚未处理该请求");}else {var h=Notification;if(h!=null)h(method,J.Get(msg,"params"));}continue;}
            TaskCompletionSource<object> source=null;lock(gate) {if(id!=null&&pending.TryGetValue(J.Text(id),out source))pending.Remove(J.Text(id));}
            if(source!=null) {var error=J.Get(msg,"error");if(error!=null)source.TrySetException(new Exception(J.S(error,"message")));else source.TrySetResult(J.Get(msg,"result"));}
        }}catch(Exception e){failure=e.Message;}
        finally {if(ownGeneration==generation){Connected=false;lock(gate){foreach(var p in pending.Values)p.TrySetException(new IOException(failure));pending.Clear();}var h=Disconnected;if(h!=null)h(failure);}}
    }
    async void DrainErrors(Process instance) {try {string line;while((line=await instance.StandardError.ReadLineAsync().ConfigureAwait(false))!=null) {if(line.IndexOf("error",StringComparison.OrdinalIgnoreCase)>=0)Paths.Log("Codex: "+J.Short(line,800));}}catch {}}
    public async Task<object> Call(string method,object parameters,int timeout=30000) {
        var id=Interlocked.Increment(ref seq);var key=id.ToString();var tcs=new TaskCompletionSource<object>();lock(gate)pending[key]=tcs;
        try {await Send(J.Obj("id",id,"method",method,"params",parameters??J.Obj())).ConfigureAwait(false);
            if(await Task.WhenAny(tcs.Task,Task.Delay(timeout)).ConfigureAwait(false)!=tcs.Task)throw new TimeoutException(method+" 请求超时；不会自动重发任务。");
            return await tcs.Task.ConfigureAwait(false);
        }finally {lock(gate)pending.Remove(key);}
    }
    public async Task Send(object message) {await writer.WaitAsync().ConfigureAwait(false);try {if(process==null||process.HasExited)throw new IOException("Codex 进程未运行");await input.WriteLineAsync(J.Encode(message)).ConfigureAwait(false);}finally {writer.Release();}}
    public Task Reply(object id,object result) {return Send(J.Obj("id",id,"result",result));}
    public Task ReplyError(object id,string message) {return Send(J.Obj("id",id,"error",J.Obj("code",-32601,"message",message)));}
    public void Dispose() {Connected=false;generation++;lock(gate){foreach(var t in pending.Values)t.TrySetException(new IOException("连接已关闭"));pending.Clear();}var p=process;if(p==null)return;process=null;try {input.Close();}catch {}try {if(!p.HasExited&&!p.WaitForExit(1800))p.Kill();}catch {}try {p.Dispose();}catch {}}
}
}
