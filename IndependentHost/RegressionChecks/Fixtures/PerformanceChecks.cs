// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using HarmonyLib;
class PerformanceChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
  AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var timing=mod.GetType("HollowMario.PresentationTiming",true);
  var samples=timing.GetNestedType("Samples",BindingFlags.NonPublic);object s=Activator.CreateInstance(samples,true);var add=(Action<int,double>)Delegate.CreateDelegate(typeof(Action<int,double>),s,samples.GetMethod("Add",I));
  add(2,3);add(2,9);Check(((double[])samples.GetField("Total",I).GetValue(s))[2]==12&&((double[])samples.GetField("Peak",I).GetValue(s))[2]==9&&((int[])samples.GetField("Calls",I).GetValue(s))[2]==2,"Catch-up work keeps total, largest call and actual call count");
  samples.GetMethod("Clear",I).Invoke(s,new object[]{42});Check(((int[])samples.GetField("Calls",I).GetValue(s))[2]==0&&((double[])samples.GetField("Total",I).GetValue(s))[2]==0&&(int)samples.GetField("Frame",I).GetValue(s)==42,"Next frame cannot inherit earlier costs");
  var wtype=timing.GetNestedType("Window",BindingFlags.NonPublic);object w=Activator.CreateInstance(wtype,true);var observe=(Func<double,double,double,bool>)Delegate.CreateDelegate(typeof(Func<double,double,double,bool>),w,wtype.GetMethod("Observe",I));
  Check(!observe(1,16,1)&&observe(2,80,1)&&!observe(3,90,1)&&observe(7.2,16,13),"Frame and individual section hitches share a five-second rate limit");
  Check((int)wtype.GetField("Hitches",I).GetValue(w)==3&&(int)wtype.GetField("Suppressed",I).GetValue(w)==1,"Suppressed spikes remain counted in the summary");
  wtype.GetMethod("Reset",I).Invoke(w,null);Check(!observe(7.3,100,1),"Summary reset cannot bypass the log rate limit");
  var alloc=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Static|BindingFlags.Public);
  if(alloc!=null){var bytes=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),alloc);for(int n=0;n<1000;n++)add(0,.01);long before=bytes();for(int n=0;n<200000;n++)add(0,.01);long added=bytes()-before;Check(added==0,"Normal timing accumulation must not allocate: "+added);Console.WriteLine("200,000 timing samples allocated zero managed bytes.");}
  var h=new Harmony("performance-checks");timing.GetMethod("Arm",S).Invoke(null,new object[]{h,engine.GetType("MarioHollow.MarioController",true)});
  var methods=(IDictionary)timing.GetField("methods",S).GetValue(null);Check(methods.Count==11,"All requested performance scopes resolve");foreach(DictionaryEntry entry in methods){var info=Harmony.GetPatchInfo((MethodBase)entry.Key);Check(info!=null,"Native timing hook missing");bool prefix=false,finalizer=false;foreach(var p in info.Prefixes)if(p.PatchMethod.DeclaringType==timing)prefix=true;foreach(var p in info.Finalizers)if(p.PatchMethod.DeclaringType==timing)finalizer=true;Check(prefix&&finalizer,"Timing hook preserves completion and exception paths");}
  var error=new InvalidOperationException("keep original failure");Check(ReferenceEquals(error,timing.GetMethod("After",S).Invoke(null,new object[]{null,0L,error})),"Disabled timing preserves original game exceptions");h.UnpatchSelf();Console.WriteLine("PASS: frame totals/peaks/catch-up counts, clean boundaries, shared hitch throttling, suppressed-spike accounting, eleven real runtime scopes and exception preservation. No game launched.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
