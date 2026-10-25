using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

// Layout is independent of plan. Only reported codex windows supply values.
public sealed class DisplayOptions {
 public string LogoPath="";
 public bool ShowIcon=true,ShowLabels=true,ShowAmounts=true,ShowReset=true,ShowPace=true,ShowLines=true,ShowFive=true,ShowWeek=true,ShowCountdown=true;
 public bool AnyData {get{return ShowLabels||ShowAmounts||ShowReset||ShowPace||ShowLines||ShowCountdown;}}
 [ScriptIgnore] public string Signature {get{return LogoPath+"|"+ShowIcon+"|"+ShowLabels+"|"+ShowAmounts+"|"+ShowReset+"|"+ShowPace+"|"+ShowLines+"|"+ShowFive+"|"+ShowWeek+"|"+ShowCountdown;}}
 public static DisplayOptions Minimal(){return new DisplayOptions{ShowIcon=false,ShowLabels=true,ShowAmounts=true,ShowReset=false,ShowPace=false,ShowLines=false};}
}
public static class LocalLogo {
 public static Bitmap Load(string path){
  if(string.IsNullOrWhiteSpace(path))return null;
  try{
   if(!Path.IsPathRooted(path)||path.StartsWith(@"\\"))return null;
   string ext=Path.GetExtension(path).ToLowerInvariant();if(ext!=".png"&&ext!=".jpg"&&ext!=".jpeg"&&ext!=".bmp")return null;
   var info=new FileInfo(path);if(!info.Exists||info.Length>4*1024*1024)return null;
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var image=Image.FromStream(stream,false,true)){
    if(image.RawFormat.Guid!=ImageFormat.Png.Guid&&image.RawFormat.Guid!=ImageFormat.Jpeg.Guid&&image.RawFormat.Guid!=ImageFormat.Bmp.Guid)return null;
    if(image.Width>4096||image.Height>4096)return null;
    var result=new Bitmap(64,64);using(var g=Graphics.FromImage(result)){g.Clear(Color.Transparent);float ratio=Math.Min(64f/image.Width,64f/image.Height);float w=image.Width*ratio,h=image.Height*ratio;g.DrawImage(image,(64-w)/2,(64-h)/2,w,h);}return result;
   }
  }catch{return null;}
 }
 public static void Test(){
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logo-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{string path=Path.Combine(folder,"synthetic.png");using(var b=new Bitmap(32,16)){using(var g=Graphics.FromImage(b))g.Clear(Color.Magenta);b.Save(path,ImageFormat.Png);}using(var loaded=Load(path)){if(loaded==null||loaded.Width!=64)throw new Exception("Logo decode failed");}
   string settingsPath=Path.Combine(folder,"settings.json");SettingsStore.Save(settingsPath,new Settings{Display=new DisplayOptions{LogoPath=path,ShowIcon=false}});var saved=SettingsStore.Load(settingsPath);if(saved.Display.LogoPath!=path||saved.Display.ShowIcon)throw new Exception("Logo preferences did not persist");
   using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{}))foreach(string mode in new[]{"single","double"})foreach(int dpi in new[]{96,120,144,192}){strip.ApplyDpi(dpi);strip.SetWindows(LayoutReview.Example(true),mode,"auto",LayoutReview.Now,"ERROR",new DisplayOptions{LogoPath=path});using(var layer=strip.RenderLayer()){bool magenta=false;for(int y=0;y<layer.Height;y++)for(int x=0;x<Math.Min(layer.Width,40*dpi/96);x++){var c=layer.GetPixel(x,y);if(c.R>200&&c.B>200&&c.G<80)magenta=true;}if(!magenta)throw new Exception("Custom logo did not render");}}
   File.Delete(path);if(Load(path)!=null||Load(@"\\server\share\logo.png")!=null)throw new Exception("Missing or network logo accepted");File.WriteAllText(path,"invalid");if(Load(path)!=null)throw new Exception("Invalid logo accepted");
  }finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
 }
}
public sealed class WindowView {
 public string Label,Amount,Countdown,ResetText,ResetDetail;public double? Remaining,Pace;public bool Reported;
 public static string ResetAt(DateTime? reset,TimeZoneInfo zone,bool full){
  if(!reset.HasValue)return "Reset unavailable";
  var local=TimeZoneInfo.ConvertTime(new DateTimeOffset(reset.Value.ToUniversalTime()),zone);
  string date=local.ToString("dddd, MMMM d, yyyy 'at' h:mm tt",System.Globalization.CultureInfo.InvariantCulture);
  string zoneName=zone.IsDaylightSavingTime(local.DateTime)?zone.DaylightName:zone.StandardName;
  return full?date+" ("+zoneName+"; UTC"+local.ToString("zzz",System.Globalization.CultureInfo.InvariantCulture)+")":local.ToString("ddd, MMM d \u00b7 h:mm tt",System.Globalization.CultureInfo.InvariantCulture);
 }
 public static double? PaceAt(DateTime? reset,double? minutes,DateTime now){
  if(!reset.HasValue||!minutes.HasValue||minutes<=0||double.IsNaN(minutes.Value)||double.IsInfinity(minutes.Value))return null;
  double left=(reset.Value.ToUniversalTime()-now.ToUniversalTime()).TotalMinutes;
  return left<=0||left>minutes.Value?(double?)null:100*left/minutes.Value;
 }
 public static WindowView From(List<Quota> rows,string name,DateTime now){
  var q=rows.FirstOrDefault(x=>x.Name=="codex / "+name);
  var view=new WindowView{Label=name=="weekly"?"WEEKLY":"5-HOUR",Amount="Unavailable",Countdown="Not reported",ResetText="Reset unavailable",ResetDetail="Reset unavailable",Reported=q!=null};
  if(q==null)return view;
  view.ResetText=ResetAt(q.Reset,TimeZoneInfo.Local,false);view.ResetDetail=ResetAt(q.Reset,TimeZoneInfo.Local,true);
  view.Countdown=q.Reset.HasValue?TaskbarStrip.FormatWeekly(null,q.Reset,now,"").Countdown.Replace(" until reset",""):"Reset unknown";
  if(view.Countdown.StartsWith("0d "))view.Countdown=view.Countdown.Substring(3);
  if(q.Reset.HasValue&&q.Reset.Value<=now){view.Amount="Verify";view.Countdown="Reset passed";return view;}
  if(q.Used.HasValue&&!double.IsNaN(q.Used.Value)&&!double.IsInfinity(q.Used.Value)&&q.Used>=0){view.Remaining=Math.Max(0,Math.Min(100,100-q.Used.Value));view.Amount=view.Remaining.Value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"% left";view.Pace=PaceAt(q.Reset,q.WindowMinutes,now);}
  return view;
 }
}
public sealed class UsageLayout {
 public WindowView Week,Five,Selected;public string Layout,Status;public DisplayOptions Options;public bool NoViews;public List<WindowView> Visible=new List<WindowView>();
 public int Width {get{if(NoViews)return 330;if(!Options.ShowReset)return Visible.Count>1?(Layout=="double"?330:440):330;return Visible.Count>1?(Layout=="double"?560:760):(Layout=="double"?500:440);}}
 public static string NormalizeLayout(string s){return s=="double"?"double":"single";}
 public static string NormalizeWindow(string s){return s=="weekly"||s=="5-hour"?s:"auto";}
 public static UsageLayout Create(List<Quota> rows,string layout,string window,DateTime now,string status,DisplayOptions options=null){
  options=options??new DisplayOptions();var v=new UsageLayout{Week=WindowView.From(rows,"weekly",now),Five=WindowView.From(rows,"5-hour",now),Layout=NormalizeLayout(layout),Status=status??"",Options=options};window=NormalizeWindow(window);
  v.Selected=window=="weekly"?v.Week:window=="5-hour"?v.Five:v.Week.Remaining.HasValue?v.Week:v.Five.Remaining.HasValue?v.Five:v.Week;
  if(v.Layout=="single"&&window!="auto"){if((window=="weekly"&&options.ShowWeek)||(window=="5-hour"&&options.ShowFive))v.Visible.Add(v.Selected);}
  else {if(v.Five.Reported&&options.ShowFive)v.Visible.Add(v.Five);if(v.Week.Reported&&options.ShowWeek)v.Visible.Add(v.Week);if(v.Visible.Count==0&&(options.ShowFive||options.ShowWeek))v.Visible.Add(options.ShowWeek?v.Week:v.Five);}
  if(v.Visible.Count==0||!options.AnyData)v.NoViews=true;
  return v;
 }
 public string Signature {get{return Options.Signature+"|"+NoViews+"|"+Layout+"|"+Status+"|"+string.Join("|",Visible.Select(x=>x.Label+":"+x.Amount+":"+x.ResetText+":"+x.Countdown+":"+(x.Pace.HasValue?x.Pace.Value.ToString("F3",System.Globalization.CultureInfo.InvariantCulture):"none")));}}
 public string Detail {get{return "Codex allowance; "+Layout+" layout\n"+string.Join("\n",Visible.Select(x=>x.Label+": "+x.Amount+"; "+x.Countdown+"\nReset: "+x.ResetDetail+(x.Pace.HasValue?"\nWhite notch: even-use reference "+x.Pace.Value.ToString("0.0")+"%":"\nEven-use reference unavailable")))+"\n"+Status+". Local timezone. Right-click to switch layout.";}}
}
public static class SettingsStore {
 public static Settings Load(string path){var s=File.Exists(path)?new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path)):new Settings();s=s??new Settings();s.Display=s.Display??new DisplayOptions();s.Layout=UsageLayout.NormalizeLayout(s.Layout);s.SelectedWindow=UsageLayout.NormalizeWindow(s.SelectedWindow);return s;}
 public static void Save(string path,Settings settings){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(settings));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
}
public static class LayoutReview {
 public static readonly DateTime Now=new DateTime(2026,10,24,12,0,0,DateTimeKind.Utc);
 public static List<Quota> Example(bool both){var rows=new List<Quota>{new Quota{Name="codex / weekly",Used=both?46:32,Reset=Now.AddDays(4).AddHours(12),WindowMinutes=10080}};if(both)rows.Add(new Quota{Name="codex / 5-hour",Used=18,Reset=Now.AddHours(2).AddMinutes(10),WindowMinutes=300});return rows;}
 static void Check(bool ok,string name){if(!ok)throw new Exception("Layout: "+name);}
 public static void Test(){
  LocalLogo.Test();
  var one=Example(false);var both=Example(true);
  Check(UsageLayout.Create(one,"double","auto",Now,"").Visible.Count==1,"weekly-only compact has no irrelevant placeholder");
  var pair=UsageLayout.Create(both,"single","auto",Now,"");Check(pair.Visible.Count==2&&pair.Visible[0].Label=="5-HOUR"&&pair.Visible[1].Label=="WEEKLY","reported pair ordered five-hour left weekly right");
  Check(Math.Abs(WindowView.PaceAt(Now.AddMinutes(150),300,Now).Value-50)<1e-9,"five-hour midpoint pace");
  Check(Math.Abs(WindowView.PaceAt(Now.AddDays(3.5),10080,Now).Value-50)<1e-9,"weekly midpoint pace");
  Check(WindowView.PaceAt(Now.AddMinutes(150),null,Now)==null&&WindowView.PaceAt(null,300,Now)==null&&WindowView.PaceAt(Now,300,Now)==null&&WindowView.PaceAt(Now.AddMinutes(301),300,Now)==null,"invalid and missing metadata suppress notch");
  var missing=Example(true);missing[1].WindowMinutes=null;Check(WindowView.From(missing,"5-hour",Now).Pace==null,"no inferred duration");
  var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
  string early=WindowView.ResetAt(new DateTime(2026,11,1,5,30,0,DateTimeKind.Utc),zone,true),late=WindowView.ResetAt(new DateTime(2026,11,1,6,30,0,DateTimeKind.Utc),zone,true);
  Check(early.Contains("Sunday, November 1, 2026")&&early.Contains("1:30 AM")&&early.Contains("UTC-04:00")&&late.Contains("1:30 AM")&&late.Contains("UTC-05:00"),"DST repeated hour preserves date and offset");
  Check(WindowView.ResetAt(new DateTime(2026,3,8,7,0,0,DateTimeKind.Utc),zone,true).Contains("3:00 AM")&&WindowView.ResetAt(new DateTime(2026,3,8,6,59,0,DateTimeKind.Utc),zone,true).Contains("1:59 AM"),"DST spring transition");
  string readable=WindowView.ResetAt(new DateTime(2027,1,1,2,15,0,DateTimeKind.Utc),zone,false);
  Check(readable=="Thu, Dec 31 \u00b7 9:15 PM"&&!readable.Contains("UTC"),"readable local date with year rollover, no raw UTC in meter");
  Check(WindowView.ResetAt(new DateTime(2026,3,8,7,0,0,DateTimeKind.Utc),zone,false).Contains("3:00 AM"),"meter spring DST conversion");
  Check(new DisplayOptions().ShowCountdown,"balanced default includes reset countdown");
  Check(WindowView.ResetAt(new DateTime(2026,11,1,9,57,0,DateTimeKind.Utc),zone,false)=="Sun, Nov 1 \u00b7 4:57 AM","original abbreviated reset format");
  var hidden=new DisplayOptions{ShowFive=false,ShowWeek=false,ShowIcon=false};var hiddenView=UsageLayout.Create(both,"single","auto",Now,"ERROR",hidden);Check(hiddenView.NoViews&&hiddenView.Status=="ERROR","zero sections retains mandatory error");
  var quiet=new DisplayOptions{ShowIcon=false,ShowLabels=false,ShowAmounts=false,ShowReset=false,ShowCountdown=false,ShowPace=false,ShowLines=false};Check(UsageLayout.Create(both,"single","auto",Now,"STALE",quiet).NoViews,"all optional content off has minimum UI");
  using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){
   foreach(string mode in new[]{"single","double"}){strip.ApplyDpi(96);strip.SetWindows(both,mode,"auto",Now,"",new DisplayOptions());using(var with=strip.RenderLayer()){strip.SetWindows(both,mode,"auto",Now,"",new DisplayOptions{ShowPace=false});using(var without=strip.RenderLayer()){int pixels=0;for(int y=0;y<with.Height;y++)for(int x=0;x<with.Width;x++)if(with.GetPixel(x,y)!=without.GetPixel(x,y))pixels++;Check(pixels>5,"white pacing markers affect actual rendered pixels");}}}
   foreach(string mode in new[]{"single","double"}){strip.SetWindows(both,mode,"auto",Now,"",new DisplayOptions());using(var layer=strip.RenderLayer()){int red=0;for(int y=0;y<layer.Height;y++)for(int x=0;x<layer.Width;x++){var c=layer.GetPixel(x,y);if(c.A>100&&c.R>200&&c.G<160&&c.B<160)red++;}Check(mode=="double"?red==0:red>0,"compact removes dial needles; standard retains dials");}}
  var noMetadata=Example(true);foreach(var q in noMetadata)q.WindowMinutes=null;strip.SetWindows(noMetadata,"double","auto",Now,"",new DisplayOptions());using(var with=strip.RenderLayer()){strip.SetWindows(noMetadata,"double","auto",Now,"",new DisplayOptions{ShowPace=false});using(var without=strip.RenderLayer()){bool same=true;for(int y=0;y<with.Height;y++)for(int x=0;x<with.Width;x++)if(with.GetPixel(x,y)!=without.GetPixel(x,y))same=false;Check(same,"missing duration has no hidden/fabricated marker");}}
  }
  Check(UsageLayout.Create(both,"double","auto",Now,"STALE",new DisplayOptions{ShowIcon=false}).Status=="STALE","icon toggle cannot hide stale state");
  Check(UsageLayout.Create(one,"double","auto",Now,"").Five.Amount=="Unavailable","absent five-hour");
  Check(UsageLayout.Create(one,"single","5-hour",Now,"").Selected.Amount=="Unavailable","explicit selection not silently substituted");
  Check(UsageLayout.Create(both,"single","5-hour",Now,"").Selected.Remaining==82,"single five-hour selection");
  Check(UsageLayout.Create(both,"single","auto",Now,"").Selected.Label=="WEEKLY","auto selects available weekly");
  Check(UsageLayout.Create(both.FindAll(x=>x.Name.EndsWith("5-hour")),"single","auto",Now,"").Selected.Label=="5-HOUR","auto falls back to available five-hour");
  var state=new QuotaSnapshot();state.Accept(both,Now);state.Begin();var empty=UsageLayout.Create(state.Rows,"double","auto",Now,"ERROR");Check(!empty.Week.Remaining.HasValue&&!empty.Five.Remaining.HasValue&&empty.Status=="ERROR","account refresh/error clears both layouts");
  var expired=Example(true);expired[1].Reset=Now.AddSeconds(-1);Check(UsageLayout.Create(expired,"single","5-hour",Now,"").Selected.Amount=="Verify","expired selected window");
  using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){string layout="single";strip.SetWindows(both,layout,"auto",Now,"");strip.LayoutToggleRequested+=()=>{layout=layout=="single"?"double":"single";strip.SetWindows(both,layout,"auto",Now,"");};var toggle=strip.ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(x=>x.Text.StartsWith("Switch standard"));toggle.PerformClick();Check(strip.CurrentLayout=="double","menu toggle to double");toggle.PerformClick();Check(strip.CurrentLayout=="single","menu toggle back to single");strip.SetWindows(both,"single","5-hour",Now,"");Check(strip.CurrentWindow=="5-HOUR","selected label updated");}
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"usage-layout-test-"+Guid.NewGuid().ToString("N"),"settings.json");
  try{var s=new Settings{Layout="double",SelectedWindow="5-hour",StripX=123,Display=new DisplayOptions{ShowIcon=false,ShowPace=false,ShowWeek=false}};SettingsStore.Save(file,s);var read=SettingsStore.Load(file);Check(read.Layout=="double"&&read.SelectedWindow=="5-hour"&&read.StripX==123&&!read.Display.ShowIcon&&!read.Display.ShowPace&&!read.Display.ShowWeek,"persist double/selection without losing placement");read.Layout="single";SettingsStore.Save(file,read);Check(SettingsStore.Load(file).Layout=="single","persist toggle back");File.WriteAllText(file,"{\"StripX\":456}");read=SettingsStore.Load(file);Check(read.Layout=="single"&&read.SelectedWindow=="auto"&&read.StripX==456,"older settings migrate without resetting placement");}finally{try{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".tmp"))File.Delete(file+".tmp");if(Directory.Exists(Path.GetDirectoryName(file)))Directory.Delete(Path.GetDirectoryName(file));}catch{}}
  using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{}))foreach(int dpi in new[]{96,120,144,192})foreach(string mode in new[]{"single","double"})foreach(var rows in new[]{one,both,new List<Quota>()})foreach(string status in new[]{"","ERROR","STALE","refreshing"}){
   strip.ApplyDpi(dpi);strip.SetWindows(rows,mode,"auto",Now,status);using(var image=strip.RenderLayer()){Check(image.Width==(int)Math.Round(UsageLayout.Create(rows,mode,"auto",Now,status).Width*dpi/96.0)&&image.Height==44*dpi/96,"DPI frame size");}
   Check(SmoothText.MeasureWidth("Unavailable",11*dpi/96.0)<=112*dpi/96.0,"missing-value label fit");
  }
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-tests.txt"),"PASS: pacing by actual duration; missing-metadata notch suppression; DST date/offset formatting; optional display settings and minimum status; standard/compact switching; explicit and auto window selection; missing/expired windows; cleared account/error snapshots; persistence and legacy settings migration in temporary fixture only; 96/120/144/192 DPI rendering for normal/error/stale/refreshing states. No real preference file changed.");
 }
 public static void Preview(string directory){
  Directory.CreateDirectory(directory);Application.EnableVisualStyles();
  using(var sheet=new Bitmap(840,840))using(var g=Graphics.FromImage(sheet))using(var heading=new Font("Segoe UI",23,FontStyle.Bold))using(var label=new Font("Segoe UI",15,FontStyle.Bold))using(var small=new Font("Segoe UI",11)){
   g.Clear(Color.FromArgb(16,25,22));g.DrawString("Usage Tray · choose your view",heading,Brushes.White,26,20);g.DrawString("DEMO / synthetic examples. Plan names do not determine available windows.",small,Brushes.LightGray,26,64);
   using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{}))for(int i=0;i<4;i++){
    bool plus=i>=2;string mode=i%2==0?"single":"double";string title=(plus?"Plus":"Pro")+" · "+(mode=="single"?(plus?"5-hour left / weekly right":"single weekly dial"):"compact");int x=26,y=112+i*174;
    using(var panel=new SolidBrush(Color.FromArgb(26,40,33)))g.FillRectangle(panel,x,y,788,147);
    g.DrawString(title,label,Brushes.White,x+18,y+14);g.DrawString(plus?"Example: weekly + five-hour reported":"Example: reported weekly window",small,Brushes.LightGray,x+18,y+43);
    strip.ApplyDpi(96);strip.SetWindows(Example(plus),mode,"auto",Now,"DEMO");using(var layer=strip.RenderLayer()){g.DrawImageUnscaled(layer,x+18,y+81);layer.Save(Path.Combine(directory,(plus?"plus":"pro")+"-"+mode+".png"),ImageFormat.Png);}
    if(mode=="single"){foreach(string window in new[]{"5-hour","weekly"}){strip.SetWindows(Example(plus),mode,window,Now,"DEMO");using(var layer=strip.RenderLayer())layer.Save(Path.Combine(directory,(plus?"plus":"pro")+"-single-"+(window=="5-hour"?"five":"weekly")+".png"),ImageFormat.Png);}}
   }
   sheet.Save(Path.Combine(directory,"layout-comparison.png"),ImageFormat.Png);
  }
  using(var sheet=new Bitmap(1560,850))using(var g=Graphics.FromImage(sheet))using(var font=new Font("Segoe UI",12))using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){
   g.Clear(Color.FromArgb(16,25,22));g.DrawString("Synthetic DPI review — standard and compact",font,Brushes.White,18,14);int y=48;
   foreach(int dpi in new[]{96,120,144,192}){g.DrawString((dpi*100/96)+"% scaling",font,Brushes.LightGray,18,y);y+=25;strip.ApplyDpi(dpi);foreach(string mode in new[]{"single","double"}){strip.SetWindows(Example(true),mode,"auto",Now,"DEMO");using(var image=strip.RenderLayer()){g.DrawImageUnscaled(image,18,y);y+=image.Height+12;}}y+=15;}
   sheet.Save(Path.Combine(directory,"layout-dpi.png"),ImageFormat.Png);
  }
  using(var sheet=new Bitmap(780,490))using(var g=Graphics.FromImage(sheet))using(var font=new Font("Segoe UI",12))using(var strip=new TaskbarStrip(()=>{},()=>{},()=>{})){
   g.Clear(Color.FromArgb(16,25,22));g.DrawString("Synthetic customization examples — status remains visible",font,Brushes.White,18,14);
   var presets=new[]{new DisplayOptions(),DisplayOptions.Minimal(),new DisplayOptions{ShowIcon=false,ShowLabels=false},new DisplayOptions{ShowFive=false,ShowWeek=false,ShowIcon=false},new DisplayOptions{ShowReset=false,ShowCountdown=true}};
   var labels=new[]{"Balanced defaults","Minimal preset","Symbol and labels hidden","Both sections hidden + ERROR","Date hidden; relative countdown enabled"};
   for(int i=0;i<presets.Length;i++){int y=50+i*86;g.DrawString(labels[i],font,Brushes.LightGray,18,y);strip.ApplyDpi(96);strip.SetWindows(Example(true),"double","auto",Now,i==3?"ERROR":"DEMO",presets[i]);using(var image=strip.RenderLayer())g.DrawImageUnscaled(image,18,y+25);}
   sheet.Save(Path.Combine(directory,"display-options.png"),ImageFormat.Png);
  }
 }
}






