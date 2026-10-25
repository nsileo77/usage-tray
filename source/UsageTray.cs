using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

public class Settings {
 public double? Five, Week;
 public DateTime? FiveReset, WeekReset, Updated;
 public string CodexPath = ""; public string Layout="single",SelectedWindow="auto"; public DisplayOptions Display=new DisplayOptions();
 public int? StripX,StripY;
}
public class Quota {
 public string Name; public double? Used,WindowMinutes; public DateTime? Reset;
}
public sealed class QuotaSnapshot {
 public List<Quota> Rows=new List<Quota>(); public DateTime? Fetched;
 public void Begin(){Rows.Clear();Fetched=null;}
 public void Accept(List<Quota> rows,DateTime now){Rows=rows;Fetched=now;}
}
public static class Protocol {
 public static Quota Weekly(List<Quota> rows){return rows.FirstOrDefault(q=>q.Name=="codex / weekly");}
 public static string Discover(string preferred) {
  if(!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred)) return preferred;
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
  if(Directory.Exists(root)) {
   foreach(var dir in new DirectoryInfo(root).GetDirectories().OrderByDescending(d=>d.LastWriteTimeUtc)) {
    string candidate=Path.Combine(dir.FullName,"codex.exe"); if(File.Exists(candidate))return candidate;
   }
  }
  foreach(string part in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) {
   try {if(!Path.IsPathRooted(part))continue;string candidate=Path.Combine(part.Trim('"'),"codex.exe");if(File.Exists(candidate))return candidate;}catch{}
  }
  return preferred??"";
 }
 public static Dictionary<string,object> Obj(object x) { return x as Dictionary<string,object>; }
 public static object Get(Dictionary<string,object> d,string k) { return d!=null && d.ContainsKey(k)?d[k]:null; }
 public static double? Number(object value) {
  if(value==null || value is string || value is bool) return null;
  try {double n=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);return double.IsNaN(n)||double.IsInfinity(n)?(double?)null:n;}catch{return null;}
 }
 public static List<Quota> Parse(Dictionary<string,object> result) {
  var rows=new List<Quota>(); var map=Obj(Get(result,"rateLimitsByLimitId"));
  if(map==null) {map=new Dictionary<string,object>();var old=Obj(Get(result,"rateLimits"));if(old!=null)map[Convert.ToString(Get(old,"limitId"))??"legacy"]=old;}
  foreach(var pair in map) foreach(string key in new[]{"primary","secondary"}) {
   var w=Obj(Get(Obj(pair.Value),key));if(w==null)continue;
   var used=Number(Get(w,"usedPercent"));if(used<0)used=null;
   var mins=Number(Get(w,"windowDurationMins"));
   string duration=mins==300?"5-hour":mins==10080?"weekly":mins.HasValue&&mins>0?mins.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+" min":"unknown window";
   var seconds=Number(Get(w,"resetsAt"));DateTime? reset=null;
   if(seconds.HasValue && seconds>=0 && seconds<=253402300799 && seconds==Math.Truncate(seconds.Value))try{reset=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds.Value);}catch{}
   rows.Add(new Quota{Name=pair.Key+" / "+duration,Used=used,Reset=reset,WindowMinutes=mins});
  }return rows;
 }
 public static string Identity(Dictionary<string,object> result) {
  var a=Obj(Get(result,"account"));if(a==null||Convert.ToString(Get(a,"type"))!="chatgpt")throw new Exception("Existing ChatGPT sign-in unavailable. No login was started.");
  var id=Convert.ToString(Get(a,"email"));if(string.IsNullOrEmpty(id))throw new Exception("Cannot verify account isolation; usage unavailable.");
  return new JavaScriptSerializer().Serialize(result);
 }
 public static bool IsStale(DateTime? fetched,DateTime now,bool failed){return fetched.HasValue && (failed || now-fetched.Value>TimeSpan.FromMinutes(2));}
 public static void SameAccount(string before,string after){if(before!=after)throw new Exception("Account changed during refresh; usage discarded.");}
 public static Dictionary<string,object> Response(Process p,int id) {return ReadResponse(p.StandardOutput,id);}
 public static Dictionary<string,object> ReadResponse(TextReader reader,int id) {
  var json=new JavaScriptSerializer();
  string stage=id==1?"initialize":id==2?"quota read":"account check";
  while(true) { string line=reader.ReadLine(); if(line==null) throw new Exception("Codex closed the connection during "+stage+".");
   Dictionary<string,object> msg; try { msg=json.Deserialize<Dictionary<string,object>>(line); } catch { continue; }
   if(Convert.ToString(Get(msg,"id"))!=id.ToString()) continue;
   if(Get(msg,"error")!=null) throw new Exception("Codex rejected "+stage+". Check its sign-in and version; no login was started.");
   var result=Obj(Get(msg,"result"));if(result==null)throw new Exception("Codex returned an invalid "+stage+" response.");return result;
  }
 }
 public static List<Quota> Fetch(string path){return Fetch(path,null);}
 public static List<Quota> Fetch(string path,Action<int,bool> lifecycle) {
  if(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) throw new Exception("Choose the official codex.exe first.");
  var start=new ProcessStartInfo(path,"app-server --listen stdio://") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetTempPath() };
  using(var p=new Process {StartInfo=start}) {
   string startupReason="";int timedOut=0;
   p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null && e.Data.IndexOf("failed to initialize sqlite state runtime",StringComparison.OrdinalIgnoreCase)>=0)startupReason="Codex could not initialize its local SQLite runtime. Check execution permissions; no account request completed.";}; p.Start();if(lifecycle!=null)lifecycle(p.Id,true); p.BeginErrorReadLine();
   using(var timeout=new System.Threading.Timer(s=>{try{if(!p.HasExited){Interlocked.Exchange(ref timedOut,1);p.Kill();}}catch{}},null,25000,Timeout.Infinite)) {
    try {
     p.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"usage_tray_review\",\"title\":\"Usage Tray review\",\"version\":\"2.0.2\"}}}"); p.StandardInput.Flush(); Response(p,1);
     p.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
     p.StandardInput.WriteLine("{\"id\":3,\"method\":\"account/read\",\"params\":{\"refreshToken\":false}}");p.StandardInput.Flush();
     string identity=Identity(Response(p,3));
     p.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}"); p.StandardInput.Flush();
          var result=Response(p,2);
     p.StandardInput.WriteLine("{\"id\":4,\"method\":\"account/read\",\"params\":{\"refreshToken\":false}}");p.StandardInput.Flush();
     SameAccount(identity,Identity(Response(p,4)));return Parse(result);
    } catch {if(p.HasExited)p.WaitForExit();if(timedOut!=0)throw new Exception("Codex read timed out after 25 seconds; usage unavailable.");if(startupReason.Length>0)throw new Exception(startupReason);throw;}
    finally { try { p.StandardInput.Close(); if(!p.WaitForExit(1500)){p.Kill();p.WaitForExit(1500);}if(lifecycle!=null)lifecycle(p.Id,false); }catch{} }
   }
  }
 }
}
public class TrayForm : Form {
 readonly string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UsageTrayReview");
 Settings settings=new Settings(); ComboBox layoutChoice,windowChoice; bool updatingDisplay; NotifyIcon tray; Label status; TextBox display; ComboBox mode;
 NumericUpDown week; CheckBox weekKnown,top,auto; DateTimePicker weekReset;
 Panel manual; Button refresh; bool busy,exiting; readonly QuotaSnapshot snapshot=new QuotaSnapshot(); List<Quota> live {get{return snapshot.Rows;}} DateTime? fetched {get{return snapshot.Fetched;}} string error="";
 System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); DateTime nextPoll=DateTime.MinValue;
 TaskbarStrip strip;
 public TrayForm() {
  Text="Usage Tray"; ClientSize=new Size(620,660); MinimumSize=new Size(636,699); Font=new Font("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
  try { string file=Path.Combine(folder,"settings.json"); if(File.Exists(file)) settings=SettingsStore.Load(file); } catch { error="Settings could not be read. Enter values again."; }
  var layout=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18),AutoScroll=true }; Controls.Add(layout);
  layout.Controls.Add(new Label {Text="Usage Tray | review prototype",Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true});
  layout.Controls.Add(new Label {Text="Remaining allowance • resets • source",AutoSize=true});
  mode=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=560}; mode.Items.AddRange(new object[]{"Manual fallback — values entered by you","Automatic — account usage (Codex feed)"}); mode.SelectedIndex=1; layout.Controls.Add(mode);
  var layoutRow=new FlowLayoutPanel {Width=570,Height=64};layout.Controls.Add(layoutRow);
  layoutChoice=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=260,AccessibleName="Meter layout"};layoutChoice.Items.AddRange(new object[]{"Standard dials","Compact rows / lines"});layoutChoice.SelectedIndex=settings.Layout=="double"?1:0;
  windowChoice=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=290,AccessibleName="Standard dials window"};windowChoice.Items.AddRange(new object[]{"Standard: all reported windows","Standard: weekly only","Standard: five-hour only"});windowChoice.SelectedIndex=settings.SelectedWindow=="weekly"?1:settings.SelectedWindow=="5-hour"?2:0;windowChoice.Enabled=layoutChoice.SelectedIndex==0;
  layoutRow.Controls.Add(layoutChoice);layoutRow.Controls.Add(windowChoice);layoutRow.Controls.Add(new Label{Text="Layout preference is saved only for this review build.",AutoSize=true});
  var preferences=new GroupBox{Text="Display preferences",Width=560,Height=225};var prefFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(8,16,4,2)};preferences.Controls.Add(prefFlow);layout.Controls.Add(preferences);
  var fields=new[]{"ShowIcon","ShowFive","ShowWeek","ShowLabels","ShowAmounts","ShowReset","ShowPace","ShowLines","ShowCountdown"};var names=new[]{"Show logo","Five-hour section","Weekly section","Window labels","Remaining amount","Reset date / time","White pace notches","Compact lines","Relative countdown"};var checks=new List<CheckBox>();
  for(int n=0;n<fields.Length;n++){string field=fields[n];var check=new CheckBox{Text=names[n],Width=170,Height=23,Checked=(bool)typeof(DisplayOptions).GetField(field).GetValue(settings.Display)};checks.Add(check);prefFlow.Controls.Add(check);check.CheckedChanged+=(sender,e)=>{if(updatingDisplay)return;typeof(DisplayOptions).GetField(field).SetValue(settings.Display,check.Checked);Persist();Render();};}
  Action<DisplayOptions> preset=value=>{updatingDisplay=true;settings.Display=value;for(int n=0;n<fields.Length;n++)checks[n].Checked=(bool)typeof(DisplayOptions).GetField(fields[n]).GetValue(value);updatingDisplay=false;Persist();Render();};
  var defaults=new Button{Text="Balanced defaults",Width=160};defaults.Click+=(sender,e)=>preset(new DisplayOptions());var minimal=new Button{Text="Minimal",Width=120};minimal.Click+=(sender,e)=>preset(DisplayOptions.Minimal());prefFlow.Controls.Add(defaults);prefFlow.Controls.Add(minimal);
  var chooseLogo=new Button{Text="Choose local image...",Width=170};var genericLogo=new Button{Text="Use default AI logo",Width=170};
  chooseLogo.Click+=(sender,e)=>{using(var dialog=new OpenFileDialog{Title="Select a local logo (PNG, JPEG or BMP; max 4 MB / 4096 px)",Filter="Images|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;using(var check=LocalLogo.Load(dialog.FileName)){if(check==null){MessageBox.Show("Choose a valid local PNG, JPEG or BMP, no larger than 4 MB and 4096 pixels per side. The current preference is unchanged.","Logo unavailable");return;}}settings.Display.LogoPath=dialog.FileName;settings.Display.ShowIcon=true;checks[0].Checked=true;Persist();Render();}};
  genericLogo.Click+=(sender,e)=>{settings.Display.LogoPath="";settings.Display.ShowIcon=true;checks[0].Checked=true;Persist();Render();};prefFlow.Controls.Add(chooseLogo);prefFlow.Controls.Add(genericLogo);
  prefFlow.Controls.Add(new Label{Text="Missing/invalid images use the AI logo. Images stay local; uncheck Show logo to hide.",Width=530,Height=32});
  display=new TextBox {Multiline=true,ReadOnly=true,Width=560,Height=175,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(242,246,249),Font=new Font("Segoe UI",11)}; layout.Controls.Add(display);
  status=new Label {Width=560,Height=50}; layout.Controls.Add(status);
  manual=new Panel {Width=560,Height=90,Enabled=false}; layout.Controls.Add(manual);
  weekKnown=new CheckBox {Text="Weekly remaining %",Left=0,Top=6,Width=185};
  week=new NumericUpDown {Left=190,Top=3,Width=70,Maximum=100};weekReset=Picker(270,3);
  manual.Controls.AddRange(new Control[]{weekKnown,week,weekReset});
  var save=new Button {Text="Save manual values",Left=0,Top=44,Width=180};save.Click+=(s,e)=>SaveManual();manual.Controls.Add(save);
  manual.Controls.Add(new Label {Text="Checked date = reset time (local)",Left=270,Top=48,Width=285});
  weekKnown.Checked=settings.Week.HasValue;week.Value=(decimal)Math.Max(0,Math.Min(100,settings.Week??0));SetPicker(weekReset,settings.WeekReset);
  var actions=new FlowLayoutPanel {Width=570,Height=40}; layout.Controls.Add(actions);
  refresh=new Button {Text="Refresh now",Width=140,Enabled=true}; refresh.Click+=async(s,e)=>await RefreshLive(); actions.Controls.Add(refresh);
  var choose=new Button {Text="Choose codex.exe",Width=170}; choose.Click+=(s,e)=>{using(var d=new OpenFileDialog {Filter="Codex executable|codex.exe",Title="Select the official Codex executable"}) if(d.ShowDialog()==DialogResult.OK){settings.CodexPath=d.FileName; Persist();}}; actions.Controls.Add(choose);
  var web=new Button {Text="Open ChatGPT",Width=140}; web.Click+=(s,e)=>Process.Start("https://chatgpt.com/"); actions.Controls.Add(web);
  auto=new CheckBox {Text="Refresh automatically every 60 seconds",AutoSize=true,Checked=true}; layout.Controls.Add(auto);
  top=new CheckBox {Text="Keep this window on top",AutoSize=true}; top.CheckedChanged+=(s,e)=>TopMost=top.Checked; layout.Controls.Add(top);
  layout.Controls.Add(new Label {Text="Closing hides to tray. Right-click tray icon to quit.\nManual timers never assume your quota has replenished.",Width=560,Height=45});
  tray=new NotifyIcon {Icon=SystemIcons.Information,Visible=true,Text="Usage Tray — connecting"};
  strip=new TaskbarStrip(Reveal,async()=>await RefreshLive(),()=>{exiting=true;Close();});
  strip.Restore(settings.StripX,settings.StripY);
  strip.PositionSaved+=()=>{settings.StripX=strip.Left;settings.StripY=strip.Top;Persist();};
  layoutChoice.SelectedIndexChanged+=(s,e)=>{settings.Layout=layoutChoice.SelectedIndex==1?"double":"single";windowChoice.Enabled=settings.Layout=="single";Persist();Render();};
  windowChoice.SelectedIndexChanged+=(s,e)=>{settings.SelectedWindow=windowChoice.SelectedIndex==1?"weekly":windowChoice.SelectedIndex==2?"5-hour":"auto";Persist();Render();};
  strip.LayoutToggleRequested+=()=>{layoutChoice.SelectedIndex=layoutChoice.SelectedIndex==0?1:0;};
  var menu=new ContextMenuStrip(); menu.Items.Add("Show taskbar strip",null,(s,e)=>{strip.Show();strip.KeepVisible();});menu.Items.Add("Show usage details",null,(s,e)=>Reveal()); menu.Items.Add("Quit",null,(s,e)=>{exiting=true;Close();}); tray.ContextMenuStrip=menu; tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left){strip.Show();strip.KeepVisible();Reveal();}};
  mode.SelectedIndexChanged+=(s,e)=>{manual.Enabled=mode.SelectedIndex==0; refresh.Enabled=mode.SelectedIndex==1&&!busy; Render();};
  FormClosing+=(s,e)=>{if(!exiting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
  FormClosed+=(s,e)=>{timer.Dispose();tray.Dispose();strip.Dispose();};
  Shown+=(s,e)=>{strip.Show();BeginInvoke(new Action(()=>Hide()));};
  timer.Interval=1000; timer.Tick+=async(s,e)=>{Render();if(mode.SelectedIndex==1&&auto.Checked&&!busy&&DateTime.UtcNow>=nextPoll)await RefreshLive();}; timer.Start(); Render();
 }
 static DateTimePicker Picker(int x,int y) {return new DateTimePicker {Left=x,Top=y,Width=280,Format=DateTimePickerFormat.Custom,CustomFormat="MMM dd yyyy  HH:mm",ShowCheckBox=true,Checked=false};}
 static void SetPicker(DateTimePicker p,DateTime? value) {if(value.HasValue&&value.Value.ToLocalTime()>=p.MinDate&&value.Value.ToLocalTime()<=p.MaxDate)p.Value=value.Value.ToLocalTime();p.Checked=value.HasValue;}
 void Reveal(){Show();WindowState=FormWindowState.Normal;Activate();}
 void Persist(){try{SettingsStore.Save(Path.Combine(folder,"settings.json"),settings);}catch{MessageBox.Show("Could not save review settings.","Usage Tray");}}
 void SaveManual(){settings.Week=weekKnown.Checked?(double?)week.Value:null;settings.WeekReset=weekReset.Checked?(DateTime?)weekReset.Value.ToUniversalTime():null;settings.Updated=DateTime.UtcNow;Persist();Render();}
 public static string ResetText(DateTime? time){return WindowView.ResetAt(time,TimeZoneInfo.Local,true);}
 static string Amount(double? remaining,DateTime? reset){if(reset.HasValue&&reset.Value.ToUniversalTime()<=DateTime.UtcNow)return "unverified";return remaining.HasValue?Math.Max(0,Math.Min(100,remaining.Value)).ToString("0.#")+"% remaining":"unknown";}
 async Task RefreshLive(){if(busy)return;busy=true;snapshot.Begin();refresh.Enabled=false;error="Refreshing Codex…";Render();try {string path=Protocol.Discover(settings.CodexPath);snapshot.Accept(await Task.Run(()=>Protocol.Fetch(path)),DateTime.UtcNow);error=live.Count==0?"No quota windows returned; unknown is not zero.":"";}catch(Exception ex){error=ex.Message;}finally{busy=false;nextPoll=DateTime.UtcNow.AddSeconds(60);if(!IsDisposed){refresh.Enabled=mode.SelectedIndex==1;Render();}}}
 void Render(){
  bool isManual=mode.SelectedIndex==0;
  var weekly=Protocol.Weekly(live);
  double? remaining=isManual?settings.Week:weekly==null||!weekly.Used.HasValue?null:(double?)(100-weekly.Used.Value);
  DateTime? reset=isManual?settings.WeekReset:weekly==null?null:weekly.Reset;
  bool stale=!isManual&&fetched.HasValue&&Protocol.IsStale(fetched,DateTime.UtcNow,!busy&&error.Length>0);
  string state=isManual?"manual":busy?"refreshing":error.Length>0?"ERROR":stale?"STALE":!auto.Checked?"paused":"";
  if(isManual)display.Text="WEEKLY / MANUAL VALUES\r\n"+Amount(remaining,reset)+"\r\nReset: "+ResetText(reset);
  else {
   display.Text="CODEX / REPORTED ACCOUNT WINDOWS";
   foreach(var q in live){display.Text+="\r\n\r\n"+q.Name+" — "+Amount(q.Used.HasValue?(double?)(100-q.Used.Value):null,q.Reset)+"\r\nReset: "+ResetText(q.Reset);var even=WindowView.PaceAt(q.Reset,q.WindowMinutes,DateTime.UtcNow);if(even.HasValue)display.Text+="\r\nEven-use reference: "+even.Value.ToString("0.0")+"% remaining (white notch).";}
   if(live.Count==0)display.Text+="\r\nUsage unavailable — no verified window data.";
   if(settings.SelectedWindow=="5-hour"&&!live.Any(q=>q.Name=="codex / 5-hour"))display.Text+="\r\nSelected five-hour window is not reported.";
  }
  if(isManual)status.Text="Source: entered by you. Last saved: "+(settings.Updated.HasValue?settings.Updated.Value.ToLocalTime().ToString("g"):"never");
  else status.Text=(error.Length>0?error+" ":"")+(fetched.HasValue?"Source: Codex app-server. Last refresh: "+fetched.Value.ToLocalTime().ToString("g")+(stale?" — STALE":""):"No successful refresh.");
  string tip=(state.Length>0?state+" — ":"")+"Weekly: "+Amount(remaining,reset);tray.Text=tip.Length>63?tip.Substring(0,63):tip;
  if(strip!=null){var rows=isManual?new List<Quota>{new Quota{Name="codex / weekly",Used=remaining.HasValue?(double?)(100-remaining.Value):null,Reset=reset}}:live;strip.SetWindows(rows,settings.Layout,settings.SelectedWindow,DateTime.UtcNow,state,settings.Display);strip.KeepVisible();}
 }
 [STAThread] public static void Main(string[] args){
    if(args.Contains("--probe-live")){try{var rows=Protocol.Fetch(args.Length>1?args[1]:Protocol.Discover(""));File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-gate.txt"),"PASS: existing authenticated app-server read; account checked before/after; windows returned: "+rows.Count+"; weekly reported: "+(Protocol.Weekly(rows)!=null)+"; five-hour reported: "+rows.Any(q=>q.Name=="codex / 5-hour")+". No personal quota values or identity saved.");}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-gate.txt"),"UNAVAILABLE: "+ex.Message);Environment.ExitCode=1;}return;}
  if(args.Contains("--self-test")){try{SelfTest();}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"FAIL: "+ex.ToString());Environment.Exit(1);}return;}
  if(args.Length==2&&args[0]=="--layered-smoke"){try{Application.EnableVisualStyles();TaskbarStrip.SmokeTest(args[1]);}catch(Exception ex){File.WriteAllText(args[1],"FAIL: "+ex.ToString());Environment.Exit(1);}return;}
  if(args.Length==2&&args[0]=="--visibility-test"){Application.EnableVisualStyles();TaskbarStrip.VisibilityTest(args[1]);return;}
  if(args.Length==2&&args[0]=="--hosted-test"){try{Application.EnableVisualStyles();TaskbarStrip.HostedTest(args[1]);}catch(Exception ex){File.WriteAllText(args[1],"FAIL: "+ex.ToString());Environment.Exit(1);}return;}
  if(args.Length==2&&args[0]=="--preview-layouts"){LayoutReview.Preview(args[1]);return;}
  if(args.Length==2&&args[0]=="--preview"){Application.EnableVisualStyles();TaskbarStrip.Preview(args[1]);return;}
  bool first;using(var mutex=new Mutex(true,"Local\\UsageTray-review",out first)){if(!first){if(!args.Contains("--startup"))MessageBox.Show("Usage Tray is already running. Click its tray icon beside the clock.");return;}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new TrayForm());}
 }
 static void SelfTest(){
  ReviewTests.Run();LayoutReview.Test();TaskbarStrip.Test();var j=new JavaScriptSerializer();
  var primary=Protocol.Parse(j.Deserialize<Dictionary<string,object>>("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10080,\"resetsAt\":2000000000},\"secondary\":null}},\"rateLimits\":{\"primary\":{\"usedPercent\":99,\"windowDurationMins\":10080}}}"));
  if(primary.Count!=1||Protocol.Weekly(primary)==null||Protocol.Weekly(primary).Used!=0)throw new Exception("Pro primary-weekly or map precedence failed");
  var secondary=Protocol.Parse(j.Deserialize<Dictionary<string,object>>("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":80,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":20,\"windowDurationMins\":10080}}}"));
  if(Protocol.Weekly(secondary)==null||Protocol.Weekly(secondary).Used!=20)throw new Exception("Secondary weekly selection failed");
  var absent=Protocol.Parse(j.Deserialize<Dictionary<string,object>>("{\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10080}},\"codex\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300}}}}"));
  if(Protocol.Weekly(absent)!=null)throw new Exception("Wrong bucket/window substituted for weekly quota");
  if(Amount(80,DateTime.UtcNow.AddSeconds(-1))!="unverified"||Amount(null,null)!="unknown")throw new Exception("Expired/unknown value failed");
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: Pro primary-weekly parsing, legacy secondary-weekly parsing, bucket precedence/isolation, missing/expired values, weekly display, reset weekday, needle geometry, red actual needle, white even-use pointer, minute-precision pace calculations, transparency, taskbar fit, smooth text edges without colored fringes and text fit at 100/125/150/200 percent scaling.");
 }
}
















