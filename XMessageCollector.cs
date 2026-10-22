using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexQuotaMini {
 sealed class XMessageCollector : IDisposable {
  public const string Timeline=PublicXClient.Profile;
  public const string Coverage="近期原帖与公开讨论中的自回复；账号回复覆盖不完整。";
  readonly string stateFile;
  readonly TiboStore store;
  readonly PublicXClient client;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();
  readonly DispatcherTimer timer=new DispatcherTimer();
  DateTimeOffset nextRefresh=DateTimeOffset.MinValue;
  Task pending;
  bool closed,started;
  Window details;
  StackPanel messages;
  public string Status {get;private set;}
  public string Problem {get;private set;}
  public string NextRefresh {get{return nextRefresh==DateTimeOffset.MinValue?"":nextRefresh.ToOffset(RadarData.Beijing).ToString("HH:mm");}}
  public event Action Changed;
  public XMessageCollector(string dataDir,TiboStore store):this(dataDir,store,new PublicXClient()){}
  internal XMessageCollector(string dataDir,TiboStore store,PublicXClient client){
   this.store=store;this.client=client;stateFile=Path.Combine(dataDir,"collector-state.json");Status="即将免登录读取 Tibo 公开消息";
   timer.Interval=TimeSpan.FromSeconds(15);timer.Tick+=async(s,e)=>await Refresh(false);
  }
  void Notify(){
   if(closed)return;
   try{File.WriteAllText(stateFile,Json.Write(new {mode="x-public-pages",status=Status,problem=Problem,posts=store.History.Posts.Count,originals=store.History.OriginalCount,replies=store.History.ReplyCount,checkedAt=store.History.CheckedAt,nextRefreshAt=nextRefresh.ToString("o"),coverage=Coverage,completeRepliesTimeline=false,updatedAt=DateTimeOffset.UtcNow.ToString("o")}));}catch{}
   RenderMessages();if(Changed!=null)Changed();
  }
  public Task Start(){if(closed)return Task.FromResult(0);if(!started){started=true;timer.Start();}return Refresh(false);}
  public Task Refresh(bool force){
   if(closed)return Task.FromResult(0);
   if(pending!=null&&!pending.IsCompleted)return pending;
   if((!force&&DateTimeOffset.UtcNow<nextRefresh)||(client.RetryAt.HasValue&&client.RetryAt>DateTimeOffset.UtcNow))return Task.FromResult(0);
   pending=Collect();return pending;
  }
  async Task Collect(){
   nextRefresh=DateTimeOffset.UtcNow.AddMinutes(5);Status="正在免登录读取 Tibo 原帖与自回复";Notify();
   try{
    var batch=await client.Collect(lifetime.Token);if(closed)return;
    store.Merge(batch.Posts,batch.CheckedAt,Timeline,Coverage+(batch.Partial?" "+batch.Detail:""),batch.Partial);
    Problem=batch.Partial?"部分讨论页未更新":null;
    Status="X 免登录直连 · 本轮 "+store.History.OriginalCount+" 原帖 / "+store.History.ReplyCount+" 自回复"+(batch.Partial?" · "+batch.Detail:"");
   }catch(OperationCanceledException){if(closed)return;Problem="X 读取超时";Status=Problem+" · 保留上次记录";}
   catch(XReadException error){Problem=error.Message;Status=Problem+" · 保留上次记录";}
   catch(Exception){if(closed)return;Problem="X 消息读取失败";Status=Problem+" · 保留上次记录";}
   finally{if(!closed){nextRefresh=DateTimeOffset.UtcNow.AddMinutes(5);if(client.RetryAt.HasValue&&client.RetryAt>nextRefresh)nextRefresh=client.RetryAt.Value;Notify();}}
  }
  public void Show(){
   if(closed)return;
   if(details==null){
    details=new Window {Title="Tibo 公开消息 · 免登录直连",Width=530,Height=650,MinWidth=420,MinHeight=350,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brushes.White,FontFamily=new FontFamily("Microsoft YaHei UI")};
    messages=new StackPanel {Margin=new Thickness(16)};details.Content=new ScrollViewer {Content=messages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    details.Closed+=(s,e)=>{details=null;messages=null;};
   }
   RenderMessages();details.Show();details.Activate();
  }
  void RenderMessages(){
   if(messages==null)return;messages.Children.Clear();
   messages.Children.Add(new TextBlock {Text=Status,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
   messages.Children.Add(new TextBlock {Text=Coverage+"\n每 5 分钟自动更新，下次读取 "+NextRefresh+"（北京时间）。",Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)});
   var refresh=new Button {Content="立即更新消息",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,0,12)};refresh.Click+=async(s,e)=>await Refresh(true);messages.Children.Add(refresh);
   if(store.History.Posts.Count==0)messages.Children.Add(new TextBlock {Text="暂未读取到可用消息；看板会自动重试。",TextWrapping=TextWrapping.Wrap});
   foreach(var post in store.History.Posts.Take(40)){
    var card=new StackPanel {Margin=new Thickness(0,0,0,14)};var time=RadarData.Date(post.PublishedAt);
    card.Children.Add(new TextBlock {Text=(post.Kind=="reply"?"自回复":post.Kind=="post"?"原帖":"消息")+" · "+(time.HasValue?time.Value.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm:ss"):"时间未知"),FontWeight=FontWeights.SemiBold});
    card.Children.Add(new TextBlock {Text=post.Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,5)});
    string target=RadarData.SafeUrl(post.Url);var link=new Button {Content="查看 X 原文 ↗",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(6,2,6,2)};
    link.Click+=(s,e)=>{if(target!="")try{Process.Start(new ProcessStartInfo {FileName=target,UseShellExecute=true});}catch{}};card.Children.Add(link);messages.Children.Add(card);
   }
  }
  public void Dispose(){if(closed)return;closed=true;timer.Stop();lifetime.Cancel();client.Dispose();if(details!=null)details.Close();}
 }
}
