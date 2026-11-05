// Existing project regression assertions, retargeted to our independent host.
using System;using System.IO;using System.Reflection;
class CrumbleChecks {
 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic,I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool b,string text){if(!b)throw new Exception(text);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]),p=TestEnvironment.Package,g=TestEnvironment.Managed;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{p,g}){string f=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(f))return Assembly.LoadFrom(f);}return null;};
  Assembly.LoadFrom(Path.Combine(g,"Assembly-CSharp.dll"));Assembly.LoadFrom(Path.Combine(g,"PlayMaker.dll"));var a=Assembly.LoadFrom(TestEnvironment.CompanionPath);var c=a.GetType("HollowMario.CrumblingFloorContact");c.GetMethod("Arm",S).Invoke(null,null);
  var core=Assembly.LoadFrom(Path.Combine(g,"UnityEngine.CoreModule.dll"));var vector=core.GetType("UnityEngine.Vector3");var bounds=core.GetType("UnityEngine.Bounds");
  Func<float,float,float,object> v=(x,y,z)=>Activator.CreateInstance(vector,new object[]{x,y,z});
  object floor=Activator.CreateInstance(bounds,new[]{v(50.4f,113.1f,-.3f),v(3.8f,.4f,0)});var support=c.GetMethod("Supports",S);
  Check((bool)support.Invoke(null,new[]{floor,v(50.2f,113.3f,0),(object).3f}),"Recorded Deepnest_30 top contact");
  foreach(object feet in new[]{v(50.2f,112.5f,0),v(50.2f,114,0),v(54,113.3f,0)})Check(!(bool)support.Invoke(null,new[]{floor,feet,(object).3f}),"Side/underside/remote contact rejected");
  var game=Assembly.LoadFrom(Path.Combine(g,"Assembly-CSharp.dll"));var actionType=game.GetType("HutongGames.PlayMaker.Actions.Collision2dEvent");object action=Activator.CreateInstance(actionType);actionType.GetProperty("Enabled",I).SetValue(action,true,null);
  var tagField=actionType.GetField("collideTag");object tag=Activator.CreateInstance(tagField.FieldType);tagField.FieldType.GetProperty("Value").SetValue(tag,"Player",null);tagField.SetValue(action,tag);
  var eventField=actionType.GetField("sendEvent");eventField.SetValue(action,System.Runtime.Serialization.FormatterServices.GetUninitializedObject(eventField.FieldType));
  var collisionField=actionType.GetField("collision");collisionField.SetValue(action,Enum.Parse(collisionField.FieldType,"OnCollisionEnter2D"));var accepts=c.GetMethod("Accepts",S);
  Check((bool)accepts.Invoke(null,new[]{action}),"Real native Player landing action accepted");
  collisionField.SetValue(action,Enum.Parse(collisionField.FieldType,"OnCollisionExit2D"));Check(!(bool)accepts.Invoke(null,new[]{action}),"Exit actions not synthesized");collisionField.SetValue(action,Enum.Parse(collisionField.FieldType,"OnCollisionEnter2D"));
  tagField.FieldType.GetProperty("Value").SetValue(tag,"Nail Attack",null);Check(!(bool)accepts.Invoke(null,new[]{action}),"Nail actions not substituted for Player contact");
  var floorScope=c.GetMethod("IsFloor",S);Check((bool)floorScope.Invoke(null,new object[]{"collapse small"}),"Recorded Deepnest floor scope");Check(!(bool)floorScope.Invoke(null,new object[]{"Quake Floor"}),"Quake floors retain separate requirements");Check(!(bool)floorScope.Invoke(null,new object[]{"Control"}),"Unrelated state machines excluded");
  object vel=Activator.CreateInstance(core.GetType("UnityEngine.Vector2"),new object[]{1.5f,-3f});object contact=c.GetMethod("Contact",S).Invoke(null,new object[]{111,222,333,444,vel});var ct=contact.GetType();
  foreach(var pair in new[]{new object[]{"m_Collider",111},new object[]{"m_OtherCollider",222},new object[]{"m_Rigidbody",333},new object[]{"m_OtherRigidbody",444}})Check((int)ct.GetField((string)pair[0],I).GetValue(contact)==(int)pair[1],"Correct receiving-side collision IDs");
  Check((bool)ct.GetProperty("enabled").GetValue(contact,null),"Contact enabled");
  Check(actionType.GetMethod("DoCollisionEnter2D",new[]{ct})!=null,"Native action callback available");
  Console.WriteLine("Recorded Deepnest floor support, side/underside rejection, native Player collision filters, correct collision payload and quake-floor exclusion passed. No game or save opened.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
