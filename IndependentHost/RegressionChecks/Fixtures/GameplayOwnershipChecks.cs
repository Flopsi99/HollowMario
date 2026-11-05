using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
class GameplayOwnershipChecks
{
    const BindingFlags A=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    public class Controller{public static Controller Instance;public int MarioId=7;public long OwnershipEpoch=3;public bool Wanted=true;}
    static int stomp,enemy,body,switchAt;
    static bool Stomp(){stomp++;if(switchAt==1)Transfer();return false;}
    static bool Enemy(){enemy++;if(switchAt==2)Transfer();return false;}
    static bool Body(){body++;if(switchAt==3)Transfer();return false;}
    static void Transfer(){Controller.Instance.MarioId=-1;Controller.Instance.OwnershipEpoch++;}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,A).SetValue(target,value);
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var plugin=mod.GetType("HollowMario.Plugin",true);var instance=FormatterServices.GetUninitializedObject(plugin);var follow=FormatterServices.GetUninitializedObject(mod.GetType("HollowMario.KnightResetFollow",true));
        plugin.GetField("Instance",A).SetValue(null,instance);Set(instance,"knightFollow",follow);Set(follow,"instanceField",typeof(Controller).GetField("Instance"));Set(follow,"marioIdField",typeof(Controller).GetField("MarioId"));
        var current=plugin.GetMethod("GameplayTickCurrent",A);var post=plugin.GetMethod("TickPostfix",A);var tickBody=plugin.GetMethod("TickBody",A);
        Controller.Instance=new Controller{MarioId=-1};tickBody.Invoke(null,new object[]{Controller.Instance});
        Check(!(bool)current.Invoke(null,new object[]{Controller.Instance,3L}),"Parked presentation actor cannot publish gameplay state");
        Controller.Instance.MarioId=7;Check((bool)current.Invoke(null,new object[]{Controller.Instance,3L}),"Current live actor owns its tick");
        Controller.Instance.OwnershipEpoch=5;Check(!(bool)current.Invoke(null,new object[]{Controller.Instance,3L}),"Reused actor ID cannot revive a tick from an earlier ownership generation");
        Check(!(bool)current.Invoke(null,new object[]{Controller.Instance,-1L}),"Skipped/frozen tick has no publication rights");
        Check(!(bool)current.Invoke(null,new object[]{Controller.Instance,0L}),"A prefix skipped by another owner cannot publish the default state as a completed tick");
        Controller.Instance.Wanted=false;Check(!(bool)current.Invoke(null,new object[]{Controller.Instance,5L}),"Switching to Knight invalidates Mario gameplay publication");
        var h=new Harmony("hollowmario.gameplay-ownership-check");
        try{
            h.Patch(plugin.GetMethod("TickStomps",A),prefix:new HarmonyMethod(typeof(GameplayOwnershipChecks).GetMethod("Stomp",A)));
            h.Patch(plugin.GetMethod("TickEnemyHits",A),prefix:new HarmonyMethod(typeof(GameplayOwnershipChecks).GetMethod("Enemy",A)));
            h.Patch(tickBody,prefix:new HarmonyMethod(typeof(GameplayOwnershipChecks).GetMethod("Body",A)));
            Action<long> step=epoch=>{plugin.GetField("stompChecked",A).SetValue(null,false);post.Invoke(null,new object[]{Controller.Instance,epoch});};
            Controller.Instance=new Controller{MarioId=-1};step(3);Check(stomp==0&&enemy==0&&body==0,"Hit-to-respawn handover stops the entire old tick before any collision or Knight-state publication");
            Controller.Instance=new Controller{OwnershipEpoch=5};step(3);Check(stomp==0&&enemy==0&&body==0,"Same-ID reactivation also discards the old tick");
            foreach(int phase in new[]{1,2,3}){
                stomp=enemy=body=0;switchAt=phase;Controller.Instance=new Controller();step(3);
                Check(stomp==1&&enemy==(phase>=2?1:0)&&body==(phase>=3?1:0),"Transfer in a collision callback invalidates every later stage: "+phase);
            }
        }finally{h.UnpatchSelf();plugin.GetField("Instance",A).SetValue(null,null);}
        Console.WriteLine("PASS actual packaged tick publication: parked hazard recovery, reused actor generations, skipped ticks, selected character and ownership changes during stomp/enemy/body callbacks. No game or save opened.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
