// Retargeted to the authoritative bridge boundary, not the removed hazard patches.
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;
class HazardRecoilChecks {
 const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int calls;static HeroController hero;
 class Hit:IEnumerator,IDisposable {
  public int Effects,Disposals;int step;public object Current=>"native freeze";
  public bool MoveNext(){Effects++;if(step++==0){hero.cState.recoilFrozen=true;return true;}hero.cState.recoiling=true;hero.cState.recoilFrozen=true;return false;}
  public void Reset(){throw new NotSupportedException();}public void Dispose(){Disposals++;}
 }
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Set(object target,string name,object value)=>target.GetType().GetField(name,A).SetValue(target,value);
 static void NativeCall(){calls++;}
 static IEnumerable<CodeInstruction> Stub(){yield return new CodeInstruction(OpCodes.Call,typeof(HazardRecoilChecks).GetMethod("NativeCall",A));yield return new CodeInstruction(OpCodes.Ret);}
 static int Main(string[] args){try{
  var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);BridgeContracts.Install(companion);
  var oldReady=typeof(Sm64).GetField("<Ready>k__BackingField",A);bool ready=(bool)oldReady.GetValue(null);oldReady.SetValue(null,true);
  var owner=(MarioController)FormatterServices.GetUninitializedObject(typeof(MarioController));owner.Wanted=true;owner.MarioId=-1;
  hero=(HeroController)FormatterServices.GetUninitializedObject(typeof(HeroController));hero.cState=new HeroControllerStates();owner.hero=hero;MarioController.Instance=owner;Set(hero,"acceptingInput",true);
  var h=new Harmony("bridge-ordinary-input-checks");
  try{
   foreach(string name in new[]{"LookForQueueInput","LookForInput","Move","Jump","DoubleJump","Dash","BackDash","HeroJump","HeroJumpNoEffect","HeroDash","DoWallJump","DoDoubleJump","Update","FixedUpdate"}){
    var method=AccessTools.Method(typeof(HeroController),name);h.Patch(method,transpiler:new HarmonyMethod(typeof(HazardRecoilChecks),nameof(Stub)));
    foreach(int actor in new[]{-1,7})foreach(bool stagger in new[]{false,true}){
     owner.MarioId=actor;hero.cState.hazardRespawning=stagger;hero.cState.recoiling=hero.cState.recoilFrozen=true;
     int before=calls;method.Invoke(hero,name=="Move"?new object[]{1f}:null);
     bool nativeRecovery=(name=="Update"||name=="FixedUpdate")&&stagger&&actor<0;
     Check(calls==before+(nativeRecovery?1:0),"Knight locomotion never runs from an idle, parked or recovering Mario: "+name+", actor="+actor+", stagger="+stagger);
    }
    owner.MarioId=-1;hero.cState.hazardRespawning=false;hero.cState.transitioning=true;
    int count=calls;method.Invoke(hero,name=="Move"?new object[]{1f}:null);
    Check(calls==count+(name=="LookForQueueInput"?0:1),"Scripted native scene movement is preserved while ordinary player input stays blocked: "+name);
    hero.cState.transitioning=false;owner.Wanted=false;count=calls;method.Invoke(hero,name=="Move"?new object[]{1f}:null);
    Check(calls==count+1,"Selecting the Knight restores his own controller: "+name);owner.Wanted=true;
   }
   owner.MarioId=-1;hero.cState.recoiling=hero.cState.recoilFrozen=true;
   Check(!MovementOwnership.NativeBodyRequired(owner,hero),"Stale recoil flags cannot own a completed respawn");
   foreach(string action in new[]{"focus","dash"}){
    var gate=typeof(MovementOwnership).GetField(action,A);object original=gate.GetValue(null);gate.SetValue(null,(Func<object,bool>)(value=>true));
    Check(MovementOwnership.NativeBodyRequired(owner,hero),"An explicit healing/map/crystal owner remains native even between input-lock calls: "+action);
    foreach(string method in new[]{"Update","FixedUpdate","Move"}){
     int before=calls;AccessTools.Method(typeof(HeroController),method).Invoke(hero,method=="Move"?new object[]{1f}:null);
     Check(calls==before+1,"The requested native action keeps its actual controller/physics path: "+action+"/"+method);
    }
    gate.SetValue(null,original);
   }
   foreach(string field in new[]{"controlReqlinquished","acceptingInput"}){
    Set(hero,field,field=="controlReqlinquished");Check(MovementOwnership.NativeBodyRequired(owner,hero),"An explicit native lock remains authoritative: "+field);Set(hero,field,field=="acceptingInput");
   }
   hero.cState.hazardRespawning=true;Check(MovementOwnership.NativeBodyRequired(owner,hero),"The actual native respawn duration stays locked");
   hero.cState.hazardRespawning=false;hero.cState.invulnerable=true;
   var effects=new Hit();var routine=MovementOwnership.HitEffects(hero,effects);
   Check(routine.MoveNext()&&!hero.cState.recoilFrozen&&effects.Effects==1,"Native hit effects and freeze run without handing over movement");
   hero.cState.hazardRespawning=true;hero.cState.hazardRespawning=false;
   Check(!routine.MoveNext()&&!hero.cState.recoiling&&!hero.cState.recoilFrozen&&effects.Effects==2&&effects.Disposals==1,"Post-hazard recoil writes are suppressed at the coroutine boundary, with native effects completed");
   Check(hero.cState.invulnerable,"Movement ownership does not rewrite damage protection");
   effects=new Hit();routine=MovementOwnership.HitEffects(hero,effects);routine.MoveNext();owner.Wanted=false;routine.MoveNext();
   Check(hero.cState.recoiling&&hero.cState.recoilFrozen,"Switching back to the Knight preserves his native recoil");owner.Wanted=true;
   effects=new Hit();routine=MovementOwnership.HitEffects(hero,effects);routine.MoveNext();((IDisposable)routine).Dispose();Check(effects.Disposals==1&&!hero.cState.recoilFrozen,"Canceled hit effects dispose once without leaving frozen Knight state");
   foreach(string method in new[]{"TakeDamage","HazardRespawn","Invulnerable","RegainControl","RelinquishControl"})
    Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(HeroController),method))==null,"The ownership boundary leaves the native damage/respawn/control routine intact: "+method);
   Console.WriteLine("PASS actual native input/update/fixed/movement hooks across parked, active, stagger and transition states; Knight selection; explicit native locks; stale recoil versus real native hit effects/protection and disposal. Engine endpoints intercepted; no game opened.");
  }finally{h.UnpatchSelf();BridgeContracts.Dispose();oldReady.SetValue(null,ready);MarioController.Instance=null;}
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
