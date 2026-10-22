using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Codex 额度看板")]
[assembly: System.Reflection.AssemblyProduct("Codex Quota Dashboard")]
[assembly: System.Reflection.AssemblyVersion("0.4.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.4.0.0")]

namespace CodexQuotaMini {
 static class Json {
  public static Dictionary<string,object> Read(string text) { return new JavaScriptSerializer { MaxJsonLength=16777216 }.Deserialize<Dictionary<string,object>>(text); }
  public static string Write(object value) { return new JavaScriptSerializer().Serialize(value); }
  public static Dictionary<string,object> Obj(object x) { return x as Dictionary<string,object>; }
  public static object Get(Dictionary<string,object> d,string k) { object x; return d!=null && d.TryGetValue(k,out x) ? x : null; }
  public static double Num(object x,double fallback=0) { try{return x==null?fallback:Convert.ToDouble(x,CultureInfo.InvariantCulture);}catch{return fallback;} }
 }

 sealed class RpcClient : IDisposable {
  Process process;
  readonly object gate=new object();
  readonly Dictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
  int nextId;
  IntPtr job=IntPtr.Zero;
  public bool Alive {get {try{return process!=null && !process.HasExited;}catch{return false;}}}
  public async Task Start() {
   process=new Process {StartInfo=new ProcessStartInfo {
    FileName=FindCodex(), Arguments="app-server --stdio", UseShellExecute=false, CreateNoWindow=true,
    RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
    StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8,
    WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory
   }};
   process.ErrorDataReceived+=(s,e)=>{};
   process.Start(); process.BeginErrorReadLine();
   job=Native.OwnProcessTree(process.Handle);
   Task ignored=ReadLoop();
   await Request("initialize",new {clientInfo=new {name="codex_quota_mini",title="Codex Quota Mini",version="1.0.0"}});
   lock(gate) {process.StandardInput.WriteLine(Json.Write(new {method="initialized"}));process.StandardInput.Flush();}
  }
  static string FindCodex() {
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
   if(Directory.Exists(root)) foreach(var dir in new DirectoryInfo(root).GetDirectories().OrderByDescending(d=>d.LastWriteTimeUtc)) {
    string file=Path.Combine(dir.FullName,"codex.exe"); if(File.Exists(file))return file;
   }
   string npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai");
   if(Directory.Exists(npm)) {
    var file=Directory.GetFiles(npm,"codex.exe",SearchOption.AllDirectories).FirstOrDefault(); if(file!=null)return file;
   }
   throw new InvalidOperationException("找不到本机 Codex，请先安装或打开桌面应用。");
  }
  async Task ReadLoop() {
   try {
    string line;
    while((line=await process.StandardOutput.ReadLineAsync().ConfigureAwait(false))!=null) {
     Dictionary<string,object> m;try{m=Json.Read(line);}catch{continue;}
     object rawId=Json.Get(m,"id");if(rawId==null)continue;
     TaskCompletionSource<Dictionary<string,object>> tcs=null;
     lock(gate){int id=Convert.ToInt32(rawId);if(pending.TryGetValue(id,out tcs))pending.Remove(id);}
     if(tcs!=null)tcs.TrySetResult(m);
    }
   } catch {}
   lock(gate){foreach(var tcs in pending.Values)tcs.TrySetException(new IOException("额度连接已断开"));pending.Clear();}
  }
  public async Task<Dictionary<string,object>> Request(string method,object args=null) {
   int id;var tcs=new TaskCompletionSource<Dictionary<string,object>>(TaskCreationOptions.RunContinuationsAsynchronously);
   lock(gate) {
    id=++nextId;pending[id]=tcs;
    try {process.StandardInput.WriteLine(Json.Write(args==null?(object)new {id=id,method=method}:new {id=id,method=method,@params=args}));process.StandardInput.Flush();}
    catch {pending.Remove(id);throw;}
   }
   if(await Task.WhenAny(tcs.Task,Task.Delay(20000)).ConfigureAwait(false)!=tcs.Task){lock(gate)pending.Remove(id);throw new TimeoutException("额度读取超时");}
   var response=await tcs.Task.ConfigureAwait(false);
   var error=Json.Obj(Json.Get(response,"error"));
   if(error!=null)throw new InvalidOperationException("额度读取失败，请确认 Codex 已用 ChatGPT 账号登录。");
   return Json.Obj(Json.Get(response,"result"));
  }
  public void Dispose() {
   if(process!=null){try{process.StandardInput.Close();}catch{}try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();process=null;}
   if(job!=IntPtr.Zero){Native.CloseHandle(job);job=IntPtr.Zero;}
  }
 }

