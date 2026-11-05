using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using MarioHollow;
using UnityEngine;
class RecoveryJumpChecks
{
 const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static void Set(object target,string name,object value)=>target.GetType().GetField(name,A).SetValue(target,value);
 static int Main(string[] args){try{
  TestEnvironment.StartNative();var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var jump=mod.GetType("HollowMario.DoubleJump",true);
  var plugin=mod.GetType("HollowMario.Plugin",true);var owner=FormatterServices.GetUninitializedObject(plugin);
  typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(owner,new IntPtr(1));
  object logger=Activator.CreateInstance(plugin.GetField("Log",A).FieldType,new object[]{"recovery-jump-check"});Set(owner,"Log",logger);
  var follow=Activator.CreateInstance(mod.GetType("HollowMario.KnightResetFollow",true),A,null,new object[]{logger,typeof(MarioController),200},null);Set(owner,"knightFollow",follow);plugin.GetField("Instance",A).SetValue(null,owner);
  var hero=(HeroController)FormatterServices.GetUninitializedObject(typeof(HeroController));var data=(PlayerData)FormatterServices.GetUninitializedObject(typeof(PlayerData));
  Set(hero,"playerData",data);Set(hero,"cState",new HeroControllerStates());data.hasDoubleJump=true;
  var actor=(MarioController)FormatterServices.GetUninitializedObject(typeof(MarioController));actor.hero=hero;MarioController.Instance=actor;
  jump.GetMethod("Arm",A).Invoke(null,new object[]{typeof(Native)});BridgeContracts.Install(mod);
  mod.GetType("HollowMario.MapFallFlourish",true).GetMethod("Configure",A).Invoke(null,new object[]{typeof(Native),logger});
  var prefix=jump.GetMethod("TickPrefix",A);var pins=new GCHandle[4];object raw=new SM64MarioGeometryBuffers();string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());}
  var buffers=(SM64MarioGeometryBuffers)raw;
  var floor=new[]{new SM64Surface{v0x=-4000,v0z=-4000,v1x=-4000,v1z=4000,v2x=4000,v2z=4000},new SM64Surface{v0x=-4000,v0z=-4000,v1x=4000,v1z=4000,v2x=4000,v2z=-4000}};
  var pin=GCHandle.Alloc(floor,GCHandleType.Pinned);var surface=new SM64SurfaceObject{surfaceCount=2,surfaces=pin.AddrOfPinnedObject()};uint platform=Native.sm64_surface_object_create(ref surface);
  int id=Native.sm64_mario_create(0,2,0);Check(id>=0,"Native floor supports recovery actor");actor.MarioId=id;
  try{
   foreach(uint recovery in new[]{0x010208B0u,0x010208B6u,0x00020460u,0x00020462u,0x0002033Bu,0x0C400201u}){
    jump.GetMethod("Forget",A).Invoke(null,null);Set(hero,"doubleJumped",false);
    Native.sm64_set_mario_position(id,0,(recovery&0x800u)!=0?400:2,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,recovery);
    var state=new SM64MarioState();var input=default(SM64MarioInputs);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);actor.State=state;
    if(recovery==0x0C400201u){actor.BeginHardLanding(.5f);Set(actor,"landingPoseActive",true);Set(actor,"landingPosePrevious",BridgeContracts.BeginTimedLanding(id,0,.5f));}
    prefix.Invoke(null,new object[]{new object[]{id,input,state,buffers}});
    actor.RecoveryJumpHeld=actor.HardLanding;
    input.buttonA=1;prefix.Invoke(null,new object[]{new object[]{id,input,state,buffers}});
    Native.sm64_mario_tick(id,ref input,ref state,ref buffers);actor.State=state;
    Check(state.action==0x03000881u&&state.vy>0,"Production Wings prefix launches real native double jump from recovery "+recovery.ToString("X"));
    Check((bool)hero.GetType().GetField("doubleJumped",A).GetValue(hero),"Escape spends the existing Wings charge");
    Check(!actor.HardLanding&&!(bool)actor.GetType().GetField("landingPoseActive",A).GetValue(actor),"Hard landing timer and forced pose cannot survive the escape");
    input.buttonA=0;prefix.Invoke(null,new object[]{new object[]{id,input,state,buffers}});input.buttonA=1;prefix.Invoke(null,new object[]{new object[]{id,input,state,buffers}});
    Check(!(bool)jump.GetMethod("Ready",A).Invoke(null,new object[]{hero,actor,follow}),"Second airborne press cannot spend Wings again");
   }
   Native.sm64_set_mario_position(id,0,2,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,0x0C400201u);
   var grounded=new SM64MarioState();var masked=default(SM64MarioInputs);Native.sm64_mario_tick(id,ref masked,ref grounded,ref buffers);actor.State=grounded;
   actor.BeginHardLanding(.5f);actor.RecoveryJumpHeld=true;Set(hero,"doubleJumped",true);jump.GetField("lastA",A).SetValue(null,true);
   object[] held={id,masked,grounded,buffers};prefix.Invoke(null,new object[]{held});
   Check(actor.HardLanding&&((SM64MarioInputs)held[1]).buttonA==0,"Holding Jump through landing cannot automatically cancel the get-up");
   actor.RecoveryJumpHeld=false;prefix.Invoke(null,new object[]{new object[]{id,masked,grounded,buffers}});
   actor.RecoveryJumpHeld=true;object[] fresh={id,masked,grounded,buffers};prefix.Invoke(null,new object[]{fresh});masked=(SM64MarioInputs)fresh[1];
   Check(masked.buttonA==1&&!actor.HardLanding,"A fresh accepted Wings escape restores only its masked jump input");
   Native.sm64_mario_tick(id,ref masked,ref grounded,ref buffers);Check(grounded.action==0x03000881u&&grounded.vy>0,"Fresh press escapes hard landing with real native jump height");
  }finally{Native.sm64_mario_delete(id);Native.sm64_surface_object_delete(platform);pin.Free();foreach(var p in pins)p.Free();BridgeContracts.Dispose();MarioController.Instance=null;plugin.GetField("Instance",A).SetValue(null,null);}
  Console.WriteLine("PASS production Wings input prefix with real native jump/velocity from bonks, airborne/ground knockback, stuck recovery and timed hard landing; one charge and forced-pose cancellation. No game launched.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
