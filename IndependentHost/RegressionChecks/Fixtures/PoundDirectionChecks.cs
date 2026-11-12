using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;

class PoundDirectionChecks
{
    const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static HeroController hero;
    static Transform anchor;
    static HealthManager upper,lower;
    static BoxCollider2D body,upperShape,lowerShape,sensor;
    static GameObject heroObject,upperObject,lowerObject,sensorObject,downSource,upSource;
    static Bounds bodyBounds=new Bounds(new Vector3(0,1,0),new Vector3(1,2,0));
    static Bounds upperBounds,lowerBounds;
    static HitInstance last;
    static int hits,serial;
    static Collider2D[] upperShapes;
    static float upAngle,downAngle;
    static T Empty<T>()where T:UnityEngine.Object
    {
        var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(value,new IntPtr(++serial));return value;
    }
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static void Set(object value,string name,object next)=>value.GetType().GetField(name,All).SetValue(value,next);
    static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
    static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
    static bool Active(GameObject value)=>true;
    static bool Enabled(UnityEngine.Object value)=>true;
    static bool Trigger(Collider2D value)=>ReferenceEquals(value,sensor);
    static int Layer(GameObject value)=>ReferenceEquals(value,sensorObject)?20:11;
    static Collider2D Body(Component value)=>body;
    static Collider2D[] Children(Component value,bool inactive)=>ReferenceEquals(value,upper)?upperShapes:new Collider2D[]{lowerShape};
    static GameObject ObjectOf(Component value)
        =>ReferenceEquals(value,hero)?heroObject:ReferenceEquals(value,upperShape)?upperObject:ReferenceEquals(value,sensor)?sensorObject:lowerObject;
    static Bounds BoundsOf(Collider2D value)=>ReferenceEquals(value,body)?bodyBounds:ReferenceEquals(value,lowerShape)?lowerBounds:upperBounds;
    static bool Anchor(ref Transform __result){__result=anchor;return false;}
    static bool Receive(HitInstance __0){last=__0;hits++;return false;}
    static bool Configure(GameObject source,int damage,float direction)
    {
        if(ReferenceEquals(source,upSource))upAngle=direction;
        else if(ReferenceEquals(source,downSource))downAngle=direction;
        else throw new Exception("Unexpected native nail source");
        return false;
    }
    // Keep the packaged hit resolver and Strike builder. Substitute only the
    // Unity scene endpoints, since this runner opens no Unity scene or window.
    static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code)
    {
        foreach(var instruction in code)
        {
            var method=instruction.operand as MethodInfo;string name=null;
            if(method?.DeclaringType==typeof(Component)&&method.Name=="get_gameObject")name="ObjectOf";
            else if(method?.DeclaringType==typeof(Component)&&method.Name=="GetComponent"&&method.IsGenericMethod)name="Body";
            else if(method?.DeclaringType==typeof(Component)&&method.Name=="GetComponentsInChildren"&&method.IsGenericMethod)name="Children";
            else if(method?.DeclaringType==typeof(Collider2D)&&method.Name=="get_bounds")name="BoundsOf";
            else if(method?.DeclaringType==typeof(Collider2D)&&method.Name=="get_isTrigger")name="Trigger";
            else if((method?.DeclaringType==typeof(Behaviour)||method?.DeclaringType==typeof(Collider2D))&&method.Name=="get_enabled")name="Enabled";
            else if(method?.DeclaringType==typeof(GameObject)&&method.Name=="get_layer")name="Layer";
            else if(method?.DeclaringType==typeof(GameObject)&&method.Name=="get_activeInHierarchy")name="Active";
            else if(method?.DeclaringType==typeof(UnityEngine.Object)&&method.Name=="op_Equality")name="Equal";
            else if(method?.DeclaringType==typeof(UnityEngine.Object)&&method.Name=="op_Inequality")name="Unequal";
            if(name!=null){instruction.opcode=OpCodes.Call;instruction.operand=typeof(PoundDirectionChecks).GetMethod(name,All);}
            yield return instruction;
        }
    }
    static int Main(string[] args)
    {
        try
        {
            var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            var plugin=companion.GetType("HollowMario.Plugin",true);
            var direction=companion.GetType("HollowMario.NailHitDirection",true);
            var contact=direction.GetMethod("ForContact",All);
            Func<string,Bounds,Bounds,float> resolve=(move,reach,target)=>(float)contact.Invoke(null,new object[]{180f,move,reach,target});
            foreach(string move in new[]{"ground_pound","ground_pound_land"})
            foreach(float scale in new[]{1f,1.15f,1.4f})
            {
                var reach=new Bounds(new Vector3(3,7,0),new Vector3(4,4,0)*scale);
                Check(resolve(move,reach,new Bounds(new Vector3(3,8,50),Vector3.one))==90,"Upper half is an up slash, independent of draw depth");
                Check(resolve(move,reach,new Bounds(new Vector3(3,6,-50),Vector3.one))==270,"Lower half remains a down slash");
                Check(resolve(move,reach,new Bounds(new Vector3(3,7,0),Vector3.one))==270,"Exact middle resolves once with a stable downward tie");
                Check(resolve(move,reach,new Bounds(new Vector3(3,20,0),new Vector3(1,28,0)))==90,"Contact overlap determines direction for a large collider");
            }
            Check(resolve("jump_kick",bodyBounds,new Bounds(Vector3.up*5,Vector3.one))==180,"Other moves retain their facing direction");

            hero=Empty<HeroController>();anchor=Empty<Transform>();upper=Empty<HealthManager>();lower=Empty<HealthManager>();
            body=Empty<BoxCollider2D>();upperShape=Empty<BoxCollider2D>();lowerShape=Empty<BoxCollider2D>();sensor=Empty<BoxCollider2D>();
            heroObject=Empty<GameObject>();upperObject=Empty<GameObject>();lowerObject=Empty<GameObject>();sensorObject=Empty<GameObject>();
            downSource=Empty<GameObject>();upSource=Empty<GameObject>();upperShapes=new Collider2D[]{upperShape};
            var data=(PlayerData)FormatterServices.GetUninitializedObject(typeof(PlayerData));data.nailDamage=5;
            Set(hero,"playerData",data);Set(hero,"cState",new HeroControllerStates());
            var actor=(MarioController)FormatterServices.GetUninitializedObject(typeof(MarioController));actor.hero=hero;actor.MarioId=1;MarioController.Instance=actor;
            var owner=FormatterServices.GetUninitializedObject(plugin);
            typeof(UnityEngine.Object).GetField("m_CachedPtr",All).SetValue(owner,new IntPtr(++serial));
            var logger=new BepInEx.Logging.ManualLogSource("pound-direction-check");Set(owner,"Log",logger);
            var follow=Activator.CreateInstance(companion.GetType("HollowMario.KnightResetFollow",true),All,null,new object[]{logger,typeof(MarioController),200},null);
            Set(owner,"knightFollow",follow);plugin.GetField("Instance",All).SetValue(null,owner);
            plugin.GetField("healthManagerType",All).SetValue(null,typeof(HealthManager));
            var hooks=new Harmony("pound-direction-check");
            hooks.Patch(follow.GetType().GetMethod("HeroTransform",All),prefix:new HarmonyMethod(typeof(PoundDirectionChecks),nameof(Anchor)));
            foreach(string name in new[]{"PoundHitDirection","TryHurtbox","IsEnemy"})
                hooks.Patch(plugin.GetMethod(name,All),transpiler:new HarmonyMethod(typeof(PoundDirectionChecks),nameof(Endpoints)));
            hooks.Patch(typeof(HealthManager).GetMethod("Hit"),prefix:new HarmonyMethod(typeof(PoundDirectionChecks),nameof(Receive)));
            var strike=typeof(MarioAttacks).GetMethod("Strike");
            hooks.Patch(strike,transpiler:new HarmonyMethod(direction.GetMethod("BuildHits",All)));
            hooks.Patch(strike,transpiler:new HarmonyMethod(typeof(PoundDirectionChecks),nameof(Endpoints)));
            var attacks=new MarioAttacks();
            upperBounds=new Bounds(new Vector3(0,2,0),new Vector3(.4f,.4f,0));
            lowerBounds=new Bounds(new Vector3(0,-.2f,0),new Vector3(.4f,.4f,0));
            foreach(int animation in new[]{0x3B,0x3C,0x3D,0x3E})
            foreach(float facing in new[]{-1f,1f})
            {
                bool landed=animation==0x3E;
                string move=landed?"ground_pound_land":"ground_pound";
                actor.State=new SM64MarioState{action=landed?0x0080023Cu:0x008008A9u,animID=animation};
                var row=new AttackRow(move,actor.State.action,animation==0x3B||animation==0x3C?1:2);
                int before=hits;attacks.Strike(upper,hero,facing,row);
                Check(hits==before+1&&last.Direction==90,"Actual Strike sends an upper up slash in every pound phase and facing");
                int damage=last.DamageDealt;
                attacks.Strike(lower,hero,facing,row);
                Check(hits==before+2&&last.Direction==270&&last.DamageDealt==damage,"Lower target receives one down slash with the same damage");
                Check(last.AttackType==AttackTypes.Nail&&!last.IgnoreInvulnerable&&ReferenceEquals(last.Source,heroObject),"Native nail type, source and invulnerability remain intact");
            }
            upperShapes=new Collider2D[]{sensor,lowerShape};
            attacks.Strike(upper,hero,1,new AttackRow("ground_pound",0x008008A9,2));
            Check(last.Direction==270,"A high awareness sensor cannot override the actual lower hurtbox");
            upperShapes=new Collider2D[]{upperShape};
            foreach(float facing in new[]{-1f,1f})
            {
                attacks.Strike(upper,hero,facing,new AttackRow("jump_kick",0x018008AC,1));
                Check(last.Direction==(facing>0?0:180),"Both facing branches retain normal attack direction");
            }
            plugin.GetField("strikeSourceTried",All).SetValue(null,true);
            plugin.GetField("strikeSource",All).SetValue(null,downSource);
            plugin.GetField("upwardStrikeSource",All).SetValue(null,upSource);
            hooks.Patch(plugin.GetMethod("ConfigureStrikeSource",All),prefix:new HarmonyMethod(typeof(PoundDirectionChecks),nameof(Configure)));
            var source=plugin.GetMethod("StrikeSource",All);
            Check(ReferenceEquals(source.Invoke(null,new object[]{5,90f}),upSource),"Upward native callbacks use their dedicated source");
            Check(ReferenceEquals(source.Invoke(null,new object[]{5,270f}),downSource)&&upAngle==90&&downAngle==270,"Opposite contacts do not overwrite a queued upward direction");
            hooks.UnpatchSelf();MarioController.Instance=null;plugin.GetField("Instance",All).SetValue(null,null);
            Console.WriteLine("PASS actual Strike builder and hurtbox resolver across startup/descent/landing and both facings; upper/lower overlap, charm scaling, sensor filtering, unchanged hit data and independent native nail sources. Unity scene endpoints are fixtures. No game launched.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
