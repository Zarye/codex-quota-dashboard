using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexQuotaMini {
 sealed class ResetRadarControl : StackPanel,IDisposable {
  readonly TiboStore store;
  readonly XMessageCollector collector;
  readonly DispatcherTimer timer=new DispatcherTimer();
  readonly TextBlock clock=new TextBlock(),signal=new TextBlock(),assessment=new TextBlock(),source=new TextBlock(),activity=new TextBlock();
  readonly Button signalLink,sourceLink;
  ResetAssessment current;
  bool closed;
  public event Action Updated;
  public object DisplayState {get{return new {mode="x-public-pages",status=collector.Status,problem=collector.Problem,checkedAt=store.History.CheckedAt,originals=store.History.OriginalCount,replies=store.History.ReplyCount,selfReplies=store.History.SelfReplyCount,otherReplies=store.History.OtherReplyCount,partial=store.History.Partial,coverage=XMessageCollector.Coverage,summary=current==null?"":current.Summary,sourceId=current==null||current.Source==null?null:current.Source.Id,earliestDeadline=current==null?null:current.EarliestDeadline,latestDeadline=current==null?null:current.LatestDeadline};}}
  static Brush BrushOf(string value){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}
  public ResetRadarControl(string dataDir){
   store=new TiboStore(dataDir);collector=new XMessageCollector(dataDir,store);collector.Changed+=Render;
   Children.Add(new Border {Height=1,Background=BrushOf("#303640"),Margin=new Thickness(0,8,0,8)});
   var heading=new DockPanel();clock.FontSize=8.5;clock.Foreground=BrushOf("#8992A3");DockPanel.SetDock(clock,Dock.Right);heading.Children.Add(clock);heading.Children.Add(new TextBlock {Text="Tibo 直连雷达",FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=BrushOf("#98B7FF")});Children.Add(heading);
   signal.FontSize=10;signal.Foreground=BrushOf("#D7E3FF");signal.TextWrapping=TextWrapping.Wrap;signalLink=Link(signal);signalLink.Margin=new Thickness(0,5,0,0);signalLink.Click+=(s,e)=>{if(current!=null&&current.Source!=null)Open(current.Source.Url);};Children.Add(signalLink);
   assessment.FontSize=8.7;assessment.Foreground=BrushOf("#A2ACBD");assessment.Margin=new Thickness(0,4,0,0);assessment.TextWrapping=TextWrapping.Wrap;Children.Add(assessment);
   activity.FontSize=8.2;activity.Foreground=BrushOf("#8992A3");activity.Margin=new Thickness(0,4,0,0);activity.TextWrapping=TextWrapping.Wrap;Children.Add(activity);
   source.FontSize=8.2;source.Foreground=BrushOf("#778294");source.TextWrapping=TextWrapping.Wrap;sourceLink=Link(source);sourceLink.Margin=new Thickness(0,4,0,0);sourceLink.Click+=(s,e)=>collector.Show();Children.Add(sourceLink);
   Render();Loaded+=async(s,e)=>await collector.Start();timer.Interval=TimeSpan.FromSeconds(15);timer.Tick+=(s,e)=>Render();timer.Start();
  }
  static Button Link(TextBlock text){var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(ContentControl.ContentProperty));return new Button {Content=text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Left,Cursor=Cursors.Hand,Template=new ControlTemplate(typeof(Button)) {VisualTree=content}};}
  static void Open(string url){string safe=RadarData.SafeUrl(url);if(safe!="")try{Process.Start(new ProcessStartInfo {FileName=safe,UseShellExecute=true});}catch{}}
  public async Task Refresh(bool force){await collector.Refresh(force);Render();}
  void Render(){
   if(closed)return;var now=DateTimeOffset.UtcNow;current=LocalResetPredictor.Analyze(store.History.Posts,now);
   clock.Text="PT "+TimeZoneInfo.ConvertTime(now,RadarData.Pacific).ToString("HH:mm");clock.ToolTip="美国太平洋当地时间，自动处理夏令时。";
   var checkedAt=RadarData.Date(store.History.CheckedAt);bool stale=!checkedAt.HasValue||now-checkedAt.Value>TimeSpan.FromMinutes(10);
   signal.Text=current.Summary;signalLink.IsEnabled=current.Source!=null;
   string detail=current.Reason+(current.Source==null?"":"\n\n原文："+current.Source.Text+"\n发布时间："+RadarData.Date(current.Source.PublishedAt).Value.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm")+" 北京时间\n"+current.Source.Url);
   signalLink.ToolTip=new TextBlock {Text=detail,TextWrapping=TextWrapping.Wrap,MaxWidth=430};AutomationProperties.SetName(signalLink,current.Summary);
   assessment.Text="本地研判 · "+current.Strength+(stale||store.History.Partial||collector.Problem!=null?" · 缓存":"");assessment.ToolTip="本机关键词与时间规则，尚未校准，不显示精确概率。个人额度是否恢复，以上方账号读数为准。";
   activity.Text=checkedAt.HasValue?"本轮 "+store.History.OriginalCount+" 原帖 · "+store.History.SelfReplyCount+" 自回复\n累计回复他人 "+store.History.Posts.Count(p=>TiboStore.ReplyKind(p,store.History.Posts)=="other")+" · 覆盖有限":"原帖、自回复与回复他人自动更新";
   activity.ToolTip=XMessageCollector.Coverage+"\n"+XMessageCollector.WatchScope;
   source.Text=!String.IsNullOrEmpty(collector.Problem)?collector.Problem+" · 消息 ↗":checkedAt.HasValue?"免登录直连 · "+checkedAt.Value.ToOffset(RadarData.Beijing).ToString("HH:mm")+" · 消息 ↗":"免登录直连 · 正在读取 · 消息 ↗";
   source.Foreground=BrushOf(stale||collector.Problem!=null?"#D4AF74":"#778294");sourceLink.ToolTip=collector.Status+"\n每 5 分钟自动读取；下次 "+collector.NextRefresh+"。\n"+XMessageCollector.Coverage+"\n随看板启动和退出，无独立后台程序。";
   if(Updated!=null)Updated();
  }
  public void Dispose(){if(closed)return;closed=true;timer.Stop();collector.Dispose();}
 }
}
