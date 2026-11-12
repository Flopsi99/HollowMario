using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;

class RepeatStompChecks
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static float clock=100;
    static Type plugin;
    static HeroController hero;
    static GameObject source,firstObject,secondObject;
    static Enemy first,second;
    static int serial;
    static bool reenterReaction,failReaction;
    public static PlayerData Data {get;private set;}
    static float Parry
    {
        get=>Convert.ToSingle(typeof(HeroController).GetField("parryInvulnTimer",All).GetValue(hero));
        set=>Set(hero,"parryInvulnTimer",value);
    }

    public interface Receiver {void Hit(HitInstance hit);}
    public class Enemy:MonoBehaviour,Receiver
    {
        public int hp,attempts,reactions;
        public bool isDead,armoured,fail,awake;
        public float evadeUntil;
        public List<float> hits;
        public void Hit(HitInstance hit)
        {
            attempts++;
            Check(!hit.IgnoreInvulnerable,"Stomps preserve the enemy's native invulnerability contract");
            if(fail)throw new InvalidOperationException("Expected hit failure");
            if(armoured||clock<evadeUntil)return;
            hp-=hit.DamageDealt;hits.Add(clock);evadeUntil=clock+.2f;
        }
    }
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static T Empty<T>() where T:UnityEngine.Object
    {
        var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(value,new IntPtr(++serial));
        return value;
    }
    static void Set(object value,string name,object next)=>value.GetType().GetField(name,All).SetValue(value,next);
    static void Field(string name,object value)=>plugin.GetField(name,All).SetValue(null,value);
    static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
    static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
    static float Clock()=>clock;
    static string NameOf(UnityEngine.Object value)=>"stomp target";
    static int Identity(UnityEngine.Object value)=>ReferenceEquals(value,firstObject)?1:2;
    static GameObject ObjectOf(Component value)
    {
        if(ReferenceEquals(value,first))return firstObject;
        if(ReferenceEquals(value,second))return secondObject;
        throw new Exception("Unexpected component endpoint");
    }
    static Component ComponentOf(GameObject value,Type type)
    {
        Check(ReferenceEquals(value,source)&&type==typeof(HeroController),"Only the stomping hero receives contact protection");
        return hero;
    }
    static void EnemyReaction(GameObject target,string eventName,bool children)
    {
        Enemy enemy=ReferenceEquals(target,firstObject)?first:ReferenceEquals(target,secondObject)?second:null;
        Check(enemy!=null&&eventName=="TAKE DAMAGE"&&!children,"Use the standard native enemy nail reaction");
        Check(enemy.hp<1000,"The reaction observes completed damage");
        var recorded=(IDictionary<int,float>)plugin.GetField("lastLanding",All).GetValue(null);
        Check(recorded[ReferenceEquals(enemy,first)?1:2]==clock,"Contact is recorded before a synchronous reaction");
        Check((bool)plugin.GetField("deliveringTopHit",All).GetValue(null),"Reaction callbacks cannot claim another top contact");
        enemy.reactions++;
        if(failReaction)throw new InvalidOperationException("Expected reaction failure");
        enemy.awake=true;
        if(reenterReaction){reenterReaction=false;Check(Top(enemy),"A synchronous second touch remains a safe cooldown bounce");}
    }
    // Replace Unity scene/time endpoints only. Hit construction, cooldowns,
    // protection, accounting and native bounce execution remain packaged code.
    static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code)
    {
        foreach(var instruction in code)
        {
            var method=instruction.operand as MethodInfo;string name=null;
            if(method?.DeclaringType==typeof(Time)&&method.Name=="get_unscaledTime")name="Clock";
            else if(method?.DeclaringType==typeof(Component)&&method.Name=="get_gameObject")name="ObjectOf";
            else if(method?.DeclaringType==typeof(GameObject)&&method.Name=="GetComponent"&&!method.IsGenericMethod)name="ComponentOf";
            else if(method?.DeclaringType==typeof(UnityEngine.Object))
            {
                if(method.Name=="GetInstanceID")name="Identity";
                else if(method.Name=="get_name")name="NameOf";
                else if(method.Name=="op_Equality")name="Equal";
                else if(method.Name=="op_Inequality")name="Unequal";
            }
            if(name!=null){instruction.opcode=OpCodes.Call;instruction.operand=typeof(RepeatStompChecks).GetMethod(name,All);}
            yield return instruction;
        }
    }
    static bool Top(Enemy enemy,bool normal=true,int multiplier=1)
        =>(bool)plugin.GetMethod("HitEnemyFromAbove",All).Invoke(null,new object[]{enemy,source,multiplier,normal});
    static void Reset()
    {
        ((IDictionary<int,float>)plugin.GetField("lastLanding",All).GetValue(null)).Clear();
        first.hp=second.hp=1000;first.attempts=second.attempts=0;
        first.hits=new List<float>();second.hits=new List<float>();
        first.evadeUntil=second.evadeUntil=0;first.armoured=second.armoured=false;
        first.isDead=second.isDead=false;first.fail=second.fail=false;
        first.reactions=second.reactions=0;first.awake=second.awake=false;
        reenterReaction=failReaction=false;
        Parry=0;
    }
    static int Main(string[] args)
    {
        try
        {
            TestEnvironment.StartNative();
            var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            plugin=companion.GetType("HollowMario.Plugin",true);
            var owner=FormatterServices.GetUninitializedObject(plugin);
            typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(owner,new IntPtr(++serial));
            var logger=new BepInEx.Logging.ManualLogSource("repeat-stomp-check");Set(owner,"Log",logger);
            plugin.GetField("Instance",All).SetValue(null,owner);
            var follow=Activator.CreateInstance(companion.GetType("HollowMario.KnightResetFollow",true),All,null,
                new object[]{logger,typeof(MarioController),200},null);Set(owner,"knightFollow",follow);
            hero=Empty<HeroController>();source=Empty<GameObject>();firstObject=Empty<GameObject>();secondObject=Empty<GameObject>();
            Data=(PlayerData)FormatterServices.GetUninitializedObject(typeof(PlayerData));Data.nailDamage=5;
            Set(hero,"playerData",Data);Set(hero,"cState",new HeroControllerStates());Set(hero,"INVUL_TIME_PARRY",.15f);
            first=Empty<Enemy>();second=Empty<Enemy>();
            Check((bool)plugin.GetMethod("ArmedNailHit",All).Invoke(null,null),"Normal hit builder is armed");
            Field("healthManagerType",typeof(Enemy));Field("hpField",typeof(Enemy).GetField("hp"));
            Field("enemyDeadField",typeof(Enemy).GetField("isDead"));Field("responderType",typeof(Receiver));
            Field("playerDataInstance",typeof(RepeatStompChecks).GetProperty("Data"));
            Field("nativeMarioVelocity",typeof(Native).GetMethod("sm64_set_mario_velocity"));
            Field("sendFsmEvent",typeof(RepeatStompChecks).GetMethod("EnemyReaction",All));
            var hooks=new Harmony("repeat-stomp-check");
            foreach(string name in new[]{"HitEnemyFromAbove","CanLand","IsEnemy","IsGone","EnemyLostHealth","NotifyEnemyNailHit"})
                hooks.Patch(plugin.GetMethod(name,All),transpiler:new HarmonyMethod(typeof(RepeatStompChecks),nameof(Endpoints)));
            hooks.Patch(plugin.GetMethod("RecordLanding",All,null,new[]{typeof(int)},null),
                transpiler:new HarmonyMethod(typeof(RepeatStompChecks),nameof(Endpoints)));
            Reset();
            Check(Top(first)&&first.hits.Count==1&&first.awake&&first.reactions==1,"The first top landing damages and wakes a native nail listener");
            clock+=.1f;Parry=0;
            Check(Top(first)&&first.attempts==1&&Parry>0&&first.reactions==1,"An early top landing bounces safely without repeating damage or encounter reactions");
            clock+=.11f;
            Check(Top(first)&&first.hits.Count==2&&first.reactions==2,"Each later damaging hit supplies one reaction");
            Check(Top(second)&&second.hits.Count==1&&second.awake&&second.reactions==1,"A different enemy uses the same reaction path independently");
            first.armoured=true;clock+=.21f;
            Check(Top(first)&&first.hits.Count==2&&first.armoured&&first.reactions==2,"Armoured targets retain bounce/protection without false damage reactions");
            first.isDead=true;Check(!Top(first),"Dead targets cannot supply free bounces");
            first.isDead=false;first.fail=true;clock+=.21f;Parry=0;
            Check(!Top(first)&&Parry==0,"A failed hit rolls back its own protection");

            Reset();clock+=.25f;
            Check(Top(first,false,2)&&first.hp==990&&first.awake&&first.reactions==1,"Ground-pound top hits also deliver the normal enemy reaction");
            Reset();reenterReaction=true;
            Check(Top(first)&&first.attempts==1&&first.reactions==1,"Synchronous encounter callbacks cannot recursively damage or reawaken the target");
            Reset();failReaction=true;
            Check(Top(first)&&first.hp==995&&Parry>0,"A reaction failure does not undo a completed stomp or its protection");
            Check(!(bool)plugin.GetField("deliveringTopHit",All).GetValue(null),"Reaction scope is restored even if a listener fails");

            Reset();first.hp=995;
            var frameType=plugin.GetNestedType("EnemyStrike",BindingFlags.NonPublic);
            object frame=Activator.CreateInstance(frameType);
            Set(frame,"Target",first);Set(frame,"Health",1000);Set(frame,"ObjectId",1);Set(frame,"ReceiverId",51);
            Set(frame,"Action",0x018008ACu);Set(frame,"Move","jump_kick");
            plugin.GetMethod("StrikePostfix",All).Invoke(null,new[]{frame});
            Check(first.awake&&first.reactions==1,"Ordinary airborne attacks use the same native reaction helper");

            Reset();float began=clock;
            for(int i=0;i<=120;i++){clock=began+i/120f;Check(Top(first),"Valid ceiling-pinned contacts remain safe");}
            Check(first.hits.Count<=6,"A ceiling cannot produce damage every frame");
            for(int i=1;i<first.hits.Count;i++)Check(first.hits[i]-first.hits[i-1]>=.1999f,"Damaging hits retain the minimum stun interval");

            Reset();
            var floor=new[]{new SM64Surface{v0x=-2000,v0y=-2000,v0z=-2000,v1x=-2000,v1y=-2000,v1z=2000,v2x=2000,v2y=-2000,v2z=2000},
                new SM64Surface{v0x=-2000,v0y=-2000,v0z=-2000,v1x=2000,v1y=-2000,v1z=2000,v2x=2000,v2y=-2000,v2z=-2000}};
            var pin=GCHandle.Alloc(floor,GCHandleType.Pinned);
            var surface=new SM64SurfaceObject{surfaceCount=2,surfaces=pin.AddrOfPinnedObject()};uint platform=Native.sm64_surface_object_create(ref surface);pin.Free();
            int id=Native.sm64_mario_create(0,-1998,0);Check(id>=0,"Native actor created above support");
            var actor=(MarioController)FormatterServices.GetUninitializedObject(typeof(MarioController));
            actor.hero=hero;actor.MarioId=id;MarioController.Instance=actor;
            var buffers=new SM64MarioGeometryBuffers();object raw=buffers;var pins=new GCHandle[4];
            string[] names={"position","normal","color","uv"};
            for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());}
            buffers=(SM64MarioGeometryBuffers)raw;
            try
            {
                Native.sm64_set_mario_action(id,0x03000880u);Native.sm64_set_mario_position(id,0,0,0);Native.sm64_set_mario_velocity(id,0,0,0);
                var state=new SM64MarioState();var input=default(SM64MarioInputs);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);actor.State=state;
                for(int bounce=0;bounce<4;bounce++)
                {
                    int before=first.hp;Check(Top(first),"Repeated short-bounce landing accepted");
                    Check(first.hp<before,"Each normal short-bounce cycle damages its target");
                    // The scene supplies the enemy's top contact plane here.
                    Native.sm64_set_mario_position(id,0,0,0);state.py=0;actor.State=state;
                    plugin.GetMethod("Bounce",All).Invoke(null,new object[]{actor,follow});
                    bool rose=false,returned=false;float launched=clock;
                    for(int tick=0;tick<30;tick++)
                    {
                        clock+=1f/30f;Native.sm64_mario_tick(id,ref input,ref state,ref buffers);actor.State=state;
                        if(state.py>10)rose=true;
                        if(rose&&state.vy<0&&state.py<=0){returned=true;break;}
                    }
                    Check(returned&&clock-launched<.7f,"Actual native jump with Jump released returns sooner than the old stomp lockout");
                }
            }
            finally
            {
                MarioController.Instance=null;Native.sm64_mario_delete(id);Native.sm64_surface_object_delete(platform);
                foreach(var handle in pins)if(handle.IsAllocated)handle.Free();
                plugin.GetField("Instance",All).SetValue(null,null);hooks.UnpatchSelf();
            }
            Console.WriteLine("PASS packaged stomps/ground pounds/air attacks share native post-damage wake reactions; no cooldown/armour duplicates, safe reentrant/failing listeners; native released-Jump bounce cycles and damage limits. Scene endpoints and receivers are fixtures. No game launched.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
