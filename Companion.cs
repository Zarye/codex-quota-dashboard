using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace CodexQuotaMini {
 static class Companion {
  public const string WatchStop="Local\\CodexQuotaMini_WatchStop_1";
  public const string WidgetStop="Local\\CodexQuotaMini_WidgetStop_1";
  const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
  const string RunName="CodexQuotaMini";
  static readonly string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"CodexQuotaMini.exe");
  static readonly string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
  public static void Signal(string name){try{using(var stop=EventWaitHandle.OpenExisting(name))stop.Set();}catch(WaitHandleCannotBeOpenedException){}}
  public static void RemoveLegacyAutostart() {
   using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true)) {
    if(key!=null&&String.Equals(Convert.ToString(key.GetValue(RunName)),"\""+exe+"\" --watch",StringComparison.OrdinalIgnoreCase))key.DeleteValue(RunName,false);
   }
   Signal(WatchStop);
  }
  static bool IsDesktopHost(string path) {
   string file=Path.GetFileName(path);
   if(!String.Equals(file,"ChatGPT.exe",StringComparison.OrdinalIgnoreCase)&&!String.Equals(file,"Codex.exe",StringComparison.OrdinalIgnoreCase))return false;
   if(!String.Equals(Path.GetFileName(Path.GetDirectoryName(path)),"app",StringComparison.OrdinalIgnoreCase))return false;
   return path.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("\\OpenAI\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0;
  }
  public static Process DesktopHost() {
   var hosts=new List<Tuple<int,long>>();int sessionId=Process.GetCurrentProcess().SessionId;
   foreach(string name in new[]{"ChatGPT","Codex"})foreach(var process in Process.GetProcessesByName(name))using(process){try{
    if(process.SessionId!=sessionId||!IsDesktopHost(process.MainModule.FileName))continue;
    hosts.Add(Tuple.Create(process.Id,process.StartTime.ToUniversalTime().Ticks));
   }catch{}}
   var first=hosts.OrderBy(x=>x.Item2).FirstOrDefault();
   if(first==null)return null;
   try{var host=Process.GetProcessById(first.Item1);if(host.StartTime.ToUniversalTime().Ticks!=first.Item2){host.Dispose();return null;}return host;}catch{return null;}
  }
  public static int LaunchChatGPT() {
   bool created;
   using(var launcherMutex=new Mutex(true,"Local\\CodexQuotaMini_Launcher_2",out created)) {
    if(!created)return 0;
    string aumid="OpenAI.Codex_2p2nqsd0c76g0!App";
    string config=Path.Combine(data,"launch-settings.json");
    if(File.Exists(config)) {
     string configured=Convert.ToString(Json.Get(Json.Read(File.ReadAllText(config)),"appUserModelId"));
     if(!String.IsNullOrWhiteSpace(configured))aumid=configured;
    }
    if(aumid.IndexOfAny(new[]{'"','\r','\n',' '})>=0)throw new InvalidOperationException("ChatGPT 启动标识无效");
    using(var activation=Process.Start(new ProcessStartInfo {FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Arguments="shell:AppsFolder\\"+aumid,UseShellExecute=false,CreateNoWindow=true})){}
    for(int i=0;i<150;i++) {
     using(var host=DesktopHost())if(host!=null){StartAttachedWidget();launcherMutex.ReleaseMutex();return 0;}
     Thread.Sleep(200);
    }
    throw new InvalidOperationException("未检测到 ChatGPT 启动，请确认桌面应用仍已安装。");
   }
  }
  public static void StartAttachedWidget(){using(var child=Process.Start(new ProcessStartInfo {FileName=exe,Arguments="--with-chatgpt",UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory})) {}}
  public static int TestHostFilter() {
   if(IsDesktopHost("C:\\Users\\Test\\AppData\\Local\\OpenAI\\Codex\\bin\\version\\codex.exe"))throw new Exception("Quota CLI must not be a desktop host");
   if(!IsDesktopHost("C:\\Program Files\\WindowsApps\\OpenAI.Codex_version\\app\\ChatGPT.exe"))throw new Exception("Desktop host was not detected");
   using(var host=DesktopHost()) {
    Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"companion-test.json"),Json.Write(new {passed=2,hostDetected=host!=null,hostPid=host==null?(int?)null:host.Id,updatedAt=DateTime.UtcNow.ToString("o")}));
   }
   return 0;
  }
 }
}
