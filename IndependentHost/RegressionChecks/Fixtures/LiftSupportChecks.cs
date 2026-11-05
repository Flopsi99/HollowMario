using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using MarioHollow;
using UnityEngine;
using Mono.Cecil;
class LiftSupportChecks {
 const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 static List<uint> objects=new List<uint>();static int actor=-1;static SM64MarioState state;static SM64MarioGeometryBuffers buffers;static object[] audio={0u,1088u,new short[8192]};
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static uint Box(float left,float right,float bottom,float top){
  var faces=new List<SM64Surface>();float k=MarioMath.Scale;
  Geometry.Prism(new[]{new Vector2(left*k,bottom*k),new Vector2(right*k,bottom*k),new Vector2(right*k,top*k),new Vector2(left*k,top*k),new Vector2(left*k,bottom*k)},160,faces);
  var pin=GCHandle.Alloc(faces.ToArray(),GCHandleType.Pinned);try{var obj=new SM64SurfaceObject{surfaceCount=(uint)faces.Count,surfaces=pin.AddrOfPinnedObject()};uint id=Native.sm64_surface_object_create(ref obj);objects.Add(id);return id;}finally{pin.Free();}
 }
 static uint Ramp(int direction){
  float k=MarioMath.Scale;var faces=new List<SM64Surface>();Geometry.Prism(new[]{new Vector2(0,-1800*k),new Vector2(direction*400*k,-1800*k),new Vector2(direction*400*k,-100*k),Vector2.zero,new Vector2(0,-1800*k)},160,faces);
  var pin=GCHandle.Alloc(faces.ToArray(),GCHandleType.Pinned);try{var obj=new SM64SurfaceObject{surfaceCount=(uint)faces.Count,surfaces=pin.AddrOfPinnedObject()};uint id=Native.sm64_surface_object_create(ref obj);objects.Add(id);return id;}finally{pin.Free();}
 }
 static WorldCollision.Entry Platform(float width=3.7486479f,float thickness=.3157567f){uint id=Box(-width/MarioMath.Scale/2,width/MarioMath.Scale/2,-thickness/MarioMath.Scale,0);return new WorldCollision.Entry{Id=id};}
 static MethodInfo move=typeof(WorldCollision).GetMethod("UpdatePosition",A);
 static void Move(WorldCollision.Entry platform,float x,float y){move.Invoke(null,new object[]{platform,new Vector3(x*MarioMath.Scale,y*MarioMath.Scale,0)});}
 static void Spawn(float x,float y){if(actor>=0)Native.sm64_mario_delete(actor);actor=Native.sm64_mario_create(x,y,0);Check(actor>=0,"Spawn has actual support");state=new SM64MarioState();var input=default(SM64MarioInputs);for(int n=0;n<4;n++)Tick(ref input);}
 static void Tick(ref SM64MarioInputs input){Native.sm64_mario_tick(actor,ref input,ref state,ref buffers);TestEnvironment.PullAudio(audio);}
 static void Clear(){if(actor>=0){Native.sm64_mario_delete(actor);actor=-1;}foreach(uint id in objects)Native.sm64_surface_object_delete(id);objects.Clear();}
 static void Step(WorldCollision.Entry platform,float x,float y,ref SM64MarioInputs input){var contact=MovingSupport.Capture(state);Move(platform,x,y);float lift=MovingSupport.Follow(contact,actor,ref state);Tick(ref input);MovingSupport.InheritTakeoff(contact,lift,actor,ref state);}
 static int Main(string[] args){try{
  TestEnvironment.StartNative();var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};object raw=buffers;
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());}buffers=(SM64MarioGeometryBuffers)raw;
  try{
   var modAssembly=Assembly.LoadFrom(TestEnvironment.CompanionPath);
   var stepGuard=modAssembly.GetType("HollowMario.GroundStepEdges",false);
   var gates=modAssembly.GetType("HollowMario.NativeMovementGates",true);var current=gates.GetField("current",A);object policy=Activator.CreateInstance(current.FieldType);current.FieldType.GetField("Active",A).SetValue(policy,true);current.SetValue(null,policy);
   if(stepGuard!=null)stepGuard.GetMethod("Install",A).Invoke(null,new object[]{TestEnvironment.Library,GetModuleHandle(TestEnvironment.Library)});
   int drops=0;foreach(int direction in new[]{-1,1})foreach(int height in new[]{3,8,30,60,99,101,160}){
    Clear();if(direction>0){Box(-1200,0,-1800,0);Box(0,1200,-1800,-height);}else{Box(0,1200,-1800,0);Box(-1200,0,-1800,-height);}
    Spawn(-direction*250,1);Native.sm64_set_mario_faceangle(actor,direction*(float)Math.PI/2);var walking=new SM64MarioInputs{camLookX=-direction,stickY=.6f};bool fell=false,landed=false;
    for(int n=0;n<200&&state.px*direction<250;n++){
     var beforeStep=state;Tick(ref walking);bool airborne=(state.action&0x800u)!=0;fell|=airborne;
     Check(!((beforeStep.action&0x800u)==0&&!airborne&&beforeStep.py-state.py>4.1f),"Walking snapped down instead of falling: direction="+direction+", drop="+height+", delta="+(beforeStep.py-state.py));
     landed|=fell&&!airborne&&Math.Abs(state.py+height)<2;
    }
    Check(state.px*direction>=250,"Walking leaves the step without snagging");Check(height<=4?!fell:landed,"Ledge drops fall and land naturally; tiny contour seams remain grounded");drops++;
   }
   Console.WriteLine("PASS "+drops+" mirrored downward steps including the thin-lift depth; no grounded downwarp and tiny seams preserved.");
   foreach(int direction in new[]{-1,1})foreach(float speed in new[]{.6f,1f}){
    Clear();if(direction>0){Box(-1200,0,-1800,0);Box(400,1200,-1800,-100);}else{Box(0,1200,-1800,0);Box(-1200,-400,-1800,-100);}Ramp(direction);
    Spawn(-direction*250,1);Native.sm64_set_mario_faceangle(actor,direction*(float)Math.PI/2);var walking=new SM64MarioInputs{camLookX=-direction,stickY=speed};
    for(int n=0;n<300&&state.px*direction<650;n++){Tick(ref walking);Check((state.action&0x800u)==0,"Continuous slopes and their flat joins keep native grounded movement");}
    Check(state.px*direction>=650&&Math.Abs(state.py+100)<2,"Both directions cross the full continuous descending slope");
   }
   // Actual Stag Lift width/thickness, bank seams and parked-lift bobbing.
   int crossings=0;foreach(int direction in new[]{-1,1})foreach(float speed in new[]{.25f,.6f,1f})foreach(float phase in new[]{0f,1.5f,3f,4.5f}){
    Clear();Box(-1200,-178,-1800,0);Box(178,1200,-1800,0);Box(-1300,1300,-1900,-1800);var plate=Platform();Spawn(-direction*450,1);Native.sm64_set_mario_faceangle(actor,direction*(float)Math.PI/2);var input=new SM64MarioInputs{camLookX=-direction,stickY=speed};
    for(int n=0;n<600&&state.px*direction<450;n++){float bob=(float)Math.Sin(n*.21f+phase)*8;Step(plate,0,bob,ref input);Check(state.py>=-35,"Walking across a bobbing lift or bank seam must not put Mario under its deck");}
    Check(state.px*direction>=450,"Crossing stopped: direction="+direction+", speed="+speed+", phase="+phase+", x="+state.px+", y="+state.py+", action="+state.action.ToString("X"));crossings++;
   }
   Clear();Box(-1000,1000,-1900,-1800);var lift=Platform();Spawn(0,1);var still=default(SM64MarioInputs);float y=0;
   for(int n=0;n<100;n++){y=n<40?(n+1)*16:n<60?640:640-(n-59)*16;Step(lift,0,y,ref still);Check(Math.Abs(state.py-y)<2,"Rider follows ascent, stop and descent without falling through or double movement");}
   // Horizontal displacement belongs to upstream; stopped velocity must clear.
   float before=state.px;for(int n=1;n<=10;n++)Step(lift,n*5,y,ref still);float stopped=state.px;for(int n=0;n<30;n++)Step(lift,50,y,ref still);
   Check(Math.Abs(stopped-before-50)<2&&Math.Abs(state.px-stopped)<2,"A horizontal platform carries exactly once and stops carrying when stationary");
   Check(!(bool)typeof(WorldCollision.Entry).GetField("NeedsVelocityReset",A).GetValue(lift),"The production movement adapter clears its final native velocity");
   var jump=new SM64MarioInputs{buttonA=1};Step(lift,50,y,ref jump);Check((state.action&0x800u)!=0,"Jump releases the floor");Check(MovingSupport.Capture(state).Surface==IntPtr.Zero,"An airborne player cannot inherit lift carry");
   float ordinarySpeed=0,ordinaryRise=0;
   foreach(float speed in new[]{0f,16f,-16f}){
    Clear();Box(-1000,1000,-1900,-1800);var elevator=Platform();Spawn(0,1);var neutral=default(SM64MarioInputs);
    Step(elevator,0,speed,ref neutral);var takeoff=new SM64MarioInputs{buttonA=1};Step(elevator,0,2*speed,ref takeoff);
    float initial=state.vy,start=state.py,peak=start;
    Check(MovingSupport.Capture(state).Surface==IntPtr.Zero,"Jump releases its supporting elevator immediately");
    if(speed==0)ordinarySpeed=initial;
    else Check(Math.Abs(initial-ordinarySpeed-Math.Max(0,speed))<.01f,"Takeoff inherits upward elevator speed exactly once; descent cannot weaken the jump");
    float lastVelocity=initial;
    for(int n=0;n<35;n++){
     Step(elevator,0,(2+Math.Min(n+1,6))*speed,ref takeoff);peak=Math.Max(peak,state.py);
     if(n<6){Check(state.vy<=lastVelocity+.01f,"Elevator momentum cannot compound during airborne ticks");lastVelocity=state.vy;}
    }
    if(speed==0)ordinaryRise=peak-start;
    else if(speed>0)Check(peak-start>ordinaryRise+100,"Upward elevator momentum produces a meaningfully higher real jump arc");
    else Check(Math.Abs(peak-start-ordinaryRise)<2,"Descending elevator preserves ordinary jump height");
   }
   using(var host=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath)){
    bool connected=false;foreach(var method in host.MainModule.GetType("MarioHollow.MarioController").Methods)if(method.Name=="Tick")foreach(var instruction in method.Body.Instructions)
     if(instruction.Operand is MethodReference call&&call.DeclaringType.Name=="MovingSupport"&&call.Name=="InheritTakeoff")connected=true;
    Check(connected,"The installed controller actually transfers elevator takeoff momentum");
   }
   Clear();Box(-1200,-178,-1800,0);Box(178,1200,-1800,0);var adjacent=Platform();
   foreach(int direction in new[]{-1,1}){Move(adjacent,0,0);Spawn(direction*195,1);var contact=MovingSupport.Capture(state);Move(adjacent,0,-60);Check(MovingSupport.Follow(contact,actor,ref state)==0,"A nearby or still-parented lift cannot drag a player standing on the bank under its edge");Tick(ref still);Check(Math.Abs(state.py)<2,"Walking off remains on the bank");}
   Clear();Box(-1000,1000,-1900,-1800);var removed=Platform();Spawn(0,1);var support=MovingSupport.Capture(state);Native.sm64_surface_object_delete(removed.Id);objects.Remove(removed.Id);Check(MovingSupport.Follow(support,actor,ref state)==0,"Destroyed or replaced support never teleports its rider");
   using(var mod=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath)){
    bool installed=false,disposed=false;foreach(var method in mod.MainModule.GetType("HollowMario.NativeMovementGates").Methods)if(method.HasBody)foreach(var instruction in method.Body.Instructions)if(instruction.Operand is MethodReference call&&call.DeclaringType.Name=="GroundStepEdges"){installed|=method.Name=="Install"&&call.Name=="Install";disposed|=method.Name=="Dispose"&&call.Name=="Dispose";}
    Check(installed&&disposed,"The actual movement-gate lifecycle installs and removes the ledge-step correction");
    foreach(var method in mod.MainModule.GetType("HollowMario.Plugin").Methods)if(method.Name=="TickPrefix")foreach(var instruction in method.Body.Instructions)if(instruction.Operand is MethodReference call)Check(call.DeclaringType.Name!="PlatformCarry"&&call.DeclaringType.Name!="LiftCarry","Independent gameplay cannot also run the old parent-based carry/rebuild adapters");
   }
   stepGuard?.GetMethod("Dispose",A).Invoke(null,null);
   Console.WriteLine("PASS "+crossings+" left/right slow/normal/fast crossings of actual-width thin Stag Lift with bobbing; rise/stop/descent; single horizontal carry and stop; jump release; both bank-edge detaches; removed support; old carry paths excluded. Full room gameplay remains a playtest check.");
  }finally{Clear();foreach(var pin in pins)pin.Free();}return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
