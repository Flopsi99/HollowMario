// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
class GeoContactChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
  AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var contact=mod.GetType("HollowMario.GeoPickupContact",true);var ready=(Func<bool,float,float,bool>)Delegate.CreateDelegate(typeof(Func<bool,float,float,bool>),contact.GetMethod("Ready",S));
  Check(!ready(false,.1f,.25f)&&!ready(false,.249f,.25f)&&ready(false,.25f,.25f)&&ready(false,.3f,.25f),"Native pickup delay is retained and persistent contact retries at expiry");
  Check(!ready(true,.3f,.25f)&&!ready(true,100f,.25f),"A collected piece cannot credit twice");Check(!ready(false,100f,100.25f)&&ready(false,100.25f,100.25f),"Reused pieces respect their new spawn delay");
  var h=new Harmony("geo-contact-checks");contact.GetMethod("Arm",S).Invoke(null,new object[]{h});
  foreach(string name in new[]{"OnEnable","OnTriggerEnter2D"}){var info=Harmony.GetPatchInfo(AccessTools.Method(game.GetType("GeoControl"),name));bool found=false;if(info!=null){foreach(var p in name=="OnEnable"?info.Postfixes:info.Prefixes)if(p.PatchMethod.DeclaringType==contact)found=true;}Check(found,"Real Geo lifecycle/pickup hook installed: "+name);}
  foreach(string name in new[]{"OnTriggerStay2D","OnCollisionEnter2D","OnCollisionStay2D"})Check(contact.GetMethod(name,I)!=null,"Continuous or physical contact receiver missing: "+name);
  object[] missing={null};contact.GetMethod("BeforeCollect",S).Invoke(null,missing);Check(missing[0]==null,"Missing/unrelated contacts retain native behavior");h.UnpatchSelf();Console.WriteLine("PASS: native Geo spawn/pickup hooks, exact delay boundary, retry after early overlap, pooled delay reset, duplicate-credit guard and physical/trigger contact receivers. Actual gameplay pickup still needs verification.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
