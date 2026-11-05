using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HollowMario
{
    // A private presentation actor, never the gameplay controller. Its tiny native
    // stage is away from the gameplay lane and has no Unity collider. The Knight
    // remains the sole owner of the scripted sequence's motion and contacts.
    internal sealed class OpeningMarioPose:IDisposable
    {
        internal const uint SpinFall=0x00001924u, TripleLanding=0x0800023Au;
        private const float StageX=-6000, StageZ=6000, FloorY=-6000, AirY=-5000;
        private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        [StructLayout(LayoutKind.Sequential)]private struct Surface
        {public short type,force;public ushort terrain;public int ax,ay,az,bx,by,bz,cx,cy,cz;}
        [StructLayout(LayoutKind.Sequential)]private struct Transform
        {public float x,y,z,rx,ry,rz;}
        [StructLayout(LayoutKind.Sequential)]private struct SurfaceObject
        {public Transform transform;public uint count;public IntPtr surfaces;}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint CreateFloor(ref SurfaceObject value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void DeleteFloor(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void SetFrame(int id,short frame);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void SetFlags(int id,uint flags);
        [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string path);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
        private readonly object sync;
        private readonly MethodInfo create,delete,tick,position,velocity,action,faceAngle;
        private readonly DeleteFloor deleteFloor;
        private readonly SetFrame setFrame;
        private readonly SetFlags setFlags;
        private readonly object[] tickArgs;
        private readonly uint floor;
        private bool hasFloor,landed,talking,sleeping;
        private bool capHidden;
        private uint originalCapFlags;
        private int id=-1;
        internal object State {get{return tickArgs[2];}}
        internal OpeningMarioPose(Type native,string library,float face,bool resting=false)
        {
            sync=native.GetField("Sync",Any).GetValue(null);
            create=native.GetMethod("sm64_mario_create",Any);delete=native.GetMethod("sm64_mario_delete",Any);
            tick=native.GetMethod("sm64_mario_tick",Any);position=native.GetMethod("sm64_set_mario_position",Any);
            velocity=native.GetMethod("sm64_set_mario_velocity",Any);action=native.GetMethod("sm64_set_mario_action",Any);
            faceAngle=native.GetMethod("sm64_set_mario_faceangle",Any);
            Type inputs=tick.GetParameters()[1].ParameterType.GetElementType();
            object input=Activator.CreateInstance(inputs);inputs.GetField("camLookZ").SetValue(input,-1f);
            tickArgs=new object[]{-1,input,Activator.CreateInstance(tick.GetParameters()[2].ParameterType.GetElementType()),null};
            IntPtr module=GetModuleHandle(library);
            IntPtr make=GetProcAddress(module,"sm64_surface_object_create"),remove=GetProcAddress(module,"sm64_surface_object_delete");
            if(make==IntPtr.Zero||remove==IntPtr.Zero)throw new MissingMethodException("Opening animation stage exports");
            var createFloor=(CreateFloor)Marshal.GetDelegateForFunctionPointer(make,typeof(CreateFloor));
            deleteFloor=(DeleteFloor)Marshal.GetDelegateForFunctionPointer(remove,typeof(DeleteFloor));
            IntPtr frame=GetProcAddress(module,"sm64_set_mario_anim_frame");
            if(frame!=IntPtr.Zero)setFrame=(SetFrame)Marshal.GetDelegateForFunctionPointer(frame,typeof(SetFrame));
            IntPtr capFlags=GetProcAddress(module,"sm64_set_mario_state");
            if(capFlags!=IntPtr.Zero)setFlags=(SetFlags)Marshal.GetDelegateForFunctionPointer(capFlags,typeof(SetFlags));
            var surfaces=new[]{
                new Surface{type=0,terrain=1,ax=-200,az=-200,bx=-200,bz=200,cx=200,cz=200},
                new Surface{type=0,terrain=1,ax=-200,az=-200,bx=200,bz=200,cx=200,cz=-200}};
            lock(sync)
            {
                GCHandle pin=GCHandle.Alloc(surfaces,GCHandleType.Pinned);
                try
                {
                    var stage=new SurfaceObject{transform=new Transform{x=StageX,y=FloorY,z=StageZ},count=2,surfaces=pin.AddrOfPinnedObject()};
                    floor=createFloor(ref stage);hasFloor=floor!=uint.MaxValue;
                }
                finally{pin.Free();}
                try
                {
                    if(!hasFloor)throw new InvalidOperationException("Opening animation stage unavailable");
                    id=Convert.ToInt32(create.Invoke(null,new object[]{StageX,AirY,StageZ}));
                    if(id<0)throw new InvalidOperationException("Opening animation actor unavailable");
                    tickArgs[0]=id;
                    native.GetMethod("sm64_set_mario_water_level",Any).Invoke(null,new object[]{id,-1000000});
                    native.GetMethod("sm64_set_mario_gas_level",Any).Invoke(null,new object[]{id,-1000000});
                    native.GetMethod("sm64_set_mario_faceangle",Any).Invoke(null,new object[]{id,face});
                    action.Invoke(null,new object[]{id,resting?0x0C000203u:SpinFall});
                    if(resting)
                    {
                        landed=true;
                        position.Invoke(null,new object[]{id,StageX,FloorY,StageZ});
                        velocity.Invoke(null,new object[]{id,0f,0f,0f});
                    }
                }
                catch{Dispose();throw;}
            }
        }
        internal void Wake()
        {lock(sync)action.Invoke(null,new object[]{id,0x0C000204u});}
        internal void SetAnimationFrame(short frame)
        {lock(sync){if(setFrame==null)throw new MissingMethodException("Mario animation frame control");setFrame(id,frame);}}
        internal bool SetCapVisible(bool visible)
        {
            lock(sync)
            {
                if(visible==!capHidden)return true;
                uint flags=visible?originalCapFlags:Convert.ToUInt32(State.GetType().GetField("flags").GetValue(State));
                if(setFlags==null||id<0)return false;
                setFlags(id,visible?flags:flags&~(16u|32u));
                if(!visible)originalCapFlags=flags;
                capHidden=!visible;return true;
            }
        }
        internal void LimitAnimationFrame(int animation,int frame)
        {
            if(frame>=0&&Convert.ToInt32(State.GetType().GetField("animID").GetValue(State))==animation
                &&Convert.ToInt32(State.GetType().GetField("animFrame").GetValue(State))>=frame)
                SetAnimationFrame((short)(frame-1)); // Native geometry advances one frame during tick.
        }
        internal void TalkFacing(float angle,bool asleep=false)
        {
            lock(sync)
            {
                if(!talking||sleeping!=asleep)
                {action.Invoke(null,new object[]{id,asleep?0x0C000203u:0x0000130Au});talking=true;sleeping=asleep;}
                faceAngle.Invoke(null,new object[]{id,angle});
            }
        }
        internal object Tick(object buffers,bool onGround)
        {
            lock(sync)
            {
                if(onGround&&!landed)
                {
                    landed=true;
                    position.Invoke(null,new object[]{id,StageX,FloorY,StageZ});
                    velocity.Invoke(null,new object[]{id,0f,0f,0f});
                    action.Invoke(null,new object[]{id,TripleLanding});
                }
                if(!landed)
                {
                    // Let the spin advance normally, while the real cinematic
                    // decides when contact happens. No independent fall timer.
                    position.Invoke(null,new object[]{id,StageX,AirY,StageZ});
                    velocity.Invoke(null,new object[]{id,0f,0f,0f});
                }
                tickArgs[3]=buffers;tick.Invoke(null,tickArgs);return tickArgs[3];
            }
        }
        public void Dispose()
        {
            lock(sync)
            {
                try
                {
                    if(id>=0)
                    {
                        // A parked actor may become the playable Mario next.
                        // Hatlessness belongs only to this death presentation.
                        if(capHidden)SetCapVisible(true);
                        // The shared lifecycle parks actors instead of freeing
                        // them for audio safety. Detach from our private platform
                        // while it is still alive before returning that actor.
                        if(tickArgs[3]!=null)
                        {
                            position.Invoke(null,new object[]{id,StageX,AirY,StageZ});
                            velocity.Invoke(null,new object[]{id,0f,0f,0f});
                            action.Invoke(null,new object[]{id,SpinFall});tick.Invoke(null,tickArgs);
                        }
                        delete.Invoke(null,new object[]{id});
                    }
                }
                finally{id=-1;if(hasFloor){hasFloor=false;deleteFloor(floor);}}
            }
        }
    }
}
