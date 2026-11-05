using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // A visual substitution only. Keep the engine's action, velocity, controls, collision,
    // damage and landing handling untouched; never restore the old handover bug/state.
    internal static class MapFallFlourish
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate short Animation(IntPtr mario,int animation);
        private static readonly Animation callback=ChooseAnimation;
        private static Animation original;
        private static NativeDetour hook;
        private static string library;
        private static object sync;
        private static ManualLogSource log;
        private static int activeId=-1;
        private static bool attempted;
        [ThreadStatic]private static bool current;
        [ThreadStatic]private static bool forced;
        [ThreadStatic]private static int forcedAnimation;
        private static Type stateType;
        private static FieldInfo action;
        [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
        internal static bool Armed=>hook!=null;
        internal static void Configure(Type native,ManualLogSource logger)
        {
            library=Path.Combine(Path.GetDirectoryName(native.Assembly.Location),"sm64.dll");
            sync=native.GetField("Sync",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null)??new object();
            log=logger;
        }
        internal static void Begin(int id){activeId=id;}
        internal static void Forget(){activeId=-1;}
        internal static void BeforeTick(int __0,out bool __state)
        {
            __state=current;current=false;
            if(__0!=activeId)return;
            if(!Armed&&!attempted)
            {
                IntPtr module=GetModuleHandle(library);
                if(module!=IntPtr.Zero)
                {
                    attempted=true;
                    try{lock(sync)Install(library,module);}
                    catch(Exception error){log?.LogWarning("Map-return falling flourish unavailable: "+error.GetBaseException().Message);}
                }
            }
            current=Armed;
        }
        // Read output by value: never write an old ref-state/geometry snapshot back.
        internal static void AfterTick(int __0,object __2)
        {
            if(__0!=activeId||__2==null)return;
            if(stateType!=__2.GetType()){stateType=__2.GetType();action=stateType.GetField("action");}
            uint move=Convert.ToUInt32(action.GetValue(__2));
            if(move!=0x0100088Cu&&move!=0x00001924u)Forget(); // Freefall or existing airborne return.
        }
        internal static void Finish(bool __state){current=__state;}
        private static short ChooseAnimation(IntPtr mario,int animation)
            =>original(mario,forced?forcedAnimation:current&&animation==0x56?0x6F:animation); // General fall -> forward spinning.
        // Reuse the existing animation hook only within an owned cinematic tick.
        // Gameplay actors and the map-return flourish retain their own animations.
        internal static int PushCinematicAnimation(int animation)
        {
            if(!Armed)
            {
                IntPtr module=GetModuleHandle(library);
                if(module==IntPtr.Zero)throw new InvalidOperationException("Mario's animation library is not loaded.");
                lock(sync)Install(library,module);
            }
            int previous=forced?forcedAnimation:-1;forcedAnimation=animation;forced=true;return previous;
        }
        internal static void PopCinematicAnimation(int previous)
        {forced=previous>=0;forcedAnimation=previous;}
        internal static void Install(string path,IntPtr module)
        {
            if(Armed)return;
            IntPtr target=NativeSymbols.Functions(path,module,"set_mario_animation")["set_mario_animation"];
            try
            {
                hook=new NativeDetour(target,callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<Animation>();hook.Apply();
            }
            catch{Dispose();throw;}
        }
        internal static void Dispose()
        {lock(sync??typeof(MapFallFlourish)){hook?.Dispose();hook=null;Forget();}}
    }
}
