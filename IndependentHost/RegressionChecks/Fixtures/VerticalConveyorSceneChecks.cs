using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;

class VerticalConveyorSceneChecks
{
    const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly Dictionary<Component,Transform> transforms=new Dictionary<Component,Transform>();
    static readonly Dictionary<Transform,Transform> parents=new Dictionary<Transform,Transform>();
    static readonly Dictionary<Transform,Behaviour> scripts=new Dictionary<Transform,Behaviour>();
    static readonly Dictionary<int,Collider2D> shapes=new Dictionary<int,Collider2D>();
    static readonly List<RaycastHit2D> rays=new List<RaycastHit2D>();
    static readonly Dictionary<Component,GameObject> objects=new Dictionary<Component,GameObject>();
    static UnityEngine.Object disabled,inactive;
    static Collider2D trigger,self;
    static HeroController hero;
    static int serial;
    public class Proxy{public static Proxy Instance;public int MarioId=1;public object hero;}
    public class Mover{public object heroCon;}
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,A).SetValue(target,value);
    static T Empty<T>()where T:UnityEngine.Object
    {
        var value=(T)FormatterServices.GetUninitializedObject(typeof(T));
        typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(value,new IntPtr(++serial));return value;
    }
    static Collider2D Shape()
    {
        var shape=Empty<BoxCollider2D>();var t=Empty<Transform>();transforms[shape]=t;parents[t]=null;objects[shape]=Empty<GameObject>();return shape;
    }
    static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
    static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
    static bool Enabled(UnityEngine.Object value)=>!ReferenceEquals(value,disabled);
    static bool Active(GameObject value)=>!ReferenceEquals(value,inactive);
    static bool Trigger(Collider2D value)=>ReferenceEquals(value,trigger);
    static GameObject ObjectOf(Component value)=>objects[value];
    static Transform TransformOf(Component value)=>transforms[value];
    static Transform Parent(Transform value)=>parents[value];
    static Component Script(Component value,Type type)
        =>scripts.TryGetValue((Transform)value,out Behaviour script)&&script.GetType()==type?script:null;
    static HeroController Hero(Component value)=>ReferenceEquals(value,self)?hero:null;
    static Collider2D HitCollider(ref RaycastHit2D value)=>(Collider2D)shapes[(int)typeof(RaycastHit2D).GetField("m_Collider",A).GetValue(value)];
    static int Ray(Vector2 point,Vector2 direction,RaycastHit2D[] result,float distance)
    {int count=0;foreach(var hit in rays)if(hit.distance<=distance)result[count++]=hit;return count;}
    static void Add(Collider2D shape,float distance)
    {
        int id=++serial;shapes[id]=shape;object hit=default(RaycastHit2D);
        Set(hit,"m_Collider",id);Set(hit,"m_Distance",distance);rays.Add((RaycastHit2D)hit);
    }
    static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code)
    {
        foreach(var instruction in code)
        {
            var m=instruction.operand as MethodInfo;string next=null;
            if(m?.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Equality")next="Equal";
            else if(m?.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Inequality")next="Unequal";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="get_transform")next="TransformOf";
            else if(m?.DeclaringType==typeof(Transform)&&m.Name=="get_parent")next="Parent";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="GetComponent"&&!m.IsGenericMethod)next="Script";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="GetComponentInParent"&&m.IsGenericMethod)next="Hero";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")next="ObjectOf";
            else if(m?.DeclaringType==typeof(Behaviour)&&(m.Name=="get_isActiveAndEnabled"||m.Name=="get_enabled"))next="Enabled";
            else if(m?.DeclaringType==typeof(GameObject)&&m.Name=="get_activeInHierarchy")next="Active";
            else if(m?.DeclaringType==typeof(Collider2D)&&m.Name=="get_isTrigger")next="Trigger";
            else if(m?.DeclaringType==typeof(RaycastHit2D)&&m.Name=="get_collider")next="HitCollider";
            else if(m?.DeclaringType==typeof(Physics2D)&&m.Name=="RaycastNonAlloc")next="Ray";
            if(next!=null){instruction.opcode=OpCodes.Call;instruction.operand=typeof(VerticalConveyorSceneChecks).GetMethod(next,A);}
            yield return instruction;
        }
    }
    static int Main(string[] args)
    {
        var h=new Harmony("hollowmario.vertical-conveyor-scene-check");Type plugin=null;object previous=null;
        try
        {
            var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);var type=mod.GetType("HollowMario.VerticalConveyors",true);
            type.GetMethod("Configure",A).Invoke(null,new object[]{typeof(Native)});
            var endpoints=new HarmonyMethod(typeof(VerticalConveyorSceneChecks),nameof(Endpoints));
            foreach(string name in new[]{"FindBelt","ReadBelt"})h.Patch(type.GetMethod(name,A),transpiler:endpoints);
            var find=type.GetMethod("FindBelt",A);
            Func<float,bool> matches=expected=>{object[] values={Vector2.zero,1,0f};bool yes=(bool)find.Invoke(null,values);Check(!yes||Math.Abs((float)values[2]-expected)<.0001f,"Exact signed Knight belt speed is read");return yes;};
            var belt=Shape();var ordinary=Shape();hero=Empty<HeroController>();self=Shape();
            var nativeBelt=Empty<ConveyorBelt>();scripts[transforms[belt]]=nativeBelt;
            Set(nativeBelt,"vertical",true);Set(nativeBelt,"speed",3f);Add(belt,.525f);
            Check(matches(3),"Real ConveyorBelt metadata activates a physically reachable vertical belt");
            Set(nativeBelt,"speed",-6f);Check(matches(-6),"Downward belts retain their own signed speed");
            Set(nativeBelt,"vertical",false);Check(!matches(0),"Horizontal belts cannot grant vertical carry or wall jumps");Set(nativeBelt,"vertical",true);
            foreach(float invalid in new[]{0f,float.NaN,float.PositiveInfinity}){Set(nativeBelt,"speed",invalid);Check(!matches(0),"Stopped/invalid belt never attaches");}
            Set(nativeBelt,"speed",3f);disabled=nativeBelt;Check(!matches(0),"Disabled belt components stay inactive");disabled=null;
            inactive=objects[belt];Check(!matches(0),"Inactive belt objects stay inactive");inactive=null;
            rays.Clear();Add(ordinary,.25f);Add(belt,.525f);Check(!matches(0),"A belt behind a separate wall cannot affect Mario");
            rays.Clear();Add(belt,.7f);Check(!matches(0),"Distant belts are outside native wall reach");
            rays.Clear();Add(self,0);Add(belt,.525f);Check(matches(3),"The Knight's own collider does not block the scene query");
            var parent=Empty<Transform>();parents[parent]=null;parents[transforms[belt]]=parent;scripts.Remove(transforms[belt]);scripts[parent]=nativeBelt;
            Check(matches(3),"Belts on a collider's parent are recognized");
            var zone=Shape();trigger=zone;var nativeZone=Empty<ConveyorZone>();scripts[transforms[zone]]=nativeZone;
            Set(nativeZone,"vertical",true);Set(nativeZone,"speed",-4f);Set(nativeZone,"activated",true);
            rays.Clear();Add(zone,.51f);Add(ordinary,.525f);Check(matches(-4),"An active vertical trigger zone can modify its adjacent solid wall");
            Set(nativeZone,"activated",false);Check(!matches(0),"Inactive zones never attach");Set(nativeZone,"activated",true);
            rays.Clear();Add(zone,.51f);Check(!matches(0),"A trigger zone alone never creates a wall");

            type.GetMethod("Arm",A).Invoke(null,new object[]{h});
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(ConveyorMovementHero),"LateUpdate")).Prefixes.Count==1,"Production native-proxy guard installs");
            plugin=mod.GetType("HollowMario.Plugin",true);previous=plugin.GetField("Instance",A).GetValue(null);
            var owner=FormatterServices.GetUninitializedObject(plugin);var follow=FormatterServices.GetUninitializedObject(mod.GetType("HollowMario.KnightResetFollow",true));
            Set(owner,"knightFollow",follow);plugin.GetField("Instance",A).SetValue(null,owner);
            Set(follow,"instanceField",typeof(Proxy).GetField("Instance"));Set(follow,"marioIdField",typeof(Proxy).GetField("MarioId"));Set(follow,"heroField",typeof(Proxy).GetField("hero"));
            Proxy.Instance=new Proxy{hero=hero};var mover=new Mover{heroCon=hero};var guard=type.GetMethod("NativeCarry",A);
            Check(!(bool)guard.Invoke(null,new object[]{mover}),"Mario's hidden proxy cannot receive a duplicate late conveyor velocity");
            Proxy.Instance.MarioId=-1;Check((bool)guard.Invoke(null,new object[]{mover}),"Knight/scripted ownership retains native conveyor behavior");
            Proxy.Instance.MarioId=1;mover.heroCon=Empty<HeroController>();Check((bool)guard.Invoke(null,new object[]{mover}),"Other bodies remain untouched");
            Console.WriteLine("PASS actual vertical-belt detection: signed speeds, active/vertical flags, layers of geometry, reach, self colliders, parent components and zones; native proxy handoff. Only Unity scene endpoints substituted.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{h.UnpatchSelf();if(plugin!=null)plugin.GetField("Instance",A).SetValue(null,previous);}
    }
}
