// Existing project regression assertions, retargeted to our independent host.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using InControl;
class CapMenuBindingChecks {
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
  string path=Path.Combine(root,@".artifacts\menu-bindings-check.cfg");if(File.Exists(path))File.Delete(path);
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
  store.Invoke(null,new[]{room,parse.Invoke(null,new object[]{"F11"})});restore.Invoke(null,new[]{room});
  Check(has(room,"F11")&&!has(room,"F5"),"Rebinding replaces old key");
  store.Invoke(null,new[]{health,parse.Invoke(null,new object[]{"Mouse:Button4"})});restore.Invoke(null,new[]{health});Check(has(health,"Mouse:Button4"),"Mouse binding");
  room.GetType().GetMethod("ClearBindings").Invoke(room,null);restore.Invoke(null,new[]{room});Check(has(room,"F11"),"Abandoned listen restores saved key");
  object row=FormatterServices.GetUninitializedObject(game.GetType("MappableKey"));game.GetType("MappableKey").GetField("playerAction",I).SetValue(row,room);
  object[] call={row,parse.Invoke(null,new object[]{"F9"}),false};t.GetMethod("Found",S).Invoke(null,call);Check(!(bool)call[2],"Own duplicate is rejected");
  call=new object[]{row,parse.Invoke(null,new object[]{"F10"}),false};Check((bool)t.GetMethod("Found",S).Invoke(null,call),"Free key reaches native validator");
  var capKey=(PlayerAction)st.GetField("CapKey",I).GetValue(set);var capButton=(PlayerAction)st.GetField("CapButton",I).GetValue(set);
  for(int visit=0;visit<3;visit++){
   t.GetMethod("RefreshCaps",S).Invoke(null,null);
   Check(capKey.HasBinding(new KeyBindingSource(Key.LeftShift)),"Mod menu shows saved Shift");
   Check(capButton.HasBinding(new DeviceBindingSource(InputControlType.RightStickButton)),"Mod menu shows saved controller button");
   Check(actual.HasBinding(new KeyBindingSource(Key.LeftShift))&&actual.HasBinding(new DeviceBindingSource(InputControlType.RightStickButton)),"Opening menu preserves gameplay bindings");
   Check(ReferenceEquals(typeof(BindingSource).GetProperty("BoundTo",I).GetValue(capKey.UnfilteredBindings[0],null),capKey)&&ReferenceEquals(typeof(BindingSource).GetProperty("BoundTo",I).GetValue(actual.UnfilteredBindings[0],null),actual),"Menu and gameplay sources have independent ownership");
  }
  game.GetType("MappableKey").GetField("playerAction",I).SetValue(row,capKey);
  call=new object[]{row,new KeyBindingSource(Key.LeftShift),false};Check((bool)t.GetMethod("Found",S).Invoke(null,call),"Current cap key is accepted rather than conflicting with itself");
  foreach(BindingSource candidate in new BindingSource[]{new KeyBindingSource(Key.L),new MouseBindingSource(Mouse.Button4),new KeyBindingSource(Key.LeftShift)}){
   store.Invoke(null,new object[]{capKey,candidate});restore.Invoke(null,new object[]{capKey});
   Check(actual.HasBinding(candidate)&&capKey.HasBinding(candidate),"Rebind updates actual control and displayed control");
   Check(actual.HasBinding(new DeviceBindingSource(InputControlType.RightStickButton)),"Keyboard rebind preserves controller");
  }
  store.Invoke(null,new object[]{capButton,new DeviceBindingSource(InputControlType.RightBumper)});restore.Invoke(null,new object[]{capButton});
  Check(actual.HasBinding(new DeviceBindingSource(InputControlType.RightBumper))&&capButton.HasBinding(new DeviceBindingSource(InputControlType.RightBumper)),"Controller rebind mirrors correctly");
  capKey.ClearBindings();restore.Invoke(null,new object[]{capKey});Check(capKey.HasBinding(new KeyBindingSource(Key.LeftShift)),"Cancel restores actual saved key");
  Check(gameActions.Jump.HasBinding(new KeyBindingSource(Key.Space)),"Vanilla bindings stay intact");
  var harmony=Assembly.LoadFrom(Path.Combine(package,"0Harmony.dll")).GetType("HarmonyLib.Harmony");
  foreach(string name in new[]{"MappableKey","MappableControllerButton"})foreach(string method in new[]{"SetupBindingListenOptions","OnBindingAdded","OnBindingFound","AbortRebind"})Check(harmony.GetMethod("GetPatchInfo").Invoke(null,new object[]{game.GetType(name).GetMethod(method,I)})!=null,"Native hook "+name+"."+method);
  object reread=Activator.CreateInstance(ct,new object[]{path,true});object entry=ct.GetMethod("Bind").MakeGenericMethod(typeof(string)).Invoke(reread,new object[]{"Debug","RoomResetKey","F5",""});Check((string)entry.GetType().GetProperty("Value").GetValue(entry,null)=="F11","Reload persists");
  Check(game.GetType("Modding.ICustomMenuMod").IsAssignableFrom(a.GetType("HollowMario.HollowMarioApi")),"API menu registration");
  File.Delete(path);Console.WriteLine("Cap key/button mirrors, repeated menu opening, Left Shift acceptance, keyboard/mouse/controller rebinding, cancellation, vanilla bindings and disk persistence passed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
