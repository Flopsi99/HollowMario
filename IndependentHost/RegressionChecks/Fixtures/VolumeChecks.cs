// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
class VolumeChecks {
 static BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static int Main(string[] args){try {
 string root=Path.GetFullPath(args[0]);string managed=TestEnvironment.Managed;
 string package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);
 var bridge=Assembly.LoadFrom(TestEnvironment.HostPath);
 Type config=Assembly.LoadFrom(Path.Combine(package,"HollowMario.ApiRuntime.dll")).GetType("BepInEx.Configuration.ConfigFile");
 string path=Path.Combine(root,@".artifacts\volume-check.cfg");File.WriteAllText(path,"[Super Mario 64]\nVolume = 0.37\nRomPath = keep-me\n[Other]\nUnknown = preserve\n");
 object cfg=Activator.CreateInstance(config,new object[]{path,true});
 object entry=config.GetMethod("Bind").MakeGenericMethod(typeof(float)).Invoke(cfg,new object[]{"Super Mario 64","Volume",.8f,""});
 bridge.GetType("MarioHollow.Plugin").GetField("Volume",S).SetValue(null,entry);
 Type volume=mod.GetType("HollowMario.MarioVolume");var h=new Harmony("HollowMario.VolumeChecks");
 volume.GetMethod("Arm",S).Invoke(null,new object[]{h,bridge});
 Func<float> value=()=> (float)volume.GetProperty("Value",S).GetValue(null,null);
 Action<float> set=x=>volume.GetMethod("Set",S).Invoke(null,new object[]{x});
 Check(Math.Abs(value()-.37f)<.00001f,"Preserve configured value on menu open");
 Check((int)volume.GetProperty("Percent",S).GetValue(null,null)==37,"Percent conversion");
 var output=bridge.GetType("MarioHollow.MarioAudio").GetField("Volume",S);
 foreach(float x in new[]{0f,.23f,1f,-5f,7f,float.NaN}){set(x);float expected=float.IsNaN(x)?.8f:Math.Max(0,Math.Min(1,x));Check(Math.Abs(value()-expected)<.00001f,"Bounded config");Check(Math.Abs((float)output.GetValue(null)-expected)<.00001f,"Live audio output");}
 volume.GetMethod("SetPercent",S).Invoke(null,new object[]{64});Check(Math.Abs(value()-.64f)<.00001f,"API menu callback");
 object reread=Activator.CreateInstance(config,new object[]{path,true});object saved=config.GetMethod("Bind").MakeGenericMethod(typeof(float)).Invoke(reread,new object[]{"Super Mario 64","Volume",.8f,""});Check(Math.Abs((float)saved.GetType().GetProperty("Value").GetValue(saved,null)-.64f)<.00001f,"Survives reload");
 string text=File.ReadAllText(path);Check(text.Contains("RomPath = keep-me")&&text.Contains("Unknown = preserve"),"Unrelated settings preserved");
 h.UnpatchSelf();File.Delete(path);Console.WriteLine("Existing volume preserved; mute/range/live audio update, percentage controls, disk persistence, unrelated settings and API volume controls passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
