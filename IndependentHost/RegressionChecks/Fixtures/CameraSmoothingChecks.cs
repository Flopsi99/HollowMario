// Existing project regression assertions, retargeted to our independent host.
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
class CameraSmoothingChecks {
 class FakeController {public static FakeController Instance;public int MarioId; public object Renderer;}
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic, I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]), package=TestEnvironment.Package, managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
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
  var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var native=engine.GetType("MarioHollow.Native",true);var view=a.GetType("HollowMario.MarioView",true);view.GetMethod("Arm",S).Invoke(null,new object[]{harmony,engine.GetType("MarioHollow.MarioController"),native});
  var renderer=engine.GetType("MarioHollow.MarioRenderer",true);var buffersField=renderer.GetField("Buffers",I);object emptyRenderer=FormatterServices.GetUninitializedObject(renderer);object geometry=Activator.CreateInstance(buffersField.FieldType);
  var nativeMath=engine.GetType("MarioHollow.MarioMath",true);var origin=nativeMath.GetField("Origin",S|BindingFlags.Public);var coords=new float[]{3500f,-3600f,7f,3510f,-3610f,8f,3520f,-3620f,9f};var pin=System.Runtime.InteropServices.GCHandle.Alloc(coords,System.Runtime.InteropServices.GCHandleType.Pinned);buffersField.FieldType.GetField("position",I).SetValue(geometry,pin.AddrOfPinnedObject());buffersField.FieldType.GetField("numTrianglesUsed",I).SetValue(geometry,Convert.ChangeType(1,buffersField.FieldType.GetField("numTrianglesUsed",I).FieldType));buffersField.SetValue(emptyRenderer,geometry);
  view.GetField("controllerType",S).SetValue(null,typeof(FakeController));FakeController.Instance=new FakeController{MarioId=7,Renderer=emptyRenderer};
  var oldOrigin=new UnityEngine.Vector2(100f,200f);var newOrigin=new UnityEngine.Vector2(136.75f,162.2f);origin.SetValue(null,oldOrigin);
  var stamp=view.GetMethod("AfterNativeTick",S);stamp.Invoke(null,new object[]{7,geometry});
  var plugin=a.GetType("HollowMario.Plugin",true);plugin.GetMethod("RecenterPrefix",S).Invoke(null,null);Check(!(bool)plugin.GetField("snapNextRender",S).GetValue(null),"Ordinary origin reset keeps interpolation instead of snapping");
  var before=(float[])coords.Clone();origin.SetValue(null,newOrigin);view.GetMethod("BeforeGeometry",S).Invoke(null,new object[]{emptyRenderer});
  for(int j=0;j<coords.Length;j+=3){Check(Math.Abs(oldOrigin.x+before[j]*.0105f-newOrigin.x-coords[j]*.0105f)<.00003&&Math.Abs(oldOrigin.y+before[j+1]*.0105f-newOrigin.y-coords[j+1]*.0105f)<.00003,"Origin reset preserves every vertex's world position");Check(before[j+2]==coords[j+2],"Depth remains unchanged");}
  var once=(float[])coords.Clone();view.GetMethod("BeforeGeometry",S).Invoke(null,new object[]{emptyRenderer});for(int j=0;j<coords.Length;j++)Check(coords[j]==once[j],"A geometry snapshot is rebased exactly once");
  plugin.GetMethod("RecenterPrefix",S).Invoke(null,null);Check((bool)plugin.GetField("snapNextRender",S).GetValue(null),"Direct teleport retains its immediate snap");
  origin.SetValue(null,oldOrigin);stamp.Invoke(null,new object[]{99,geometry});Check(!(bool)view.GetProperty("HasPendingGeometry",S).GetValue(null,null),"Private/other Mario actors do not arm gameplay rebasing");
  view.GetMethod("Forget",S).Invoke(null,null);pin.Free();
  var original=HarmonyLib.PatchProcessor.GetOriginalInstructions(targetType.GetMethod("Update",I),(System.Reflection.Emit.ILGenerator)null);var current=HarmonyLib.PatchProcessor.GetCurrentInstructions(targetType.GetMethod("Update",I),int.MaxValue,null);
  var heroTransform=targetType.GetField("heroTransform",I);var getPosition=typeof(UnityEngine.Transform).GetProperty("position").GetGetMethod();int expectedReads=0,newReads=0,remainingReads=0,otherPositions=0;
  for(int j=1;j<original.Count;j++)if(original[j-1].opcode==System.Reflection.Emit.OpCodes.Ldfld&&Equals(original[j-1].operand,heroTransform)&&original[j].Calls(getPosition))expectedReads++;
  for(int j=0;j<current.Count;j++){if(current[j].operand is MethodInfo&&((MethodInfo)current[j].operand).DeclaringType==view&&((MethodInfo)current[j].operand).Name=="ReadHeroPosition")newReads++;if(current[j].Calls(getPosition)){otherPositions++;if(j>0&&Equals(current[j-1].operand,heroTransform))remainingReads++;}}
  Console.WriteLine("Camera IL counts: expected="+expectedReads+", replaced="+newReads+", remaining="+remainingReads+", other="+otherPositions);Check(expectedReads>=10&&newReads==expectedReads&&remainingReads==0&&otherPositions>0,"All actual Hero-position reads use rendered sampling; camera/target positions retain native behavior");
  foreach(var method in new[]{targetType.GetMethod("Update",I),game.GetType("CameraController").GetMethod("LateUpdate",I),renderer.GetMethod("TakeTick",I),renderer.GetMethod("Render",I),native.GetMethod("sm64_mario_tick",S|BindingFlags.Public)})Check(HarmonyLib.Harmony.GetPatchInfo(method)!=null,"Camera/mesh integration hook installed");
  Console.WriteLine("Recenter world-position continuity, depth preservation, one-shot rebasing, teleport snapping, private-actor isolation and all "+expectedReads+" actual camera Hero reads passed");
  pt[0].ParameterType.GetMethod("UnpatchSelf").Invoke(harmony,null);look.GetMethod("Dispose",S).Invoke(null,null);
  Console.WriteLine("Installed-runtime camera hooks, stale-fall landing recovery, both look directions, room limits, rising/falling/swimming classification and gameplay-state restoration passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
