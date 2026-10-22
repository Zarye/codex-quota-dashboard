using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuotaMini {
 sealed class PublicXPage {
  public List<TiboPost> Posts=new List<TiboPost>();
  public int PayloadCount;
 }
 static class PublicXParser {
  const int MaxBytes=2*1024*1024;
  static readonly Regex Scripts=new Regex(@"<script\b[^>]*>([\s\S]*?)</script>",RegexOptions.IgnoreCase);
  static object At(object value,params string[] keys){foreach(string key in keys)value=Json.Get(Json.Obj(value),key);return value;}
  static string Text(object value){return Convert.ToString(value,CultureInfo.InvariantCulture)??"";}
  static IEnumerable<object> Payloads(string html){
   var json=new JavaScriptSerializer {MaxJsonLength=4*MaxBytes,RecursionLimit=128};
   foreach(Match scriptMatch in Scripts.Matches(html)){
    string script=scriptMatch.Groups[1].Value;
    for(int i=0;i<script.Length;i++){
     if(script[i]!='"')continue;int end=i+1;
     for(;end<script.Length;end++){if(script[end]=='\\'){end++;continue;}if(script[end]=='"')break;}
     if(end>=script.Length)break;object value;
     try{value=json.DeserializeObject(script.Substring(i,end-i+1));}catch(ArgumentException){continue;}
     i=end;
     // Only decode JSON string literals. Never evaluate page JavaScript.
     for(int depth=0;depth<4&&value is string;depth++){
      try{value=json.DeserializeObject((string)value);}catch(ArgumentException){break;}
      if(value is Dictionary<string,object>||value is object[]||value is System.Collections.ArrayList)yield return value;
     }
    }
   }
  }
  static IEnumerable<object> Timelines(object value,int depth=0){
   if(value==null||depth>80)yield break;var dict=Json.Obj(value);
   if(dict!=null){object instructions=Json.Get(dict,"instructions");if(instructions is object[]||instructions is System.Collections.ArrayList)yield return instructions;
    foreach(var child in dict.Values)foreach(var timeline in Timelines(child,depth+1))yield return timeline;
   }else foreach(var child in RadarData.Items(value))foreach(var timeline in Timelines(child,depth+1))yield return timeline;
  }
  static IEnumerable<object> PrimaryResults(object content){
   object item=At(content,"content")??At(content,"itemContent")??At(content,"item_content");
   object result=At(item,"tweet_results","result");if(result!=null)yield return result;
   foreach(var moduleItem in RadarData.Items(At(content,"items")))foreach(var child in PrimaryResults(At(moduleItem,"item")))yield return child;
  }
  static TiboPost Normalize(object tweet,string handle){
   if(Text(At(tweet,"__typename"))=="TweetWithVisibilityResults")tweet=At(tweet,"tweet");
   if(Text(At(tweet,"__typename"))!="Tweet")return null;object legacy=At(tweet,"legacy");
   if(At(legacy,"retweeted_status_result")!=null||At(tweet,"retweeted_status_result")!=null||At(tweet,"retweeted_tweet_results")!=null)return null;
   object user=At(tweet,"core","user_results","result");
   if(!String.Equals(Text(At(user,"core","screen_name")??At(user,"legacy","screen_name")),handle,StringComparison.OrdinalIgnoreCase))return null;
   string authorId=Text(At(user,"rest_id")??At(legacy,"user_id_str"));
   if(handle.Equals("thsottiaux",StringComparison.OrdinalIgnoreCase)&&authorId!="1953337039510003712")return null;
   string id=Text(At(tweet,"rest_id")??At(legacy,"id_str"));
   string text=Text(At(tweet,"note_tweet","note_tweet_results","result","text")??At(tweet,"details","full_text")??At(legacy,"full_text"));
   long stamp;DateTimeOffset date;
   if(Int64.TryParse(Text(At(tweet,"details","created_at_ms")),out stamp)){try{date=DateTimeOffset.FromUnixTimeMilliseconds(stamp);}catch(ArgumentOutOfRangeException){return null;}}
   else if(!DateTimeOffset.TryParse(Text(At(legacy,"created_at")),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date))return null;
   string parent=Text(At(tweet,"reply_to_results","rest_id")??At(legacy,"in_reply_to_status_id_str"));
   var post=new TiboPost {Id=id,AuthorId=authorId,Text=text,PublishedAt=date.ToUniversalTime().ToString("o"),Url="https://x.com/"+handle+"/status/"+id,Kind=parent!=""||At(tweet,"reply_to_user_results")!=null?"reply":"post",ReplyToId=parent,ReplyToAuthor=Text(At(tweet,"reply_to_user_results","result","core","screen_name")??At(legacy,"in_reply_to_screen_name"))};
   if(Regex.IsMatch(parent,@"^\d{10,25}$"))post.ContextIds.Add(parent);return post;
  }
  public static PublicXPage Parse(string html,DateTimeOffset now){return Parse(html,now,"thsottiaux");}
  internal static PublicXPage Parse(string html,DateTimeOffset now,string handle){
   if(!Regex.IsMatch(handle??"",@"^[A-Za-z0-9_]{1,15}$"))throw new ArgumentException("X 账号名无效");
   if(html==null||html.Length>MaxBytes)throw new InvalidDataException("X 页面大小超出读取范围");
   var output=new PublicXPage();var posts=new List<TiboPost>();
   foreach(var payload in Payloads(html)){output.PayloadCount++;
    foreach(var instructions in Timelines(payload))foreach(var instruction in RadarData.Items(instructions)){
     var entries=RadarData.Items(At(instruction,"entries")).ToList();if(At(instruction,"entry")!=null)entries.Add(At(instruction,"entry"));
     foreach(var entry in entries){object content=At(entry,"content");if(At(content,"promoted_metadata")!=null||At(content,"promotedMetadata")!=null)continue;
      foreach(var result in PrimaryResults(content)){var post=Normalize(result,handle);if(post!=null)posts.Add(post);}
     }
    }
   }
   output.Posts=posts.Where(p=>Regex.IsMatch(p.Id??"",@"^\d{10,25}$")&&!String.IsNullOrWhiteSpace(p.Text)&&p.Text.Length<=12000&&RadarData.Date(p.PublishedAt)>=now.AddDays(-180)&&RadarData.Date(p.PublishedAt)<=now.AddMinutes(5)).GroupBy(p=>p.Id).Select(g=>g.Last()).OrderByDescending(p=>RadarData.Date(p.PublishedAt)).ToList();return output;
  }
 }
 sealed class XReadException : Exception {
  public readonly DateTimeOffset? RetryAt;
  public XReadException(string message,DateTimeOffset? retryAt=null):base(message){RetryAt=retryAt;}
 }
 sealed class PublicXBatch {
  public List<TiboPost> Posts=new List<TiboPost>();
  public DateTimeOffset CheckedAt;
  public bool Partial;
  public string Detail;
  public int SupplementalDiscussions,WatchedParents;
 }
 sealed class PublicXParent {
  public string Id,Author,Url,PublishedAt,LastCheckedAt;
 }
 sealed class PublicXParentPool {
  readonly string file;
  List<PublicXParent> parents=new List<PublicXParent>();
  public int Count {get{return parents.Count;}}
  public PublicXParentPool(string directory){
   if(directory==null)return;file=Path.Combine(directory,"tibo-public-parents.json");
   try{if(new FileInfo(file).Length<=256*1024){parents=new JavaScriptSerializer().Deserialize<List<PublicXParent>>(File.ReadAllText(file))??parents;Clean(DateTimeOffset.UtcNow);}}catch{parents=new List<PublicXParent>();}
  }
  void Clean(DateTimeOffset now){
   parents=parents.Where(p=>p!=null&&PublicXClient.IsWatched(p.Author)&&Regex.IsMatch(p.Id??"",@"^\d{10,25}$")&&p.Url=="https://x.com/"+p.Author+"/status/"+p.Id&&PublicXClient.Allowed(new Uri(p.Url))&&RadarData.Date(p.PublishedAt)>=now.AddDays(-7)&&RadarData.Date(p.PublishedAt)<=now.AddMinutes(5)).GroupBy(p=>p.Id).Select(g=>g.First()).GroupBy(p=>p.Author,StringComparer.OrdinalIgnoreCase).SelectMany(g=>g.OrderByDescending(p=>RadarData.Date(p.PublishedAt)).Take(10)).ToList();
  }
  public void Add(string author,IEnumerable<TiboPost> originals,DateTimeOffset now){
   foreach(var post in originals.Where(p=>p.Kind=="post"))if(!parents.Any(p=>p.Id==post.Id))parents.Add(new PublicXParent {Id=post.Id,Author=author,Url=post.Url,PublishedAt=post.PublishedAt});
   Clean(now);
  }
  public List<PublicXParent> Select(string author,DateTimeOffset now){
   Clean(now);return parents.Where(p=>p.Author.Equals(author,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>RadarData.Date(p.LastCheckedAt)??DateTimeOffset.MinValue).ThenByDescending(p=>RadarData.Date(p.PublishedAt)).Take(2).ToList();
  }
  public void Mark(PublicXParent parent){parent.LastCheckedAt=DateTimeOffset.UtcNow.ToString("o");}
  public void Save(){
   if(file==null)return;Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(parents),Encoding.UTF8);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
  }
 }
 sealed class PublicXClient : IDisposable {
  public const string Profile="https://x.com/thsottiaux";
  internal static readonly string[] WatchAccounts={"claudeai","theo","JustLingonberry"};
  readonly HttpClient client;
  readonly PublicXParentPool parents;
  bool disposed;
  public DateTimeOffset? RetryAt {get;private set;}
  public PublicXClient(string directory=null):this(new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate},directory){}
  internal PublicXClient(HttpMessageHandler handler,string directory=null){
   parents=new PublicXParentPool(directory);
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaDashboard/0.4 public-page-reader");client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
  }
  internal static bool IsWatched(string handle){return WatchAccounts.Contains(handle,StringComparer.OrdinalIgnoreCase);}
  internal static bool Allowed(Uri url){
   if(url==null||url.Scheme!="https"||url.Host!="x.com"||!url.IsDefaultPort||!String.IsNullOrEmpty(url.UserInfo)||!String.IsNullOrEmpty(url.Query)||!String.IsNullOrEmpty(url.Fragment))return false;
   var match=Regex.Match(url.AbsolutePath,@"^/([A-Za-z0-9_]{1,15})(?:/status/\d{10,25})?$");return match.Success&&(match.Groups[1].Value.Equals("thsottiaux",StringComparison.OrdinalIgnoreCase)||IsWatched(match.Groups[1].Value));
  }
  internal async Task<string> ReadPage(string target,CancellationToken cancel){
   if(disposed)throw new ObjectDisposedException("PublicXClient");
   if(RetryAt.HasValue&&RetryAt.Value>DateTimeOffset.UtcNow)throw new XReadException("X 暂停访问，稍后自动重试",RetryAt);
   Uri url=new Uri(target);
   for(int hop=0;hop<3;hop++){
    if(!Allowed(url))throw new XReadException("X 返回的页面地址不在采集范围内");
    using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
     deadline.CancelAfter(TimeSpan.FromSeconds(15));
     using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)){
      int status=(int)response.StatusCode;
      if(status==401||status==403||status==429){
       var retry=response.Headers.RetryAfter;RetryAt=DateTimeOffset.UtcNow.AddMinutes(15);
       if(retry!=null){var server=retry.Date??(retry.Delta.HasValue?DateTimeOffset.UtcNow.Add(retry.Delta.Value):(DateTimeOffset?)null);if(server.HasValue&&server>RetryAt)RetryAt=server;}
       throw new XReadException(status==429?"X 访问频率受限":"X 暂时拒绝匿名访问",RetryAt);
      }
      if(status>=300&&status<400&&response.Headers.Location!=null){url=new Uri(url,response.Headers.Location);continue;}
      if(!response.IsSuccessStatusCode)throw new XReadException("X 页面暂时无法读取（"+status+"）");
      using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){
       var buffer=new byte[16384];int read;while((read=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){if(output.Length+read>2*1024*1024)throw new XReadException("X 页面大小超出读取范围");output.Write(buffer,0,read);}
       return Encoding.UTF8.GetString(output.ToArray());
      }
     }
    }
   }
   throw new XReadException("X 页面跳转过多");
  }
  public async Task<PublicXBatch> Collect(CancellationToken cancel){
   var batch=new PublicXBatch();var posts=new Dictionary<string,TiboPost>();var errors=new List<string>();bool readable=false;
   try{
    var profile=PublicXParser.Parse(await ReadPage(Profile,cancel).ConfigureAwait(false),DateTimeOffset.UtcNow);
    if(profile.Posts.Count==0)throw new XReadException("Tibo 主页未返回可读消息");readable=true;
    foreach(var post in profile.Posts){post.DiscoveredOn=Profile;post.DiscoveryProfile=Profile;posts[post.Id]=post;}
    foreach(var original in profile.Posts.Where(p=>p.Kind=="post").Take(3)){
     cancel.ThrowIfCancellationRequested();if(RetryAt.HasValue&&RetryAt>DateTimeOffset.UtcNow)break;
     try{
      var page=PublicXParser.Parse(await ReadPage(original.Url,cancel).ConfigureAwait(false),DateTimeOffset.UtcNow);
      if(page.Posts.Count==0)throw new XReadException("本人讨论页未返回消息");
      foreach(var post in page.Posts){post.DiscoveredOn=original.Url;post.DiscoveryProfile=Profile;posts[post.Id]=post;}
     }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;errors.Add("本人讨论页读取超时");}
     catch(XReadException error){errors.Add(error.Message);}
     catch(HttpRequestException){errors.Add("本人讨论页连接失败");}
    }
   }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;errors.Add("Tibo 主页读取超时");}
   catch(XReadException error){errors.Add(error.Message);}
   catch(HttpRequestException){errors.Add("Tibo 主页连接失败");}
   foreach(string handle in WatchAccounts){
    cancel.ThrowIfCancellationRequested();if(RetryAt.HasValue&&RetryAt>DateTimeOffset.UtcNow)break;string home="https://x.com/"+handle;
    try{
     var profile=PublicXParser.Parse(await ReadPage(home,cancel).ConfigureAwait(false),DateTimeOffset.UtcNow,handle);
     if(profile.Posts.Count==0)throw new XReadException("@"+handle+" 主页未返回消息");readable=true;parents.Add(handle,profile.Posts,DateTimeOffset.UtcNow);
    }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;errors.Add("@"+handle+" 主页读取超时");}
    catch(XReadException error){errors.Add(error.Message);}
    catch(HttpRequestException){errors.Add("@"+handle+" 主页连接失败");}
    foreach(var parent in parents.Select(handle,DateTimeOffset.UtcNow)){
     cancel.ThrowIfCancellationRequested();if(RetryAt.HasValue&&RetryAt>DateTimeOffset.UtcNow)break;parents.Mark(parent);
     try{
      string html=await ReadPage(parent.Url,cancel).ConfigureAwait(false);var now=DateTimeOffset.UtcNow;
      var context=PublicXParser.Parse(html,now,handle);
      if(!context.Posts.Any(p=>p.Id==parent.Id))throw new XReadException("@"+handle+" 讨论页父帖未核实");
      readable=true;batch.SupplementalDiscussions++;
      foreach(var post in PublicXParser.Parse(html,now).Posts){
       if(post.Kind!="reply"||String.IsNullOrEmpty(post.ReplyToId)||String.IsNullOrEmpty(post.ReplyToAuthor)||post.ReplyToAuthor.Equals("thsottiaux",StringComparison.OrdinalIgnoreCase))continue;
       post.DiscoveredOn=parent.Url;post.DiscoveryProfile=home;posts[post.Id]=post;
      }
     }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;errors.Add("@"+handle+" 讨论页读取超时");}
     catch(XReadException error){errors.Add(error.Message);}
     catch(HttpRequestException){errors.Add("@"+handle+" 讨论页连接失败");}
    }
   }
   cancel.ThrowIfCancellationRequested();
   try{parents.Save();}catch(IOException){errors.Add("观察父帖缓存未保存");}catch(UnauthorizedAccessException){errors.Add("观察父帖缓存未保存");}
   if(!readable)throw new XReadException(errors.Count==0?"X 公开页面未返回可读消息":String.Join("；",errors.Distinct()),RetryAt);
   batch.Posts=posts.Values.OrderByDescending(p=>RadarData.Date(p.PublishedAt)).ToList();batch.CheckedAt=DateTimeOffset.UtcNow;batch.Partial=errors.Count>0;batch.Detail=String.Join("；",errors.Distinct());batch.WatchedParents=parents.Count;return batch;
  }
  public void Dispose(){if(disposed)return;disposed=true;client.Dispose();}
 }
}
