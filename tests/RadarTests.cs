using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexQuotaMini.Tests {
 static class RadarTests {
  static int checks;
  static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
  static TiboPost Post(string id,string text,string time){return new TiboPost {Id=id,Text=text,PublishedAt=time,Url="https://x.com/thsottiaux/status/"+id};}
  static void Rules(){
   var now=DateTimeOffset.Parse("2026-10-22T12:00:00Z");
   Check(LocalResetPredictor.Classify("Day 17/ It's reset day today.")=="planned","announcement is not completion");
   Check(LocalResetPredictor.Classify("The reset has been processed. Enjoy!")=="completed","explicit completion");
   Check(LocalResetPredictor.Classify("Put the reset in the bag. All propagated.")=="completed","completion phrase associated with reset");
   Check(LocalResetPredictor.Classify("One banked reset loaded into every account")=="banked","banked card is separate");
   Check(LocalResetPredictor.Classify("No reset today")=="withdrawn","negation");
   Check(LocalResetPredictor.Classify("I hope we might get a reset tomorrow?")=="hint","wish is not promise");
   Check(LocalResetPredictor.Classify("Run git reset --hard")=="other","software command ignored");
   Check(LocalResetPredictor.Classify("Please reset your password")=="other","password reset ignored");
   Check(LocalResetPredictor.Classify("The reset has not been processed yet")=="withdrawn","negated completion never marked complete");
   Check(LocalResetPredictor.Classify("Done")=="other","context-free Done is not completion");
   Check(LocalResetPredictor.Classify("We improved context caching")=="other","unrelated post ignored");
   DateTimeOffset early,late;string detail;
   var timed=Post("2113162413008884099","Lands tomorrow before 10am PST.","2026-10-22T06:55:54Z");
   Check(LocalResetPredictor.TryDeadline(timed,out early,out late,out detail)&&early==DateTimeOffset.Parse("2026-10-22T17:00:00Z")&&late==DateTimeOffset.Parse("2026-10-22T18:00:00Z"),"PST/PDT ambiguity preserves both deadlines");
   var parent=Post("2113162277688070445","Day 17/ It's reset day today.","2026-10-22T06:55:22Z");
   Check(LocalResetPredictor.Analyze(new[]{timed},now).Kind=="context","unlinked time hint stays unconfirmed");
   timed.ContextIds.Add(parent.Id);
   Check(LocalResetPredictor.Analyze(new[]{parent,timed},now).Kind=="planned","same-page recent context links time hint");
   Check(LocalResetPredictor.Analyze(new[]{parent,timed},DateTimeOffset.Parse("2026-10-22T19:00:00Z")).Summary.Contains("已过时限"),"expired deadline");
   var hours=Post("2110595554036400151","Reset landing within the next 2 hours","2026-10-22T10:00:00Z");
   Check(LocalResetPredictor.TryDeadline(hours,out early,out late,out detail)&&late==now,"relative hours use publication timestamp");
   var winter=Post("2113162413008884100","Reset tomorrow before 10am PST","2026-01-21T23:00:00Z");
   Check(LocalResetPredictor.TryDeadline(winter,out early,out late,out detail)&&early==late,"winter PST has one deadline");
   var missingZone=Post("2113162413008884101","Reset before 10am","2026-10-22T07:00:00Z");
   Check(!LocalResetPredictor.TryDeadline(missingZone,out early,out late,out detail),"missing timezone not invented");
   var invalid=Post("2113162413008884102","Reset before 18pm PT","2026-10-22T07:00:00Z");
   Check(!LocalResetPredictor.TryDeadline(invalid,out early,out late,out detail),"invalid clock rejected");
   var nextWeek=Post("2113162413008884107","Reset next week before 10am PST","2026-10-22T07:00:00Z");
   Check(!LocalResetPredictor.TryDeadline(nextWeek,out early,out late,out detail),"unsupported next-week date not interpreted as today");
   var cancel=Post("2113162413008884103","Reset postponed","2026-10-22T11:00:00Z");
   Check(LocalResetPredictor.Analyze(new[]{parent,timed,cancel},now).Kind=="withdrawn","later postponement overrides plan");
   var complete=Post("2113162413008884104","The reset is complete","2026-10-22T11:30:00Z");
   Check(LocalResetPredictor.Analyze(new[]{parent,timed,complete},now).Kind=="completed","later completion overrides plan");
   var forged=Post("2113162413008884105","The reset is complete","2026-10-22T11:30:00Z");forged.Url="https://x.com/someone/status/"+forged.Id;
   Check(TiboStore.Clean(new[]{forged},now).Count==0,"other author URL rejected");
   Check(TiboStore.Clean(new[]{parent,parent},now).Count==1,"duplicate posts merged");
   Check(LocalResetPredictor.Analyze(new[]{Post("2113162413008884106","Reset tomorrow","2026-10-18T11:00:00Z")},now).Source==null,"old signal not current");
   Check(RadarData.SafeUrl("https://x.com/thsottiaux/status/123?redirect=evil")=="https://x.com/thsottiaux/status/123","source query stripped");
   var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"store-test-"+Guid.NewGuid().ToString("N"));var store=new TiboStore(directory);var currentPost=Post(parent.Id,parent.Text,DateTimeOffset.UtcNow.ToString("o"));var currentReply=Post(timed.Id,timed.Text,DateTimeOffset.UtcNow.ToString("o"));store.Merge(new[]{currentPost,currentReply},DateTimeOffset.UtcNow,"https://x.com/thsottiaux/with_replies");var restored=new TiboStore(directory);
   Check(restored.History.Posts.Count==2,"public history survives restart");
  }
  static async Task BrowserFixture(){
   var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"webview-test-"+Guid.NewGuid().ToString("N"));
   using(var view=new WebView2()){
    IntPtr processJob=IntPtr.Zero;int browserPid=0;
    var window=new Window {Width=400,Height=300,ShowActivated=false,ShowInTaskbar=false,Content=view};window.Show();
    try {
     await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,directory));
     browserPid=(int)view.CoreWebView2.BrowserProcessId;using(var process=Process.GetProcessById(browserPid))processJob=Native.OwnProcessTree(process.Handle);
     Check(processJob!=IntPtr.Zero,"WebView2 process tree owned by application");
     var ready=new TaskCompletionSource<bool>();view.CoreWebView2.NavigationCompleted+=(s,e)=>ready.TrySetResult(e.IsSuccess);
     string html="<html><body><article><div data-testid='User-Name'><a href='/thsottiaux'>@thsottiaux</a></div><div data-testid='tweetText'>Reset tomorrow before 10am PST.</div><a href='/thsottiaux/status/2113162413008884099'><time datetime='2026-10-22T06:55:54Z'></time></a></article><article><div data-testid='User-Name'><a href='/someone'>@someone</a></div><div data-testid='tweetText'>Reset complete</div><a href='/someone/status/2113162413008884100'><time datetime='2026-10-22T06:55:54Z'></time></a></article><article><div data-testid='User-Name'><a href='/thsottiaux'>@thsottiaux</a></div><div data-testid='tweetText'>Original post</div><div role='link'><a href='/thsottiaux/status/2113162413008884101'><time datetime='2026-10-22T07:00:00Z'></time></a></div><a href='/thsottiaux/status/2113162413008884102'><time datetime='2026-10-22T07:05:00Z'></time></a></article></body></html>";
     view.NavigateToString(html);if(await Task.WhenAny(ready.Task,Task.Delay(15000))!=ready.Task)throw new Exception("fixture navigation timed out");
     string script;using(var input=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TiboCollector.js"))using(var reader=new StreamReader(input))script=reader.ReadToEnd();
     var result=await view.CoreWebView2.ExecuteScriptAsync(script+"(document,'https://x.com/thsottiaux/with_replies')");
     var capture=new JavaScriptSerializer().Deserialize<XCapture>(result);
     Check(capture.Posts.Length==2,"DOM collector excludes other authors");
     Check(capture.Posts[1].Id=="2113162413008884102","quoted timestamp not mistaken for original post");
     Check(capture.Posts[0].ContextIds.Count==1,"same-page context preserved");
     result=await view.CoreWebView2.ExecuteScriptAsync(script+"(document,'https://other.example/thsottiaux')");
     Check(!new JavaScriptSerializer().Deserialize<XCapture>(result).IsTiboPage,"foreign host not collected");
    }finally{view.Dispose();window.Close();if(processJob!=IntPtr.Zero)Native.CloseHandle(processJob);}
    bool exited=false;try{using(var browser=Process.GetProcessById(browserPid))exited=await Task.Run(()=>browser.WaitForExit(5000));}catch(ArgumentException){exited=true;}
    Check(exited,"collector browser exits when application releases it");
   }
  }
  [STAThread] public static int Main(){
   try{Rules();var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=0;app.Startup+=async(s,e)=>{try{await BrowserFixture();Console.WriteLine("TOTAL "+checks+" checks passed");}catch(Exception error){Console.Error.WriteLine(error);result=1;}finally{app.Shutdown();}};app.Run();return result;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
  }
 }
}
