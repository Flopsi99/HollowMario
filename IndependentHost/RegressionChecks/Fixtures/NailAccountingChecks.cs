using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Mono.Cecil;
class NailAccountingChecks
{
    const BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    public class Receiver{public bool Enabled{get;set;}}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static bool Calls(MethodDefinition method,string type,string name){foreach(var i in method.Body.Instructions)if(i.Operand is MethodReference m&&m.DeclaringType.Name==type&&m.Name==name)return true;return false;}
    static bool NoHealthLoss(ref bool __result){__result=false;return false;}
    static int Main(string[] args){try{
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(string d in new[]{TestEnvironment.Package,TestEnvironment.Managed}){string p=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var signals=mod.GetType("HollowMario.NailTriggerSignals",true);var enabled=signals.GetMethod("Enabled",S);var claim=signals.GetMethod("ClaimDamageEvent",S);
        var receiver=new Receiver();var swing=new HashSet<object>();
        Check(!(bool)enabled.Invoke(null,new object[]{receiver})&&!(bool)claim.Invoke(null,new object[]{receiver,swing})&&swing.Count==0,"Disabled ReceivedDamage listener cannot receive a fallback HIT or consume the next valid contact");
        receiver.Enabled=true;Check((bool)claim.Invoke(null,new object[]{receiver,swing}),"An active listener accepts its first nail contact");
        for(int n=0;n<8;n++)Check(!(bool)claim.Invoke(null,new object[]{receiver,swing}),"Additional colliders or event routes cannot replay the same listener within one swing");
        var other=new Receiver{Enabled=true};Check((bool)claim.Invoke(null,new object[]{other,swing}),"A different rock/listener is unaffected");
        swing.Clear();Check((bool)claim.Invoke(null,new object[]{receiver,swing}),"A genuinely new swing can hit the rock again");
        var plugin=mod.GetType("HollowMario.Plugin",true);var strikeState=plugin.GetNestedType("EnemyStrike",BindingFlags.NonPublic);
        var gameAssembly=Assembly.LoadFrom(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"));
        object blockedTarget=FormatterServices.GetUninitializedObject(gameAssembly.GetType("HealthManager",true));
        var enemyHits=(HashSet<int>)plugin.GetField("hitThisSwing",S).GetValue(null);enemyHits.Clear();
        var hooks=new Harmony("blocked-nail-accounting");
        hooks.Patch(plugin.GetMethod("EnemyLostHealth",S),prefix:new HarmonyMethod(typeof(NailAccountingChecks),nameof(NoHealthLoss)));
        Action<bool,int> finish=(blocked,id)=>{
            object state=Activator.CreateInstance(strikeState);
            var fields=BindingFlags.Instance|BindingFlags.NonPublic;
            strikeState.GetField("Target",fields).SetValue(state,blockedTarget);
            strikeState.GetField("Blocked",fields).SetValue(state,blocked);
            strikeState.GetField("ReceiverId",fields).SetValue(state,id);
            plugin.GetMethod("StrikePostfix",S).Invoke(null,new[]{state});
        };
        finish(true,71);finish(true,71);
        Check(enemyHits.Count==1&&enemyHits.Contains(71),"Armoured contact is consumed once even when native HP stays unchanged");
        finish(false,72);Check(!enemyHits.Contains(72),"An ordinary no-damage/evasion attempt retains its existing retry rules");
        enemyHits.Clear();finish(true,71);Check(enemyHits.Contains(71),"A new swing can produce a fresh blocked contact");
        enemyHits.Clear();hooks.UnpatchSelf();
        using(var host=AssemblyDefinition.ReadAssembly(TestEnvironment.HostPath)){
            var attacks=host.MainModule.GetType("MarioHollow.MarioAttacks");MethodDefinition strike=null;foreach(var m in attacks.Methods)if(m.Name=="Strike")strike=m;
            Check(Calls(strike,"IHitResponder","Hit"),"Bridge still sends the actual native nail hit");
            Check(!Calls(strike,"HeroController","AddMPCharge")&&!Calls(strike,"HeroController","SoulGain"),"Bridge cannot add a second Soul reward beyond the native receiver");
            MethodDefinition tick=null;foreach(var m in attacks.Methods)if(m.Name=="Tick")tick=m;
            Check(Calls(tick,"HealthManager","get_IsInvincible"),"Host candidate pass also remembers native armoured contacts instead of retrying them every tick");
        }
        using(var game=AssemblyDefinition.ReadAssembly(Path.Combine(TestEnvironment.Managed,"Assembly-CSharp.dll"))){bool nativeReward=false;foreach(var method in game.MainModule.GetType("HealthManager").Methods)if(method.HasBody&&Calls(method,"HeroController","SoulGain"))nativeReward=true;Check(nativeReward,"Installed game's own HealthManager retains normal charm-aware Soul gain");}
        using(var companion=AssemblyDefinition.ReadAssembly(TestEnvironment.CompanionPath)){
            MethodDefinition send=null;foreach(var m in companion.MainModule.GetType("HollowMario.Plugin").Methods)if(m.Name=="SendReceivedDamageSignals")send=m;
            Check(Calls(send,"NailTriggerSignals","Enabled")&&Calls(send,"NailTriggerSignals","ClaimDamageEvent"),"Actual scenery dispatch uses the enabled-state and per-swing event gates");
        }
        Console.WriteLine("PASS native Soul owner retained with no bridge top-up; disabled receiver reproduces geo-rock double-contact route; enabled contact once per swing, multiple colliders, separate targets and next-swing reset.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
