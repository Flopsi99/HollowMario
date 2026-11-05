using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
class BridgeContractChecks
{
    const BindingFlags A=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,Path.Combine(TestEnvironment.Package,"MarioHollow-native"),TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var host=Assembly.LoadFrom(TestEnvironment.HostPath);var game=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);
        var contracts=host.GetType("MarioHollow.BridgeContracts",true);contracts.GetMethod("Install").Invoke(null,new object[]{mod});
        var hero=FormatterServices.GetUninitializedObject(game.GetType("HeroController",true));var other=FormatterServices.GetUninitializedObject(hero.GetType());
        var capture=contracts.GetMethod("CaptureVisibility");var baseline=contracts.GetMethod("BaselineHidden");
        Check(!(bool)baseline.Invoke(null,new[]{hero}),"An unknown later hidden state is never mistaken for the original player visibility");
        capture.Invoke(null,new[]{hero,(object)false});capture.Invoke(null,new[]{hero,(object)true});
        Check(!(bool)baseline.Invoke(null,new[]{hero}),"NPC/map/heal/pickup handovers cannot overwrite the original visible baseline with their own suppression");
        capture.Invoke(null,new[]{other,(object)true});Check((bool)baseline.Invoke(null,new[]{other}),"An actual preexisting hidden flag on another hero remains respected");
        var inputs=host.GetType("MarioHollow.HostInput",true);var build=inputs.GetMethod("Build");
        Func<object,string,object> field=(value,name)=>value.GetType().GetField(name).GetValue(value);
        foreach(float x in new[]{-1f,-.5f,-.1f,0f,.1f,.5f,1f}){
            var value=build.Invoke(null,new object[]{x,true,false,true});
            Check(Math.Abs((float)field(value,"stickY")-Math.Abs(x))<.00001f,"Stick magnitude survives the direct native input conversion");
            Check((float)field(value,"camLookX")== (x==0?0:-Math.Sign(x)),"Left/right orientation is unchanged");
            Check((byte)field(value,"buttonA")==1&&(byte)field(value,"buttonB")==0&&(byte)field(value,"buttonZ")==1,"Native buttons are independent");
        }
        foreach(float x in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})Check((float)field(build.Invoke(null,new object[]{x,false,false,false}),"stickY")==0,"Invalid axis values cannot move Mario");
        foreach(bool blocked in new[]{false,true}){
            var value=inputs.GetMethod("Read").Invoke(null,new object[]{null,blocked});
            Check((float)field(value,"stickY")==0&&(byte)field(value,"buttonA")==0&&(byte)field(value,"buttonB")==0&&(byte)field(value,"buttonZ")==0,"Missing or blocked input is neutral");
        }
        var actor=FormatterServices.GetUninitializedObject(host.GetType("MarioHollow.MarioController",true));actor.GetType().GetField("MarioId").SetValue(actor,1);
        Check(!(bool)contracts.GetMethod("BeforePublishSpeed").Invoke(null,new[]{actor}),"Native movement cannot be published as a second Unity kinematic velocity");
        actor.GetType().GetField("MarioId").SetValue(actor,-1);Check((bool)contracts.GetMethod("BeforePublishSpeed").Invoke(null,new[]{actor}),"Scripted handovers preserve their own dynamic physics");
        foreach(string fsm in new[]{"npc_control","Inspection","Bench Control","Door Control","collapse tute","Quake Floor"})Check((bool)contracts.GetMethod("AllowsSupportSensor").Invoke(null,new object[]{fsm}),"Foot sensor retains its intended interaction/floor role: "+fsm);
        foreach(string fsm in new[]{"Battle Scene","Control","Darkness Region","CameraLockArea","Start Battle","Transition Control"})Check(!(bool)contracts.GetMethod("AllowsSupportSensor").Invoke(null,new object[]{fsm}),"Room/battle/camera position requires the real body: "+fsm);
        var speed=(Vector2)contracts.GetMethod("ProxyVelocity").Invoke(null,new object[]{20f,-75f});
        Check(Math.Abs(speed.x-6.3f)<.00001f&&Math.Abs(speed.y+23.625f)<.00001f,"Native per-step speed is exposed to room FSMs in world units per second without a second physics move");
        // Exercise the actual managed velocity-reader outputs with our proxy.
        var readType=game.GetType("HutongGames.PlayMaker.Actions.GetVelocity2d",true);var read=Activator.CreateInstance(readType);
        Type baseType=readType;FieldInfo component=null;while(baseType!=null&&component==null){component=baseType.GetField("component",A|BindingFlags.DeclaredOnly);baseType=baseType.BaseType;}
        var proxy=FormatterServices.GetUninitializedObject(typeof(Rigidbody2D));component.SetValue(read,proxy);actor.GetType().GetField("body",A).SetValue(actor,proxy);
        var stateField=actor.GetType().GetField("State");object state=Activator.CreateInstance(stateField.FieldType);stateField.FieldType.GetField("vx").SetValue(state,20f);stateField.FieldType.GetField("vy").SetValue(state,-75f);stateField.SetValue(actor,state);
        foreach(string name in new[]{"vector","x","y"}){var f=readType.GetField(name);f.SetValue(read,Activator.CreateInstance(f.FieldType));}
        readType.GetField("space").SetValue(read,Space.World);actor.GetType().GetField("MarioId").SetValue(actor,1);actor.GetType().GetField("Instance").SetValue(null,actor);
        contracts.GetMethod("AfterReadVelocity",A).Invoke(null,new[]{read});
        Func<string,object> output=name=>{object value=readType.GetField(name).GetValue(read);return value.GetType().GetProperty("Value").GetValue(value,null);};
        Check((Vector2)output("vector")==speed&&Math.Abs((float)output("x")-6.3f)<.00001f&&Math.Abs((float)output("y")+23.625f)<.00001f,"Actual PlayMaker velocity outputs expose native motion while Unity stays still");
        actor.GetType().GetField("Instance").SetValue(null,null);
        var pad=(Collider2D)FormatterServices.GetUninitializedObject(typeof(BoxCollider2D));
        var physical=(Collider2D)FormatterServices.GetUninitializedObject(typeof(BoxCollider2D));
        var supportField=contracts.GetField("supportSensor",A);object originalSensor=supportField.GetValue(null);
        supportField.SetValue(null,(Func<Collider2D,bool>)(value=>ReferenceEquals(value,pad)));
        foreach(int id in new[]{-1,1,-1,1})foreach(string phase in new[]{"Enter","Stay","Exit"}){
            actor.GetType().GetField("MarioId").SetValue(actor,id);
            var callback=contracts.GetMethod("BeforeCamera"+phase,A);
            Check((bool)callback.Invoke(null,new object[]{null,physical}),"Native body contacts survive camera entry/exit across every ownership change");
            Check(!(bool)callback.Invoke(null,new object[]{null,pad}),"Support sensor never owns a camera lock, including native handovers");
        }
        supportField.SetValue(null,originalSensor);actor.GetType().GetField("MarioId").SetValue(actor,1);
        var landed=contracts.GetMethod("LandedOnFloor");Check((bool)landed.Invoke(null,new object[]{0x0100088Cu,0x0C400201u}),"Actual fall-to-ground transition emits a landing");
        var grounded=contracts.GetMethod("Grounded");var climb=contracts.GetMethod("FinishedLedgeClimb");
        foreach(uint action in new[]{0x0800034Bu,0x0000054Cu,0x0000054Du,0x0000054Fu,0x380022C0u,0x0100088Cu,0u})
            Check(!(bool)grounded.Invoke(null,new object[]{action}),"Hanging/climbing/swimming/air cannot trigger the tutorial floor impact: "+action.ToString("X"));
        foreach(uint action in new[]{0x0C400201u,0x04000440u,0x00800380u})Check((bool)grounded.Invoke(null,new object[]{action}),"Actual supported idle/walk/carry poses remain grounded");
        foreach(uint action in new[]{0x0000054Cu,0x0000054Du,0x0000054Fu})Check((bool)climb.Invoke(null,new object[]{action,0x0C400201u}),"Completing a ledge climb consumes the pending forced fall without impact");
        Check(!(bool)climb.Invoke(null,new object[]{0x0800034Bu,0x0100088Cu}),"Letting go retains the pending impact for the eventual real floor");
        // The fixture has no Unity scene. Mark this managed proxy live so Unity's
        // overloaded null comparison exercises the active-owner branch.
        typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(actor,new IntPtr(1));
        actor.GetType().GetField("hero").SetValue(actor,hero);actor.GetType().GetField("Instance").SetValue(null,actor);
        foreach(uint action in new[]{0x0800034Bu,0x0000054Fu,0x0100088Cu,0x0C400201u}){
            stateField.FieldType.GetField("action").SetValue(state,action);stateField.SetValue(actor,state);
            object[] call={hero,false};bool allow=(bool)contracts.GetMethod("BeforeHardLanding",A).Invoke(null,call);
            Check(allow==(bool)grounded.Invoke(null,new object[]{action})&&(bool)call[1]==allow,"Actual hard-landing prefix gates both native method and postfix on Mario's real support state: "+action.ToString("X"));
        }
        actor.GetType().GetField("Instance").SetValue(null,null);
        typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(actor,IntPtr.Zero);
        foreach(uint after in new[]{0x0C400201u,0x00800380u})Check(!(bool)landed.Invoke(null,new object[]{after,after}),"Remaining grounded never repeats the landing event");
        foreach(uint after in new[]{0x380022C0u,0x0800034Bu,0x0100088Cu})Check(!(bool)landed.Invoke(null,new object[]{0x0100088Cu,after}),"Swimming, ledge hanging and continuing air are not floor impacts");
        contracts.GetMethod("Dispose").Invoke(null,null);
        Console.WriteLine("PASS shared visibility baseline isolation, analog input magnitude, single-owner proxy movement, auxiliary-sensor scope and floor-landing notifications. Scene visuals and live room events still require playtesting.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
