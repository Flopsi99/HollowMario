using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
class PickupFacingChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static int captured;
 static bool CapturePickup(int clip,float yaw,int hold,string group,ref bool __result){
  if(clip!=0xCD||Math.Abs(yaw)>.001f||hold!=62||group!="pickup")throw new Exception("Ordinary pickup diverged from the shared star-dance presentation");
  captured++;__result=true;return false;
 }
 static int Main(string[] args){try{
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,Path.Combine(TestEnvironment.Package,"MarioHollow-native"),TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var host=Assembly.LoadFrom(TestEnvironment.HostPath);var native=host.GetType("MarioHollow.Native",true);TestEnvironment.StartNative();
 var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var flourish=mod.GetType("HollowMario.MapFallFlourish",true);
 flourish.GetMethod("Configure",S).Invoke(null,new object[]{native,new BepInEx.Logging.ManualLogSource("pickup-facing")});
 var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
 for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}
 object[] audio={0u,1088u,new short[8192]};
 var conversation=mod.GetType("HollowMario.ConversationMario",true);int clip=(int)conversation.GetField("PickupDance",S).GetRawConstantValue(),last=(int)conversation.GetField("PickupDanceLastFrame",S).GetRawConstantValue();
 if(clip!=0xCD)throw new Exception("Pickup must use the actual star dance, not the turning credits animation");
 var hooks=new HarmonyLib.Harmony("ordinary-pickup-dispatch");
 hooks.Patch(conversation.GetMethod("StartAction",S),prefix:new HarmonyLib.HarmonyMethod(typeof(PickupFacingChecks),nameof(CapturePickup)));
 var game=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));var hero=(UnityEngine.Component)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType("HeroController"));
 var flags=hero.GetType().GetField("cState");flags.SetValue(hero,Activator.CreateInstance(flags.FieldType));
 foreach(string family in new[]{"Collect Normal 1","Collect Normal 2","Collect Normal 3","Collect SD 1","Collect SD 2","Collect SD 3","Collect Magical 1","Collect Magical 2","Collect Magical 3","Collect Magical 3b"}){
  if(!(bool)conversation.GetMethod("PickupClip",S).Invoke(null,new object[]{family}))throw new Exception("Collection family missed: "+family);
  if((bool)conversation.GetMethod("ScriptedPickup",S).Invoke(null,new object[]{family}))throw new Exception("An ordinary pickup was incorrectly claimed by a story sequence: "+family);
  conversation.GetMethod("Pickup",S).Invoke(null,new object[]{hero});
 }
 foreach(string other in new[]{"Collect StandToIdle","Collect Normal To Idle","Idle","Wake Up Ground"})
  if((bool)conversation.GetMethod("PickupClip",S).Invoke(null,new object[]{other}))throw new Exception("Return/wake clip incorrectly starts a new collection: "+other);
 if(captured!=10)throw new Exception("Every ordinary collection family must use the common pickup path");
 var story=mod.GetType("HollowMario.UpgradeKnockoutMario");var actorField=story.GetField("animator",S);
 var actor=(UnityEngine.Component)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType("tk2dSpriteAnimator"));
 typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,new IntPtr(1));actorField.SetValue(null,actor);
 if(!(bool)conversation.GetMethod("ScriptedPickup",S).Invoke(null,new object[]{"Collect SD 1"}))throw new Exception("Story knockout owner must keep priority over ordinary pickup mapping");
 actorField.SetValue(null,null);hooks.UnpatchSelf();
 foreach(bool right in new[]{false,true})using(var pose=new OpeningMarioPose(native,TestEnvironment.Library,0f,true)){
 float yaw=(float)conversation.GetMethod("PickupFacing",S).Invoke(null,new object[]{right});if(Math.Abs(yaw)>.001)throw new Exception("Both approach directions must face camera");
 for(int frame=0;frame<120;frame++){
 pose.TalkFacing(yaw);pose.LimitAnimationFrame(clip,last);int prior=(int)flourish.GetMethod("PushCinematicAnimation",S).Invoke(null,new object[]{clip});
 try{b=pose.Tick(b,true);}finally{flourish.GetMethod("PopCinematicAnimation",S).Invoke(null,new object[]{prior});}TestEnvironment.PullAudio(audio);
 int count=Convert.ToInt32(bt.GetField("numTrianglesUsed").GetValue(b));var ns=new float[count*9];var uv=new float[count*6];Marshal.Copy((IntPtr)bt.GetField("normal").GetValue(b),ns,0,ns.Length);Marshal.Copy((IntPtr)bt.GetField("uv").GetValue(b),uv,0,uv.Length);
 double x=0,z=0;int faces=0;for(int t=0;t<count;t++){float u=(uv[t*6]+uv[t*6+2]+uv[t*6+4])/3;if(u<320f/704||u>=576f/704)continue;for(int v=0;v<3;v++){x+=ns[t*9+v*3];z+=ns[t*9+v*3+2];faces++;}}
 if(faces==0)throw new Exception("ROM-backed face geometry missing");
 if(frame>=30&&(z<=0||Math.Abs(Math.Atan2(x,z))>Math.PI/6))throw new Exception("Star dance finish turns away from camera at "+frame);
 if(frame>=last+2&&Convert.ToInt32(pose.State.GetType().GetField("animFrame").GetValue(pose.State))!=last)throw new Exception("Pickup must hold the celebration finish instead of repeating its spin");
 }}
 flourish.GetMethod("Dispose",S).Invoke(null,null);foreach(var p in pins)p.Free();Console.WriteLine("PASS real ROM star dance from both approach directions; natural opening spin, camera-facing finish and held pose across 120 frames. GPU appearance remains a playtest check.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
