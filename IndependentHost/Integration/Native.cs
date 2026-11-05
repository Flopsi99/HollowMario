using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HollowMario.Host;

// These legacy type/member names are the reflection contract used by OUR
// companion. This assembly is new source, not the original MarioHollow.dll.
namespace MarioHollow
{
    [StructLayout(LayoutKind.Sequential)]public struct SM64Surface
    {public short type,force;public ushort terrain;public int v0x,v0y,v0z,v1x,v1y,v1z,v2x,v2y,v2z;}
    [StructLayout(LayoutKind.Sequential)]public struct SM64MarioInputs
    {public float camLookX,camLookZ,stickX,stickY;public byte buttonA,buttonB,buttonZ;}
    [StructLayout(LayoutKind.Sequential)]public struct SM64MarioState
    {public float px,py,pz,vx,vy,vz,faceAngle,forwardVelocity;public short health;public uint action;public int animID;public short animFrame;public uint flags,particleFlags;public short invincTimer;}
    [StructLayout(LayoutKind.Sequential)]public struct SM64MarioGeometryBuffers
    {public IntPtr position,normal,color,uv;public ushort numTrianglesUsed;}
    [StructLayout(LayoutKind.Sequential)]public struct SM64ObjectTransform
    {public float px,py,pz,rx,ry,rz;}
    [StructLayout(LayoutKind.Sequential)]public struct SM64SurfaceObject
    {public SM64ObjectTransform transform;public uint surfaceCount;public IntPtr surfaces;}

