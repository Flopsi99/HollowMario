using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HollowMario
{
    // Last-resort penetration recovery. Normal movement never changes: this acts only
    // when two points inside Mario's body are strictly inside the joined terrain solid.
    // Recheck a recent clear position before restoring it; never guess the far side of a wall.
    internal static class TerrainRecovery
    {
        private const BindingFlags Any=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Vector3[] history=new Vector3[12];
        private static int count,next,lastId=-1,logs;
        private static bool enabled;
        private static FieldInfo state,previous,action,forward,vx,vy,vz;
        private static MethodInfo setAction,setVelocity,setForward;
        private static Forward nativeForward;
        private static object sync;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Forward(int id,float speed);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr module,string name);

        internal static void Arm(Type controller,Type native,bool active)
        {
            enabled=active;if(!active)return;
            state=controller.GetField("State",Any);previous=controller.GetField("PrevState",Any);
            Type type=state.FieldType;action=type.GetField("action",Any);forward=type.GetField("forwardVelocity",Any);
            vx=type.GetField("vx",Any);vy=type.GetField("vy",Any);vz=type.GetField("vz",Any);
            setAction=native.GetMethod("sm64_set_mario_action",Any);setVelocity=native.GetMethod("sm64_set_mario_velocity",Any);
            setForward=native.GetMethod("sm64_set_mario_forward_velocity",Any);sync=native.GetField("Sync",Any).GetValue(null);
            if(setForward==null)
            {
                IntPtr module=GetModuleHandle("sm64.dll");IntPtr entry=module==IntPtr.Zero?IntPtr.Zero:GetProcAddress(module,"sm64_set_mario_forward_velocity");
                if(entry!=IntPtr.Zero)nativeForward=(Forward)Marshal.GetDelegateForFunctionPointer(entry,typeof(Forward));
            }
            enabled=setAction!=null&&setVelocity!=null&&(setForward!=null||nativeForward!=null);
        }
        private static bool Clear(Vector3 p) => !TerrainConversion.ContainsSolid(p.x,p.y+.25)
            &&!TerrainConversion.ContainsSolid(p.x,p.y+.8)&&!TerrainConversion.ContainsSolid(p.x,p.y+1.5)
            &&!TerrainConversion.ContainsSolid(p.x-.3,p.y+.8)&&!TerrainConversion.ContainsSolid(p.x+.3,p.y+.8);

        internal static void Tick(object controller,KnightResetFollow follow)
        {
            if(!enabled||follow==null||!TerrainConversion.Ready||!follow.MarioAlive(controller)){Forget();return;}
            int id=follow.MarioId(controller);if(id!=lastId){Forget();lastId=id;}
            uint move=follow.Action(controller);
            if(follow.Busy(controller)||follow.ControlHeld(controller)||!follow.AcceptingInput(controller)
                ||Swimming.UsesSwimControls(move)||(move&0x1000u)!=0){count=next=0;return;}
            Vector3? point=follow.Ground(controller);if(!point.HasValue)return;Vector3 p=point.Value;
            if(Clear(p)){history[next]=p;next=(next+1)%history.Length;count=Math.Min(count+1,history.Length);return;}
            if(!TerrainConversion.ContainsSolid(p.x,p.y+.8)
                ||(!TerrainConversion.ContainsSolid(p.x,p.y+.25)&&!TerrainConversion.ContainsSolid(p.x,p.y+1.5)))return;
            for(int i=0;i<count;i++)
            {
                Vector3 safe=history[(next-1-i+history.Length)%history.Length];Vector3 step=safe-p;
                if(step.sqrMagnitude>9||!Clear(safe))continue;
                AutoMove.Prepare(controller);
                lock(sync)
                {
                    AutoMove.Shift(controller,step.x,step.y);
                    setAction.Invoke(null,new object[]{id,0x0100088Cu});
                    if(setForward!=null)setForward.Invoke(null,new object[]{id,0f});else nativeForward(id,0f);
                    setVelocity.Invoke(null,new object[]{id,0f,0f,0f});
                }
                object value=state.GetValue(controller);action.SetValue(value,0x0100088Cu);
                forward.SetValue(value,0f);vx.SetValue(value,0f);vy.SetValue(value,0f);vz.SetValue(value,0f);
                state.SetValue(controller,value);previous.SetValue(controller,value);
                Transform hero=follow.HeroTransform(controller);
                if(hero!=null){hero.position+=step;Rigidbody2D body=hero.GetComponent<Rigidbody2D>();if(body!=null){body.position=new Vector2(hero.position.x,hero.position.y);body.velocity=Vector2.zero;}}
                if(logs++<8)Plugin.Instance?.Log.LogInfo("Mario entered solid terrain; restored his most recent verified clear position.");
                return;
            }
        }
        internal static void Forget(){count=next=0;lastId=-1;}
    }
}
