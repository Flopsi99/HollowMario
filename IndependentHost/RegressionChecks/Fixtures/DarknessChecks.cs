using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;

// Root-only hiding must preserve all child darkness and spell-effect renderers.
class DarknessChecks
{
 static void Check(bool good,string why){if(!good)throw new Exception(why);}
 static int Main(string[] args){try{
  var host=Assembly.LoadFrom(TestEnvironment.HostPath);var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);
  var controller=host.GetType("MarioHollow.MarioController",true);var h=new Harmony("hollowmario.darkness.regression");
  var flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  companion.GetType("HollowMario.DarknessMario",true).GetMethod("Arm",flags).Invoke(null,new object[]{h,controller});
  var patches=Harmony.GetPatchInfo(controller.GetMethod("LateUpdate",flags));
  Check(patches==null||!patches.Transpilers.Any(),"Independent rendering needs no hierarchy hide-loop rewrite");
  using(var assembly=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath)){
   var type=assembly.MainModule.GetType(controller.FullName);
   var calls=type.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand as MethodReference).Where(m=>m!=null).ToArray();
   Check(!calls.Any(m=>m.Name=="GetComponentsInChildren"||m.Name=="GetComponents"),"Controller cannot enumerate and hide child effects/darkness renderers");
   var activate=type.Methods.Single(m=>m.Name=="Activate");
   Check(activate.Body.Instructions.Any(i=>i.Operand is GenericInstanceMethod&&((GenericInstanceMethod)i.Operand).Name=="GetComponent"&&((GenericInstanceMethod)i.Operand).GenericArguments.Any(t=>t.FullName=="UnityEngine.MeshRenderer")),"Knight renderer comes from the hero root, not its children");
   var draw=type.Methods.Single(m=>m.Name=="LateUpdate");
   Check(draw.Body.Instructions.Count(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="set_forceRenderingOff")==1,"Late frame hiding has one root-mesh target");
   Check(draw.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="knight"),"Hide target is the cached Knight root");
  }
  h.UnpatchSelf();Console.WriteLine("PASS packaged root-only Knight hiding, no child-renderer traversal or obsolete hide-loop patch. Darkness coverage on screen still needs gameplay verification.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
