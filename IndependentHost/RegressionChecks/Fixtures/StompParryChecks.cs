// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
class StompParryChecks {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),package=TestEnvironment.Package,managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string dir in new[]{package,managed}){string path=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;};
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);
  var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;var heroType=game.GetType("HeroController",true);object hero=FormatterServices.GetUninitializedObject(heroType);var timer=heroType.GetField("parryInvulnTimer",flags);var duration=heroType.GetField("INVUL_TIME_PARRY",flags);var grant=mod.GetType("HollowMario.NativeDamageTimer",true).GetMethod("GrantStomp",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(float configured in new[]{.3f,.6f,.9f}){
   duration.SetValue(hero,configured);heroType.GetMethod("NailParry",flags).Invoke(hero,null);float native=(float)timer.GetValue(hero);
   timer.SetValue(hero,0f);grant.Invoke(null,new[]{hero});Check((float)timer.GetValue(hero)==native,"Stomp exactly matches the real native NailParry timer");
   for(int i=0;i<100;i++)grant.Invoke(null,new[]{hero});Check((float)timer.GetValue(hero)==native,"Repeated contacts never add or multiply protection");
   timer.SetValue(hero,.05f);grant.Invoke(null,new[]{hero});Check((float)timer.GetValue(hero)==native,"A later successful stomp refreshes one window");
   timer.SetValue(hero,configured+1f);grant.Invoke(null,new[]{hero});Check(Math.Abs((float)timer.GetValue(hero)-(configured+1f))<.000001f,"An existing longer window is preserved");
  }
  foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity}){timer.SetValue(hero,.1f);duration.SetValue(hero,invalid);grant.Invoke(null,new[]{hero});Check((float)timer.GetValue(hero)==.1f,"Invalid duration cannot grant permanent protection");}
  var policy=mod.GetType("HollowMario.NativeDamageTimer",true);
  var begin=policy.GetMethod("BeginStomp",BindingFlags.Static|BindingFlags.NonPublic);
  var cancel=policy.GetMethod("CancelStomp",BindingFlags.Static|BindingFlags.NonPublic);
  duration.SetValue(hero,.6f);timer.SetValue(hero,.05f);
  object window=begin.Invoke(null,new[]{hero});
  Check((float)timer.GetValue(hero)==.6f,"Protection is active before synchronous enemy Hit/explosion callback");
  cancel.Invoke(null,new[]{window});Check((float)timer.GetValue(hero)==.05f,"Failed hit rolls back only its own grant");
  window=begin.Invoke(null,new[]{hero});timer.SetValue(hero,1f);cancel.Invoke(null,new[]{window});
  Check((float)timer.GetValue(hero)==1f,"Rollback preserves protection changed by another action");
  grant.Invoke(null,new object[]{null});Console.WriteLine("Stomp protection matches actual HeroController.NailParry for configured durations; refreshing, non-stacking and preservation of longer protection passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
