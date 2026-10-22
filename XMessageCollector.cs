using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexQuotaMini {
 sealed class XCapture {
  public TiboPost[] Posts {get;set;}
  public string PageUrl {get;set;}
  public bool IsTiboPage {get;set;}
  public bool LoginRequired {get;set;}
 }
 sealed class XMessageCollector : IDisposable {
  public const string Timeline="https://x.com/thsottiaux/with_replies";
  readonly string profile;
  readonly string stateFile;
  readonly TiboStore store;
  readonly DispatcherTimer timer=new DispatcherTimer();
  readonly string extractionScript;
  Window window;
  WebView2 view;
  TextBlock status;
  bool closed,initializing,capturing,navigating;
  DateTimeOffset nextRefresh=DateTimeOffset.MinValue,lastNavigation=DateTimeOffset.MinValue,nextCapture=DateTimeOffset.MinValue;
  IntPtr processJob=IntPtr.Zero;
  public string Status {get;private set;}
  public bool NeedsLogin {get;private set;}
  public string Problem {get;private set;}
  public bool Ready {get{return view!=null&&view.CoreWebView2!=null;}}
  public event Action Changed;
  public XMessageCollector(string dataDir,TiboStore store){
   this.store=store;profile=Path.Combine(dataDir,"x-browser-profile");stateFile=Path.Combine(dataDir,"collector-state.json");
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TiboCollector.js"))using(var reader=new StreamReader(stream))extractionScript=reader.ReadToEnd();
   Status="正在启动内置 X 采集器";
   timer.Interval=TimeSpan.FromSeconds(3);timer.Tick+=async(s,e)=>await Tick();
  }
  void SetStatus(string text){Status=text;if(status!=null)status.Text=text;try{File.WriteAllText(stateFile,Json.Write(new {status=text,needsLogin=NeedsLogin,ready=Ready,posts=store.History.Posts.Count,checkedAt=store.History.CheckedAt,updatedAt=DateTimeOffset.UtcNow.ToString("o")}));}catch{}if(Changed!=null)Changed();}
  public async Task Start(){
   if(closed||initializing||Ready)return;initializing=true;
   try {
    if(String.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString()))throw new InvalidOperationException("请安装 Microsoft Edge WebView2 Runtime");
    window=new Window {Title="Tibo 直连采集 · 首次登录后自动监控",Width=1050,Height=780,MinWidth=700,MinHeight=500,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.CenterScreen};
    var panel=new DockPanel();var bar=new StackPanel {Margin=new Thickness(12)};DockPanel.SetDock(bar,Dock.Top);
    bar.Children.Add(new TextBlock {Text="消息直接来自 X，分类和时间推断在本机完成。首次使用请登录 X；关闭此窗口会收起采集页。",TextWrapping=TextWrapping.Wrap});
    status=new TextBlock {Text=Status,Margin=new Thickness(0,6,0,6)};bar.Children.Add(status);
    var reload=new Button {Content="查看 Tibo 最新动态",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,4,10,4)};reload.Click+=async(s,e)=>await Refresh(true);bar.Children.Add(reload);panel.Children.Add(bar);
    view=new WebView2();panel.Children.Add(view);window.Content=panel;window.Closing+=OnClosing;
    window.Show();if(store.History.Posts.Count>0){window.ShowInTaskbar=false;window.Hide();}
    Directory.CreateDirectory(profile);
    var environment=await CoreWebView2Environment.CreateAsync(null,profile);
    if(closed)return;
    await view.EnsureCoreWebView2Async(environment);
    if(closed)return;
    view.CoreWebView2.Settings.IsWebMessageEnabled=false;
    view.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;
    view.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;};
    view.CoreWebView2.DownloadStarting+=(s,e)=>{e.Cancel=true;};
    view.CoreWebView2.NavigationStarting+=(s,e)=>{navigating=true;};
    view.CoreWebView2.NavigationCompleted+=(s,e)=>{
     navigating=false;
     if(!e.IsSuccess){Problem="X 页面连接失败";SetStatus(Problem+" · 可打开采集页检查");return;}
     lastNavigation=DateTimeOffset.UtcNow;nextCapture=DateTimeOffset.MinValue;
     Uri uri;if(Uri.TryCreate(view.Source==null?"":view.Source.AbsoluteUri,UriKind.Absolute,out uri)&&uri.Host=="x.com"&&uri.AbsolutePath=="/home")view.CoreWebView2.Navigate(Timeline);
    };
    try{using(var process=Process.GetProcessById((int)view.CoreWebView2.BrowserProcessId))processJob=Native.OwnProcessTree(process.Handle);}catch{}
    view.CoreWebView2.Navigate(Timeline);nextRefresh=DateTimeOffset.UtcNow.AddMinutes(5);timer.Start();
    SetStatus("正在读取 Tibo 页面；如需登录，请在采集窗口完成");
   }catch(Exception error){Problem="采集器未就绪";SetStatus(Problem+"："+error.Message);if(view!=null){view.Dispose();view=null;}if(window!=null){window.Closing-=OnClosing;window.Close();window=null;}}
   finally{initializing=false;}
  }
  void OnClosing(object sender,CancelEventArgs e){if(!closed){e.Cancel=true;window.Hide();window.ShowInTaskbar=false;}}
  public void Show(){if(closed)return;if(window==null){var ignored=Start();return;}window.ShowInTaskbar=true;window.Show();window.WindowState=WindowState.Normal;window.Activate();}
  public async Task Refresh(bool force){
   if(closed)return;if(!Ready){await Start();return;}
   if(navigating)return;
   if(force||DateTimeOffset.UtcNow>=nextRefresh){
    nextRefresh=DateTimeOffset.UtcNow.AddMinutes(5);
    // Do not interrupt login or user-entered forms with an automatic reload.
    if(!force&&window.IsVisible){await Capture();return;}
    SetStatus("正在刷新 Tibo 公开动态");
    if(view.Source!=null&&view.Source.AbsoluteUri==Timeline)view.CoreWebView2.Reload();else view.CoreWebView2.Navigate(Timeline);
   }
  }
  async Task Tick(){
   if(closed||!Ready||navigating)return;
   if(DateTimeOffset.UtcNow>=nextRefresh){await Refresh(false);return;}
   if(DateTimeOffset.UtcNow>=nextCapture&&DateTimeOffset.UtcNow-lastNavigation<TimeSpan.FromSeconds(45)){nextCapture=DateTimeOffset.UtcNow.AddSeconds(5);await Capture();}
  }
  async Task Capture(){
   if(closed||capturing||!Ready)return;capturing=true;
   try {
    var raw=await view.CoreWebView2.ExecuteScriptAsync(extractionScript+"(document, location.href)");if(closed)return;
    var capture=new JavaScriptSerializer {MaxJsonLength=2*1024*1024}.Deserialize<XCapture>(raw);
    if(capture==null)return;
    NeedsLogin=capture.LoginRequired;
    if(!capture.IsTiboPage||capture.Posts==null||capture.Posts.Length==0){Problem=NeedsLogin?"需要登录 X":"采集页未就绪";SetStatus(Problem+" · 点击打开采集页检查");return;}
    var cleaned=TiboStore.Clean(capture.Posts,DateTimeOffset.UtcNow);
    if(cleaned.Count==0){Problem="消息校验未通过";SetStatus(Problem+" · 查看采集页");return;}
    // Re-reading the same loaded page must not make it appear freshly fetched.
    if(lastNavigation!=DateTimeOffset.MinValue)store.Merge(cleaned,lastNavigation,capture.PageUrl);
    NeedsLogin=false;Problem=null;SetStatus("X 直连 · 已保存 "+store.History.Posts.Count+" 条公开消息");
    if(!window.IsVisible)nextCapture=DateTimeOffset.UtcNow.AddMinutes(5);
   }catch(Exception){Problem="X 页面读取失败";SetStatus(Problem+" · 保留上次记录");}
   finally{capturing=false;}
  }
  public void Dispose(){if(closed)return;closed=true;timer.Stop();if(view!=null){view.Dispose();view=null;}if(window!=null){window.Closing-=OnClosing;window.Close();window=null;}if(processJob!=IntPtr.Zero){Native.CloseHandle(processJob);processJob=IntPtr.Zero;}}
 }
}
