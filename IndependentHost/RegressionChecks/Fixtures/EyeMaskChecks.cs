// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
class EyeMaskChecks {
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),package=TestEnvironment.Package,managed=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  TestEnvironment.StartNative();
  var native=Assembly.LoadFrom(TestEnvironment.HostPath).GetType("MarioHollow.Native");
  var engine=native.GetField("Engine",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  byte[] rgba=(byte[])engine.GetType().GetField("Atlas").GetValue(engine);var raw=new Color32[704*64];for(int i=0;i<raw.Length;i++)raw[i]=new Color32(rgba[i*4],rgba[i*4+1],rgba[i*4+2],rgba[i*4+3]);
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var build=mod.GetType("HollowMario.MarioEyeMask").GetMethod("Build",BindingFlags.Static|BindingFlags.NonPublic);var mask=(byte[])build.Invoke(null,new object[]{raw});int[] counts=new int[11];int pupil=0;
  for(int y=0;y<64;y++)for(int x=0;x<704;x++){
   int i=y*704+x;if(mask[i]==0)continue;counts[x/64]++;Check(raw[i].a!=0,"Transparent face rectangle stays black");Check(y>=10,"Disconnected eyebrow components stay black");if(raw[i].b>raw[i].r+30&&raw[i].a>0)pupil++;
  }
  Check(counts[5]==420&&counts[6]==295&&counts[7]==0&&counts[8]==410,"Original rounded eyes, half blink, closed blink and death-eye texture silhouettes are preserved");
  Check(pupil>0,"Pupil/iris areas become white inside the eye outline");for(int slot=0;slot<11;slot++)if(slot<5||slot>8)Check(counts[slot]==0,"Other Mario textures cannot become white");
  for(int i=0;i<raw.Length;i++)Check(raw[i].r==rgba[i*4]&&raw[i].g==rgba[i*4+1]&&raw[i].b==rgba[i*4+2]&&raw[i].a==rgba[i*4+3],"Source ROM textures remain unchanged");
  Console.WriteLine("Actual ROM eye masks passed: rounded outlines, white pupils, both blink stages, black eyebrows/face corners, unchanged other textures and source data.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
