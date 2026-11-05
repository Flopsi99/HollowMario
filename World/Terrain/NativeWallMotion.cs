using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HollowMario
{
    // Translate an existing native surface; the engine decides contact and correction.
    internal static class NativeWallMotion
    {
        private const BindingFlags Any=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const float K=.0105f;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Wall(ref float x,ref float y,ref float z,float height,float radius);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
        private static MethodInfo move;
        private static Type pose;
        private static FieldInfo tx,ty,tz,state,px,py,pz;
        private static Wall resolve;
        private static object sync;
        internal static bool Configure(Type controller,Type native)
        {
            move=native.GetMethod("sm64_surface_object_move",Any);if(move==null)return false;
            pose=move.GetParameters()[1].ParameterType;if(pose.IsByRef)pose=pose.GetElementType();
            tx=pose.GetField("px");ty=pose.GetField("py");tz=pose.GetField("pz");
            state=controller.GetField("State",Any);px=state?.FieldType.GetField("px");py=state?.FieldType.GetField("py");pz=state?.FieldType.GetField("pz");
            sync=native.GetField("Sync",Any)?.GetValue(null)??new object();
            MethodInfo probe=native.GetMethod("sm64_surface_find_wall_collision",Any);
            if(probe!=null)resolve=(Wall)Delegate.CreateDelegate(typeof(Wall),probe);
            else
            {
                IntPtr module=GetModuleHandle("sm64.dll"),entry=module==IntPtr.Zero?IntPtr.Zero:GetProcAddress(module,"sm64_surface_find_wall_collision");
                if(entry!=IntPtr.Zero)resolve=(Wall)Marshal.GetDelegateForFunctionPointer(entry,typeof(Wall));
            }
            return tx!=null&&ty!=null&&tz!=null&&px!=null&&py!=null&&pz!=null&&resolve!=null;
        }
        internal static int Move(object controller,uint id,Vector3 basis,Vector3 before,Vector3 now,bool contact,bool grounded)
        {
            // Quarter steps subdivide Mario's motion, not a wall's. Slice nearby wall motion
            // below the native collision radius, preventing its face stepping behind him.
            int steps=contact?Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(now.x-before.x),Math.Abs(now.y-before.y))/(K*25f))):1;
            object transform=Activator.CreateInstance(pose);object[] args={id,transform};int hits=0;
            lock(sync)
            {
                for(int i=1;i<=steps;i++)
                {
                    float t=(float)i/steps;
                    tx.SetValue(transform,(before.x+(now.x-before.x)*t-basis.x)/K);
                    ty.SetValue(transform,(before.y+(now.y-before.y)*t-basis.y)/K);
                    tz.SetValue(transform,0f);args[1]=transform;move.Invoke(null,args);
                    if(!contact)continue;
                    object value=state.GetValue(controller);
                    float x=Convert.ToSingle(px.GetValue(value)),y=Convert.ToSingle(py.GetValue(value)),z=Convert.ToSingle(pz.GetValue(value));float oldX=x;
                    hits+=resolve(ref x,ref y,ref z,30f,grounded?24f:50f);
                    hits+=resolve(ref x,ref y,ref z,grounded?60f:150f,50f);
                    if(Math.Abs(x-oldX)>.0001f){AutoMove.Prepare(controller);AutoMove.Shift(controller,(x-oldX)*K,0);}
                }
            }
            return hits;
        }
        internal static bool Near(Bounds before,Bounds now,Vector3 feet)
            =>feet.x+.525f>=Mathf.Min(before.min.x,now.min.x)&&feet.x-.525f<=Mathf.Max(before.max.x,now.max.x)
                &&feet.y+1.575f>=Mathf.Min(before.min.y,now.min.y)&&feet.y<=Mathf.Max(before.max.y,now.max.y);
    }
}
