using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.IO;
public static class ReviewTests {
 public static void Run(){
  var json=new JavaScriptSerializer();
  Func<string,List<Quota>> parse=s=>Protocol.Parse(json.Deserialize<Dictionary<string,object>>(s));
  var rows=parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":32,\"windowDurationMins\":300,\"resetsAt\":2000000000},\"secondary\":{\"usedPercent\":105,\"windowDurationMins\":10080,\"resetsAt\":2000000000000}}}}" );
  Check(rows[0].Name=="codex / 5-hour" && rows[0].Reset.Value.Year==2033,"minutes / Unix seconds");
  Check(Protocol.Weekly(rows).Reset==null && Protocol.Weekly(rows).Used==105,"millisecond rejection / overage");
  foreach(string bad in new[]{"null","\"50\"","true","-1","{}"}){
   var r=parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":"+bad+",\"windowDurationMins\":10080}}}");Check(r[0].Used==null,"bad percentage remains unavailable");
  }
  Check(parse("{}").Count==0 && parse("{\"rateLimitsByLimitId\":{}}").Count==0,"absent windows");
  bool rejected=false;try{Protocol.SameAccount("A","B");}catch{rejected=true;}Check(rejected,"account switch rejected");
  rejected=false;try{Protocol.Identity(new Dictionary<string,object>());}catch{rejected=true;}Check(rejected,"missing identity rejected");
  Protocol.SameAccount("A","A");
  Check(Protocol.ReadResponse(new StringReader("{\"method\":\"notification\"}\n{\"id\":9,\"result\":{}}\n{\"id\":2,\"result\":{}}\n"),2)!=null,"notification and unrelated response filtering");
  foreach(string response in new[]{"", "malformed\n", "{\"id\":2,\"error\":{\"message\":\"do not expose raw errors\"}}\n", "{\"id\":2,\"result\":null}\n"}){
   rejected=false;try{Protocol.ReadResponse(new StringReader(response),2);}catch(Exception e){rejected=!e.Message.Contains("do not expose raw errors");}Check(rejected,"closed, malformed, API error and invalid result fail safely");
  }
  var now=DateTime.UtcNow;Check(Protocol.IsStale(now.AddMinutes(-3),now,false)&&Protocol.IsStale(now,now,true)&&!Protocol.IsStale(now,now,false)&&!Protocol.IsStale(null,now,true),"freshness and error states");Check(TaskbarStrip.FormatWeekly(68,now.AddDays(1),now,"STALE").Countdown.Contains("STALE"),"stale visible");
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"review-tests.txt"),"PASS: numeric type validation; absent windows; minutes and Unix seconds; millisecond rejection; overage handling; missing identity and account switch rejection; stale UI. Original geometry, expiry and DPI suite runs separately.");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}