 static class Native {
  [DllImport("kernel32.dll")] static extern IntPtr CreateJobObject(IntPtr a,string name);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr h,int kind,IntPtr info,uint length);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr h,IntPtr process);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
  [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h,int index,int value);
  [StructLayout(LayoutKind.Sequential)] struct BasicLimit { public long a,b;public uint flags;public UIntPtr min,max;public uint active;public UIntPtr affinity;public uint priority,scheduling; }
  [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong a,b,c,d,e,f;}
  [StructLayout(LayoutKind.Sequential)] struct ExtendedLimit {public BasicLimit basic;public IoCounters io;public UIntPtr a,b,c,d;}
  public static IntPtr OwnProcessTree(IntPtr process) {
   IntPtr job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)return job;
   var info=new ExtendedLimit();info.basic.flags=0x2000;int size=Marshal.SizeOf(info);IntPtr p=Marshal.AllocHGlobal(size);
   try{Marshal.StructureToPtr(info,p,false);if(!SetInformationJobObject(job,9,p,(uint)size)||!AssignProcessToJobObject(job,process)){CloseHandle(job);return IntPtr.Zero;}return job;}
   finally{Marshal.FreeHGlobal(p);}
  }
 }

 sealed class QuotaRing : FrameworkElement {
  readonly double value;
  readonly Brush color;
  public QuotaRing(double remaining,Brush foreground) {value=Math.Max(0,Math.Min(100,remaining));color=foreground;Width=68;Height=68;IsHitTestVisible=false;}
  protected override void OnRender(DrawingContext drawing) {
   base.OnRender(drawing);
   var center=new Point(34,34);const double radius=29,thickness=4.5;
   drawing.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromRgb(57,59,63)),thickness),center,radius,radius);
   if(value<=0)return;
   var pen=new Pen(color,thickness) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
   if(value>=100){drawing.DrawEllipse(null,pen,center,radius,radius);return;}
   double angle=value/100*Math.PI*2;
   var arc=new StreamGeometry();
   using(var path=arc.Open()) {
    path.BeginFigure(new Point(center.X,center.Y-radius),false,false);
    path.ArcTo(new Point(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle)),new Size(radius,radius),0,value>50,SweepDirection.Clockwise,true,false);
   }
   arc.Freeze();drawing.DrawGeometry(null,pen,arc);
  }
 }

 sealed class QuotaWindow : Window {
  readonly string dataDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
  readonly string codexState=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex",".codex-global-state.json");
  RpcClient client;
  readonly StackPanel rows=new StackPanel();
  readonly TextBlock footer=new TextBlock();
  readonly TextBlock subtitle=new TextBlock();
  readonly List<Tuple<TextBlock,DateTimeOffset>> countdowns=new List<Tuple<TextBlock,DateTimeOffset>>();
  readonly ResetRadarControl radar;
  readonly ResetCreditsControl resetCredits;
  readonly MenuItem followItem=new MenuItem {Header="跟随 Mini",IsCheckable=true,IsChecked=true};
  readonly DispatcherTimer positionTimer=new DispatcherTimer();
  readonly DispatcherTimer refreshTimer=new DispatcherTimer();
  readonly DispatcherTimer healthTimer=new DispatcherTimer();
  DateTime nextRefresh=DateTime.MinValue,lastSuccess=DateTime.MinValue,stateTime=DateTime.MinValue;
  Dictionary<string,object> lastResult;
  bool refreshing,closing,following=true,miniOpen=true,connectionFailed;
  double anchorX=2335,anchorY=1155,offsetX=-50,offsetY=128,manualX=double.NaN,manualY=double.NaN;
  Rect display=new Rect(0,0,SystemParameters.PrimaryScreenWidth,SystemParameters.PrimaryScreenHeight);
  public QuotaWindow(bool inspectable=false) {
   Title="Codex 额度小窗";Width=216;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=inspectable;ShowActivated=false;
   FontFamily=new FontFamily("Microsoft YaHei UI");UseLayoutRounding=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
   Directory.CreateDirectory(dataDir);LoadSettings();
   var shell=new Border {Margin=new Thickness(6),CornerRadius=new CornerRadius(21),Background=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#222833"),(Color)ColorConverter.ConvertFromString("#171B23"),90),BorderBrush=ColorBrush("#3A4250"),BorderThickness=new Thickness(1),Padding=new Thickness(12,11,12,11),Effect=new DropShadowEffect {Color=Colors.Black,Opacity=.26,BlurRadius=12,ShadowDepth=3}};
   var layer=new Grid();var body=new StackPanel();layer.Children.Add(body);shell.Child=layer;Content=shell;
   subtitle.Text="正在读取…";subtitle.FontSize=10;subtitle.Foreground=ColorBrush("#AEAFB7");subtitle.TextAlignment=TextAlignment.Center;subtitle.TextWrapping=TextWrapping.Wrap;subtitle.Margin=new Thickness(0,4,0,4);body.Children.Add(subtitle);
   body.Children.Add(rows);footer.FontSize=9;footer.Foreground=ColorBrush("#9A9BA3");footer.TextAlignment=TextAlignment.Center;footer.TextWrapping=TextWrapping.Wrap;footer.Margin=new Thickness(0,6,0,0);footer.Text="每 60 秒更新";body.Children.Add(footer);
   resetCredits=new ResetCreditsControl();body.Children.Add(resetCredits);
   radar=new ResetRadarControl(dataDir);radar.Updated+=()=>Dispatcher.BeginInvoke(new Action(SaveDisplayProof),DispatcherPriority.ContextIdle);body.Children.Add(radar);
   var controls=new Grid {VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(-8,-9,-8,0),Opacity=0};
   var close=SmallButton("×","关闭额度小窗");close.HorizontalAlignment=HorizontalAlignment.Right;close.Click+=(s,e)=>Close();controls.Children.Add(close);
   var refresh=SmallButton("↻","刷新额度与重置雷达");refresh.HorizontalAlignment=HorizontalAlignment.Left;refresh.Click+=async(s,e)=>{await Task.WhenAll(RefreshQuota(true),radar.Refresh(true));};controls.Children.Add(refresh);layer.Children.Add(controls);
   shell.MouseEnter+=(s,e)=>controls.Opacity=1;shell.MouseLeave+=(s,e)=>controls.Opacity=0;
   var menu=new ContextMenu();menu.Items.Add(followItem);followItem.IsChecked=following;
   followItem.Click+=(s,e)=>{following=followItem.IsChecked;SaveSettings();PositionWindow();};
   var update=new MenuItem {Header="立即刷新"};update.Click+=async(s,e)=>{await Task.WhenAll(RefreshQuota(true),radar.Refresh(true));};menu.Items.Add(update);
   var exit=new MenuItem {Header="关闭"};exit.Click+=(s,e)=>Close();menu.Items.Add(exit);shell.ContextMenu=menu;
   shell.MouseLeftButtonDown+=(s,e)=>{DependencyObject source=e.OriginalSource as DependencyObject;while(source is Visual&&source!=shell){if(source is Button)return;source=VisualTreeHelper.GetParent(source);}try{DragMove();if(following){offsetX=Left-anchorX;offsetY=Top-anchorY;}else{manualX=Left;manualY=Top;}SaveSettings();}catch{}};
   if(!inspectable)SourceInitialized+=(s,e)=>{var h=new WindowInteropHelper(this).Handle;Native.SetWindowLong(h,-20,Native.GetWindowLong(h,-20)|0x80);};
   Loaded+=async(s,e)=>{PositionWindow();await RefreshQuota(false);};
   SizeChanged+=(s,e)=>PositionWindow();
   positionTimer.Interval=TimeSpan.FromSeconds(1);positionTimer.Tick+=(s,e)=>{if(Program.HostExited()||(Program.widgetStop!=null&&Program.widgetStop.WaitOne(0))){Close();return;}ReadMiniPosition();PositionWindow();};positionTimer.Start();
   refreshTimer.Interval=TimeSpan.FromSeconds(2);refreshTimer.Tick+=async(s,e)=>{if(DateTime.UtcNow>=nextRefresh)await RefreshQuota(false);};refreshTimer.Start();
   healthTimer.Interval=TimeSpan.FromSeconds(10);healthTimer.Tick+=(s,e)=>UpdateHealth();healthTimer.Start();
   ReadMiniPosition();PositionWindow();LoadCache();
   Closed+=(s,e)=>{closing=true;positionTimer.Stop();refreshTimer.Stop();healthTimer.Stop();resetCredits.Dispose();radar.Dispose();if(client!=null)client.Dispose();SaveSettings();};
  }
  static SolidColorBrush ColorBrush(string color){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}
  Button SmallButton(string text,string label){
   var b=new Button {Content=text,FontFamily=new FontFamily("Segoe UI"),FontSize=13,Width=21,Height=21,Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=ColorBrush("#C7C7CE"),BorderThickness=new Thickness(0),Cursor=Cursors.Hand,ToolTip=label};
   var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
   var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
   var template=new ControlTemplate(typeof(Button)) {VisualTree=border};var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,ColorBrush("#55565E")));template.Triggers.Add(hover);b.Template=template;
   AutomationProperties.SetName(b,label);return b;
  }
  void ReadMiniPosition() {
   try {
    DateTime stamp=File.GetLastWriteTimeUtc(codexState);if(stamp==stateTime)return;
    string text;using(var f=new FileStream(codexState,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(f)){if(f.Length>16777216)return;text=r.ReadToEnd();}
    var d=Json.Read(text);stateTime=stamp;var bounds=Json.Obj(Json.Get(d,"electron-avatar-overlay-bounds"));
    if(bounds!=null){anchorX=Json.Num(Json.Get(bounds,"x"),anchorX);anchorY=Json.Num(Json.Get(bounds,"y"),anchorY);var b=Json.Obj(Json.Get(bounds,"displayBounds"));if(b!=null){double w=Json.Num(Json.Get(b,"width")),h=Json.Num(Json.Get(b,"height"));if(w>0&&h>0)display=new Rect(Json.Num(Json.Get(b,"x")),Json.Num(Json.Get(b,"y")),w,h);}}
    object open=Json.Get(d,"electron-avatar-overlay-open");if(open is bool)miniOpen=(bool)open;
   }catch{}
  }
  void PositionWindow() {
   if(closing)return;
   if(following&&!miniOpen){if(IsVisible)Hide();return;}
   if(IsLoaded&&!IsVisible)Show();
   double x=following?anchorX+offsetX:(double.IsNaN(manualX)?display.Right-Width-24:manualX);
   double y=following?anchorY+offsetY:(double.IsNaN(manualY)?display.Bottom-ActualHeight-64:manualY);
   double height=ActualHeight>0?ActualHeight:126;
   var work=SystemParameters.WorkArea;var usable=display;
   if(work.Contains(new Point(anchorX,anchorY)))usable=Rect.Intersect(display,work);
   x=Math.Max(usable.Left,Math.Min(x,usable.Right-Width));y=Math.Max(usable.Top,Math.Min(y,usable.Bottom-height));
   Left=x;Top=y;
  }
  async Task RefreshQuota(bool force) {
   if(refreshing||closing)return;if(!force&&DateTime.UtcNow<nextRefresh)return;
   refreshing=true;nextRefresh=DateTime.UtcNow.AddSeconds(60);
   try {
    if(client==null||!client.Alive){if(client!=null)client.Dispose();client=new RpcClient();await client.Start();}
    var rawResult=await client.Request("account/rateLimits/read");if(closing)return;
    var result=new Dictionary<string,object>{{"rateLimits",Json.Get(rawResult,"rateLimits")},{"rateLimitsByLimitId",Json.Get(rawResult,"rateLimitsByLimitId")},{"rateLimitResetCredits",ResetCreditData.Project(Json.Get(rawResult,"rateLimitResetCredits"))}};
    connectionFailed=false;lastResult=result;lastSuccess=DateTime.UtcNow;RenderQuota(result);
    WriteAtomic(Path.Combine(dataDir,"quota-cache.json"),Json.Write(new {updatedAt=lastSuccess.ToString("o"),result=result}));
    UpdateHealth();
    var proofOperation=Dispatcher.BeginInvoke(new Action(SaveDisplayProof),DispatcherPriority.ContextIdle);
   }catch(Exception e) {
    if(closing)return;
    nextRefresh=DateTime.UtcNow.AddSeconds(60);
    connectionFailed=true;subtitle.Visibility=Visibility.Visible;footer.Visibility=Visibility.Visible;
    if(lastResult==null){subtitle.Text="暂时无法读取";footer.Text="确认 Codex 已登录 · 60 秒后重试";}else{subtitle.Text="连接暂不可用 · 保留上次读数";UpdateHealth();}
    ToolTip=e.Message;
    if(client!=null){client.Dispose();client=null;}
   }finally{refreshing=false;}
  }
  void RenderQuota(Dictionary<string,object> result) {
   rows.Children.Clear();countdowns.Clear();subtitle.Text="账号共享额度";subtitle.Visibility=Visibility.Collapsed;footer.Visibility=Visibility.Collapsed;
   var buckets=Json.Obj(Json.Get(result,"rateLimitsByLimitId"));
   var entries=new List<KeyValuePair<string,object>>();
   if(buckets!=null&&buckets.Count>0){foreach(var item in buckets.OrderBy(x=>x.Key=="codex"?0:1))entries.Add(item);}
   else{var legacy=Json.Obj(Json.Get(result,"rateLimits"));if(legacy!=null)entries.Add(new KeyValuePair<string,object>("codex",legacy));}
   int count=0;
   foreach(var item in entries){var bucket=Json.Obj(item.Value);foreach(string key in new[]{"primary","secondary"}){
    var window=Json.Obj(Json.Get(bucket,key));if(window==null||Json.Get(window,"usedPercent")==null)continue;
    double remaining=Math.Max(0,Math.Min(100,100-Json.Num(Json.Get(window,"usedPercent"))));
    double minutes=Json.Num(Json.Get(window,"windowDurationMins"));string label=minutes==10080?"周额度":minutes==300?"5 小时额度":minutes>=1440?(minutes/1440).ToString("0.#")+" 天额度":minutes>=60?(minutes/60).ToString("0.#")+" 小时额度":minutes.ToString("0.#")+" 分钟额度";
    if(item.Key!="codex")label=(Convert.ToString(Json.Get(bucket,"limitName"))??item.Key)+" · "+label;
    string reset="";double seconds=Json.Num(Json.Get(window,"resetsAt"));DateTimeOffset resetAt=DateTimeOffset.MinValue;if(seconds>0){resetAt=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(seconds);reset=resetAt.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm");}
    Brush accent=ColorBrush(remaining<15?"#FF706B":remaining<35?"#F5CE72":"#52DBAC");
    var card=new Grid {Margin=new Thickness(0,count==0?0:10,0,0)};card.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(68)});card.ColumnDefinitions.Add(new ColumnDefinition());
    var gauge=new Grid {Width=68,Height=68};gauge.Children.Add(new QuotaRing(remaining,accent));
    var center=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,-1,0,0)};
    var percentage=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
    percentage.Children.Add(new TextBlock {Text=remaining.ToString("0.#"),FontFamily=new FontFamily("Segoe UI"),FontSize=20,FontWeight=FontWeights.SemiBold,Foreground=ColorBrush("#F5F5F7")});
    percentage.Children.Add(new TextBlock {Text="%",FontFamily=new FontFamily("Segoe UI"),FontSize=9,FontWeight=FontWeights.Medium,Foreground=ColorBrush("#C9D2DE"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(1,0,0,4)});center.Children.Add(percentage);center.Children.Add(new TextBlock {Text="剩余",FontSize=7.5,Foreground=ColorBrush("#94A1B5"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,-2,0,0)});gauge.Children.Add(center);card.Children.Add(gauge);
    var info=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(11,0,0,0)};Grid.SetColumn(info,1);card.Children.Add(info);
    info.Children.Add(new TextBlock {Text=(item.Key=="codex"?"CODEX · ":"")+label,FontSize=9,FontWeight=FontWeights.Medium,Foreground=ColorBrush("#A7B4C6"),TextWrapping=TextWrapping.Wrap});
    info.Children.Add(new TextBlock {Text=reset!=""?reset+" 重置":"重置时间未知",FontFamily=new FontFamily("Segoe UI"),FontSize=10.5,FontWeight=FontWeights.SemiBold,Foreground=ColorBrush("#ECF0F7"),Margin=new Thickness(0,5,0,0)});
    var countdown=new TextBlock {FontSize=9,Foreground=ColorBrush("#8E9BAF"),Margin=new Thickness(0,4,0,0)};info.Children.Add(countdown);if(resetAt!=DateTimeOffset.MinValue)countdowns.Add(Tuple.Create(countdown,resetAt));else countdown.Text="北京时间 · 周期重置";
    card.ToolTip=label+" · 剩余 "+remaining.ToString("0.#")+"%"+(reset!=""?"\n周期重置："+reset+"（北京时间）":"")+"\n来自账号接口，每 60 秒自动刷新。";
    AutomationProperties.SetName(card,label+"剩余 "+remaining.ToString("0.#")+"%"+(reset!=""?"，"+reset+" 重置":""));rows.Children.Add(card);
    count++;
   }}
   if(count==0){subtitle.Text="当前账号暂无可用额度信息";subtitle.Visibility=Visibility.Visible;}
   ToolTip="账号共享的 Codex 额度；重置日期为北京时间。更新于 "+lastSuccess.ToLocalTime().ToString("HH:mm:ss")+"。拖动可调整与 Mini 的间距，右键可关闭跟随。";
   resetCredits.SetData(Json.Get(result,"rateLimitResetCredits"),lastSuccess,connectionFailed||(DateTime.UtcNow-lastSuccess).TotalMinutes>3);UpdateCountdowns();PositionWindow();
  }
  void UpdateCountdowns() {foreach(var item in countdowns){var remaining=item.Item2-DateTimeOffset.UtcNow;item.Item1.Text=remaining<=TimeSpan.Zero?"到期 · 等待账号刷新":remaining.TotalDays>=1?"还剩 "+((int)remaining.TotalDays)+"天 "+remaining.Hours+"小时":remaining.TotalHours>=1?"还剩 "+((int)remaining.TotalHours)+"小时 "+remaining.Minutes+"分":"还剩 "+Math.Max(1,(int)Math.Ceiling(remaining.TotalMinutes))+"分钟";}}
  void UpdateHealth() {
   UpdateCountdowns();
   resetCredits.UpdateHealth(lastSuccess,connectionFailed||lastSuccess==DateTime.MinValue||(DateTime.UtcNow-lastSuccess).TotalMinutes>3);
   if(lastSuccess==DateTime.MinValue)return;
   double age=(DateTime.UtcNow-lastSuccess).TotalMinutes;
   footer.Visibility=(age>3||connectionFailed)?Visibility.Visible:Visibility.Collapsed;
   footer.Text=(age>3?"数据已过期 · 上次 ":"上次成功更新 ")+lastSuccess.ToLocalTime().ToString("HH:mm");
   footer.Foreground=ColorBrush(age>3?"#F4C575":"#8794A8");
  }
  void SaveDisplayProof() {
   try {
    WriteAtomic(Path.Combine(dataDir,"window-state.json"),Json.Write(new {visible=IsVisible,topmost=Topmost,left=Left,top=Top,width=ActualWidth,height=ActualHeight,following=following,anchorX=anchorX,anchorY=anchorY,updatedAt=lastSuccess.ToString("o"),radar=radar.DisplayState}));
    var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"小窗预览.png")))encoder.Save(file);
   }catch{}
  }
  void LoadCache() {try{var cache=Json.Read(File.ReadAllText(Path.Combine(dataDir,"quota-cache.json")));lastResult=Json.Obj(Json.Get(cache,"result"));lastSuccess=DateTime.Parse(Convert.ToString(Json.Get(cache,"updatedAt")),null,DateTimeStyles.RoundtripKind).ToUniversalTime();RenderQuota(lastResult);subtitle.Text="上次读数 · 正在刷新";subtitle.Visibility=Visibility.Visible;connectionFailed=true;UpdateHealth();}catch{}}
  void LoadSettings() {try{var d=Json.Read(File.ReadAllText(Path.Combine(dataDir,"settings.json")));following=Convert.ToBoolean(Json.Get(d,"following"));offsetX=Json.Num(Json.Get(d,"offsetX"),-66);offsetY=Json.Num(Json.Get(d,"offsetY"),128);manualX=Json.Num(Json.Get(d,"manualX"),double.NaN);manualY=Json.Num(Json.Get(d,"manualY"),double.NaN);double version=Json.Num(Json.Get(d,"uiVersion"));if(version<2){offsetX+=56;if(!double.IsNaN(manualX))manualX+=56;}if(version<3){offsetX-=40;if(!double.IsNaN(manualX))manualX-=40;SaveSettings();}}catch{}}
  void SaveSettings() {try{WriteAtomic(Path.Combine(dataDir,"settings.json"),Json.Write(new {uiVersion=3,following=following,offsetX=offsetX,offsetY=offsetY,manualX=double.IsNaN(manualX)?(object)null:manualX,manualY=double.IsNaN(manualY)?(object)null:manualY}));}catch{}}
  static void WriteAtomic(string file,string text) {string temp=file+".tmp";File.WriteAllText(temp,text,Encoding.UTF8);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
 }

 static class Program {
  static Mutex singleton;
  internal static EventWaitHandle widgetStop;
  static Process desktopHost;
  public static bool HostExited(){try{return desktopHost==null||desktopHost.HasExited;}catch{return true;}}
  [STAThread] public static int Main(string[] args) {
   try {
    if(args.Contains("--remove-legacy-autostart")||args.Contains("--disable-autostart")){Companion.RemoveLegacyAutostart();return 0;}
    if(args.Contains("--launch-chatgpt"))return Companion.LaunchChatGPT();
    if(args.Contains("--stop-watch")){Companion.Signal(Companion.WatchStop);return 0;}
    if(args.Contains("--exit-widget")){Companion.Signal(Companion.WidgetStop);return 0;}
    if(args.Contains("--watch")){using(var host=Companion.DesktopHost())if(host!=null)Companion.StartAttachedWidget();return 0;}
    if(args.Contains("--test-companion"))return Companion.TestHostFilter();
    if(args.Contains("--probe"))return Probe().GetAwaiter().GetResult();
    int testHostIndex=Array.IndexOf(args,"--test-host");
    desktopHost=testHostIndex>=0?Process.GetProcessById(Int32.Parse(args[testHostIndex+1])):Companion.DesktopHost();
    if(HostExited()){if(desktopHost!=null)desktopHost.Dispose();return 0;}
    bool created;singleton=new Mutex(true,"Local\\CodexQuotaMini_1",out created);if(!created)return 0;
    widgetStop=new EventWaitHandle(false,EventResetMode.AutoReset,Companion.WidgetStop);
    var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
    desktopHost.EnableRaisingEvents=true;
    desktopHost.Exited+=(s,e)=>app.Dispatcher.BeginInvoke(new Action(()=>app.Shutdown()));
    app.DispatcherUnhandledException+=(s,e)=>{try{Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","error.txt"),e.Exception.ToString());}catch{}e.Handled=true;app.Shutdown(1);};
    app.Run(new QuotaWindow(args.Contains("--inspectable")));desktopHost.Dispose();widgetStop.Dispose();singleton.ReleaseMutex();return 0;
   }catch(Exception e){MessageBox.Show(e.Message,"Codex 额度小窗",MessageBoxButton.OK,MessageBoxImage.Information);return 1;}
  }
  static async Task<int> Probe(){using(var rpc=new RpcClient()){await rpc.Start();var result=await rpc.Request("account/rateLimits/read");Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","probe-result.json"),Json.Write(new {updatedAt=DateTime.UtcNow.ToString("o"),result=result}),Encoding.UTF8);return 0;}}
 }
}
