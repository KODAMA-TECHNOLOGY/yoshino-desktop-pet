using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YoshinoPet {
public sealed class ToolAction {
    public string Key,Kind,Text;public int Added,Removed;public bool Done,Failed;
    public string Display {get {
        string verb=Kind=="edit"?(Done?"已编辑":"编辑中"):Kind=="command"?(Done?"已执行":"执行中"):(Done?"已调用":"调用中");
        if(Failed)verb="返回错误";
        return verb+" "+J.Short(Text,115)+(Kind=="edit"?"  +"+Added+" -"+Removed:"");
    }}
}
public static class ActionParser {
    public static List<ToolAction> Parse(string name,string input,string key) {
        var list=new List<ToolAction>();input=input??"";object args=null;try {args=J.Parse(input);}catch {}
        if(name.EndsWith("apply_patch")||input.TrimStart().StartsWith("*** Begin Patch"))AddPatch(list,input,key);
        else if(name.EndsWith("exec_command")&&J.Get(args,"cmd")!=null)list.Add(new ToolAction {Kind="command",Text=J.S(args,"cmd")});
        else {
            // Read string literals only. Never evaluate model code or command text.
            foreach(Match m in Regex.Matches(input,@"\b(?:tools\.)?exec_command\s*\(\s*\{\s*(?:['"" ]?cmd['"" ]?)\s*:\s*(?:String\.raw\s*)?")) {
                string value;int end;if(ReadLiteral(input,m.Index+m.Length,out value,out end))list.Add(new ToolAction {Kind="command",Text=value});
            }
            foreach(Match m in Regex.Matches(input,@"\b(?:tools\.)?apply_patch\s*\(\s*(?:String\.raw\s*)?")) {
                string value;int end;if(ReadLiteral(input,m.Index+m.Length,out value,out end))AddPatch(list,value,key);
            }
            if(list.Count==0) {
                string label=J.S(args,"tool");if(label.Length==0)label=name;
                list.Add(new ToolAction {Kind="tool",Text=label});
            }
        }
        for(int i=0;i<list.Count;i++){list[i].Key=key+"/"+i;if(list[i].Text==null)list[i].Text=name;}
        return list;
    }
    static void AddPatch(List<ToolAction> list,string patch,string key) {
        ToolAction current=null;
        foreach(string raw in patch.Replace("\r","").Split('\n')) {
            if(raw.StartsWith("*** Update File: ")||raw.StartsWith("*** Add File: ")||raw.StartsWith("*** Delete File: ")) {
                string path=raw.Substring(raw.IndexOf(": ")+2).Trim();current=new ToolAction {Kind="edit",Text=Path.GetFileName(path.Replace('/',Path.DirectorySeparatorChar))};list.Add(current);
            }else if(current!=null&&raw.StartsWith("+"))current.Added++;
            else if(current!=null&&raw.StartsWith("-"))current.Removed++;
        }
    }
    static bool ReadLiteral(string text,int start,out string value,out int end) {
        value="";end=start;while(start<text.Length&&char.IsWhiteSpace(text[start]))start++;
        if(start>=text.Length||("\"'`".IndexOf(text[start])<0))return false;char quote=text[start++];var b=new StringBuilder();
        for(int i=start;i<text.Length;i++) {
            char c=text[i];if(c==quote){value=b.ToString();end=i+1;return true;}
            if(c=='\\'&&i+1<text.Length) {
                c=text[++i];if(c=='n')b.Append('\n');else if(c=='r')b.Append('\r');else if(c=='t')b.Append('\t');
                else if(c=='u'&&i+4<text.Length){int hex;if(int.TryParse(text.Substring(i+1,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out hex)){b.Append((char)hex);i+=4;}else b.Append(c);}
                else b.Append(c);
            }else b.Append(c);
        }
        return false;
    }
    public static bool Failed(object output) {
        string text=OutputText(output).Replace("\\\"","\"");
        if(text==null)return false;
        if(Regex.IsMatch(text,@"(?i)(?:script error|\bexception:|\bisError[""\s]*:[\s]*true)"))return true;
        foreach(Match m in Regex.Matches(text,@"(?i)(?:exit_code|exit code|process exited with code)[\s""\:]*(-?\d+)"))if(m.Groups[1].Value!="0")return true;
        return false;
    }
    static string OutputText(object output) {
        if(output==null)return "";if(output is string)return (string)output;
        var fields=output as IDictionary<string,object>;
        if(fields!=null){if(J.Get(output,"text")!=null)return J.S(output,"text");if(J.Get(output,"content")!=null)return OutputText(J.Get(output,"content"));return J.Encode(output);}
        return string.Join("\n",J.Items(output).Select(OutputText));
    }
}
}
