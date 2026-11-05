using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
class SpellDamageHandoverChecks
{
    const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    public class Flags{public bool dead,hazardDeath,hazardRespawning,transitioning;}
    public class Hero{public static Hero instance;public Flags cState=new Flags();}
    public class Picture{public int Disposals;public void Dispose(){Disposals++;}}
    public class Rendered{public int Triangles;}
    public class Controller{
        public static Controller Instance;public Hero hero;public int MarioId=-1,Hits,Damage,Hazard;public bool Wanted=true;public object Renderer=new Rendered();
        public void SetDamageSource(Vector3 position){}
        public void OnKnightHit(int damage,int hazard){Hits++;Damage=damage;Hazard=hazard;}
    }
    public class Pools{public int red=5,blue;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Set(object target,string field,object value)=>target.GetType().GetField(field,A).SetValue(target,value);
    static object Empty(Type t)=>FormatterServices.GetUninitializedObject(t);
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var plugin=mod.GetType("HollowMario.Plugin",true);var follow=Empty(mod.GetType("HollowMario.KnightResetFollow",true));
        var instance=Empty(plugin);plugin.GetField("Instance",A).SetValue(null,instance);Set(instance,"knightFollow",follow);
        Set(instance,"Log",Activator.CreateInstance(plugin.GetField("Log",A).FieldType,new object[]{"spell-damage-check"}));
        Set(follow,"instanceField",typeof(Controller).GetField("Instance"));Set(follow,"marioIdField",typeof(Controller).GetField("MarioId"));Set(follow,"heroField",typeof(Controller).GetField("hero"));
        var conversation=mod.GetType("HollowMario.ConversationMario",true);conversation.GetField("heroType",A).SetValue(null,typeof(Hero));conversation.GetField("controllerType",A).SetValue(null,typeof(Controller));
        var reaction=mod.GetType("HollowMario.DamageReaction",true);var frameType=reaction.GetNestedType("Frame",A);var current=reaction.GetField("current",A);var finish=reaction.GetMethod("FinishDamage",A);
        var confirmed=reaction.GetEvent("Confirmed",A);var callback=Delegate.CreateDelegate(confirmed.EventHandlerType,plugin.GetMethod("IndependentHit",A));confirmed.GetAddMethod(true).Invoke(null,new object[]{callback});
        Func<string,Picture> arm=group=>{
            Hero.instance=new Hero();Controller.Instance=new Controller{hero=Hero.instance};
            var picture=new Picture();
            foreach(var item in new[]{new object[]{"requested",true},new object[]{"actionPose",true},new object[]{"actionHeld",true},new object[]{"returning",false},new object[]{"actionGroup",group},new object[]{"renderer",picture},new object[]{"dispose",typeof(Picture).GetMethod("Dispose")},new object[]{"oldHidden",false}})
                conversation.GetField((string)item[0],A).SetValue(null,item[1]);
            return picture;
        };
        Action<bool,bool,int> damage=(loss,presentation,hazard)=>{
            var data=new Pools();object frame=Activator.CreateInstance(frameType);
            foreach(var item in new[]{new object[]{"Valid",true},new object[]{"Hero",Hero.instance},new object[]{"Data",data},new object[]{"Red",typeof(Pools).GetField("red")},new object[]{"Blue",typeof(Pools).GetField("blue")},new object[]{"StartRed",5},new object[]{"CompareRed",5},new object[]{"StartBlue",0},new object[]{"CompareBlue",0},new object[]{"Presentation",presentation},new object[]{"Damage",2},new object[]{"Hazard",hazard},new object[]{"BlockerKey","spell fixture"}})frameType.GetField((string)item[0],A).SetValue(frame,item[1]);
            if(loss)data.red=3;current.SetValue(null,frame);finish.Invoke(null,new[]{Activator.CreateInstance(frameType)});
        };
        foreach(string group in new[]{"fireballLevel","quakeLevel","screamLevel","focus"}){
            var picture=arm(group);
            Check((bool)conversation.GetMethod("CanInterruptForDamage",A).Invoke(null,new object[]{Hero.instance}),"Parked spell/focus presentation is a recognised damage owner");
            Check(!(bool)conversation.GetMethod("CanInterruptForDamage",A).Invoke(null,new object[]{new Hero()}),"A different hero cannot interrupt the cast");
            damage(true,true,1);Check(Controller.Instance.Hits==1&&Controller.Instance.Damage==2&&Controller.Instance.Hazard==1,"Actual health-loss finalizer dispatches once to parked Mario with the real damage");
            Check((bool)conversation.GetField("returning",A).GetValue(null)&&!(bool)conversation.GetField("actionHeld",A).GetValue(null),"Accepted hit releases spell control but retains visual cover");
            Check(picture.Disposals==0,"Old image is held until a real gameplay mesh exists");
            conversation.GetMethod("AfterPlayableRender",A).Invoke(null,new[]{Controller.Instance.Renderer});Check(picture.Disposals==0,"Empty first gameplay draw cannot expose a gap");
            ((Rendered)Controller.Instance.Renderer).Triangles=120;conversation.GetMethod("AfterPlayableRender",A).Invoke(null,new[]{Controller.Instance.Renderer});Check(picture.Disposals==1,"First populated gameplay draw completes the damage handover");
        }
        foreach(string group in new[]{"upgrade","roar","death","web","wake","inventory",null}){arm(group);Check(!(bool)conversation.GetMethod("CanInterruptForDamage",A).Invoke(null,new object[]{Hero.instance}),"Scripted/non-cast sequences do not grant damage reactivation");}
        arm("fireballLevel");damage(false,true,1);Check(Controller.Instance.Hits==0&&!(bool)conversation.GetField("returning",A).GetValue(null),"Rejected/invulnerable hit cannot cancel a spell or invent knockback");
        arm("quakeLevel");damage(true,true,2);Check(Controller.Instance.Hits==0,"Hazard respawn remains the hazard controller's job");
        foreach(string flag in new[]{"dead","hazardDeath","hazardRespawning","transitioning"}){arm("screamLevel");Set(Hero.instance.cState,flag,true);damage(true,true,1);Check(Controller.Instance.Hits==0,"Death/hazard/transition cannot be stolen by a spell damage handover");}
        confirmed.GetRemoveMethod(true).Invoke(null,new object[]{callback});plugin.GetField("Instance",A).SetValue(null,null);
        Console.WriteLine("PASS packaged damage finalizer to interrupted spell/focus handover with parked actor; exact-once damage dispatch, held image through empty render, rejection/hazard/death/transition gates. Native Unity combat still needs playtesting.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
