using System;
using System.Reflection;
using HarmonyLib;
class HazardHandoverChecks {
 const BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 public class Hero {public static Hero instance;}
 public class Controller {public static Controller Instance;public object Renderer;public bool GameplayReady;}
 public class Picture {public int Disposals;public void Dispose(){Disposals++;}}
 static Type conversation,arrival;static int reads;
 static bool StaleAnimation(ref bool __result){reads++;__result=true;return false;}
 static bool ValidHero(ref bool __result){__result=true;return false;}
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static void Set(string field,object value)=>conversation.GetField(field,S).SetValue(null,value);
 static object Get(string field)=>conversation.GetField(field,S).GetValue(null);
 static object Call(string method,params object[] values)=>conversation.GetMethod(method,S).Invoke(null,values);
 static void Arm(Picture picture,string group){Set("requested",true);Set("actionPose",true);Set("actionHeld",false);Set("actionGroup",group);Set("returning",false);Set("renderer",picture);Set("dispose",typeof(Picture).GetMethod("Dispose"));Set("oldHidden",false);arrival.GetField("clip",S).SetValue(null,new object());}
 static int Main(string[] args){try{
  var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);conversation=mod.GetType("HollowMario.ConversationMario",true);arrival=mod.GetType("HollowMario.ArrivalMario",true);
  var h=new Harmony("authoritative-presentation-return");h.Patch(arrival.GetProperty("Playing",S).GetGetMethod(true),prefix:new HarmonyMethod(typeof(HazardHandoverChecks),nameof(StaleAnimation)));
  h.Patch(conversation.GetMethod("Valid",S),prefix:new HarmonyMethod(typeof(HazardHandoverChecks),nameof(ValidHero)));
  Set("heroType",typeof(Hero));Set("controllerType",typeof(Controller));Hero.instance=new Hero();Controller.Instance=new Controller{Renderer=new object()};
  foreach(string group in new[]{"hazard-return","wake","focus","pickup"}){
   var picture=new Picture();Arm(picture,group);Controller.Instance.GameplayReady=false;
   Check(!(bool)Call("BeforeUpdate",Controller.Instance)&&!(bool)Get("returning"),"Native action remains covered until the bridge releases its body: "+group);
   Controller.Instance.GameplayReady=true;Set("actionHeld",true);
   Check(!(bool)Call("BeforeUpdate",Controller.Instance),"Explicit scripted holds remain respected: "+group);Set("actionHeld",false);
   reads=0;Check((bool)Call("BeforeUpdate",Controller.Instance)&&(bool)Get("returning")&&reads==0,"Bridge readiness wins over a stale arrival clock: "+group);
   Check(picture.Disposals==0&&arrival.GetField("clip",S).GetValue(null)==null,"Last visible mesh is retained until real gameplay draws");
   Call("AfterPlayableRender",new object());Check(picture.Disposals==0,"Unrelated renders cannot release the cover");
   Call("AfterPlayableRender",Controller.Instance.Renderer);Check(picture.Disposals==1&&!(bool)Get("requested")&&(bool)Get("oldHidden"),"First playable draw finishes the single generic handover without exposing the Knight");
  }
  Check(conversation.GetMethod("AfterHeroReturned",S)==null&&conversation.GetMethod("CanFinishHazardReturn",S)==null&&mod.GetType("HollowMario.HazardRecoil")==null,"Superseded hazard-specific ownership handlers were removed");
  h.UnpatchSelf();Console.WriteLine("PASS one generic bridge-authorized presentation return: active native lock, explicit hold, stale animation ignored, last mesh retained and first gameplay frame cleanup for recovery/wake/focus/pickup. No game opened.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
