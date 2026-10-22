using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CodexQuotaMini {
 sealed class ResetCredit {
  public string Status,Title;
  public DateTimeOffset? GrantedAt,ExpiresAt;
  public bool Available {get{return Status=="available";}}
 }
 sealed class ResetCreditSnapshot {
  public bool Provided,DetailsProvided;
  public int? AvailableCount;
  public List<ResetCredit> Cards=new List<ResetCredit>();
  public bool PartialDetails {get{return AvailableCount.HasValue&&AvailableCount.Value>Cards.Count(c=>c.Available);}}
 }
 static class ResetCreditData {
  public static object Project(object value) {
   var source=Json.Obj(value);if(source==null)return null;
   var projected=new Dictionary<string,object>{{"availableCount",Json.Get(source,"availableCount")},{"credits",null}};
   object raw=Json.Get(source,"credits");
   if(raw!=null){var cards=new List<Dictionary<string,object>>();foreach(object item in RadarData.Items(raw)){var card=Json.Obj(item);if(card==null)continue;cards.Add(new Dictionary<string,object>{{"status",Json.Get(card,"status")},{"grantedAt",Json.Get(card,"grantedAt")},{"expiresAt",Json.Get(card,"expiresAt")},{"title",Json.Get(card,"title")},{"resetType",Json.Get(card,"resetType")}});}projected["credits"]=cards;}
   return projected;
  }
  public static DateTimeOffset? Timestamp(object value) {
   if(value==null||value is bool)return null;
   double seconds=Json.Num(value,double.NaN);if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<=0||seconds>253402300799)return null;
   try{return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(seconds);}catch{return null;}
  }
  public static ResetCreditSnapshot Read(object value) {
   var snapshot=new ResetCreditSnapshot();var source=Json.Obj(value);if(source==null)return snapshot;
   snapshot.Provided=true;object rawCount=Json.Get(source,"availableCount");double count=Json.Num(rawCount,double.NaN);
   if(rawCount!=null&&!(rawCount is bool)&&!double.IsNaN(count)&&count>=0&&count<=Int32.MaxValue&&count==Math.Floor(count))snapshot.AvailableCount=(int)count;
   object rows=Json.Get(source,"credits");snapshot.DetailsProvided=rows!=null;
   foreach(object item in RadarData.Items(rows)) {var card=Json.Obj(item);if(card==null)continue;snapshot.Cards.Add(new ResetCredit {Status=RadarData.Str(card,"status").ToLowerInvariant(),Title=RadarData.Str(card,"title"),GrantedAt=Timestamp(Json.Get(card,"grantedAt")),ExpiresAt=Timestamp(Json.Get(card,"expiresAt"))});}
   snapshot.Cards=snapshot.Cards.OrderBy(c=>c.Available?0:1).ThenBy(c=>c.ExpiresAt??DateTimeOffset.MaxValue).ToList();return snapshot;
  }
  public static string Remaining(DateTimeOffset expires,DateTimeOffset now) {
   var remaining=expires-now;if(remaining<=TimeSpan.Zero)return "已到期";
   if(remaining.TotalDays>=1)return (int)remaining.TotalDays+"天 "+remaining.Hours+"小时";
   if(remaining.TotalHours>=1)return (int)remaining.TotalHours+"小时 "+remaining.Minutes+"分";
   return Math.Max(1,(int)Math.Ceiling(remaining.TotalMinutes))+"分钟";
  }
  public static string Summary(ResetCreditSnapshot snapshot,DateTimeOffset now,out bool urgent) {
   urgent=false;
   if(!snapshot.Provided)return "账号暂未返回重置卡信息";
   if(snapshot.AvailableCount==0)return "暂无可用重置卡";
   var available=snapshot.Cards.Where(c=>c.Available).ToList();
   if(available.Any(c=>c.ExpiresAt.HasValue&&c.ExpiresAt<=now)){urgent=true;return "有卡已到期 · 等待账号刷新";}
   var next=available.Where(c=>c.ExpiresAt.HasValue).OrderBy(c=>c.ExpiresAt).FirstOrDefault();
   if(next==null)return "到期时间未知 · 点此查看";
   urgent=next.ExpiresAt.Value-now<=TimeSpan.FromHours(24);
   string prefix=!snapshot.AvailableCount.HasValue||snapshot.PartialDetails||available.Any(c=>!c.ExpiresAt.HasValue)?"已知 ":"";
   var left=next.ExpiresAt.Value-now;string shortLeft=left.TotalDays>=1?(int)left.TotalDays+"天":left.TotalHours>=1?(int)left.TotalHours+"小时":Math.Max(1,(int)Math.Ceiling(left.TotalMinutes))+"分钟";
   return prefix+next.ExpiresAt.Value.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm")+" 到期 · "+shortLeft;
  }
  public static string Status(ResetCredit card,DateTimeOffset now) {
   if(card.Status=="expired"||(card.Available&&card.ExpiresAt.HasValue&&card.ExpiresAt<=now))return "已到期";
   if(card.Status=="consumed"||card.Status=="used"||card.Status=="redeemed")return "已使用";
   if(card.Available)return "可用";
   return "状态未确认";
  }
 }

 sealed class ResetCreditsControl : StackPanel,IDisposable {
  readonly Button button;
  readonly TextBlock title=new TextBlock(),hint=new TextBlock();
  readonly Popup popup=new Popup();
  readonly StackPanel details=new StackPanel();
  ResetCreditSnapshot snapshot=new ResetCreditSnapshot();
  DateTime lastRead=DateTime.MinValue;
  bool stale=true;
  static Brush ColorOf(string value){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}
  public ResetCreditsControl() {
   Margin=new Thickness(0,8,0,0);
   var content=new StackPanel();var header=new DockPanel();
   header.Children.Add(new TextBlock {Text="›",FontSize=13,Foreground=ColorOf("#B1A4DE"),HorizontalAlignment=HorizontalAlignment.Right});DockPanel.SetDock(header.Children[0],Dock.Right);
   title.FontSize=10;title.FontWeight=FontWeights.SemiBold;title.Foreground=ColorOf("#D1C5F5");header.Children.Add(title);content.Children.Add(header);
   hint.FontSize=8.8;hint.Margin=new Thickness(0,3,0,0);hint.Foreground=ColorOf("#A9A6BA");hint.TextWrapping=TextWrapping.Wrap;content.Children.Add(hint);
   var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.PaddingProperty,new Thickness(9,5,9,6));
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(ContentControl.ContentProperty));border.AppendChild(presenter);
   button=new Button {Content=content,BorderThickness=new Thickness(0),Background=ColorOf("#2A293A"),Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Stretch,Template=new ControlTemplate(typeof(Button)) {VisualTree=border}};
   button.MouseEnter+=(s,e)=>button.Background=ColorOf("#333146");button.MouseLeave+=(s,e)=>button.Background=ColorOf("#2A293A");button.Click+=(s,e)=>{RenderDetails();popup.IsOpen=!popup.IsOpen;};Children.Add(button);
   var card=new Border {Margin=new Thickness(7),CornerRadius=new CornerRadius(16),Background=ColorOf("#212632"),BorderBrush=ColorOf("#4A5062"),BorderThickness=new Thickness(1),Padding=new Thickness(13),Width=248,Effect=new DropShadowEffect {BlurRadius=12,ShadowDepth=3,Opacity=.28}};
   System.Windows.Documents.TextElement.SetFontFamily(card,new FontFamily("Microsoft YaHei UI"));
   card.Child=new ScrollViewer {Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=420};
   popup.Child=card;popup.PlacementTarget=button;popup.Placement=PlacementMode.Right;popup.HorizontalOffset=1;popup.AllowsTransparency=true;popup.StaysOpen=false;popup.PopupAnimation=PopupAnimation.Fade;
   IsVisibleChanged+=(s,e)=>{if(!IsVisible)popup.IsOpen=false;};Render();
  }
  public void SetData(object value,DateTime updatedAt,bool dataStale) {snapshot=ResetCreditData.Read(value);lastRead=updatedAt;stale=dataStale;Render();}
  public void UpdateHealth(DateTime updatedAt,bool dataStale) {lastRead=updatedAt;stale=dataStale;Render();if(popup.IsOpen)RenderDetails();}
  void Render() {
   var now=DateTimeOffset.UtcNow;
   title.Text="重置卡 · "+(snapshot.AvailableCount.HasValue?snapshot.AvailableCount.Value+" 张":snapshot.Provided?"数量未知":"接口未提供");if(stale&&snapshot.Provided)title.Text+="（缓存）";
   bool urgent;hint.Text=ResetCreditData.Summary(snapshot,now,out urgent);hint.Foreground=ColorOf(urgent?"#F1C576":"#A9A6BA");
   button.ToolTip="点击查看每张重置卡的到期时间与剩余时长（北京时间）。\n只查询，不使用重置卡。\n"+(lastRead!=DateTime.MinValue?"账号读数更新于 "+new DateTimeOffset(lastRead).ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm:ss"):"等待账号查询")+(stale?"\n当前为缓存数据，等待刷新。":"");
   AutomationProperties.SetName(button,title.Text+"，"+hint.Text+"，点击查询明细");
  }
  TextBlock Text(string value,double size,string color) {return new TextBlock {Text=value,FontSize=size,Foreground=ColorOf(color),TextWrapping=TextWrapping.Wrap};}
  void RenderDetails() {
   details.Children.Clear();var heading=Text("账号重置卡",12,"#EBE8F7");heading.FontWeight=FontWeights.SemiBold;details.Children.Add(heading);
   var meta=Text((snapshot.AvailableCount.HasValue?"可用 "+snapshot.AvailableCount.Value+" 张":"可用数量暂未提供")+" · 北京时间",9,"#AAA6BE");meta.Margin=new Thickness(0,5,0,9);details.Children.Add(meta);
   if(stale){var warning=Text("缓存读数 · 到期情况等待账号刷新",9,"#F1C576");warning.Margin=new Thickness(0,0,0,8);details.Children.Add(warning);}
   if(!snapshot.Provided||!snapshot.DetailsProvided||snapshot.Cards.Count==0)details.Children.Add(Text(snapshot.AvailableCount==0?"当前没有可用重置卡。":"账号接口未返回卡片明细，暂不能确认各张卡的到期时间。",10,"#B5B9C9"));
   var now=DateTimeOffset.UtcNow;int index=0;
   foreach(var credit in snapshot.Cards) {
    var row=new StackPanel {Margin=new Thickness(0,0,0,10)};string status=ResetCreditData.Status(credit,now);bool urgent=credit.Available&&credit.ExpiresAt.HasValue&&credit.ExpiresAt.Value-now<=TimeSpan.FromHours(24);
    row.Children.Add(Text("重置卡 "+(++index)+" · "+status,10,urgent?"#F1C576":"#D5C8FF"));
    var expiry=Text(credit.ExpiresAt.HasValue?credit.ExpiresAt.Value.ToOffset(RadarData.Beijing).ToString("yyyy/MM/dd HH:mm")+" 到期":"接口未提供到期时间",10,"#E2E7F1");expiry.Margin=new Thickness(0,4,0,0);row.Children.Add(expiry);
    if(credit.ExpiresAt.HasValue){var remaining=Text((credit.ExpiresAt.Value>now?"还剩 ":"")+ResetCreditData.Remaining(credit.ExpiresAt.Value,now),9,urgent?"#F1C576":"#94A0B4");remaining.Margin=new Thickness(0,3,0,0);row.Children.Add(remaining);}
    if(credit.GrantedAt.HasValue){var granted=Text("发放 "+credit.GrantedAt.Value.ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm"),8.5,"#7F8A9E");granted.Margin=new Thickness(0,3,0,0);row.Children.Add(granted);}
    details.Children.Add(row);
   }
   if(snapshot.PartialDetails)details.Children.Add(Text("接口仅返回部分明细，未列出的卡片到期时间未知。可用总张数以账号返回的数量为准。",9,"#F1C576"));
   var footer=Text("每 60 秒查询 · 只读，不使用卡片\n"+(lastRead!=DateTime.MinValue?"更新 "+new DateTimeOffset(lastRead).ToOffset(RadarData.Beijing).ToString("MM/dd HH:mm:ss"):"等待查询"),8.5,"#8490A3");footer.Margin=new Thickness(0,4,0,0);details.Children.Add(footer);
  }
  public void Dispose() {popup.IsOpen=false;}
 }
}
