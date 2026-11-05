using System;
using System.Runtime.InteropServices;
using MarioHollow;
using UnityEngine;
class GameplayReturnChecks {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  TestEnvironment.StartNative();var pins=new GCHandle[4];object raw=new SM64MarioGeometryBuffers();string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());}
  var buffers=(SM64MarioGeometryBuffers)raw;
  var faces=new[]{new SM64Surface{terrain=1,v0x=-2000,v0z=-2000,v1x=-2000,v1z=2000,v2x=2000,v2z=2000},new SM64Surface{terrain=1,v0x=-2000,v0z=-2000,v1x=2000,v1z=2000,v2x=2000,v2z=-2000}};
  var floorPin=GCHandle.Alloc(faces,GCHandleType.Pinned);uint platform=uint.MaxValue;int id=-1;
  try{
   var obj=new SM64SurfaceObject{surfaceCount=2,surfaces=floorPin.AddrOfPinnedObject()};platform=Native.sm64_surface_object_create(ref obj);
   var input=default(SM64MarioInputs);object[] audio={0u,1088u,new short[8192]};
   foreach(float facing in new[]{-Mathf.PI*.5f,Mathf.PI*.5f})foreach(float gap in new[]{-1f,0f,3f,10f}){
    id=Native.sm64_mario_create(0,20,0);
    var state=MarioController.StartGameplay(id,new Vector3(0,gap,0),Vector2.zero,true,facing);
    Check(Math.Abs(state.py)<.001f&&state.action==0x0C400201u,"Grounded heal/dialogue return starts on the actual floor without a spawn gap");
    for(int n=0;n<8;n++){
     Native.sm64_mario_tick(id,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);
     Check(Math.Abs(state.py)<.01f&&BridgeContracts.Grounded(state.action),"Native gameplay remains grounded instead of dropping after activation");
     Check(Math.Abs(state.faceAngle-facing)<.001f,"Facing survives the real native activation/tick");
    }
    Native.sm64_mario_delete(id);id=-1;
   }
   foreach(float vy in new[]{-8f,8f})foreach(float direction in new[]{-1f,1f}){
    id=Native.sm64_mario_create(0,400,0);var velocity=new Vector2(3f,vy);
    var state=MarioController.StartGameplay(id,new Vector3(0,400,0),velocity,false,direction*Mathf.PI*.5f);
    Check(state.action==0x0100088Cu&&state.py==400f&&Math.Abs(state.vy-vy/.315f)<.001f,"Genuine airborne handover preserves height and host motion");
    Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
    Check(state.px>0f,"Airborne handover keeps its horizontal momentum even when facing against it");
    Check(vy<0?state.py<400f:state.py>400f,"Real native step follows the inherited fall/rise instead of idle or floor snap");
    Native.sm64_mario_delete(id);id=-1;
   }
   id=Native.sm64_mario_create(0,400,0);
   var unsupported=MarioController.StartGameplay(id,new Vector3(0,400,0),Vector2.zero,true,0);
   Check(unsupported.py==400f&&unsupported.action==0x0100088Cu,"Stale onGround cannot pull Mario down to distant terrain");
   var rising=MarioController.StartGameplay(id,new Vector3(0,3,0),new Vector2(0,5),true,0);
   Check(rising.py==3f&&rising.action==0x0100088Cu,"A rising return near a floor cannot be flattened into idle");
   Console.WriteLine("PASS real native gameplay return: repeated grounded heal/dialogue placement in both directions, pooled actors, airborne velocity and no remote-floor snap. No game opened.");
  }finally{if(id>=0)Native.sm64_mario_delete(id);if(platform!=uint.MaxValue)Native.sm64_surface_object_delete(platform);floorPin.Free();foreach(var pin in pins)pin.Free();}
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
