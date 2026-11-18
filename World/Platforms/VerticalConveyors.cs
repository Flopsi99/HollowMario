using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // A wall-only exception. Native air steps still resolve every movement;
    // the belt changes velocity, never teleports Mario or changes his inputs.
    internal static class VerticalConveyors
    {
        private const float Scale=.0105f;
        private const uint Freefall=0x0100088Cu,WallKick=0x03000886u,AirHitWall=0x000008A7u;
        private static readonly RaycastHit2D[] hits=new RaycastHit2D[24];
        [StructLayout(LayoutKind.Sequential)]private struct Point{internal float X,Y,Z;}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate IntPtr WallProbe(ref Point point,float height,float radius);
        private static WallProbe probe;
        private static FieldInfo originField;
        private static Type beltType,zoneType;
        [ThreadStatic]private static bool enabled;
        [ThreadStatic]private static int actor;
        [ThreadStatic]private static IntPtr contact;
        [ThreadStatic]private static Vector2 origin;
        internal static bool Pending=>enabled&&contact!=IntPtr.Zero;
        internal struct Saved{internal uint Action,Previous;internal byte Timer;internal IntPtr Wall;}

        internal static void Arm(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("ConveyorMovementHero"),"LateUpdate"),
                prefix:new HarmonyMethod(typeof(VerticalConveyors),nameof(NativeCarry)));
        }
        private static bool NativeCarry(object __instance)
        {
            var follow=Plugin.Instance?.knightFollow;var controller=follow?.Controller();
            return controller==null||follow.MarioId(controller)<0
                ||!ReferenceEquals(follow.Hero(controller),CapAccess.Read(__instance,"heroCon"));
        }

        internal static void Configure(Type native)
        {
            originField=AccessTools.Field(native.Assembly.GetType("MarioHollow.MarioMath",false),"Origin");
            beltType=AccessTools.TypeByName("ConveyorBelt");zoneType=AccessTools.TypeByName("ConveyorZone");
        }
        internal static void Install(string path,IntPtr module)
        {
            var address=NativeSymbols.Functions(path,module,"resolve_and_return_wall_collisions")["resolve_and_return_wall_collisions"];
            probe=(WallProbe)Marshal.GetDelegateForFunctionPointer(address,typeof(WallProbe));
        }
        internal static void BeginTick(bool allow,int id)
        {
            if(!allow||actor!=id){Forget();actor=id;}
            enabled=allow&&probe!=null&&originField!=null;
            if(!enabled)return;
            origin=(Vector2)originField.GetValue(null);
            if(contact!=IntPtr.Zero&&!FindContact(contact,out _,out _))contact=IntPtr.Zero;
        }
        private static bool Controllable(uint action)=>(action&0x800u)!=0&&(action&(0x20000u|0x1000u))==0;
        private static unsafe bool FindContact(IntPtr mario,out float speed,out IntPtr wall)
        {
            speed=0;wall=IntPtr.Zero;
            if(!enabled||probe==null||!Controllable(*(uint*)((byte*)mario+0xC)))return false;
            // Initial attachment requires a real native wall hit. The tiny probe
            // tolerance subsequently keeps exact resting contact from flickering.
            if(contact!=mario&&Marshal.ReadIntPtr(mario,0x60)==IntPtr.Zero)return false;
            float x=*(float*)((byte*)mario+0x3C),y=*(float*)((byte*)mario+0x40),z=*(float*)((byte*)mario+0x44);
            var point=new Point{X=x,Y=y,Z=z};wall=probe(ref point,60f,52f);
            float correction=point.X-x;
            if(wall==IntPtr.Zero||Mathf.Abs(correction)<.001f)return false;
            int side=correction<0?1:-1;
            // Jumping or steering away ends attachment immediately.
            if(*(float*)((byte*)mario+0x48)*side<-.1f)return false;
            return FindBelt(new Vector2(origin.x+x*Scale,origin.y+(y+60f)*Scale),side,out speed);
        }
        private static bool FindBelt(Vector2 point,int side,out float speed)
        {
            speed=0;Collider2D solid=null,zone=null;float nearest=float.PositiveInfinity,zoneDistance=float.PositiveInfinity;
            int count=Physics2D.RaycastNonAlloc(point,new Vector2(side,0),hits,54f*Scale);
            for(int i=0;i<count;i++)
            {
                var hit=hits[i];var shape=hit.collider;
                if(shape==null||!shape.enabled||!shape.gameObject.activeInHierarchy||shape.GetComponentInParent<HeroController>()!=null)continue;
                if(!shape.isTrigger&&hit.distance<nearest){solid=shape;nearest=hit.distance;}
                if(shape.isTrigger&&hit.distance<zoneDistance&&ReadBelt(shape,out _)){zone=shape;zoneDistance=hit.distance;}
            }
            // An ordinary wall in front of a belt must not become a conveyor.
            if(solid==null)return false;
            if(ReadBelt(solid,out speed))return true;
            return zone!=null&&zoneDistance<=nearest+.03f&&ReadBelt(zone,out speed);
        }
        private static bool ReadBelt(Collider2D shape,out float speed)
        {
            speed=0;
            for(Transform t=shape.transform;t!=null;t=t.parent)
                for(int kind=0;kind<2;kind++)
                {
                    Type type=kind==0?beltType:zoneType;if(type==null)continue;
                    var script=t.GetComponent(type) as Behaviour;
                    if(script==null||!script.isActiveAndEnabled)continue;
                    if(!CapAccess.Flag(script,"vertical"))continue;
                    if(kind==1&&!CapAccess.Flag(script,"activated"))continue;
                    speed=CapAccess.Number(script,"speed");
                    if(!float.IsNaN(speed)&&!float.IsInfinity(speed)&&Mathf.Abs(speed)>.0001f)return true;
                }
            return false;
        }
        internal static unsafe void BeforeAirStep(IntPtr mario)
        {
            if(contact==mario&&FindContact(mario,out float speed,out _))
                *(float*)((byte*)mario+0x4C)=speed/(30f*Scale);
        }
        internal static unsafe bool AfterAirStep(IntPtr mario,int result,uint source)
        {
            if((result!=0&&result!=2)||!Controllable(source)||!FindContact(mario,out float speed,out _))
            {contact=IntPtr.Zero;return false;}
            contact=mario;
            *(float*)((byte*)mario+0x4C)=speed/(30f*Scale);
            *(float*)((byte*)mario+0x54)=0;*(float*)((byte*)mario+0x48)=0;*(float*)((byte*)mario+0x50)=0;
            return true;
        }
        internal static bool PrepareKick(IntPtr mario,out Saved saved)
        {
            saved=default;
            if(!Pending||contact!=mario||(Marshal.ReadInt16(mario,2)&2)==0||!FindContact(mario,out _,out IntPtr wall))return false;
            saved=new Saved{Action=unchecked((uint)Marshal.ReadInt32(mario,0xC)),Previous=unchecked((uint)Marshal.ReadInt32(mario,0x10)),
                Timer=Marshal.ReadByte(mario,0x2A),Wall=Marshal.ReadIntPtr(mario,0x60)};
            Marshal.WriteInt32(mario,0x10,unchecked((int)AirHitWall));Marshal.WriteByte(mario,0x2A,1);Marshal.WriteIntPtr(mario,0x60,wall);
            return true;
        }
        internal static void RestoreKick(IntPtr mario,Saved saved)
        {
            Marshal.WriteByte(mario,0x2A,saved.Timer);
            if(unchecked((uint)Marshal.ReadInt32(mario,0xC))==saved.Action)
            {Marshal.WriteInt32(mario,0x10,unchecked((int)saved.Previous));Marshal.WriteIntPtr(mario,0x60,saved.Wall);}
        }
        internal static void ActionInitialized(IntPtr mario,uint action)
        {if(contact==mario&&(action==WallKick||!Controllable(action)))contact=IntPtr.Zero;}
        internal static void CancelRecovery(){contact=IntPtr.Zero;}
        internal static void Forget(){contact=IntPtr.Zero;enabled=false;}
        internal static void Dispose(){Forget();probe=null;}
    }
}