    public static class Native
    {
        public static object Sync=new object();
        private const ushort FullHealth=0x880;
        internal static Engine Engine;
        private static readonly HashSet<int> live=new HashSet<int>();
        private static readonly Dictionary<string,Delegate> exports=new Dictionary<string,Delegate>();
        private static readonly Stack<int> parked=new Stack<int>();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Tick(int id,ref SM64MarioInputs input,ref SM64MarioState state,ref SM64MarioGeometryBuffers buffers);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint CreateObject(ref SM64SurfaceObject value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void MoveObject(uint id,ref SM64ObjectTransform value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void DeleteObject(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Scalar(int id,float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void ActionArg(int id,uint action,uint arg);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Walls(ref float x,ref float y,ref float z,float offset,float radius);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate float Floor(float x,float y,float z,out IntPtr surface);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Cap(int id,uint flags,ushort time,byte music);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Damage(int id,uint damage,uint subtype,float x,float y,float z);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Frame(int id,short value);
        private static T Export<T>(string symbol)where T:class
        {
            if(!exports.TryGetValue(symbol,out Delegate value)){value=(Delegate)(object)Engine.Api.Bind<T>(symbol);exports.Add(symbol,value);}
            return (T)(object)value;
        }
        internal static void Start(string library,string rom)
        {
            if(Engine!=null)throw new InvalidOperationException("Independent engine already running.");
            Engine=new Engine(library,rom);Sync=Engine.Gate;
            // Resolve every required entry before starting a producer thread.
            lock(Sync)
            {
                Export<Tick>("sm64_mario_tick");Export<CreateObject>("sm64_surface_object_create");
                Export<MoveObject>("sm64_surface_object_move");Export<DeleteObject>("sm64_surface_object_delete");
            }
        }
        internal static void Stop()
        {
            lock(Sync){if(Engine==null)return;foreach(int id in live)Engine.Api.DeleteActor(id);live.Clear();parked.Clear();Engine.Dispose();Engine=null;exports.Clear();}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public static int sm64_mario_create(float x,float y,float z)
        {
            lock(Sync)
            {
                int id=parked.Count>0?parked.Pop():Engine.Api.CreateActor(x,y,z);
                if(id<0)return id;live.Add(id);Engine.Api.Position(id,x,y,z);Engine.Api.Velocity(id,0,0,0);
                Engine.Api.SetAction(id,0x0C400201);Engine.Api.SetHealth(id,2176);Engine.Api.Water(id,-1000000);Engine.Api.Gas(id,-1000000);
                Export<Frame>("sm64_set_mario_invincibility")(id,0);return id;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public static void sm64_mario_delete(int marioId)
        {
            lock(Sync){if(marioId<0||!live.Contains(marioId)||parked.Contains(marioId))return;Engine.Api.Velocity(marioId,0,0,0);Engine.Api.Position(marioId,0,-1000000,0);parked.Push(marioId);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public static void sm64_mario_tick(int marioId,ref SM64MarioInputs inputs,ref SM64MarioState state,ref SM64MarioGeometryBuffers buffers)
        {
            lock(Sync)
            {
                // Hollow Knight owns life/death. Refresh before native death
                // checks and clear any new fall/drowning damage afterward.
                // This setter clears health counters, not actions or invincibility.
                Engine.Api.SetHealth(marioId,FullHealth);
                // Upstream side flip intentionally draws with a half-turn gfx
                // offset. Pound startup does not perform an air step to clear
                // that offset, so carry its actual facing into the new pose.
                if(state.action==0x01000887&&inputs.buttonZ!=0)
                    Export<Scalar>("sm64_set_mario_faceangle")(marioId,state.faceAngle);
                Export<Tick>("sm64_mario_tick")(marioId,ref inputs,ref state,ref buffers);
                Engine.Api.SetHealth(marioId,FullHealth);state.health=(short)FullHealth;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public static uint sm64_audio_tick(uint queued,uint wanted,IntPtr samples)
        {lock(Sync)return Engine.Api.PullAudio(queued,wanted,samples);}
        public static void sm64_set_mario_position(int id,float x,float y,float z){lock(Sync)Engine.Api.Position(id,x,y,z);}
        public static void sm64_set_mario_velocity(int id,float x,float y,float z){lock(Sync)Engine.Api.Velocity(id,x,y,z);}
        public static void sm64_set_mario_action(int id,uint action)
        {
            if(MarioController.Instance!=null&&MarioController.Instance.MarioId==id&&MarioController.Instance.State.action!=action)
            {
                var caller=new System.Diagnostics.StackTrace(1,false).GetFrame(0)?.GetMethod();
                HostTrace.Write("explicit action request "+action.ToString("X")+" from "+caller?.DeclaringType?.Name+"."+caller?.Name+"; "+MarioController.Instance.Diagnostics);
            }
            lock(Sync)Engine.Api.SetAction(id,action);
        }
        public static void sm64_set_mario_health(int id,ushort health){lock(Sync)Engine.Api.SetHealth(id,health);}
        public static void sm64_set_mario_water_level(int id,int level){lock(Sync)Engine.Api.Water(id,level);}
        public static void sm64_set_mario_gas_level(int id,int level){lock(Sync)Engine.Api.Gas(id,level);}
        public static void sm64_play_sound_global(uint sound){lock(Sync)Engine.Api.PlaySound(sound);}
        public static void sm64_set_mario_faceangle(int id,float angle){lock(Sync)Export<Scalar>("sm64_set_mario_faceangle")(id,angle);}
        public static void sm64_set_mario_forward_velocity(int id,float speed){lock(Sync)Export<Scalar>("sm64_set_mario_forward_velocity")(id,speed);}
        public static void sm64_set_mario_action_arg(int id,uint action,uint arg){lock(Sync)Export<ActionArg>("sm64_set_mario_action_arg")(id,action,arg);}
        public static void sm64_set_mario_anim_frame(int id,short frame){lock(Sync)Export<Frame>("sm64_set_mario_anim_frame")(id,frame);}
        public static void sm64_set_mario_state(int id,uint flags){lock(Sync)Export<NativeApi.Action>("sm64_set_mario_state")(id,flags);}
        public static void sm64_mario_interact_cap(int id,uint flag,ushort time,byte music){lock(Sync)Export<Cap>("sm64_mario_interact_cap")(id,flag,time,music);}
        public static void sm64_mario_take_damage(int id,uint damage,uint subtype,float x,float y,float z){lock(Sync)Export<Damage>("sm64_mario_take_damage")(id,damage,subtype,x,y,z);}
        public static int sm64_surface_find_wall_collision(ref float x,ref float y,ref float z,float offsetY,float radius)
        {lock(Sync)return Export<Walls>("sm64_surface_find_wall_collision")(ref x,ref y,ref z,offsetY,radius);}
        public static float sm64_surface_find_floor(float x,float y,float z,out IntPtr surface)
        {lock(Sync)return Export<Floor>("sm64_surface_find_floor")(x,y,z,out surface);}
        public static uint sm64_surface_object_create(ref SM64SurfaceObject value){lock(Sync)return Export<CreateObject>("sm64_surface_object_create")(ref value);}
        public static void sm64_surface_object_move(uint id,ref SM64ObjectTransform value){lock(Sync)Export<MoveObject>("sm64_surface_object_move")(id,ref value);}
        public static void sm64_surface_object_delete(uint id){lock(Sync)Export<DeleteObject>("sm64_surface_object_delete")(id);}
    }
}
