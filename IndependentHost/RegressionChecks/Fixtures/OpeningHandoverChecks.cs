// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
class OpeningHandoverChecks {
 public sealed class Controller {public static Controller Instance; public object Renderer;}
 public sealed class Picture {public int Disposals;public void Dispose(){Disposals++;}}
 static Type opening;
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
 static void Set(string name,object value){opening.GetField(name,flags).SetValue(null,value);}
 static object Get(string name){return opening.GetField(name,flags).GetValue(null);}
 static void Call(string name,params object[] args){opening.GetMethod(name,flags).Invoke(null,args);}
 static void Check(bool condition,string why){if(!condition)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);string managed=TestEnvironment.Managed;
  string package=TestEnvironment.Package;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string dir in new[]{TestEnvironment.Package,package,managed}){string path=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}return null;};
  opening=Assembly.LoadFrom(TestEnvironment.CompanionPath).GetType("HollowMario.OpeningMario",true);
  var intro=new Picture();var playable=new object();Controller.Instance=new Controller{Renderer=playable};
  Set("controllerType",typeof(Controller));Set("renderer",intro);Set("dispose",typeof(Picture).GetMethod("Dispose"));
  Set("presenting",true);Set("pending",true);Set("conversation",true);Set("oldHidden",false);
  Call("ReleaseToGameplay");
  Check((bool)Get("presenting")&&(bool)Get("returning")&&!(bool)Get("pending")&&!(bool)Get("conversation"),"Release allows gameplay while retaining presentation ownership");
  Check(ReferenceEquals(Get("renderer"),intro)&&intro.Disposals==0,"No mesh destruction during the activation gap or retries");
  Call("AfterPlayableRender",intro);Call("AfterPlayableRender",new object());
  Check(intro.Disposals==0&&(bool)Get("returning"),"Private and unrelated renders cannot finish the handover");
  Call("AfterPlayableRender",playable);
  Check(intro.Disposals==1&&Get("renderer")==null&&!(bool)Get("presenting")&&!(bool)Get("returning"),"Actual playable draw releases the intro exactly once");
  Check((bool)Get("oldHidden"),"Cleanup preserves gameplay ownership of hiding the Knight");
  Call("AfterPlayableRender",playable);Call("Stop");Check(intro.Disposals==1,"Repeated draws and cancellation are idempotent");
  intro=new Picture();Set("renderer",intro);Set("presenting",true);Set("pending",true);Set("oldHidden",false);Call("ReleaseToGameplay");Call("Stop");
  Check(intro.Disposals==1&&!(bool)Get("returning")&&!(bool)Get("oldHidden"),"Cancellation clears a pending handover without retaining the hiding override");
  Console.WriteLine("PASS: retained intro through activation gap; ignored unrelated draws; released on gameplay draw; preserved Knight hiding; cancellation/repeated cleanup safe.");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
