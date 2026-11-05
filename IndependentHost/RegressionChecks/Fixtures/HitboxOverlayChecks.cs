// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using InControl;
using UnityEngine;
class HitboxOverlayChecks {
 class GameActions : PlayerActionSet {public readonly PlayerAction Jump;public GameActions(){Jump=CreatePlayerAction("Jump");Jump.AddBinding(new KeyBindingSource(Key.Space));}}
 class Handler {public static Handler Instance;public object inputActions;}

 static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic;
 static BindingFlags I=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool b,string why){if(!b)throw new Exception(why);}
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]), managed=TestEnvironment.Managed, package=TestEnvironment.Package;
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{package,managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
  var a=Assembly.LoadFrom(TestEnvironment.CompanionPath);
  var game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
  var t=a.GetType("HollowMario.ApiModSettings",true); var init=t.GetMethod("Initialize",S);var ct=init.GetParameters()[0].ParameterType;
  string path=Path.Combine(root,@".artifacts\hitbox-bindings-check.cfg");if(File.Exists(path))File.Delete(path);
  object config=Activator.CreateInstance(ct,new object[]{path,true});
  Type caps=a.GetType("HollowMario.CapControls",true);var gameActions=new GameActions();Handler.Instance=new Handler{inputActions=gameActions};
  caps.GetField("handlerType",S).SetValue(null,typeof(Handler));caps.GetField("settings",S).SetValue(null,config);
  foreach(string kind in new[]{"key","mouse","button"}){
   Type enumType=kind=="key"?typeof(Key):kind=="mouse"?typeof(Mouse):typeof(InputControlType);
   Type sourceType=kind=="key"?typeof(KeyBindingSource):kind=="mouse"?typeof(MouseBindingSource):typeof(DeviceBindingSource);
   caps.GetField(kind+"Type",S).SetValue(null,enumType);caps.GetField(kind+"Source",S).SetValue(null,sourceType);
  }
  object keyEntry=ct.GetMethod("Bind").MakeGenericMethod(typeof(string)).Invoke(config,new object[]{"Caps","SelectionKey","LeftShift",""});
  object buttonEntry=ct.GetMethod("Bind").MakeGenericMethod(typeof(string)).Invoke(config,new object[]{"Caps","SelectionButton","RightStickButton",""});
  caps.GetField("keyboard",S).SetValue(null,keyEntry);caps.GetField("controller",S).SetValue(null,buttonEntry);
  var actual=(PlayerAction)caps.GetMethod("Ensure",S).Invoke(null,new object[]{Handler.Instance});
  Check(actual.HasBinding(new KeyBindingSource(Key.LeftShift)),"Gameplay starts with saved Left Shift");
  init.Invoke(null,new[]{config});
  object set=t.GetField("bindings",S).GetValue(null); var st=set.GetType();
  object room=st.GetField("Room",I).GetValue(set), health=st.GetField("Health",I).GetValue(set), bench=st.GetField("Bench",I).GetValue(set);
  var parse=t.GetMethod("Parse",S); var restore=t.GetMethod("Restore",S);var store=t.GetMethod("Store",S);
  Func<object,string,bool> has=(action,name)=>(bool)action.GetType().GetMethod("HasBinding").Invoke(action,new[]{parse.Invoke(null,new object[]{name})});
  Check(has(room,"F5")&&has(health,"F8")&&has(bench,"F9"),"Defaults");

  object hitbox=st.GetField("Hitboxes",I).GetValue(set);
  Check(has(hitbox,"F7"),"Hitbox toggle defaults to F7");
  store.Invoke(null,new[]{hitbox,parse.Invoke(null,new object[]{"F10"})});restore.Invoke(null,new[]{hitbox});
  Check(has(hitbox,"F10")&&!has(hitbox,"F7")&&has(room,"F5")&&has(health,"F8")&&has(bench,"F9"),"Hitbox rebind leaves existing debug keys intact");
  object reread=Activator.CreateInstance(ct,new object[]{path,true});object persisted=ct.GetMethod("Bind").MakeGenericMethod(typeof(string)).Invoke(reread,new object[]{"Debug","HitboxDisplayKey","F7",""});
  Check((string)persisted.GetType().GetProperty("Value").GetValue(persisted,null)=="F10","Hitbox key persists on disk");
  Type debug=a.GetType("HollowMario.DebugSettings",true),overlay=a.GetType("HollowMario.HitboxOverlay",true);
  debug.GetMethod("Bind",S).Invoke(null,new[]{config});
  Check(!(bool)debug.GetProperty("ShowHitboxes",S).GetValue(null,null),"Hitbox display is off by default");
  overlay.GetMethod("BeginTick",S).Invoke(null,null);overlay.GetMethod("Attack",S).Invoke(null,new object[]{new Bounds(Vector3.zero,Vector3.one),Vector3.one});
  overlay.GetMethod("Tick",S).Invoke(null,new object[]{null,null});
  Check((int)overlay.GetField("count",S).GetValue(null)==0&&!(bool)overlay.GetField("listening",S).GetValue(null)&&overlay.GetField("material",S).GetValue(null)==null,"Disabled display captures nothing and allocates no renderer/material");
  debug.GetMethod("ToggleHitboxes",S).Invoke(null,null);Check((bool)debug.GetProperty("ShowHitboxes",S).GetValue(null,null),"Toggle enables display");
  debug.GetMethod("ToggleHitboxes",S).Invoke(null,null);Check(!(bool)debug.GetProperty("ShowHitboxes",S).GetValue(null,null),"Second toggle hides display");
  object master=debug.GetField("master",S).GetValue(null);master.GetType().GetProperty("Value").SetValue(master,false,null);debug.GetMethod("ToggleHitboxes",S).Invoke(null,null);
  Check(!(bool)debug.GetProperty("ShowHitboxes",S).GetValue(null,null),"Debug master disables the toggle");
  MethodInfo sweep=overlay.GetMethod("SweepPolygon",S),corner=overlay.GetMethod("BoxCorner",S);
  var shape=new Bounds(new Vector3(10,20,0),new Vector3(2,4,0));
  foreach(Vector3 motion in new[]{Vector3.zero,new Vector3(4,3,0),new Vector3(-4,-3,0),new Vector3(4,0,0),new Vector3(0,3,0)}){
   var points=new Vector3[16];int n=(int)sweep.Invoke(null,new object[]{shape,motion,points});
   float area=0;for(int i=0;i<n;i++)area+=points[i].x*points[(i+1)%n].y-points[(i+1)%n].x*points[i].y;
   float expected=8+Math.Abs(motion.x)*4+Math.Abs(motion.y)*2;
   Check(Math.Abs(area*.5f-expected)<.0001f,"Swept geometry has exact area, excluding diagonal broad-phase corners");
   Check(n==((motion.x!=0&&motion.y!=0)?6:4),"Box remains rectangular or becomes the actual swept hexagon");
   var scenery=a.GetType("HollowMario.SceneryContact",true).GetMethod("Sweeps",S);
   for(float x=4;x<=16;x+=.5f)for(float y=14;y<=26;y+=.5f){
    var p=new Vector3(x,y,0);bool inside=true;
    for(int i=0;i<n;i++){var first=points[i];var second=points[(i+1)%n];if((second.x-first.x)*(p.y-first.y)-(second.y-first.y)*(p.x-first.x)<-.00001f)inside=false;}
    bool contactResult=(bool)scenery.Invoke(null,new object[]{shape,motion,new Bounds(p,Vector3.zero)});
    Check(inside==contactResult,"Drawn sweep agrees with actual combat contact test across sampled interior/exterior points");
   }
  }
  for(int i=0;i<4;i++){
   var standing=(Vector3)corner.Invoke(null,new object[]{new Vector2(2,4),new Vector2(0,2),i});
   var crouched=(Vector3)corner.Invoke(null,new object[]{new Vector2(2,2.5f),new Vector2(0,1.25f),i});
   Check(standing.x==crouched.x&&((i<2)?standing.y==0&&crouched.y==0:standing.y==4&&crouched.y==2.5f),"Live collider dimensions preserve bottom and width while crouching");
  }
  var foot=new Bounds(new Vector3(5,7,0),new Vector3(2,0,0));var footprint=new Vector3[16];int footCount=(int)sweep.Invoke(null,new object[]{foot,new Vector3(3,-4,0),footprint});
  Check(footCount==4,"Foot contact sweep is a parallelogram rather than the enclosing search box");
  File.Delete(path);Console.WriteLine("PASS: default off/no disabled rendering; session toggle/master; F7 default, independent rebinding and persistence; live box dimensions; exact diagonal swept geometry versus actual combat tests; stomp foot path.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
