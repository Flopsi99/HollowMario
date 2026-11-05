using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MarioHollow;
using Mono.Cecil;
using UnityEngine;
class CompoundCollisionChecks {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static Vector2[] Rect(float left,float right,float bottom,float top,bool reverse=false){var p=new[]{new Vector2(left,bottom),new Vector2(right,bottom),new Vector2(right,top),new Vector2(left,top),new Vector2(left,bottom)};for(int i=0;i<p.Length;i++)p[i]=MarioMath.Origin+p[i]*MarioMath.Scale;if(reverse)Array.Reverse(p);return p;}
 static uint Load(List<SM64Surface> faces){var pin=GCHandle.Alloc(faces.ToArray(),GCHandleType.Pinned);try{var shape=new SM64SurfaceObject{surfaceCount=(uint)faces.Count,surfaces=pin.AddrOfPinnedObject()};return Native.sm64_surface_object_create(ref shape);}finally{pin.Free();}}
 static float Floor(float x,float y){IntPtr surface;return Native.sm64_surface_find_floor(x,y,0,out surface);}
 static int Main(string[] args){try{
  TestEnvironment.StartNative();var batch=typeof(Geometry).GetMethod("Prisms",BindingFlags.Static|BindingFlags.Public);var baseFaces=new List<SM64Surface>();Geometry.Prism(Rect(-2000,2000,-900,-800),160,baseFaces);uint baseline=Load(baseFaces);int cases=0;
  try{
   foreach(var origin in new[]{Vector2.zero,new Vector2(100f,200f)})foreach(bool outerReverse in new[]{false,true})foreach(bool innerReverse in new[]{false,true}){
    MarioMath.Origin=origin;
    var paths=new[]{Rect(-500,500,-500,500,outerReverse),Rect(-200,200,-200,200,innerReverse),Rect(-40,40,-40,40,outerReverse)};var faces=new List<SM64Surface>();
    if(batch==null)foreach(var path in paths)Geometry.Prism(path,160,faces);else batch.Invoke(null,new object[]{paths,160f,faces});
    uint id=Load(faces);try{
     Check(Math.Abs(Floor(100,0)+200)<2,"Compound collider cavity was filled or lost its inner floor: expected -200, got "+Floor(100,0));
     Check(Math.Abs(Floor(0,100)-40)<2,"A solid island nested inside the opening must remain solid");
     Check(Math.Abs(Floor(300,600)-500)<2,"Outer solid floor remains unchanged");cases++;
    }finally{Native.sm64_surface_object_delete(id);}
   }
   var separate=new[]{Rect(-500,-100,-300,100),Rect(100,500,-300,100)};var split=new List<SM64Surface>();batch.Invoke(null,new object[]{separate,160f,split});uint detached=Load(split);try{Check(Math.Abs(Floor(0,200)+800)<2,"Separate contour islands do not acquire an invented bridge between them");}finally{Native.sm64_surface_object_delete(detached);}
   using(var host=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath)){
    bool create=false,tick=false,batchRoute=false;
    foreach(var method in host.MainModule.GetType("MarioHollow.WorldCollision").Methods)if(method.HasBody)foreach(var instruction in method.Body.Instructions)if(instruction.Operand is MethodReference call){
     if(call.Name=="get_usedByComposite"){create|=method.Name=="Create";tick|=method.Name=="Tick";}
     batchRoute|=method.Name=="Create"&&call.DeclaringType.Name=="Geometry"&&call.Name=="Prisms";
    }
    Check(create&&tick&&batchRoute,"Actual creation and streaming exclude composite source shapes and route compound paths through the joined converter");
   }
   Console.WriteLine("PASS "+cases+" contour winding combinations through real native collision: open cavity, nested solid island, outer floor, and separate islands.");
  }finally{Native.sm64_surface_object_delete(baseline);}return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
