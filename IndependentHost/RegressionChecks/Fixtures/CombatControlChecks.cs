using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using UnityEngine;
class CombatControlChecks
{
    static BindingFlags I=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static BindingFlags S=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static object Empty(Type t)=>FormatterServices.GetUninitializedObject(t);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,I).SetValue(o,value);
    static bool Calls(MethodDefinition method,string type,string name)
    {
        foreach(var instruction in method.Body.Instructions)
            if(instruction.Operand is MethodReference m&&m.DeclaringType.Name==type&&m.Name==name)return true;
        return false;
    }
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string dir in new[]{TestEnvironment.Package,TestEnvironment.Managed}){string p=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var host=Assembly.LoadFrom(TestEnvironment.HostPath);
        var actor=Empty(host.GetType("MarioHollow.MarioController",true));var stateField=actor.GetType().GetField("State");object state=Activator.CreateInstance(stateField.FieldType);
        var game=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));object hero=Empty(game.GetType("HeroController",true)),data=Empty(game.GetType("PlayerData",true));
        Set(hero,"playerData",data);Set(data,"hasDoubleJump",true);Set(actor,"hero",hero);Set(actor,"MarioId",1);
        var followType=mod.GetType("HollowMario.KnightResetFollow",true);object follow=Empty(followType);
        Set(follow,"heroField",actor.GetType().GetField("hero"));Set(follow,"stateField",stateField);Set(follow,"stateActionField",state.GetType().GetField("action"));
        var jump=mod.GetType("HollowMario.DoubleJump",true);var ready=jump.GetMethod("Ready",S);var sync=jump.GetMethod("Sync",S);
        Action<uint> action=a=>{Set(state,"action",a);stateField.SetValue(actor,state);};
        action(0x0100088C);Check((bool)ready.Invoke(null,new[]{hero,actor,follow}),"Ordinary airborne Wings use stays available");
        Set(actor,"damagePending",true);Check(!(bool)ready.Invoke(null,new[]{hero,actor,follow}),"Jump cannot overwrite an accepted hit before its first native tick");
        Set(actor,"damagePending",false);
        foreach(uint recovery in new[]{0x010208B0u,0x010208B6u,0x00020460u,0x00020462u,0x0002033Bu}){
            action(recovery);Check((bool)ready.Invoke(null,new[]{hero,actor,follow}),"Unused Wings escape movement recovery: "+recovery.ToString("X"));
            Set(data,"hasDoubleJump",false);Check(!(bool)ready.Invoke(null,new[]{hero,actor,follow}),"Recovery escape still requires Wings");Set(data,"hasDoubleJump",true);
            Set(hero,"doubleJumped",true);Check(!(bool)ready.Invoke(null,new[]{hero,actor,follow}),"Recovery escape cannot reuse spent Wings");Set(hero,"doubleJumped",false);
        }
        foreach(uint other in new[]{0x0188088Au,0x008008A9u,0x0C400201u,0x300022E3u}){action(other);Check(!(bool)ready.Invoke(null,new[]{hero,actor,follow}),"Attacks, ordinary ground movement and swimming retain their own controls");}
        Set(hero,"doubleJumped",true);action(0x0100088C);sync.Invoke(null,new[]{actor,follow});Check((bool)hero.GetType().GetField("doubleJumped",I).GetValue(hero),"Air ticks do not refund Wings");
        action(0x0C400201);sync.Invoke(null,new[]{actor,follow});action(0x0100088C);sync.Invoke(null,new[]{actor,follow});
        Check((bool)ready.Invoke(null,new[]{hero,actor,follow}),"A brief landing followed by another air tick restores Wings");
        Set(hero,"doubleJumped",true);action(0x03000886);sync.Invoke(null,new[]{actor,follow});Check(!(bool)hero.GetType().GetField("doubleJumped",I).GetValue(hero),"First wall-kick tick restores Wings");
        Set(hero,"doubleJumped",true);sync.Invoke(null,new[]{actor,follow});Check((bool)hero.GetType().GetField("doubleJumped",I).GetValue(hero),"Remaining in wall-kick does not continuously refund Wings");
        var window=mod.GetType("HollowMario.Plugin",true).GetMethod("HitWindow",S);
        Check((float)window.Invoke(null,new object[]{"ground_pound"})<0f,"Ground-pound descent has no elapsed-time cutoff on long falls");
        Check((float)window.Invoke(null,new object[]{"ground_pound_land"})==.35f,"Ground-pound landing burst keeps its short duration");
        var shape=mod.GetType("HollowMario.Plugin",true).GetMethod("MoveShape",S);var reach=mod.GetType("HollowMario.SceneryContact",true).GetMethod("Reach",S);
        foreach(string move in new[]{"punch","jump_kick"})foreach(float facing in new[]{-1f,1f})foreach(float scale in new[]{1f,1.15f,1.25f,1.4f}){
            object[] parameters={move,0f,0f,0f};Check((bool)shape.Invoke(null,parameters),"Tuned move exists");
            var body=new Bounds(new Vector3(4,6,0),new Vector3(.6f,1.2f,0));
            var b=(Bounds)reach.Invoke(null,new object[]{body,facing,parameters[1],parameters[2],parameters[3],scale});
            float forward=move=="punch"?.55f:.7f,up=move=="punch"?.05f:-.05f;
            Check(Math.Abs(b.size.x-1.3f*scale)<.00001f&&Math.Abs(b.size.y-1.9f*scale)<.00001f,"Existing punch/kick padding, scaled once");
            Check(Math.Abs(b.center.x-(4+facing*forward*scale))<.00001f&&Math.Abs(b.center.y-(6+up*scale))<.00001f,"Existing punch/kick offset, scaled once");
        }
        using(var assembly=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath)){
            var plugin=assembly.MainModule.GetType("HollowMario.Plugin");MethodDefinition tick=null,body=null;foreach(var m in plugin.Methods){if(m.Name=="TickPostfix")tick=m;if(m.Name=="TickBody")body=m;}
            Check(Calls(tick,"DoubleJump","Sync")&&!Calls(body,"DoubleJump","Sync"),"Wings reset runs on every simulation step, outside the rendered-frame guard");
        }
        using(var assembly=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath)){
            foreach(var t in assembly.MainModule.Types)if(t.Name=="MarioController")foreach(var m in t.Methods)if(m.Name=="Activate"||m.Name=="Deactivate")
                Check(!Calls(m,"BoxCollider2D","set_size")&&!Calls(m,"BoxCollider2D","set_offset"),"Host preserves existing body/hurtbox dimensions across handovers");
            foreach(var t in assembly.MainModule.Types)if(t.Name=="MarioAttacks")foreach(var m in t.Methods)if(m.Name=="Tick")
                Check(Calls(m,"Physics2D","OverlapBoxNonAlloc")&&!Calls(m,"Physics2D","OverlapCircleNonAlloc"),"Host queries shared move bounds instead of provisional circles");
        }
        Console.WriteLine("PASS shared punch/kick geometry and one charm scale; unchanged body dimensions; queued/native damage commitment; per-simulation Wings reset and wall-kick refund. No gameplay launched.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
