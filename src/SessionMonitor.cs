using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YoshinoPet {
public sealed class RolloutState {
    readonly List<ToolAction> actions=new List<ToolAction>();readonly Dictionary<string,List<ToolAction>> calls=new Dictionary<string,List<ToolAction>>();
    DateTime actionStamp=DateTime.MinValue;bool typedItems;
    public PetTask Task=new PetTask();public long LastLength=-1;public bool UsesTurnApi;public DateTime LastHistoryRead=DateTime.MinValue;public DateTime LastWrite=DateTime.MinValue,Lifecycle=DateTime.MinValue,Message=DateTime.MinValue;
    public static DateTime Stamp(object o) {DateTimeOffset x;return DateTimeOffset.TryParse(J.Text(o),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out x)?x.UtcDateTime:DateTime.MinValue;}
    public void Apply(object record) {
        var p=J.Get(record,"payload");var stamp=Stamp(J.Get(record,"timestamp"));var kind=J.S(record,"type");
        if(kind=="event_msg") {
            string type=J.S(p,"type");
            bool started=type=="task_started"||type=="turn_started",completed=type=="task_complete"||type=="task_completed"||type=="turn_completed",aborted=type=="turn_aborted"||type=="task_aborted";
            if(stamp>=Lifecycle&&(started||completed||aborted)) {
                string incoming=J.S(p,"turn_id");if(incoming.Length==0)incoming=J.S(p,"root_turn_id");
                if(!started&&Task.TurnId.Length>0&&incoming.Length>0&&incoming!=Task.TurnId)return;
                Lifecycle=stamp;Task.TurnId=incoming;UsesTurnApi=false;
                string status=J.S(p,"status");if(status.Length==0)status=J.S(p,"turn","status");
                Task.State=started?"running":completed&&status!="failed"&&status!="interrupted"?"completed":"failed";
                if(started&&Message<stamp)Task.Summary="正在处理请求…";
                if(started&&stamp>actionStamp){actions.Clear();calls.Clear();Task.Actions=new string[0];}
                if(!started)Task.CompletedUtc=stamp;
                if(completed&&!string.IsNullOrWhiteSpace(J.S(p,"last_agent_message")))SetSummary(J.S(p,"last_agent_message"),stamp);
            }
            if(type=="agent_message")SetSummary(J.S(p,"message"),stamp);
            if(type=="item_started"||type=="item_completed") {
                if(!typedItems){typedItems=true;actions.Clear();calls.Clear();Task.Actions=new string[0];}
                // New paginated rollouts persist typed items instead of raw
                // response_item records. They also prove a turn is active when
                // its start marker has scrolled out of the bounded log tail.
                string turn=J.S(p,"turn_id");
                if(turn.Length>0&&stamp>Lifecycle) {
                    if(Task.TurnId!=turn&&stamp>actionStamp){actions.Clear();calls.Clear();Task.Actions=new string[0];}
                    Task.State="running";Task.TurnId=turn;Lifecycle=stamp;UsesTurnApi=false;
                }
                ApplyItem(J.Get(p,"item"),stamp,type=="item_completed");
            }
        }
        string itemType=J.S(p,"type");
        if(!typedItems&&kind=="response_item"&&(itemType=="function_call"||itemType=="custom_tool_call")&&stamp>=actionStamp) {
            string id=J.S(p,"call_id");
            if(!calls.ContainsKey(id)) {
                var parsed=ActionParser.Parse(J.S(p,"name"),J.S(p,itemType=="custom_tool_call"?"input":"arguments"),id);calls[id]=parsed;actions.AddRange(parsed);
                while(actions.Count>30)actions.RemoveAt(0);
                if(calls.Count>200){var keep=new HashSet<string>(actions.Select(x=>x.Key.Split('/')[0]));foreach(var old in calls.Keys.Where(x=>!keep.Contains(x)).ToArray())calls.Remove(old);}
                actionStamp=stamp;Task.ActivityKey=id+"/start";Task.Actions=actions.Skip(Math.Max(0,actions.Count-3)).Select(x=>x.Display).ToArray();
            }
        }
        if(!typedItems&&kind=="response_item"&&(itemType=="function_call_output"||itemType=="custom_tool_call_output")) {
            List<ToolAction> pending;string id=J.S(p,"call_id");
            if(calls.TryGetValue(id,out pending)&&pending.Any(x=>!x.Done)) {
                bool failed=ActionParser.Failed(J.Get(p,"output"));foreach(var a in pending){a.Done=true;a.Failed=failed;}
                if(stamp>=actionStamp){actionStamp=stamp;Task.ActivityKey=id+"/done";}
                Task.Actions=actions.Skip(Math.Max(0,actions.Count-3)).Select(x=>x.Display).ToArray();
            }
        }
        if(kind=="response_item"&&J.S(p,"type")=="message"&&J.S(p,"role")=="assistant"&&J.S(p,"channel")!="analysis") {
            var parts=J.Items(J.Get(p,"content")).Select(x=>J.S(x,"text")).Where(x=>x.Length>0);
            SetSummary(string.Join("\n",parts),stamp);
        }
        if(stamp>Task.LastUtc)Task.LastUtc=stamp;
    }
    void ApplyItem(object item,DateTime stamp,bool completed) {
        string type=J.S(item,"type"),id=J.S(item,"id"),status=J.S(item,"status");
        if(type=="AgentMessage"||type=="agentMessage") {
            if(J.S(item,"phase")=="analysis")return;
            var content=J.Get(item,"content");string text=content is string?(string)content:string.Join("\n",J.Items(content).Select(x=>J.S(x,"text")).Where(x=>x.Length>0));
            SetSummary(text,stamp);return;
        }
        if(type=="Reasoning"||type=="reasoning"||id.Length==0||stamp<actionStamp)return;
        List<ToolAction> parsed;
        if(!calls.TryGetValue(id,out parsed)) {
            parsed=new List<ToolAction>();
            if(type=="CommandExecution"||type=="commandExecution") {
                string cmd=J.Items(J.Get(item,"parsed_cmd")).Select(x=>J.S(x,"cmd")).FirstOrDefault(x=>x.Length>0);
                var args=J.Items(J.Get(item,"command")).Select(J.Text).ToArray();
                if(string.IsNullOrEmpty(cmd))cmd=args.Length>2&&(args[1]=="-Command"||args[1]=="-c"||args[1]=="/c")?args[2]:string.Join(" ",args);
                parsed.Add(new ToolAction {Kind="command",Text=cmd});
            } else if(type=="FileChange"||type=="fileChange") {
                var changes=J.Get(item,"changes") as IDictionary<string,object>;
                if(changes!=null)foreach(var pair in changes) {
                    var edit=new ToolAction {Kind="edit",Text=Path.GetFileName(pair.Key.Replace('/',Path.DirectorySeparatorChar))};
                    string diff=J.S(pair.Value,"unified_diff");if(diff.Length==0)diff=J.S(pair.Value,"diff");
                    foreach(var line in diff.Replace("\r","").Split('\n')){if(line.StartsWith("+")&&!line.StartsWith("+++"))edit.Added++;else if(line.StartsWith("-")&&!line.StartsWith("---"))edit.Removed++;}
                    parsed.Add(edit);
                }
            } else if(type=="Extension")parsed.Add(new ToolAction {Kind="tool",Text=J.S(item,"kind")});
            else if(type=="McpToolCall"||type=="mcpToolCall")parsed.Add(new ToolAction {Kind="tool",Text=J.S(item,"server")+"/"+J.S(item,"tool")});
            if(parsed.Count==0)return;
            for(int i=0;i<parsed.Count;i++)parsed[i].Key=id+"/"+i;
            calls[id]=parsed;actions.AddRange(parsed);while(actions.Count>30)actions.RemoveAt(0);
            if(calls.Count>200){var keep=new HashSet<string>(actions.Select(x=>x.Key.Split('/')[0]));foreach(var old in calls.Keys.Where(x=>!keep.Contains(x)).ToArray())calls.Remove(old);}
        }
        bool done=completed&&status!="inProgress"&&status!="in_progress"&&status!="running";
        bool failed=status=="failed"||status=="declined"||(J.Get(item,"exit_code")!=null&&J.Number(J.Get(item,"exit_code"))!=0);
        foreach(var action in parsed){action.Done=done;action.Failed=failed;}
        actionStamp=stamp;Task.ActivityKey=id+(done?"/done":"/start");Task.Actions=actions.Skip(Math.Max(0,actions.Count-3)).Select(x=>x.Display).ToArray();
    }
    void SetSummary(string text,DateTime stamp) {if(stamp>=Message&&!string.IsNullOrWhiteSpace(text)){Task.Summary=J.Short(text,340);if(stamp>=actionStamp)Task.ActivityKey=stamp.ToString("o")+"/message";Message=stamp;}}
    public void Read() {
        var path=Task.Rollout;if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return;
        var info=new FileInfo(path);if(info.Length==LastLength&&info.LastWriteTimeUtc==LastWrite)return;
        if(info.Length<LastLength){Lifecycle=DateTime.MinValue;Message=DateTime.MinValue;Task.State="unknown";}
        bool initial=LastLength<0;int window=128*1024;
        for(;;) {
            string chunk=Tail(path,window);var lines=chunk.Split('\n');
            for(int i=0;i<lines.Length-1;i++){try{if(lines[i].Length<16*1024*1024)Apply(J.Parse(lines[i]));}catch{}}
            if(!initial||Lifecycle!=DateTime.MinValue||window>=8*1024*1024||info.Length<=window)break;
            window*=2;
        }
        LastLength=info.Length;LastWrite=info.LastWriteTimeUtc;
    }
    static string Tail(string path,int max) {
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            long start=Math.Max(0,f.Length-max);f.Seek(start,SeekOrigin.Begin);var data=new byte[(int)(f.Length-start)];int n=0,r;
            while(n<data.Length&&(r=f.Read(data,n,data.Length-n))>0)n+=r;
            string text=Encoding.UTF8.GetString(data,0,n);if(start>0){int p=text.IndexOf('\n');text=p<0?"":text.Substring(p+1);}return text;
        }
    }
}
public sealed class SessionMonitor {
    readonly Dictionary<string,RolloutState> states=new Dictionary<string,RolloutState>();
    public DateTime LastSuccess=DateTime.MinValue;public string Error="";public int Seen;
    public async Task<List<PetTask>> Poll(CodexClient client) {
        var all=new List<object>();string cursor=null;int pages=0;
        // Paginate all root sessions so an older but still running task is not hidden by recent chats.
        do {
            var p=J.Obj("limit",100,"sortKey","updated_at","useStateDbOnly",true);
            if(cursor!=null)p["cursor"]=cursor;
            var result=await client.Call("thread/list",p);all.AddRange(J.Items(J.Get(result,"data")));
            cursor=J.S(result,"nextCursor");pages++;
            if(pages>=50&&cursor.Length>0)throw new Exception("任务超过5000条，状态扫描未完成；保持桌宠可见。");
        }while(!string.IsNullOrEmpty(cursor));
        var resultTasks=await System.Threading.Tasks.Task.Run(()=> {
            var tasks=new List<PetTask>();
            foreach(var meta in all) {
                if(!string.IsNullOrEmpty(J.S(meta,"parentThreadId")))continue;
                var id=J.S(meta,"id");if(id.Length==0)continue;
                RolloutState s;if(!states.TryGetValue(id,out s)){s=new RolloutState();s.Task.Id=id;states[id]=s;}
                s.Task.Name=J.S(meta,"name");if(string.IsNullOrWhiteSpace(s.Task.Name))s.Task.Name=J.Short(J.S(meta,"preview"),55);if(s.Task.Name.Length==0)s.Task.Name="未命名任务";
                s.Task.Cwd=J.S(meta,"cwd");s.Task.Rollout=J.S(meta,"path");
                try {s.Read();}catch(Exception e){Paths.Log("读取活动记录: "+e.Message);s.Task.State="unknown";}
                var runtime=J.S(meta,"status","type");if(runtime=="active") {
                    var flags=J.Items(J.At(meta,"status","activeFlags")).Select(J.Text).ToArray();
                    s.Task.State=flags.Any(x=>x.IndexOf("waiting",StringComparison.OrdinalIgnoreCase)>=0)?"waiting":"running";
                }else if(runtime=="systemError")s.Task.State="failed";
                tasks.Add(s.Task.Clone());
            }
            return tasks.OrderByDescending(t=>t.LastUtc).ToList();
        });
        foreach(var task in resultTasks) {
            var state=states[task.Id];if(task.State!="unknown"&&!state.UsesTurnApi)continue;
            if((DateTime.UtcNow-state.LastHistoryRead).TotalSeconds<(task.Busy?3:30))continue;
            state.LastHistoryRead=DateTime.UtcNow;state.UsesTurnApi=true;
            try {
                // Older/imported histories may not have usable lifecycle events in the rollout tail.
                // Read their persisted final turn without resuming or taking ownership of the thread.
                var history=await client.Call("thread/turns/list",J.Obj("threadId",task.Id,"limit",1,"sortDirection","desc","itemsView","notLoaded"),15000);
                var last=J.Items(J.Get(history,"data")).FirstOrDefault();
                if(last==null){task.State="idle";}else {
                    var status=J.S(last,"status");task.State=status=="completed"?"completed":status=="inProgress"?"running":status=="failed"||status=="interrupted"?"failed":"unknown";
                    task.TurnId=J.S(last,"id");var seconds=J.Get(last,"completedAt")??J.Get(last,"startedAt");
                    if(seconds!=null){var time=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(J.Number(seconds));if(time>task.LastUtc)task.LastUtc=time;if(task.State!="running")task.CompletedUtc=time;}
                    if(task.Summary.Length==0)task.Summary=task.StateLabel;
                }
                state.Task.State=task.State;state.Task.TurnId=task.TurnId;state.Task.LastUtc=task.LastUtc;state.Task.CompletedUtc=task.CompletedUtc;state.Task.Summary=task.Summary;
            }catch(Exception e){Paths.Log("旧版任务状态: "+e.Message);}
        }
        LastSuccess=DateTime.UtcNow;Error="";Seen=resultTasks.Count;return resultTasks.OrderByDescending(t=>t.LastUtc).ToList();
    }
}
}
