using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using System.Runtime.CompilerServices;
class DamageDeliveryChecks
{
 const BindingFlags Any=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 class Pools{public int red=5,blue=2;}
 static Type reaction;static int delivered;static object expectedHero,expectedSource;
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 [MethodImpl(MethodImplOptions.NoInlining)] static void Contact(object source,int side,int amount,int hazard){}
 static void Received(object hero){delivered++;Check(ReferenceEquals(hero,expectedHero),"Confirmed reaction belongs to damaged hero");Check(ReferenceEquals(reaction.GetProperty("ContactSource",Any).GetValue(null,null),expectedSource),"Damage source reaches knockback dispatch");}
 static object Frame(Type type,Pools data,int red,int blue,bool maskless=false){
  var f=Activator.CreateInstance(type);foreach(var row in new[]{new object[]{"Valid",true},new object[]{"Hero",expectedHero},new object[]{"Data",data},new object[]{"Red",typeof(Pools).GetField("red")},new object[]{"Blue",typeof(Pools).GetField("blue")},new object[]{"StartRed",red},new object[]{"CompareRed",red},new object[]{"StartBlue",blue},new object[]{"CompareBlue",blue},new object[]{"Maskless",maskless},new object[]{"ContactSource",expectedSource},new object[]{"Damage",1},new object[]{"Hazard",1},new object[]{"BlockerKey","fixture"}})type.GetField((string)row[0],Any).SetValue(f,row[1]);return f;
 }
 static int Main(string[] args){try{
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);reaction=mod.GetType("HollowMario.DamageReaction",true);var type=reaction.GetNestedType("Frame",Any);var current=reaction.GetField("current",Any);var finish=reaction.GetMethod("FinishDamage",Any);
  var harmony=new Harmony("hollowmario.damage-startup-check");
  var adapter=mod.GetType("HollowMario.ApiArgumentAdapter",true);adapter.GetMethod("Install",Any).Invoke(null,null);
  try{
   var game=Assembly.LoadFrom(System.IO.Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));var target=game.GetType("HeroController",true).GetMethod("TakeDamage",Any);
   harmony.Patch(target,prefix:new HarmonyMethod(reaction.GetMethod("BeforeDamage",Any)){priority=Priority.First+100},finalizer:new HarmonyMethod(reaction.GetMethod("FinishDamage",Any)));
   harmony.Patch(target,prefix:new HarmonyMethod(reaction.GetMethod("CaptureDamageInputs",Any)){priority=Priority.First+90});
   harmony.Patch(target,prefix:new HarmonyMethod(mod.GetType("HollowMario.Plugin",true).GetMethod("IndependentContactGuard",Any)));
   var info=Harmony.GetPatchInfo(target);Check(info.Prefixes.Count(p=>p.owner==harmony.Id)==3&&info.Finalizers.Any(p=>p.owner==harmony.Id),"Exact production damage hooks register through the installed API adapter, including snapshot state and argument capture");
  }finally{harmony.UnpatchSelf();new Harmony("gg.melty.hollowmario.api-arguments").UnpatchSelf();}
  expectedHero=new object();expectedSource=new object();
  try{
   var data=new Pools();current.SetValue(null,Frame(type,data,5,2));
   var contact=typeof(DamageDeliveryChecks).GetMethod("Contact",Any);harmony.Patch(contact,prefix:new HarmonyMethod(reaction.GetMethod("CaptureDamageInputs",Any)));
   contact.Invoke(null,new object[]{expectedSource,0,2,3});
   Check(ReferenceEquals(reaction.GetProperty("ContactSource",Any).GetValue(null,null),expectedSource)&&(int)reaction.GetProperty("ContactDamage",Any).GetValue(null,null)==2&&(int)reaction.GetProperty("ContactHazard",Any).GetValue(null,null)==3,"Typed argument capture executes and retains the actual source, amount and hazard");
  }finally{harmony.UnpatchSelf();current.SetValue(null,Activator.CreateInstance(type));}
var signal=reaction.GetEvent("Confirmed",Any);var callback=Delegate.CreateDelegate(signal.EventHandlerType,typeof(DamageDeliveryChecks).GetMethod("Received",Any));signal.GetAddMethod(true).Invoke(null,new object[]{callback});
  try{
   foreach(string scenario in new[]{"red loss","blue loss red normalization","blocked","metal accepted"}){
    var data=new Pools();object frame=Frame(type,data,5,2,scenario=="metal accepted");if(scenario=="red loss")data.red=4;if(scenario=="blue loss red normalization"){data.red=6;data.blue=1;}int before=delivered;
    current.SetValue(null,frame);finish.Invoke(null,new[]{Activator.CreateInstance(type)});int expected=scenario=="blocked"?0:1;Check(delivered-before==expected,"Exactly one actual-loss reaction, none for rejected hit: "+scenario);finish.Invoke(null,new[]{Activator.CreateInstance(type)});Check(delivered-before==expected,"Finalizer cleanup prevents replaying a reaction");
   }
   var shared=new Pools();object outer=Frame(type,shared,5,2),inner=Frame(type,shared,5,2);shared.red=4;int prior=delivered;current.SetValue(null,inner);finish.Invoke(null,new[]{outer});finish.Invoke(null,new[]{Activator.CreateInstance(type)});Check(delivered-prior==1,"Nested hit dispatch is not repeated by outer finalizer");
   var host=Assembly.LoadFrom(TestEnvironment.HostPath);var controller=host.GetType("MarioHollow.MarioController",true);var actor=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(controller);controller.GetField("MarioId").SetValue(actor,1);var state=controller.GetField("State");object s=Activator.CreateInstance(state.FieldType);state.FieldType.GetField("invincTimer").SetValue(s,(short)10);state.SetValue(actor,s);
   var owns=controller.GetMethod("DamageOwnsControl");
   foreach(var phase in new[]{new object[]{1,true,false,false,0u},new object[]{1,false,true,false,0u},new object[]{1,false,false,true,0u},new object[]{1,false,false,false,0x20000u}})Check((bool)owns.Invoke(null,phase),"Accepted hit retains Mario through frozen, queued and native recoil phases");
   Check((bool)owns.Invoke(null,new object[]{-1,false,false,true,0u}),"Explicit accepted-damage handover can reactivate a parked gameplay actor");
   Check(!(bool)owns.Invoke(null,new object[]{-1,true,true,false,0x20000u})&&!(bool)owns.Invoke(null,new object[]{1,false,false,false,0u}),"Old parked recoil and ordinary script locks cannot inherit damage ownership without a fresh accepted hit");
   var block=controller.GetMethod("BlocksContactDamage");Check((bool)block.Invoke(actor,new object[]{1,true})&&!(bool)block.Invoke(actor,new object[]{2,true})&&!(bool)block.Invoke(actor,new object[]{3,true}),"Actual post-hit contact protection does not swallow real pit/acid/hazard resets");
   Check(!(bool)block.Invoke(actor,new object[]{1,false}),"A native timer without real hit protection cannot grant mask immunity");
   state.FieldType.GetField("invincTimer").SetValue(s,(short)0);
   foreach(uint recovery in new[]{0x010208B0u,0x010208B6u,0x00020462u,0x00020460u}){
    state.FieldType.GetField("action").SetValue(s,recovery);state.SetValue(actor,s);
    Check(!(bool)block.Invoke(actor,new object[]{1,false}),"Wall-bonk/fall recovery without a real hit stays vulnerable: "+recovery.ToString("X"));
    Check((bool)block.Invoke(actor,new object[]{1,true}),"Real post-hit protection remains through recovery: "+recovery.ToString("X"));
   }
   controller.GetField("MarioId").SetValue(actor,-1);Check(!(bool)block.Invoke(actor,new object[]{1,true}),"Parked native state cannot grant Knight invulnerability");
  }finally{signal.GetRemoveMethod(true).Invoke(null,new object[]{callback});current.SetValue(null,Activator.CreateInstance(type));}
  Console.WriteLine("PASS exact API damage-hook registration and typed capture; packaged damage finalizer: red/blue loss, normalization, rejection, Metal, nested delivery, source identity and active-only native contact protection. Unity damage collisions remain a playtest check.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
