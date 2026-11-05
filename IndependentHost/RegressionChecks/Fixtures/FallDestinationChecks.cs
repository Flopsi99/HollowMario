// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
class FallDestinationChecks {
 const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public class State {public bool dead,transitioning,hazardDeath,hazardRespawning;}
 public class Hero {public State cState=new State();}
 static int Main(string[] args){try{
 string root=args[0],managed=TestEnvironment.Managed,package=TestEnvironment.Package;
 AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
 var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
 var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var guard=mod.GetType("HollowMario.FallDestination",true);
 var hazard=guard.GetMethod("IsHazard",Any);for(int kind=0;kind<=6;kind++)Check((bool)hazard.Invoke(null,new object[]{kind})==(kind>=2&&kind<=5),"Native hazard type classification");
 var exit=guard.GetMethod("IsExit",Any);
 Check((bool)exit.Invoke(null,new object[]{false,"NextRoom","top1"}),"Automatic room exit counts");
 Check(!(bool)exit.Invoke(null,new object[]{true,"NextRoom","door1"}),"Input-only door does not count");
 Check(!(bool)exit.Invoke(null,new object[]{false,"","top1"})&&!(bool)exit.Invoke(null,new object[]{false,"NextRoom",null}),"Incomplete gate does not count");
 var owns=guard.GetMethod("NativeOwns",Any);var hero=new Hero();
 Check(!(bool)owns.Invoke(null,new object[]{hero}),"Normal control remains eligible");
 foreach(string field in new[]{"dead","transitioning","hazardDeath","hazardRespawning"}){typeof(State).GetField(field).SetValue(hero.cState,true);Check((bool)owns.Invoke(null,new object[]{hero}),"Native ownership: "+field);typeof(State).GetField(field).SetValue(hero.cState,false);}
 Check(game.GetType("DamageHero").GetField("hazardType",Any).FieldType==typeof(int),"Installed native hazard uses integer type");
 foreach(string field in new[]{"isADoor","targetScene","entryPoint"})Check(game.GetType("TransitionPoint").GetField(field,Any)!=null,"Installed native gate field: "+field);
 Console.WriteLine("PASS actual release destination classification, native ownership flags and installed game metadata. Scene physics still needs gameplay verification.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
