using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

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
  static Dictionary<string,object> D(params object[] fields){var d=new Dictionary<string,object>();for(int i=0;i<fields.Length;i+=2)d[(string)fields[i]]=fields[i+1];return d;}
  static Dictionary<string,object> Tweet(string id,string text,string handle="thsottiaux"){
   return D("__typename","Tweet","rest_id",id,"core",D("user_results",D("result",D("rest_id","1953337039510003712","core",D("screen_name",handle)))),"details",D("created_at_ms",DateTimeOffset.UtcNow.AddMinutes(-3).ToUnixTimeMilliseconds(),"full_text",text));
  }
  static object Entry(object tweet){return D("content",D("__typename","TimelineTimelineItem","content",D("tweet_results",D("result",tweet))));}
  static string Page(params object[] entries){var payload=Json.Write(new {data=new {timeline=new {instructions=new[]{new {__typename="TimelineAddEntries",entries=entries}}}}});return "<script>self.fixture="+Json.Write(payload)+";</script>";}
  static string WatchPage(string handle){return Page(Entry(Tweet((2113162277688070500L+Array.IndexOf(PublicXClient.WatchAccounts,handle)).ToString(),"An unrelated public original",handle)));}
  sealed class Handler : HttpMessageHandler {
   public int Count;
   public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Reply;
   protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){
    Count++;Check(request.Method==HttpMethod.Get&&request.RequestUri.Host=="x.com","runtime request is direct X GET");
    Check(!request.Headers.Contains("Cookie")&&!request.Headers.Contains("Authorization"),"anonymous request has no account credentials");return Reply(request,cancel);
   }
  }
  static HttpResponseMessage Response(string body,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new StringContent(body)};}
  static async Task PublicPages(){
   const string parent="2113162277688070445",replyId="2113162413008884099";
   var own=Tweet(parent,"Day 17/ It's reset day today. 你好 \"X\"\nnext line");own["quoted_status_result"]=D("result",Tweet("2113162277688070446","Quoted reset complete"));
   var reply=Tweet(replyId,"Lands tomorrow before 10am PST.");reply["reply_to_results"]=D("rest_id",parent);
   string profile=Page(Entry(own),Entry(Tweet("2113162277688070447","Reset complete","someoneelse")));
   string discussion=Page(Entry(own),D("content",D("__typename","TimelineTimelineModule","items",new[]{D("item",D("content",D("tweet_results",D("result",D("__typename","TweetWithVisibilityResults","tweet",reply)))))})),Entry(reply));
   var parsed=PublicXParser.Parse(profile+"<script>throw new Error('must never execute');</script>",DateTimeOffset.UtcNow);
   Check(parsed.Posts.Count==1&&parsed.Posts[0].Id==parent,"only primary own post is collected, not quotes or other authors");
   Check(parsed.Posts[0].Text.Contains("你好 \"X\"\nnext line"),"nested JSON preserves escaped Unicode and newlines");
   var thread=PublicXParser.Parse(discussion,DateTimeOffset.UtcNow);var found=thread.Posts.Single(p=>p.Id==replyId);
   Check(thread.Posts.Count==2&&found.Kind=="reply"&&found.ReplyToId==parent&&found.ContextIds.SequenceEqual(new[]{parent}),"conversation modules preserve exact reply parent and deduplicate");
   Check(PublicXParser.Parse("<html><script>self.login=true;</script></html>",DateTimeOffset.UtcNow).Posts.Count==0,"login shell is not fabricated as a successful timeline");
   Check(LocalResetPredictor.Analyze(thread.Posts,DateTimeOffset.UtcNow).Kind=="planned","collected self reply drives the local reset prediction");
   using(var http=new PublicXClient(new Handler {Reply=(r,c)=>{string handle=r.RequestUri.AbsolutePath.Split('/')[1];return Task.FromResult(Response(PublicXClient.IsWatched(handle)?WatchPage(handle):r.RequestUri.AbsoluteUri==PublicXClient.Profile?profile:discussion));}})){
    var batch=await http.Collect(CancellationToken.None);Check(batch.Posts.Count==2&&!batch.Partial,"automatic collection discovers reply without a prefilled post ID");
   }
   using(var http=new PublicXClient(new Handler {Reply=(r,c)=>Task.FromResult(Response(r.RequestUri.AbsoluteUri==PublicXClient.Profile?profile:"",r.RequestUri.AbsoluteUri==PublicXClient.Profile?HttpStatusCode.OK:HttpStatusCode.NotFound))})){
    var batch=await http.Collect(CancellationToken.None);Check(batch.Partial&&batch.Posts.Count==1,"failed discussion retains original and marks partial coverage");
   }
   var redirect=new Handler {Reply=(r,c)=>{var response=Response("",HttpStatusCode.Found);response.Headers.Location=new Uri("https://other.example/");return Task.FromResult(response);}};
   using(var http=new PublicXClient(redirect)){try{await http.ReadPage(PublicXClient.Profile,CancellationToken.None);throw new Exception("redirect should fail");}catch(XReadException){Check(redirect.Count==1,"foreign redirect blocked before network access");}}
   var limited=new Handler {Reply=(r,c)=>{var response=Response("",(HttpStatusCode)429);response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(30));return Task.FromResult(response);}};
   using(var http=new PublicXClient(limited)){
    for(int i=0;i<2;i++)try{await http.ReadPage(PublicXClient.Profile,CancellationToken.None);}catch(XReadException){}
    Check(limited.Count==1&&http.RetryAt>DateTimeOffset.UtcNow.AddMinutes(29),"rate limit respected without retry or token rotation");
   }
   var blocked=new Handler {Reply=async(r,c)=>{await Task.Delay(Timeout.Infinite,c);return Response("");}};
   using(var http=new PublicXClient(blocked))using(var cancel=new CancellationTokenSource()){
    var task=http.Collect(cancel.Token);cancel.Cancel();try{await task;throw new Exception("cancel should fail");}catch(OperationCanceledException){Check(blocked.Count==1,"shutdown cancellation interrupts collection");}
   }
   var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"public-store-"+Guid.NewGuid().ToString("N"));var store=new TiboStore(directory);store.Merge(thread.Posts,DateTimeOffset.UtcNow,PublicXClient.Profile,"limited",true);var restored=new TiboStore(directory);
   Check(restored.History.Partial&&restored.History.ReplyCount==1&&restored.History.Posts.Single(p=>p.Id==replyId).ContextIds.Contains(parent),"cache preserves reply relation, counts and partial status");
   string checkedAt=store.History.CheckedAt;
   using(var collector=new XMessageCollector(directory,store,new PublicXClient(new Handler {Reply=(r,c)=>Task.FromResult(Response("<html>login shell</html>"))}))){
    await collector.Refresh(true);Check(store.History.CheckedAt==checkedAt&&store.History.Posts.Count==2&&collector.Problem!=null,"failed refresh preserves history and does not advance successful check time");
   }
   var waiting=new Handler {Reply=async(r,c)=>{await Task.Delay(Timeout.Infinite,c);return Response("");}};
   var running=new XMessageCollector(directory,store,new PublicXClient(waiting));
   var first=running.Refresh(true);var second=running.Refresh(true);
   Check(Object.ReferenceEquals(first,second)&&waiting.Count==1,"manual and timer refresh share one in-flight collection");
   running.Dispose();await first;
   Check(store.History.CheckedAt==checkedAt&&waiting.Count==1,"disposing the widget cancels its collector without refreshing cache");
  }
  static async Task SupplementalPages(){
   const string ownId="2113162277688070700",selfId="2113162277688070701",otherId="2113162277688070702";
   var own=Tweet(ownId,"Day 17/ It's reset day today.");var self=Tweet(selfId,"Lands tomorrow before 10am PST.");self["reply_to_results"]=D("rest_id",ownId);self["reply_to_user_results"]=D("result",D("core",D("screen_name","thsottiaux")));
   var roots=new List<Dictionary<string,object>>();var now=DateTimeOffset.UtcNow;
   for(int i=0;i<5;i++){var p=Tweet((2113162277688070710L+i).ToString(),"Public parent "+i,"claudeai");Json.Obj(p["details"])["created_at_ms"]=now.AddMinutes(-10-i).ToUnixTimeMilliseconds();roots.Add(p);}
   string firstId=Convert.ToString(roots[0]["rest_id"]),firstUrl="https://x.com/claudeai/status/"+firstId;
   var reply=Tweet(otherId,"@claudeai Dot, meet Duet.");reply["reply_to_results"]=D("rest_id",firstId);reply["reply_to_user_results"]=D("result",D("core",D("screen_name","claudeai")));
   bool lateVisible=false;var late=Tweet("2113162277688070799","@claudeai Thanks for the update.");late["reply_to_results"]=D("rest_id",firstId);late["reply_to_user_results"]=D("result",D("core",D("screen_name","claudeai")));
   var spoof=Tweet("2113162277688070790","The reset is complete");Json.Obj(Json.Get(Json.Obj(Json.Get(Json.Obj(spoof["core"]),"user_results")),"result"))["rest_id"]="999999999999999999";spoof["reply_to_results"]=D("rest_id",firstId);spoof["reply_to_user_results"]=D("result",D("core",D("screen_name","claudeai")));
   roots[0]["quoted_tweet_results"]=D("result",Tweet("2113162277688070791","Quoted reset complete"));
   string otherDiscussion=Page(Entry(roots[0]),Entry(reply),Entry(reply),Entry(spoof),Entry(Tweet("2113162277688070792","Reset complete","someoneelse")));
   var requests=new List<string>();
   Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond=(r,c)=>{
    string url=r.RequestUri.AbsoluteUri;requests.Add(url);string handle=r.RequestUri.AbsolutePath.Split('/')[1];
    if(url==PublicXClient.Profile)return Task.FromResult(Response(Page(Entry(own))));
    if(handle=="thsottiaux")return Task.FromResult(Response(Page(Entry(own),Entry(self))));
    if(handle=="claudeai"){
     if(url=="https://x.com/claudeai")return Task.FromResult(Response(Page(roots.Select(p=>Entry(p)).ToArray())));
     if(url==firstUrl)return Task.FromResult(Response(lateVisible?Page(Entry(roots[0]),Entry(reply),Entry(late)):otherDiscussion));
     var root=roots.Single(p=>url.EndsWith("/"+p["rest_id"]));return Task.FromResult(Response(Page(Entry(root))));
    }
    return Task.FromResult(Response(WatchPage(handle)));
   };
   var directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"supplemental-"+Guid.NewGuid().ToString("N"));
   PublicXBatch initial;
   using(var client=new PublicXClient(new Handler {Reply=respond},directory))initial=await client.Collect(CancellationToken.None);
   Check(!initial.Partial&&initial.Posts.Count==3&&initial.SupplementalDiscussions==4,"supplemental collection discovers other reply while retaining own posts and self replies");
   var found=initial.Posts.Single(p=>p.Id==otherId);
   Check(found.AuthorId=="1953337039510003712"&&found.ReplyToAuthor=="claudeai"&&found.ReplyToId==firstId&&found.DiscoveredOn==firstUrl&&found.DiscoveryProfile=="https://x.com/claudeai","other reply preserves verified author, parent and live discovery chain");
   Check(!initial.Posts.Any(p=>p.Id=="2113162277688070790"||p.Id=="2113162277688070791"||p.Id=="2113162277688070792"),"spoofed author ID, quoted text, foreign authors and duplicates excluded");
   Check(requests.Count(u=>u.StartsWith("https://x.com/claudeai/status/"))==2&&initial.WatchedParents==7,"discussion budget is bounded per watched account");
   var store=new TiboStore(directory);store.Merge(initial.Posts,DateTimeOffset.UtcNow,PublicXClient.Profile,"partial coverage",false);
   Check(store.History.SelfReplyCount==1&&store.History.OtherReplyCount==1&&store.History.ReplyCount==2,"self and other reply counts remain distinct");
   var restored=new TiboStore(directory);var cached=restored.History.Posts.Single(p=>p.Id==otherId);
   Check(cached.ReplyToAuthor=="claudeai"&&cached.DiscoveredOn==firstUrl&&restored.History.OtherReplyCount==1,"restart preserves other-reply label and discovery provenance");
   Check(LocalResetPredictor.Analyze(initial.Posts,DateTimeOffset.UtcNow).Source.Id!=otherId,"unrelated other reply does not replace a reset forecast");
   requests.Clear();PublicXBatch second;
   using(var client=new PublicXClient(new Handler {Reply=respond},directory))second=await client.Collect(CancellationToken.None);
   Check(!requests.Contains(firstUrl)&&requests.Contains("https://x.com/claudeai/status/"+roots[2]["rest_id"])&&requests.Contains("https://x.com/claudeai/status/"+roots[3]["rest_id"]),"parent rotation survives restart and visits previously unchecked discussions");
   store.Merge(second.Posts,DateTimeOffset.UtcNow,PublicXClient.Profile,"partial coverage",false);
   Check(store.History.Posts.Any(p=>p.Id==otherId)&&store.History.OtherReplyCount==0,"later rounds preserve discovered replies while counts describe this round only");
   requests.Clear();
   using(var client=new PublicXClient(new Handler {Reply=(r,c)=>r.RequestUri.AbsoluteUri=="https://x.com/claudeai"?Task.FromResult(Response("",HttpStatusCode.Forbidden)):respond(r,c)})){
    var batch=await client.Collect(CancellationToken.None);
    Check(batch.Partial&&batch.Posts.Count==2&&client.RetryAt>DateTimeOffset.UtcNow&&!requests.Any(u=>u.Contains("/theo")||u.Contains("/JustLingonberry")),"supplemental access refusal retains originals and stops all further X reads");
   }
   requests.Clear();
   lateVisible=true;
   using(var client=new PublicXClient(new Handler {Reply=(r,c)=>r.RequestUri.AbsoluteUri=="https://x.com/claudeai"?Task.FromResult(Response("",HttpStatusCode.NotFound)):respond(r,c)},directory)){
    var batch=await client.Collect(CancellationToken.None);
    Check(batch.Partial&&requests.Any(u=>u.StartsWith("https://x.com/claudeai/status/"))&&requests.Any(u=>u=="https://x.com/theo"),"failed profile still polls saved parents and other accounts without dropping history");
    Check(batch.Posts.Any(p=>p.Id=="2113162277688070799"&&p.DiscoveredOn==firstUrl),"a later reply is discovered by revisiting a saved parent without a working profile");
   }
   Check(!PublicXClient.Allowed(new Uri("https://x.com/unconfigured/status/2113162277688070700"))&&!PublicXClient.Allowed(new Uri("https://x.com/claudeai/with_replies"))&&!PublicXClient.Allowed(new Uri("https://x.com/claudeai/status/2113162277688070700?x=1"))&&!PublicXClient.Allowed(new Uri("https://api.x.com/claudeai")),"expanded allowlist excludes unconfigured authors, login-only routes, queries and non-page hosts");
   var pool=new PublicXParentPool(null);var expired=Tweet("2113162277688070800","old","claudeai");Json.Obj(expired["details"])["created_at_ms"]=now.AddDays(-8).ToUnixTimeMilliseconds();
   pool.Add("claudeai",PublicXParser.Parse(Page(Entry(expired)),now,"claudeai").Posts,now);
   Check(pool.Count==0,"parent pool expires sources older than seven days");
  }
  [STAThread] public static int Main(){
   try{Rules();PublicPages().GetAwaiter().GetResult();SupplementalPages().GetAwaiter().GetResult();Console.WriteLine("TOTAL "+checks+" checks passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
  }
 }
}
