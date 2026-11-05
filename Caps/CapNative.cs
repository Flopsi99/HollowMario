using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HollowMario
{
    internal static class CapNative
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const uint Special=2|4|8, Normal=1, OnHead=16, InHand=32;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SetFlags(int id,uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Wear(int id,uint flag,ushort time,byte music);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetActionArg(int id,uint action,uint argument);
        [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32",CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        private static SetFlags set;
        private static Wear wear;
        private static SetActionArg actionArg;
        private static object sync;
        private static FieldInfo state,flags;
        internal static bool Ready => set!=null&&wear!=null&&flags!=null;
        internal static bool LavaReady=>actionArg!=null;

        internal static void Configure(Type native,Type controller)
        {
            state=controller.GetField("State",Any);flags=state?.FieldType.GetField("flags",Any);
            sync=native.GetField("Sync",Any)?.GetValue(null)??new object();
            IntPtr module=GetModuleHandle(Path.Combine(Path.GetDirectoryName(native.Assembly.Location),"sm64.dll"));
            Bind(module);
        }

        internal static void Bind(IntPtr module)
        {
            IntPtr a=module==IntPtr.Zero?IntPtr.Zero:GetProcAddress(module,"sm64_set_mario_state");
            IntPtr b=module==IntPtr.Zero?IntPtr.Zero:GetProcAddress(module,"sm64_mario_interact_cap");
            if(a==IntPtr.Zero||b==IntPtr.Zero) throw new MissingMethodException("Native cap exports were not found.");
            set=(SetFlags)Marshal.GetDelegateForFunctionPointer(a,typeof(SetFlags));
            wear=(Wear)Marshal.GetDelegateForFunctionPointer(b,typeof(Wear));
            IntPtr c=GetProcAddress(module,"sm64_set_mario_action_arg");
            actionArg=c==IntPtr.Zero?null:(SetActionArg)Marshal.GetDelegateForFunctionPointer(c,typeof(SetActionArg));
        }

        internal static void LavaBounce(int id)
        {
            // Native lava launch: vertical speed84, actionArg1 retains forward speed.
            // No native health spend; the cap session owns Hollow Knight's soul cost.
            lock(sync)actionArg(id,0x010208B7u,1u);
        }

        internal static void Apply(object controller,int id,CapDefinition cap)
        {
            lock(sync)
            {
                uint clean=Read(controller)&~Special;
                set(id,clean);
                wear(id,cap.NativeFlag,ushort.MaxValue,0); // Original cap animation/sounds, preserve HK music.
                Write(controller,(clean&~InHand)|cap.NativeFlag|OnHead);
            }
        }

        internal static void Clear(object controller,int id)
        {
            if(!Ready||id<0||controller==null) return;
            lock(sync)
            {
                uint clean=(Read(controller)&~(Special|InHand))|Normal|OnHead;
                set(id,clean);Write(controller,clean);
            }
        }

        private static uint Read(object controller)=>Convert.ToUInt32(flags.GetValue(state.GetValue(controller)));
        private static void Write(object controller,uint value)
        {
            object snapshot=state.GetValue(controller);flags.SetValue(snapshot,value);state.SetValue(controller,snapshot);
        }
    }
}
