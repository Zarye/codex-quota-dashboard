using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexQuotaDashboardSetup {
 sealed class Installer : Form {
  readonly Button install=new Button {Text="安装",Width=100,Height=32};
  readonly CheckBox launch=new CheckBox {Text="安装后启动看板（联动桌面应用）",Checked=true,AutoSize=true};
  readonly Label status=new Label {AutoSize=false,Height=70,Width=470};
  readonly string destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaDashboard");
  bool installed;
  public Installer(){
   Text="安装 Codex 额度看板";ClientSize=new System.Drawing.Size(520,310);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
   var panel=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(20)};Controls.Add(panel);
   panel.Controls.Add(new Label {Text="Codex 额度看板 · 本机直连版",AutoSize=true,Font=new System.Drawing.Font("Microsoft YaHei UI",14,System.Drawing.FontStyle.Bold),Margin=new Padding(0,0,0,14)});
   panel.Controls.Add(new Label {Text="个人额度、重置卡、Tibo 公开消息和本地规则研判。\n不需要 Agent、API 密钥、浏览器扩展或独立服务器。\n首次使用可能需要在内置采集页登录 X。",AutoSize=true,Margin=new Padding(0,0,0,12)});
   panel.Controls.Add(new Label {Text="安装位置："+destination,AutoSize=true,Margin=new Padding(0,0,0,12)});panel.Controls.Add(launch);panel.Controls.Add(status);panel.Controls.Add(install);
   install.Click+=async(s,e)=>await Install();
  }
  async Task Install(){
   if(installed){Close();return;}
   install.Enabled=false;
   try {
    var target=Path.GetFullPath(destination);if(!target.StartsWith(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("安装路径无效");
    Directory.CreateDirectory(target);status.Text="正在安装…";
    string installedExe=Path.Combine(target,"CodexQuotaMini.exe");
    if(File.Exists(installedExe))using(var stopping=Process.Start(new ProcessStartInfo {FileName=installedExe,Arguments="--exit-widget",UseShellExecute=false,CreateNoWindow=true})){await Task.Delay(1500);}
    using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var archive=new ZipArchive(resource,ZipArchiveMode.Read)){
     foreach(var entry in archive.Entries){string file=Path.GetFullPath(Path.Combine(target,entry.FullName));if(!file.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||entry.FullName.Contains("/")||entry.FullName.Contains("\\"))throw new IOException("安装内容路径无效");using(var input=entry.Open())using(var output=File.Create(file))input.CopyTo(output);}
    }
    string menu=Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
    dynamic shortcut=shell.CreateShortcut(Path.Combine(menu,"Codex 额度看板.lnk"));shortcut.TargetPath=installedExe;shortcut.Arguments="--launch-chatgpt";shortcut.WorkingDirectory=target;shortcut.Description="Codex 额度、重置卡与 Tibo 本地雷达";shortcut.Save();
    status.Text="安装完成。已创建开始菜单入口。\n电脑需要 .NET Framework 4.6.2+、WebView2 Runtime 及已登录的桌面应用。";
    if(launch.Checked){using(var stopping=Process.Start(new ProcessStartInfo {FileName=installedExe,Arguments="--exit-widget",UseShellExecute=false,CreateNoWindow=true})){await Task.Delay(1000);}Process.Start(new ProcessStartInfo {FileName=installedExe,Arguments="--launch-chatgpt",UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=target});}
    installed=true;install.Text="完成";install.Enabled=true;
   }catch(Exception error){status.Text="安装未完成："+error.Message;install.Enabled=true;}
  }
  [STAThread] static void Main(){Application.EnableVisualStyles();Application.Run(new Installer());}
 }
}
