using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using MarioHollow;
using UnityEngine;
using UnityEngine.SceneManagement;

class AnimatedColliderChecks
{
    const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    class Identity<T>:IEqualityComparer<T>where T:class
    {public bool Equals(T a,T b)=>ReferenceEquals(a,b);public int GetHashCode(T value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);}
    class Shape{public Transform Transform;public GameObject Object;public Vector2 Size,Offset;public Vector3 Position;public bool Enabled=true,Active=true;}
    static readonly Dictionary<Collider2D,Shape> shapes=new Dictionary<Collider2D,Shape>(new Identity<Collider2D>());
    static readonly Dictionary<Transform,Shape> transforms=new Dictionary<Transform,Shape>(new Identity<Transform>());
    static readonly Dictionary<GameObject,Shape> objects=new Dictionary<GameObject,Shape>(new Identity<GameObject>());
    static int serial,scans;
    static float now;
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static T Empty<T>()where T:UnityEngine.Object
    {
        var value=(T)FormatterServices.GetUninitializedObject(typeof(T));int id=++serial;
        typeof(UnityEngine.Object).GetField("m_CachedPtr",A).SetValue(value,new IntPtr(id));
        typeof(UnityEngine.Object).GetField("m_InstanceID",A)?.SetValue(value,id);return value;
    }
    static BoxCollider2D Box(Vector2 size,Vector2 offset)
    {
        var c=Empty<BoxCollider2D>();var shape=new Shape{Size=size,Offset=offset,Transform=Empty<Transform>(),Object=Empty<GameObject>()};
        shapes[c]=shape;transforms[shape.Transform]=shape;objects[shape.Object]=shape;return c;
    }
    static bool Equal(UnityEngine.Object a,UnityEngine.Object b)=>ReferenceEquals(a,b);
    static bool Unequal(UnityEngine.Object a,UnityEngine.Object b)=>!ReferenceEquals(a,b);
    static bool Enabled(Behaviour value)=>shapes[(Collider2D)value].Enabled;
    static bool Active(GameObject value)=>objects[value].Active;
    static bool No(Collider2D value)=>false;
    static Vector2 Offset(Collider2D value)=>shapes[value].Offset;
    static Vector2 Size(BoxCollider2D value)=>shapes[value].Size;
    static Transform TransformOf(Component value)=>shapes[(Collider2D)value].Transform;
    static GameObject ObjectOf(Component value)=>shapes[(Collider2D)value].Object;
    static Vector3 Position(Transform value)=>transforms[value].Position;
    static Matrix4x4 Matrix(Transform value){var m=Matrix4x4.identity;var p=Position(value);m.m03=p.x;m.m13=p.y;return m;}
    static Vector3 Point(Transform value,Vector3 point)=>point+Position(value);
    static Scene SceneOf(GameObject value)=>default;
    static bool Valid(ref Scene value)=>true;
    static float TimeNow()=>now;
    static int Mask(string[] names)=>1<<8;
    static int Overlap(Vector2 centre,Vector2 size,float angle,Collider2D[] results,int mask)
    {scans++;int i=0;foreach(var shape in shapes.Keys)results[i++]=shape;return i;}
    static IEnumerable<CodeInstruction> Endpoints(IEnumerable<CodeInstruction> code)
    {
        foreach(var instruction in code)
        {
            var m=instruction.operand as MethodInfo;string replacement=null;
            if(m?.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Equality")replacement="Equal";
            else if(m?.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="op_Inequality")replacement="Unequal";
            else if(m?.DeclaringType==typeof(Behaviour)&&m.Name=="get_enabled")replacement="Enabled";
            else if(m?.DeclaringType==typeof(GameObject)&&m.Name=="get_activeInHierarchy")replacement="Active";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="get_transform")replacement="TransformOf";
            else if(m?.DeclaringType==typeof(Component)&&m.Name=="get_gameObject")replacement="ObjectOf";
            else if(m?.DeclaringType==typeof(Collider2D)&&m.Name=="get_offset")replacement="Offset";
            else if(m?.DeclaringType==typeof(Collider2D)&&(m.Name=="get_isTrigger"||m.Name=="get_usedByComposite"||m.Name=="get_usedByEffector"))replacement="No";
            else if(m?.DeclaringType==typeof(BoxCollider2D)&&m.Name=="get_size")replacement="Size";
            else if(m?.DeclaringType==typeof(Transform)&&m.Name=="get_position")replacement="Position";
            else if(m?.DeclaringType==typeof(Transform)&&m.Name=="get_localToWorldMatrix")replacement="Matrix";
            else if(m?.DeclaringType==typeof(Transform)&&m.Name=="TransformPoint"&&m.GetParameters().Length==1)replacement="Point";
            else if(m?.DeclaringType==typeof(GameObject)&&m.Name=="get_scene")replacement="SceneOf";
            else if(m?.DeclaringType==typeof(Scene)&&m.Name=="IsValid")replacement="Valid";
            else if(m?.DeclaringType==typeof(Time)&&m.Name=="get_unscaledTime")replacement="TimeNow";
            else if(m?.DeclaringType==typeof(LayerMask)&&m.Name=="GetMask")replacement="Mask";
            else if(m?.DeclaringType==typeof(Physics2D)&&m.Name=="OverlapBoxNonAlloc")replacement="Overlap";
            if(replacement!=null){instruction.opcode=OpCodes.Call;instruction.operand=typeof(AnimatedColliderChecks).GetMethod(replacement,A);}
            yield return instruction;
        }
    }
    static bool Blocked(float x=-1.2f)
    {float px=x/MarioMath.Scale,py=.2f/MarioMath.Scale,pz=0;return Native.sm64_surface_find_wall_collision(ref px,ref py,ref pz,150f,50f)>0;}
    static int Main(string[] args)
    {
        var hooks=new Harmony("hollowmario.animated-collider-check");WorldCollision world=null;
        try
        {
            TestEnvironment.StartNative();MarioMath.Origin=Vector2.zero;
            var endpoint=new HarmonyMethod(typeof(AnimatedColliderChecks),nameof(Endpoints));
            foreach(string name in new[]{"Create","Tick"})hooks.Patch(typeof(WorldCollision).GetMethod(name,A),transpiler:endpoint);
            foreach(string name in new[]{"Paths","World"})hooks.Patch(typeof(Geometry).GetMethod(name,A),transpiler:endpoint);
            var floor=Box(new Vector2(20,2),new Vector2(0,-1));var crusher=Box(new Vector2(2,4),new Vector2(0,2));
            world=new WorldCollision();world.Tick(Vector2.zero,true);Check(Blocked(),"Lowered crusher initially blocks the passage");
            var tracked=(Dictionary<Collider2D,WorldCollision.Entry>)typeof(WorldCollision).GetField("tracked",A).GetValue(world);
            uint floorId=tracked[floor].Id;int initialScans=scans;
            now=.01f;shapes[crusher].Offset=new Vector2(0,6);world.Tick(Vector2.zero,false);
            Check(!Blocked(),"Animated offset opens the passage immediately while its Transform stays fixed");
            shapes[crusher].Offset=new Vector2(0,2);world.Tick(Vector2.zero,false);Check(Blocked(),"Returning offset restores the lowered solid shape");
            shapes[crusher].Size=new Vector2(2,.1f);world.Tick(Vector2.zero,false);Check(!Blocked(),"Animated size alone clears the old wall");
            shapes[crusher].Size=new Vector2(2,4);world.Tick(Vector2.zero,false);Check(Blocked(),"Restored size blocks normally again");
            for(int cycle=0;cycle<30;cycle++)
            {
                shapes[crusher].Offset=new Vector2(0,6);world.Tick(Vector2.zero,false);Check(!Blocked(),"Repeated raised frames leave no stale wall");
                shapes[crusher].Offset=new Vector2(0,2);world.Tick(Vector2.zero,false);Check(Blocked(),"Repeated closing frames remain solid");
                Check(tracked.Count==2&&tracked[floor].Id==floorId,"Only the animated object is replaced; static floor and object count stay intact");
            }
            Check(scans==initialScans,"Shape refreshes do not force expensive full-room rescans");
            shapes[crusher].Position=new Vector3(8,0,0);world.Tick(Vector2.zero,false);
            Check(!Blocked()&&Blocked(6.8f),"Translation after shape rebuild moves the collision to its correct new position");
            shapes[crusher].Offset=new Vector2(0,6);world.Tick(Vector2.zero,false);Check(!Blocked(6.8f),"Offset updates also work after ordinary platform motion");
            shapes[crusher].Offset=new Vector2(0,2);world.Tick(Vector2.zero,false);shapes[crusher].Enabled=false;world.Tick(Vector2.zero,false);
            Check(!Blocked(6.8f),"Native disabled-collider handling still removes the obstacle");
            Console.WriteLine("PASS actual WorldCollision streaming and native wall queries: offset-only and size-only animation, thirty raise/lower cycles, fixed Transform, mixed translation, disabled shape, stable object count and no forced room scans. Only Unity scene endpoints substituted.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{world?.Dispose();hooks.UnpatchSelf();}
    }
}
