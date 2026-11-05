using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class LedgeHangChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static int Main(string[] args){try{
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,Path.Combine(TestEnvironment.Package,"MarioHollow-native"),TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var host=Assembly.LoadFrom(TestEnvironment.HostPath);var native=host.GetType("MarioHollow.Native",true);TestEnvironment.StartNative();
 var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var flourish=mod.GetType("HollowMario.MapFallFlourish",true);
 flourish.GetMethod("Configure",S).Invoke(null,new object[]{native,new BepInEx.Logging.ManualLogSource("ledge-hang")});
 var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
 for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}
 var policy=mod.GetType("HollowMario.StartupHurtbox",true);var immune=policy.GetMethod("BlocksLedgeHazard",S);var dimensions=policy.GetMethod("Dimensions",S);
 foreach(uint action in new uint[]{0x0800034B,0x0000054C,0x0000054D,0x0000054E,0x0000054F,0x0100088C,0x03000880,0x0C400201})for(int hazard=0;hazard<=6;hazard++){
  bool expected=action==0x0800034B&&hazard>=2&&hazard<=5;
  if((bool)immune.Invoke(null,new object[]{action,hazard})!=expected)throw new Exception("Hazard protection escaped the exact hanging state or swallowed enemy damage");
 }
 var size=new Vector2(.8f,1.6f);var offset=new Vector2(0f,.1f);
 foreach(uint action in new uint[]{0x0C008220,0x0800034B,0x0000054F,0x0100088C,0x0C400201}){
  object[] values={action,0,0f,size,offset,null,null};dimensions.Invoke(null,values);var actualSize=(Vector2)values[5];var actualOffset=(Vector2)values[6];
  if(action==0x0C008220){if(Math.Abs((actualOffset.y-actualSize.y/2)-(offset.y-size.y/2))>.00001f)throw new Exception("Crouching no longer preserves its original lower edge");}
  else if(actualSize!=size)throw new Exception("Hanging/climbing/falling must retain the original hurtbox size");
  if(action!=0x0C008220&&action!=0x0800034B&&actualOffset!=offset)throw new Exception("Climbing/releasing must return to the baseline hurtbox");
 }
 object[] audio={0u,1088u,new short[8192]};float standingCentre=0;

 foreach(float yaw in new[]{-(float)Math.PI/2,(float)Math.PI/2})foreach(int clip in new[]{0xC5,0x33})using(var pose=new OpeningMarioPose(native,TestEnvironment.Library,0f,true)){
 for(int step=0;step<20;step++){
 pose.TalkFacing(yaw);int prior=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
 try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{prior});}TestEnvironment.PullAudio(audio);
 }
 int count=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));var ps=new float[count*9];Marshal.Copy((IntPtr)bt.GetField("position").GetValue(b),ps,0,ps.Length);
 float px=Convert.ToSingle(pose.State.GetType().GetField("px").GetValue(pose.State)),py=Convert.ToSingle(pose.State.GetType().GetField("py").GetValue(pose.State));float min=float.MaxValue,max=float.MinValue,minX=float.MaxValue,maxX=float.MinValue;
 for(int i=0;i<ps.Length;i+=3){min=Math.Min(min,ps[i+1]-py);max=Math.Max(max,ps[i+1]-py);minX=Math.Min(minX,ps[i]-px);maxX=Math.Max(maxX,ps[i]-px);}
 if(clip==0xC5)standingCentre=(min+max)*.5f;
 else{
  object[] values={0x0800034Bu,0x33,yaw,size,Vector2.zero,null,null};dimensions.Invoke(null,values);var shift=(Vector2)values[6];
  var localOffset=policy.GetMethod("LedgeLocalOffset",S);var baseline=new Vector2(.13f,.1f);
  foreach(float mirror in new[]{-1f,1f})foreach(float scaleY in new[]{.8f,1f}){
   var inverse=new Matrix4x4();inverse.m00=mirror;inverse.m11=1f/scaleY;inverse.m22=inverse.m33=1f;
   var forward=new Matrix4x4();forward.m00=mirror;forward.m11=scaleY;forward.m22=forward.m33=1f;
   var local=(Vector2)localOffset.Invoke(null,new object[]{baseline,baseline+shift,inverse});
   var world=(Vector2)forward.MultiplyVector(local-baseline);
   if((world-shift).sqrMagnitude>.000001f)throw new Exception("Hero/HeroBox mirroring flipped the ledge hurtbox back onto the wrong side");
  }
  float expectedY=((min+max)*.5f-standingCentre)*.0105f,expectedX=(minX+maxX)*.5f*.0105f;
  if(Math.Abs(shift.y-expectedY)>.05f||Math.Abs(shift.x-expectedX)>.05f)throw new Exception("Hanging hurtbox does not follow the actual ROM model below/outside this ledge");
 }

 }
 flourish.GetMethod("Dispose",S).Invoke(null,null);foreach(var p in pins)p.Free();Console.WriteLine("PASS both ledge directions against real ROM body geometry through mirrored/unmirrored collider transforms; original hurtbox size, crouch lower edge, climb/drop restoration and exact-state hazard protection with enemy damage retained. Live collisions remain a playtest check.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
