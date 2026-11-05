// Existing project regression assertions, retargeted to our independent host.
using System;using System.IO;using System.Reflection;
class CrouchChecks {
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),p=TestEnvironment.Package,g=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{p,g}){string f=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(f))return Assembly.LoadFrom(f);}return null;};
  var a=Assembly.LoadFrom(TestEnvironment.CompanionPath);var m=a.GetType("HollowMario.StartupHurtbox").GetMethod("IsCompact",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(uint state in new uint[]{0x0C008220,0x0C008221,0x0C008222,0x0C008223,0x0C008224,0x04008448,0x04808459})if(!(bool)m.Invoke(null,new object[]{state,0}))throw new Exception("Crouch/crawl state missed");
  foreach(uint state in new uint[]{0x0C400201,0x04000440,0x03000880,0x0100088C,0x0188088A})if((bool)m.Invoke(null,new object[]{state,0}))throw new Exception("Ordinary movement unexpectedly shortened");
  if(!(bool)m.Invoke(null,new object[]{0x008008A9u,0x3B})||!(bool)m.Invoke(null,new object[]{0x008008A9u,0x3C})||(bool)m.Invoke(null,new object[]{0x008008A9u,0x3D}))throw new Exception("Pound startup behavior changed");
  Console.WriteLine("Crouch, crawl, crouch transitions and slide states shorten the damage area; standing/jump/dive states restore it; existing pound startup stays intact.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
