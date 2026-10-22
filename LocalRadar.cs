using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexQuotaMini {
 static class RadarData {
  public static readonly TimeZoneInfo Pacific=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
  public static readonly TimeSpan Beijing=TimeSpan.FromHours(8);
  public static string Str(Dictionary<string,object> d,string key){return Convert.ToString(Json.Get(d,key))??"";}
  public static IEnumerable<object> Items(object value){var rows=value as IEnumerable;if(rows==null||value is string)yield break;foreach(var row in rows)yield return row;}
  public static DateTimeOffset? Date(string text){DateTimeOffset value;return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out value)?value.ToUniversalTime():(DateTimeOffset?)null;}
  public static string SafeUrl(string value){Uri url;if(!Uri.TryCreate(value,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="x.com"||!String.IsNullOrEmpty(url.UserInfo))return "";return Regex.IsMatch(url.AbsolutePath,@"^/thsottiaux/status/\d+$",RegexOptions.IgnoreCase)?url.GetLeftPart(UriPartial.Path):"";}
 }
 sealed class TiboPost {
  public string Id {get;set;}
  public string Text {get;set;}
  public string PublishedAt {get;set;}
  public string Url {get;set;}
  public string Kind {get;set;}
  public string ReplyToId {get;set;}
  public List<string> ContextIds=new List<string>();
 }
 sealed class TiboHistory {
  public int Version=1;
  public string CheckedAt,PageUrl;
  public string CoverageNote;
  public int OriginalCount,ReplyCount;
  public bool Partial;
  public List<TiboPost> Posts=new List<TiboPost>();
 }
 sealed class ResetAssessment {
  public string Kind="unknown",Summary="等待直接采集 Tibo 消息",Reason="看板启动后自动读取 X 公开页面，无需登录。",Strength="样本不足";
  public TiboPost Source;
  public DateTimeOffset? EarliestDeadline,LatestDeadline;
 }
 sealed class TiboStore {
  readonly string file;
  public TiboHistory History {get;private set;}
  public TiboStore(string directory){file=Path.Combine(directory,"tibo-direct-history.json");History=new TiboHistory();try{if(new FileInfo(file).Length>8*1024*1024)return;var restored=new JavaScriptSerializer {MaxJsonLength=8*1024*1024}.Deserialize<TiboHistory>(File.ReadAllText(file));if(restored!=null&&restored.Version==1){History=restored;History.Posts=Clean(restored.Posts,DateTimeOffset.UtcNow);}}catch{}}
  public static List<TiboPost> Clean(IEnumerable<TiboPost> posts,DateTimeOffset now){
   return (posts??new List<TiboPost>()).Where(p=>p!=null&&Regex.IsMatch(p.Id??"",@"^\d{10,25}$")&&RadarData.SafeUrl(p.Url)=="https://x.com/thsottiaux/status/"+p.Id&&!String.IsNullOrWhiteSpace(p.Text)&&p.Text.Length<=12000&&RadarData.Date(p.PublishedAt).HasValue&&RadarData.Date(p.PublishedAt)<=now.AddMinutes(5)&&RadarData.Date(p.PublishedAt)>=now.AddDays(-180)).GroupBy(p=>p.Id).Select(g=>g.Last()).OrderByDescending(p=>RadarData.Date(p.PublishedAt)).Take(2000).ToList();
  }
  public void Merge(IEnumerable<TiboPost> posts,DateTimeOffset now,string pageUrl){
   Merge(posts,now,pageUrl,"近期原帖及公开讨论中的作者回复，回复覆盖不完整。",false);
  }
  public void Merge(IEnumerable<TiboPost> posts,DateTimeOffset now,string pageUrl,string coverageNote,bool partial){
   var incoming=Clean(posts,now);
   var next=new TiboHistory {Posts=Clean(History.Posts.Concat(incoming),now),CheckedAt=now.ToString("o"),PageUrl=pageUrl,CoverageNote=coverageNote,Partial=partial,OriginalCount=incoming.Count(p=>p.Kind=="post"),ReplyCount=incoming.Count(p=>p.Kind=="reply")};
   Directory.CreateDirectory(Path.GetDirectoryName(file));string temporary=file+".tmp";File.WriteAllText(temporary,new JavaScriptSerializer {MaxJsonLength=8*1024*1024}.Serialize(next),Encoding.UTF8);if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);History=next;
  }
 }
 static class LocalResetPredictor {
  public const string Version="local-rules-v1";
  static bool Has(string text,string pattern){return Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);}
  static string Normal(string text){return Regex.Replace((text??"").Replace('’','\''),@"\s+"," ").Trim();}
  public static string Classify(string original){
   string text=Normal(original);
   if(Has(text,@"\b(?:git reset|password reset|reset (?:your |the |my )?password|factory reset)\b"))return "other";
   if(Has(text,@"\b(?:banked reset|reset cards?|reset bank)\b"))return "banked";
   bool reset=Has(text,@"\bresets?\b");
   if(!reset)return Has(text,@"\b(?:lands?|landing)\b")&&Has(text,@"\b(?:tomorrow|today|before|within|hours?|eod)\b")?"context":"other";
   if(Has(text,@"\b(?:no reset|not resetting|won't reset|will not reset|reset.{0,25}(?:cancelled|canceled|postponed|will not|won't|has not|not yet)|(?:cancelled|canceled|postponed).{0,25}reset)\b"))return "withdrawn";
   if(Has(text,@"\b(?:reset.{0,25}(?:will|tomorrow|before|planned|soon|eod)|(?:will|tomorrow|before|planned|soon|eod).{0,40}reset|it's reset day today)\b"))return Has(text,@"\b(?:hope|maybe|might|could|wish)\b")||text.EndsWith("?")?"hint":"planned";
   if(Has(text,@"\b(?:resets?\s+(?:(?:has|have|is|are|all|been)\s+)*(?:done|completed?|processed|propagated|applied)|(?:all|everyone)\s+(?:has\s+been\s+)?reset|reset.{0,20}enjoy|reset.{0,80}all propagated)\b"))return "completed";
   return "hint";
  }
  public static ResetAssessment Analyze(IEnumerable<TiboPost> input,DateTimeOffset now){
   var posts=TiboStore.Clean(input,now).Where(p=>RadarData.Date(p.PublishedAt)>=now.AddDays(-3)).ToList();
   var candidates=new List<ResetAssessment>();
   foreach(var post in posts){
    string kind=Classify(post.Text);if(kind=="other")continue;
    var result=new ResetAssessment {Kind=kind,Source=post};
    bool linked=false;
    if(kind=="context"){
     var stamp=RadarData.Date(post.PublishedAt).Value;
     linked=posts.Any(p=>p.Id!=post.Id&&(post.ContextIds??new List<string>()).Contains(p.Id)&&Classify(p.Text)=="planned"&&RadarData.Date(p.PublishedAt)<=stamp&&stamp-RadarData.Date(p.PublishedAt).Value<=TimeSpan.FromHours(1));
     if(!linked){result.Summary="发现时间线索 · 需核对上下文";result.Strength="弱线索";result.Reason="时间表达未与同页、此前一小时内的重置预告关联，暂不判定为重置。";candidates.Add(result);continue;}
     result.Kind="planned";
    }
    if(result.Kind=="banked"){result.Summary="发现重置卡消息 · 查看原文";result.Strength="发卡线索";result.Reason="备用重置卡消息单独分类，不据此判断全局额度已重置。";}
    else if(result.Kind=="withdrawn"){result.Summary="重置预告取消或推迟 · 待后续";result.Strength="变更信号";result.Reason="检测到否定、取消或推迟表述；请核对原帖及后续消息。";}
    else if(result.Kind=="completed"){result.Summary="发现完成公告 · 账号待核实";result.Strength="完成声明";result.Reason="公开文字包含完成表述。个人账号是否到账，仍以看板的账号额度读数为准。";}
    else if(result.Kind=="hint"){result.Summary="有重置相关发言 · 时间未确认";result.Strength="弱线索";result.Reason="关键词规则无法确认承诺或完成状态，保留原文供核对。";}
    else {
     DateTimeOffset early,late;string explanation;
     if(TryDeadline(post,out early,out late,out explanation)){
      result.EarliestDeadline=early;result.LatestDeadline=late;
      result.Summary=now>late?"预告已过时限 · 等待后续":"预告："+early.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm")+(late!=early?"–"+late.ToOffset(RadarData.Beijing).ToString("HH:mm"):"")+" 前";
      result.Reason=explanation;result.Strength=now>late?"预告已过时限":"有时间预告";
     }else{result.Summary="发现重置预告 · 时间未确认";result.Reason="检测到预告措辞，但未解析到可验证的截止时刻。";result.Strength="预告线索";}
     if(linked)result.Reason+="\n根据父帖关联到此前一小时内的重置预告；时间含义仍由本地规则推定。";
    }
    candidates.Add(result);
   }
   var latest=candidates.FirstOrDefault();
   if(latest==null)return new ResetAssessment {Summary=posts.Count==0?"等待直接采集 Tibo 消息":"近三天暂无明确重置信号",Reason="只使用本机直接采集的公开消息，不读取第三方预测。"};
   if(latest.Kind=="hint"||latest.Kind=="context"){
    var stronger=candidates.FirstOrDefault(p=>p.Kind=="planned"&&p.LatestDeadline.HasValue&&p.LatestDeadline>=now);
    var stop=candidates.FirstOrDefault(p=>p.Kind=="completed"||p.Kind=="withdrawn");
    if(stronger!=null&&(stop==null||RadarData.Date(stronger.Source.PublishedAt)>RadarData.Date(stop.Source.PublishedAt)))latest=stronger;
   }
   latest.Reason+="\n\n模型："+Version+"，仅本地规则，不调用推理模型。\n尚无校准与回测数据，不输出精确百分比。\n采集历史仅覆盖程序实际读取到的消息，可能有遗漏。";
   return latest;
  }
  public static bool TryDeadline(TiboPost post,out DateTimeOffset early,out DateTimeOffset late,out string explanation){
   early=late=DateTimeOffset.MinValue;explanation="";var stamp=RadarData.Date(post.PublishedAt);if(!stamp.HasValue)return false;
   string text=Normal(post.Text);
   if(Has(text,@"\bnext week\b"))return false;
   var hours=Regex.Match(text,@"\b(?:within\s+(?:the\s+)?next|within|in)\s+(\d{1,2})\s+hours?\b",RegexOptions.IgnoreCase);
   if(hours.Success){int amount=Int32.Parse(hours.Groups[1].Value);if(amount<1||amount>48)return false;early=late=stamp.Value.AddHours(amount);explanation="按原帖发布时间加 "+amount+" 小时计算截止时间（北京时间）。截止时间不等于保证到账时间。";return true;}
   var before=Regex.Match(text,@"\b(?:before|by)\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)\s*(PST|PDT|PT)\b",RegexOptions.IgnoreCase);
   var eod=Regex.Match(text,@"\bEOD\s+(PST|PDT|PT)\b",RegexOptions.IgnoreCase);
   if(!before.Success&&!eod.Success)return false;
   int rawHour=before.Success?Int32.Parse(before.Groups[1].Value):11;if(rawHour<1||rawHour>12)return false;
   int hour=before.Success?rawHour%12+(before.Groups[3].Value.Equals("pm",StringComparison.OrdinalIgnoreCase)?12:0):23;
   int minute=before.Success?(before.Groups[2].Success?Int32.Parse(before.Groups[2].Value):0):59;if(minute>59)return false;
   var date=TimeZoneInfo.ConvertTime(stamp.Value,RadarData.Pacific).Date;
   if(Has(text,@"\btomorrow\b"))date=date.AddDays(1);
   DateTime wall=DateTime.SpecifyKind(date.AddHours(hour).AddMinutes(minute),DateTimeKind.Unspecified);
   if(RadarData.Pacific.IsInvalidTime(wall)||RadarData.Pacific.IsAmbiguousTime(wall))return false;
   string zone=before.Success?before.Groups[4].Value:eod.Groups[1].Value;
   var local=new DateTimeOffset(wall,RadarData.Pacific.GetUtcOffset(wall));
   var literal=new DateTimeOffset(wall,zone.Equals("PST",StringComparison.OrdinalIgnoreCase)?TimeSpan.FromHours(-8):zone.Equals("PDT",StringComparison.OrdinalIgnoreCase)?TimeSpan.FromHours(-7):RadarData.Pacific.GetUtcOffset(wall));
   early=literal<local?literal:local;late=literal>local?literal:local;
   explanation="按发帖时的太平洋当地日期解释 today/tomorrow，再换算为北京时间；这依赖原帖以太平洋当地日期表达的假设。";
   if(early!=late)explanation+="\n原帖时区缩写与当地夏令时不一致，保留两个可能截止时刻，二者并非到账区间。";
   explanation+="\n预告截止时刻不保证个人账号到账。";return true;
  }
 }
}
