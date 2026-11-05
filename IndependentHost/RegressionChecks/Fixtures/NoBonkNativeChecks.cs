// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario;
using UnityEngine;
class KnockoutFrameCheck {
 class FakeHero {public static int Reads; public static object instance {get{Reads++;throw new Exception("Player should not be inspected");}}}
 class FakeFsm {public string Name="Enemy Control";}
 class FakeState {public FakeFsm Fsm=new FakeFsm();public string Name {get{throw new Exception("Enemy state details should not be inspected");}}}
 class FakeClip {public string name="Run";}

 class MapController {public static MapController Instance;public object hero;public int MarioId=-1;}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Volume(float volume);
 class Controller {public static Controller Instance;public object Renderer;}
 static Delegate originalSet;static IntPtr mario;
 static uint Capture(IntPtr ptr,uint action,uint arg){mario=ptr;return Convert.ToUInt32(originalSet.DynamicInvoke(ptr,action,arg));}
 [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 static void Policy(Type gates,bool badge,bool claws){var f=gates.GetField("current",S);object p=Activator.CreateInstance(f.FieldType);foreach(string name in new[]{"Active","Claws","Dash","NoBonk"})f.FieldType.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(p,name=="NoBonk"?badge:name=="Claws"?claws:true);f.SetValue(null,p);}
 static int muted; static int lavaRequests;
 static void SoundObserved(uint __0,bool __result){if(__result)muted++;if((__0&0xF0FF0000u)==0x20140000u)lavaRequests++;}
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);
  string package=TestEnvironment.Package, managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,Path.Combine(package,"MarioHollow-native"),managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var engine=Assembly.LoadFrom(TestEnvironment.HostPath);var native=engine.GetType("MarioHollow.Native",true);
  string library=TestEnvironment.Library;TestEnvironment.StartNative();
  var audio=native.GetMethod("sm64_audio_tick");object[] audioArgs={0u,1088u,new short[8192]};Console.WriteLine("ROM and audio initialized");var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);var bench=companion.GetType("HollowMario.BenchMario",true);
  var flourish=companion.GetType("HollowMario.MapFallFlourish",true);var configure=flourish.GetMethod("Configure",S);object logger=Activator.CreateInstance(configure.GetParameters()[1].ParameterType,new object[]{"bench-checks"});configure.Invoke(null,new object[]{native,logger});
  var harmony=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.Harmony");object hooks=Activator.CreateInstance(harmony,new object[]{"bench-checks"});

  var plugin=companion.GetType("HollowMario.Plugin",true);foreach(string n in new[]{"Position","Velocity","Action","Health"})plugin.GetField("nativeMario"+n,S).SetValue(null,native.GetMethod("sm64_set_mario_"+n.ToLowerInvariant()));
  var hm=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.HarmonyMethod");

var bt=native.GetMethod("sm64_mario_tick").GetParameters()[3].ParameterType.GetElementType();object b=Activator.CreateInstance(bt);var pins=new GCHandle[4];string[] names={"position","normal","color","uv"};
  for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);bt.GetField(names[i]).SetValue(b,pins[i].AddrOfPinnedObject());}







 var gates=companion.GetType("HollowMario.NativeMovementGates",true);gates.GetMethod("Install",S).Invoke(null,new object[]{library,GetModuleHandle(library)});
 var setter=gates.GetField("originalAction",S);originalSet=(Delegate)setter.GetValue(null);setter.SetValue(null,Delegate.CreateDelegate(setter.FieldType,typeof(KnockoutFrameCheck).GetMethod("Capture",S)));
 var badge=companion.GetType("HollowMario.NoBonk",true);var action=native.GetMethod("sm64_set_mario_action");var kick=gates.GetMethod("WallCheck",S);var step=badge.GetMethod("Step",S);
 using(var pose=new OpeningMarioPose(native,library,0f,true)){
 pose.TalkFacing(0f);b=pose.Tick(b,true);int id=(int)typeof(OpeningMarioPose).GetField("id",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(pose);
 foreach(string scenario in new[]{"equipped","unequipped","no claws","enemy hit","expired","held jump"}){
  Policy(gates,false,true);action.Invoke(null,new object[]{id,scenario=="enemy hit"?0x0C400201u:0x0188088Au});
  Policy(gates,scenario!="unequipped",scenario!="no claws");action.Invoke(null,new object[]{id,0x010208B0u});
  Check(mario!=IntPtr.Zero,"Live native Mario pointer captured");
  Marshal.WriteInt16(mario,2,scenario=="held jump"?(short)0:(short)0x82);Marshal.WriteByte(mario,0x2A,0);
  if(scenario=="expired")for(int i=0;i<9;i++)step.Invoke(null,new object[]{true});
  int result=(int)kick.Invoke(null,new object[]{mario});uint actual=unchecked((uint)Marshal.ReadInt32(mario,0xC));
  if(scenario=="equipped"){Check(result!=0&&actual==0x03000886u,"Real native wall kick starts from badge-enabled dive bonk");Check(unchecked((uint)Marshal.ReadInt32(mario,0x10))==0x010208B0u,"Native wall kick owns new action history");Check(BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(mario,0x4C)),0)>40f,"Native wall kick supplies upward launch");}
  else Check(result==0&&actual==0x010208B0u,"No badge escape for "+scenario);
  Check(Marshal.ReadByte(mario,0x2A)==0&&Marshal.ReadInt16(mario,2)==(scenario=="held jump"?0:0x82),"Native timer restored and unrelated input preserved");
 }
 Policy(gates,false,true);
 }
 gates.GetMethod("Dispose",S).Invoke(null,null);Console.WriteLine("PASS: actual ROM/runtime dive-bonk wall kick; unequipped, missing Claw, enemy damage, expiry and held-button exclusions; native vertical launch, action history and input/timer restoration.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
