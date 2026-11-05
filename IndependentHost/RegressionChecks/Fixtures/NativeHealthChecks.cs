using System;
using System.Runtime.InteropServices;
using MarioHollow;
class NativeHealthChecks {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  TestEnvironment.StartNative();var pins=new GCHandle[4];var buffers=new SM64MarioGeometryBuffers();object raw=buffers;string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());}buffers=(SM64MarioGeometryBuffers)raw;
  var floor=new[]{new SM64Surface{terrain=1,v0x=-8000,v0z=-8000,v1x=-8000,v1z=8000,v2x=8000,v2z=8000},new SM64Surface{terrain=1,v0x=-8000,v0z=-8000,v1x=8000,v1z=8000,v2x=8000,v2z=-8000}};var floorPin=GCHandle.Alloc(floor,GCHandleType.Pinned);uint platform=0;int id=-1;
  try{
   var obj=new SM64SurfaceObject{surfaceCount=2,surfaces=floorPin.AddrOfPinnedObject()};platform=Native.sm64_surface_object_create(ref obj);id=Native.sm64_mario_create(0,5,0);Check(id>=0,"Native test actor starts above real floor");
   var state=new SM64MarioState();var input=default(SM64MarioInputs);object[] audio={0u,1088u,new short[8192]};int impactReactions=0;
   for(int fall=0;fall<12;fall++){
    Native.sm64_set_mario_position(id,0,4000,0);Native.sm64_set_mario_action(id,0x0100088C);Native.sm64_set_mario_velocity(id,0,-75,0);bool reacted=false;
    for(int n=0;n<180;n++){
     Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);
     if(state.py<5&&(state.action&0x20000u)!=0)reacted=true;
     Check(state.health==0x880,"Native fall damage drained the hidden health pool: fall="+fall+", frame="+n+", health="+state.health+", action="+state.action.ToString("X"));
    }
    Check(reacted,"Real long fall still plays its native hard-impact reaction");impactReactions++;
   }
   foreach(bool wallBonk in new[]{true,false}){
    Native.sm64_set_mario_position(id,0,wallBonk?20:4000,0);Native.sm64_set_mario_velocity(id,wallBonk?-16:0,wallBonk?0:-75,0);
    Native.sm64_set_mario_action(id,wallBonk?0x010208B0u:0x0100088Cu);
    bool protectedBefore=false,hitBoundary=false;
    for(int n=0;n<400;n++){
     Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);
     bool protectedNow=(state.action&0x20000u)!=0||state.invincTimer!=0;
     if(protectedBefore&&!protectedNow){
      uint recoveryAction=state.action;float before=state.px;
      Native.sm64_mario_take_damage(id,1,0,state.px+100,state.py,0);
      Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
      Check((state.action&0x20000u)!=0&&Math.Abs(state.vx)>.1f&&Math.Abs(state.px-before)>.1f,"First hittable get-up frame must accept native knockback after "+(wallBonk?"wall bonk":"long fall")+"; before="+recoveryAction.ToString("X")+", after="+state.action.ToString("X"));
      hitBoundary=true;break;
     }
     protectedBefore=protectedNow;
    }
    Check(hitBoundary,"Actual native recovery reaches its first hittable frame");
   }
   Native.sm64_set_mario_position(id,0,2,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,0x0C400201);
   for(int n=0;n<5;n++)Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
   Native.sm64_set_mario_position(id,0,4000,0);Native.sm64_set_mario_action(id,0x010208B0u);
   Native.sm64_mario_take_damage(id,4,0,100,4000,0);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
   Check(state.action==0x010208B0u,"A newly protected native recovery still rejects damage even when the previous tick was hittable");
   var eligibility=System.Reflection.Assembly.LoadFrom(TestEnvironment.CompanionPath).GetType("HollowMario.NativeDamageEligibility",true);
   var scope=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
   foreach(uint recovery in new[]{0x010208B0u,0x010208B6u,0x00020462u,0x00020460u}){
    Native.sm64_set_mario_position(id,0,(recovery&0x800u)!=0?200:2,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,recovery);
    Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check((state.action&0x20000u)!=0,"Test reaches the real protected recovery action");
    float before=state.px;
    bool previous=(bool)eligibility.GetMethod("BeginConfirmedHit",scope).Invoke(null,null);
    try{Native.sm64_mario_take_damage(id,1,0,state.px+100,state.py,0);}
    finally{eligibility.GetMethod("EndConfirmedHit",scope).Invoke(null,new object[]{previous});}
    Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
    Check(Math.Abs(state.vx)>.1f&&Math.Abs(state.px-before)>.1f,"HK-confirmed hit interrupts wall-bonk/fall recovery with real native knockback: "+recovery.ToString("X"));
    Check(!(bool)eligibility.GetField("confirmedHit",scope).GetValue(null),"Confirmed-hit override is released after dispatch");
   }
   Native.sm64_set_mario_position(id,0,2,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,0x0C400201);
   for(int n=0;n<5;n++)Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
   Native.sm64_set_mario_health(id,0x100);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.health==0x880,"Low inherited native HP is repaired before death checks");
   Native.sm64_mario_take_damage(id,1,0,100,0,0);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
   Check((state.action&0x20000u)!=0&&Math.Abs(state.vx)>.1f,"Accepted hit keeps its actual native knockback and protected recoil action");Check(state.health==0x880,"Accepted knockback cannot spend native HP");
   for(int n=0;n<180;n++){Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);}
   Check((state.action&0x20000u)==0,"Ordinary native recoil protection still expires; health maintenance does not grant invincibility");
   Console.WriteLine("PASS "+impactReactions+" real lethal-height falls; first-hittable-frame knockback; HK-confirmed hits interrupt four wall-bonk/fall recovery actions; raw native protection and scoped override cleanup; full hidden HP and normal recovery. Hollow Knight's mask-loss delivery is checked separately.");
  }finally{if(id>=0)Native.sm64_mario_delete(id);Native.sm64_surface_object_delete(platform);floorPin.Free();foreach(var pin in pins)pin.Free();}return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
