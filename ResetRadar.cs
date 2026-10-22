using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexQuotaMini {
 sealed class RadarForecast {
  public string Date,EvaluatedAt,Reason;
  public double? Probability;
  public bool Stale;
  public List<string> Sources=new List<string>();
 }
 sealed class RadarSnapshot {
  public string FeedCheckedAt,RetrievedAt,SignalText,SignalUrl,SignalAt,SignalKind;
  public bool FeedAvailable,FeedStale,SignalActive,ForecastAvailable;
  public double ForecastMaxAgeSeconds=21600;
  public List<RadarForecast> Forecasts=new List<RadarForecast>();
 }
 static class RadarData {
  public static readonly TimeZoneInfo Pacific=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
  public static readonly TimeSpan Beijing=TimeSpan.FromHours(8);
  public static string Str(Dictionary<string,object> d,string key) {return Convert.ToString(Json.Get(d,key))??"";}
  public static bool Flag(Dictionary<string,object> d,string key) {return Json.Get(d,key) is bool&&(bool)Json.Get(d,key);}
  public static DateTimeOffset? Date(string text) {DateTimeOffset value;return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out value)?value.ToUniversalTime():(DateTimeOffset?)null;}
  public static IEnumerable<object> Items(object value) {var items=value as IEnumerable;if(items==null||value is string)yield break;foreach(var item in items)yield return item;}
  public static string SafeUrl(string value) {
   Uri url;if(!Uri.TryCreate(value,UriKind.Absolute,out url)||url.Scheme!="https"||!String.IsNullOrEmpty(url.UserInfo))return "";
   return (url.Host=="x.com"&&url.AbsolutePath.StartsWith("/thsottiaux/status/",StringComparison.Ordinal))?url.AbsoluteUri:"";
  }
  public static void ApplyFeed(RadarSnapshot snapshot,Dictionary<string,object> data,DateTimeOffset now) {
   var profile=Json.Obj(Json.Get(data,"profile"));if(Str(profile,"handle")!="thsottiaux")throw new InvalidDataException("公开信源账号不匹配");
   var stamp=Date(Str(data,"fetched_at"));if(!stamp.HasValue)throw new InvalidDataException("公开信源没有更新时间");
   var signal=Json.Obj(Json.Get(data,"signal"));
   snapshot.FeedCheckedAt=stamp.Value.ToString("o");snapshot.FeedAvailable=true;
   snapshot.FeedStale=Flag(data,"stale")||stamp>now.AddMinutes(5)||now-stamp.Value>TimeSpan.FromMinutes(30);
   snapshot.SignalText=Str(signal,"summary");snapshot.SignalUrl=SafeUrl(Str(signal,"url"));snapshot.SignalAt=Str(signal,"at");snapshot.SignalKind=Str(signal,"kind");snapshot.SignalActive=Flag(signal,"active");
  }
  public static void ApplyOverview(RadarSnapshot snapshot,Dictionary<string,object> data) {
   var daily=Json.Obj(Json.Get(data,"daily_forecast"));if(daily==null||!Flag(daily,"available")||Str(daily,"timezone")!="Asia/Shanghai")throw new InvalidDataException("当日预测暂不可用");
   var forecasts=new List<RadarForecast>();
   foreach(string key in new[]{"today","tomorrow"}) {
    var day=Json.Obj(Json.Get(daily,key));if(day==null)continue;
    object raw=Json.Get(day,"probability");double number=Json.Num(raw,double.NaN);
    var forecast=new RadarForecast {Date=Str(day,"date"),Probability=raw!=null&&!double.IsNaN(number)&&number>=0&&number<=100?(double?)number:null,EvaluatedAt=Str(day,"evaluated_at"),Reason=Str(day,"reason"),Stale=Flag(daily,"stale")||Flag(day,"stale")};
    foreach(object url in Items(Json.Get(day,"source_urls"))) {string safe=SafeUrl(Convert.ToString(url));if(safe!="")forecast.Sources.Add(safe);}
    forecasts.Add(forecast);
   }
   snapshot.ForecastMaxAgeSeconds=Math.Max(300,Math.Min(21600,Json.Num(Json.Get(daily,"stale_after_seconds"),21600)));
   snapshot.Forecasts=forecasts;snapshot.ForecastAvailable=true;
  }
  public static bool ForecastCurrent(RadarSnapshot snapshot,RadarForecast forecast,DateTimeOffset now) {
   if(forecast==null||!snapshot.ForecastAvailable||forecast.Stale||!forecast.Probability.HasValue)return false;
   var evaluated=Date(forecast.EvaluatedAt);if(!evaluated.HasValue||evaluated>now.AddMinutes(5)||now-evaluated.Value>TimeSpan.FromSeconds(snapshot.ForecastMaxAgeSeconds))return false;
   if(forecast.Sources==null||forecast.Sources.Count==0)return false;
   var signal=Date(snapshot.SignalAt);
   if(snapshot.SignalActive&&signal.HasValue&&signal.Value>evaluated.Value.AddMinutes(1)&&!forecast.Sources.Contains(snapshot.SignalUrl))return false;
   return true;
  }
  public static string ForecastLine(RadarSnapshot snapshot,DateTimeOffset now) {
   string today=now.ToOffset(Beijing).ToString("yyyy-MM-dd"),tomorrow=now.ToOffset(Beijing).AddDays(1).ToString("yyyy-MM-dd");
   var current=(snapshot.Forecasts??new List<RadarForecast>()).FirstOrDefault(f=>f.Date==today);
   var next=(snapshot.Forecasts??new List<RadarForecast>()).FirstOrDefault(f=>f.Date==tomorrow);
   if(!ForecastCurrent(snapshot,current,now))return snapshot.ForecastAvailable?"第三方预测 · 待更新":"第三方预测 · 暂无数据";
   string line="预测  今日 "+current.Probability.Value.ToString("0")+"%";
   if(ForecastCurrent(snapshot,next,now))line+=" / 明日 "+next.Probability.Value.ToString("0")+"%";
   return line+" · 非官方";
  }
  public static string SignalLine(RadarSnapshot snapshot,DateTimeOffset now,out string explanation) {
   explanation="公开动态副本来自 codex-reset.com；点击查看 Tibo 原帖。\n个人额度是否恢复，以账号读数为准。";
   if(!snapshot.FeedAvailable)return "重置信号暂不可用";
   if(!snapshot.SignalActive||String.IsNullOrWhiteSpace(snapshot.SignalText))return "暂无有效的额外重置预告";
   var published=Date(snapshot.SignalAt);if(!published.HasValue)return "预告时间未知 · 请查看原文";
   explanation+="\n\n原文："+snapshot.SignalText+"\n发布于 "+published.Value.ToOffset(Beijing).ToString("MM/dd HH:mm")+" 北京时间\n"+snapshot.SignalUrl;
   if(snapshot.FeedStale||now-Date(snapshot.FeedCheckedAt).GetValueOrDefault(DateTimeOffset.MinValue)>TimeSpan.FromMinutes(30))return "缓存信号 · 请核对最新原帖";
   if(now-published.Value>TimeSpan.FromDays(3))return "历史信号 · 暂无新预告";
   string text=snapshot.SignalText;
   if((snapshot.SignalKind??"").IndexOf("banked",StringComparison.OrdinalIgnoreCase)>=0||Regex.IsMatch(text,@"\bbanked reset\b",RegexOptions.IgnoreCase))return "备用重置消息 · 需手动核对";
   var hours=Regex.Match(text,@"(?:within|in) (?:the )?next (\d+) hours?",RegexOptions.IgnoreCase);
   if(hours.Success) {
    DateTimeOffset deadline=published.Value.AddHours(Int32.Parse(hours.Groups[1].Value));
    return now>deadline?"预告已过时限 · 等待后续":"预告："+deadline.ToOffset(Beijing).ToString("MM/dd HH:mm")+" 前";
   }
   var before=Regex.Match(text,@"before\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)\s*(PST|PDT|PT)\b",RegexOptions.IgnoreCase);
   bool eod=Regex.IsMatch(text,@"\bEOD\s+(PST|PDT|PT)\b",RegexOptions.IgnoreCase);
   if(before.Success||eod) {
    DateTime authorDate=TimeZoneInfo.ConvertTime(published.Value,Pacific).Date;
    if(Regex.IsMatch(text,@"\btomorrow\b",RegexOptions.IgnoreCase))authorDate=authorDate.AddDays(1);
    int hour=eod?23:Int32.Parse(before.Groups[1].Value)%12+(before.Groups[3].Value.Equals("pm",StringComparison.OrdinalIgnoreCase)?12:0);
    int minute=eod?59:(before.Groups[2].Success?Int32.Parse(before.Groups[2].Value):0);
    if(hour>23||minute>59)return "有重置预告 · 点击查看原文";
    DateTime wall=DateTime.SpecifyKind(authorDate.AddHours(hour).AddMinutes(minute),DateTimeKind.Unspecified);
    if(Pacific.IsInvalidTime(wall))return "有重置预告 · 时区需核实";
    DateTimeOffset pacific=new DateTimeOffset(wall,Pacific.GetUtcOffset(wall));
    string zone=before.Success?before.Groups[4].Value.ToUpperInvariant():Regex.Match(text,@"EOD\s+(PST|PDT|PT)",RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
    DateTimeOffset literal=zone=="PST"?new DateTimeOffset(wall,TimeSpan.FromHours(-8)):zone=="PDT"?new DateTimeOffset(wall,TimeSpan.FromHours(-7)):pacific;
    DateTimeOffset early=literal<pacific?literal:pacific,late=literal>pacific?literal:pacific;
    if(now>late)return "预告已过时限 · 等待后续";
    string deadline=early==late?late.ToOffset(Beijing).ToString("MM/dd HH:mm"):early.ToOffset(Beijing).ToString("MM/dd HH:mm")+"–"+late.ToOffset(Beijing).ToString("HH:mm");
    explanation+="\n\n时间均换算为北京时间。";
    if(early!=late)explanation+="原帖写 PST（UTC−8），而太平洋当地此时采用夏令时（UTC−7），故显示两个可能的截止时刻；这是最晚落地预告，不是开始时间或保证到账时间。";
    return "预告："+deadline+" 前";
   }
   return "有额外重置信号 · 查看原文";
  }
 }

 sealed class ResetRadarControl : StackPanel,IDisposable {
  readonly string cacheFile;
  readonly HttpClient http;
  readonly DispatcherTimer timer=new DispatcherTimer();
  readonly TextBlock clock=new TextBlock(),signalText=new TextBlock(),forecastText=new TextBlock(),sourceText=new TextBlock();
  readonly Button signalLink,forecastLink,sourceLink;
  RadarSnapshot snapshot=new RadarSnapshot();
  DateTimeOffset nextCheck=DateTimeOffset.MinValue;
  bool refreshing,closed,feedFailed,forecastFailed;
  public event Action Updated;
  static Brush BrushOf(string color) {return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}
  public ResetRadarControl(string dataDir) {
   cacheFile=Path.Combine(dataDir,"reset-radar-cache.json");
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   http=new HttpClient {Timeout=TimeSpan.FromSeconds(18),MaxResponseContentBufferSize=3*1024*1024};http.DefaultRequestHeaders.Accept.ParseAdd("application/json");http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaMini/2.0");
   Children.Add(new Border {Height=1,Background=BrushOf("#303640"),Margin=new Thickness(0,8,0,8)});
   var heading=new DockPanel();clock.FontFamily=new FontFamily("Segoe UI");clock.FontSize=8.5;clock.Foreground=BrushOf("#8992A3");clock.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(clock,Dock.Right);heading.Children.Add(clock);
   heading.Children.Add(new TextBlock {Text="Tibo 雷达",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=BrushOf("#98B7FF")});Children.Add(heading);
   signalText.FontSize=10;signalText.Foreground=BrushOf("#D7E3FF");signalText.TextTrimming=TextTrimming.CharacterEllipsis;signalLink=Link(signalText);signalLink.Margin=new Thickness(0,5,0,0);signalLink.Click+=(s,e)=>Open(snapshot.SignalUrl);Children.Add(signalLink);
   forecastText.FontSize=8.7;forecastText.Foreground=BrushOf("#A2ACBD");forecastLink=Link(forecastText);forecastLink.Margin=new Thickness(0,4,0,0);forecastLink.Click+=(s,e)=>Open("https://www.codexreset-news.com/?lang=zh-CN");Children.Add(forecastLink);
   sourceText.FontFamily=new FontFamily("Segoe UI");sourceText.FontSize=8;sourceText.Foreground=BrushOf("#778294");sourceLink=Link(sourceText);sourceLink.Margin=new Thickness(0,4,0,0);sourceLink.Click+=(s,e)=>Open("https://codex-reset.com/zh/radar");Children.Add(sourceLink);
   try{snapshot=new JavaScriptSerializer().Deserialize<RadarSnapshot>(File.ReadAllText(cacheFile))??new RadarSnapshot();}catch{}
   Render();Loaded+=async(s,e)=>{await Refresh(false);};
   timer.Interval=TimeSpan.FromSeconds(15);timer.Tick+=async(s,e)=>{Render();if(DateTimeOffset.UtcNow>=nextCheck)await Refresh(false);};timer.Start();
  }
  static Button Link(TextBlock text) {
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(ContentControl.ContentProperty));
   var button=new Button {Content=text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Left,Cursor=Cursors.Hand,Template=new ControlTemplate(typeof(Button)) {VisualTree=presenter}};
   button.MouseEnter+=(s,e)=>text.Opacity=.72;button.MouseLeave+=(s,e)=>text.Opacity=1;return button;
  }
  static void Open(string url) {Uri parsed;if(!Uri.TryCreate(url,UriKind.Absolute,out parsed)||parsed.Scheme!="https")return;try{Process.Start(new ProcessStartInfo {FileName=parsed.AbsoluteUri,UseShellExecute=true});}catch{}}
  async Task FetchFeed() {try{var raw=Json.Read(await http.GetStringAsync("https://codex-reset.com/api/feed"));if(closed)return;RadarData.ApplyFeed(snapshot,raw,DateTimeOffset.UtcNow);feedFailed=false;}catch{feedFailed=true;}}
  async Task FetchForecast() {try{var raw=Json.Read(await http.GetStringAsync("https://codexreset-news.com/api/v1/overview"));if(closed)return;RadarData.ApplyOverview(snapshot,raw);forecastFailed=false;}catch{forecastFailed=true;}}
  public async Task Refresh(bool force) {
   if(closed||refreshing||(!force&&DateTimeOffset.UtcNow<nextCheck))return;
   refreshing=true;nextCheck=DateTimeOffset.UtcNow.AddMinutes(5);
   try {
    await Task.WhenAll(FetchFeed(),FetchForecast());if(closed)return;
    if(!feedFailed||!forecastFailed){snapshot.RetrievedAt=DateTimeOffset.UtcNow.ToString("o");try{string temp=cacheFile+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(snapshot),Encoding.UTF8);if(File.Exists(cacheFile))File.Replace(temp,cacheFile,null);else File.Move(temp,cacheFile);}catch{}}
    Render();if(Updated!=null)Updated();
   }finally{refreshing=false;}
  }
  void Render() {
   var now=DateTimeOffset.UtcNow;
   clock.Text="PT "+TimeZoneInfo.ConvertTime(now,RadarData.Pacific).ToString("HH:mm");clock.ToolTip="美国太平洋当地时间（自动处理夏令时），不是对 Tibo 实际所在地或作息的确认。";
   string detail;signalText.Text=RadarData.SignalLine(snapshot,now,out detail);signalLink.IsEnabled=!String.IsNullOrEmpty(snapshot.SignalUrl);signalLink.ToolTip=new TextBlock {Text=detail,TextWrapping=TextWrapping.Wrap,MaxWidth=330};AutomationProperties.SetName(signalLink,signalText.Text);
   forecastText.Text=RadarData.ForecastLine(snapshot,now);if(forecastFailed)forecastText.Text="第三方预测 · 连接失败";
   string forecastDetail="来源：Codex Reset News（第三方模型，非 Tibo 本人的概率预测）。\n日期按北京时间自然日计算；不等于账号额度恢复。\nhttps://www.codexreset-news.com/\n";
   foreach(var forecast in snapshot.Forecasts??new List<RadarForecast>())forecastDetail+="\n"+forecast.Date+" · 评估于 "+(RadarData.Date(forecast.EvaluatedAt).HasValue?RadarData.Date(forecast.EvaluatedAt).Value.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm"):"未知")+"\n"+forecast.Reason+"\n";
   forecastLink.ToolTip=new TextBlock {Text=forecastDetail,MaxWidth=380,MaxHeight=480,TextWrapping=TextWrapping.Wrap};AutomationProperties.SetName(forecastLink,forecastText.Text+"，查看预测来源");
   var stamp=RadarData.Date(snapshot.FeedCheckedAt);bool stale=feedFailed||snapshot.FeedStale||!stamp.HasValue||now-stamp.Value>TimeSpan.FromMinutes(30);
   sourceText.Text="codex-reset.com · "+(stamp.HasValue?stamp.Value.ToOffset(RadarData.Beijing).ToString("HH:mm"):"未同步")+(stale?" 缓存":" 同步")+" ↗";sourceText.Foreground=BrushOf(stale?"#D4AF74":"#778294");
   sourceLink.ToolTip="公开信号来自 codex-reset.com 副本；预测来自 Codex Reset News。\n每 5 分钟同步，ChatGPT 退出后同步随小窗停止。\n"+(feedFailed?"本次信号同步失败，保留上次数据。":"");
  }
  public void Dispose() {closed=true;timer.Stop();http.Dispose();}
 }
}
