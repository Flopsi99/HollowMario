// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
class CameraChecks {
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic, I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]), package=TestEnvironment.Package, managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));var a=Assembly.LoadFrom(TestEnvironment.CompanionPath);var look=a.GetType("HollowMario.KnightLook");
  var arm=look.GetMethod("Arm",S);var pt=arm.GetParameters();object harmony=Activator.CreateInstance(pt[0].ParameterType,new object[]{"camera-checks"}), log=Activator.CreateInstance(pt[1].ParameterType,new object[]{"camera-checks"});arm.Invoke(null,new[]{harmony,log});
  var targetType=game.GetType("CameraTarget");object target=FormatterServices.GetUninitializedObject(targetType);
  var stateType=game.GetType("HeroController").GetField("cState",I).FieldType;object flags=Activator.CreateInstance(stateType);
  var falling=stateType.GetField("falling",I);var up=stateType.GetField("lookingUp",I);var down=stateType.GetField("lookingDown",I);
  var stick=targetType.GetField("fallStick",I);var catcher=targetType.GetField("fallCatcher",I);var min=targetType.GetField("yLockMin",I);var max=targetType.GetField("yLockMax",I);
  min.SetValue(target,8.3f);max.SetValue(target,400f);
  var prepare=look.GetMethod("PrepareFalling",S);var finish=look.GetMethod("AfterTarget",S);var isFalling=look.GetMethod("IsCameraFalling",S);
  Check(!(bool)isFalling.Invoke(null,new object[]{true,-10f,0x0C400201u}),"Grounded overrides residual downward speed");
  Check((bool)isFalling.Invoke(null,new object[]{false,-10f,0x0100088Cu}),"Descent follows the normal falling camera");
  Check(!(bool)isFalling.Invoke(null,new object[]{false,10f,0x03000880u}),"Rising jump is not falling");
  Check(!(bool)isFalling.Invoke(null,new object[]{false,-10f,0x380022C0u}),"Swimming does not engage the fall catcher");
  foreach(var gaze in new[]{up,down}){
   up.SetValue(flags,false);down.SetValue(flags,false);gaze.SetValue(flags,true);
   falling.SetValue(flags,true);stick.SetValue(target,true);catcher.SetValue(target,25f);
   object snapshot=prepare.Invoke(null,new[]{target,flags,(object)false});
   Check(!(bool)falling.GetValue(flags)&&(bool)stick.GetValue(target)&&(float)catcher.GetValue(target)==25f,"Landing supplies the native falling flag without overriding camera-owned smoothing state");
   Check((bool)gaze.GetValue(flags),"Looking direction remains active");
   Check((float)min.GetValue(target)==8.3f&&(float)max.GetValue(target)==400f,"Room camera limits preserved");
   finish.Invoke(null,new[]{snapshot});Check((bool)falling.GetValue(flags),"Scope restores the Knight's original gameplay flag");
  }
  falling.SetValue(flags,false);stick.SetValue(target,true);catcher.SetValue(target,10f);
  object air=prepare.Invoke(null,new[]{target,flags,(object)true});Check((bool)falling.GetValue(flags)&&(bool)stick.GetValue(target)&&(float)catcher.GetValue(target)==10f,"Real descent retains native fall tracking");finish.Invoke(null,new[]{air});Check(!(bool)falling.GetValue(flags),"Temporary fall state restores after camera step");
  foreach(var m in new[]{targetType.GetMethod("Update",I),game.GetType("CameraController").GetMethod("LateUpdate",I)})Check(pt[0].ParameterType.GetMethod("GetPatchInfo").Invoke(null,new object[]{m})!=null,"Camera integration hook installed");
  pt[0].ParameterType.GetMethod("UnpatchSelf").Invoke(harmony,null);look.GetMethod("Dispose",S).Invoke(null,null);
  Console.WriteLine("Installed-runtime camera hooks, stale-fall landing recovery, both look directions, room limits, rising/falling/swimming classification and gameplay-state restoration passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
