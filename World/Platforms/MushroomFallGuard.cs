using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // A one-flight exemption for the native fall-damage decision, not general damage immunity.
    // Skip before hurtCounter, squishTimer and hurt actions are changed. No MarioState offsets,
    // health refunds, gravity changes or suppression of the game's hazard/damage entry points.
    internal static class MushroomFallGuard
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FallCheck(IntPtr mario,uint hardFallAction);
        private static readonly FallCheck callback=CheckFall;
        private static FallCheck original;
        private static NativeDetour hook;
        private static string library;
        private static object sync;
        private static ManualLogSource log;
        private static int protectedId=-1;
        private static bool attempted;
        [ThreadStatic]private static bool current;
        private static Type stateType;
        private static FieldInfo action;
        internal static bool Armed=>hook!=null;

        [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
        internal static void Configure(Type native,ManualLogSource logger)
        {
            library=Path.Combine(Path.GetDirectoryName(native.Assembly.Location),"sm64.dll");
            sync=native.GetField("Sync",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null)??new object();
            log=logger;
        }
        internal static void Grant(int marioId){protectedId=marioId;}
        internal static void Forget(){protectedId=-1;}
        internal static void Wanted(bool wanted){if(!wanted)Forget();}
        internal static void BeforeTick(int __0,out bool __state)
        {
            __state=current;current=false;
            if(!Armed&&!attempted)
            {
                IntPtr module=GetModuleHandle(library);
                if(module!=IntPtr.Zero)
                {
                    attempted=true;
                    try
                    {
                        lock(sync)Install(library,module);
                        log?.LogInfo("Mushroom launches now protect their landing from Mario's native fall damage and squashing.");
                    }
                    catch(Exception error){log?.LogWarning("Mushroom landing protection could not be armed: "+error.GetBaseException().Message);}
                }
            }
            current=Armed&&__0==protectedId;
        }
        // Read output by value. An __args postfix would write stale ref state/geometry back
        // under the installed HarmonyX. Protection remains live throughout the landing tick.
        internal static void AfterTick(int __0,object __2)
        {
            if(__0!=protectedId||__2==null)return;
            if(stateType!=__2.GetType()){stateType=__2.GetType();action=stateType.GetField("action");}
            uint move=Convert.ToUInt32(action.GetValue(__2));
            if((move&0x800u)==0||(move&0x6000u)!=0)Forget(); // Solid landing or swimming.
        }
        internal static void Finish(bool __state){current=__state;}
        private static int CheckFall(IntPtr mario,uint hardFallAction)=>current?0:original(mario,hardFallAction);
        internal static void Install(string path,IntPtr module)
        {
            if(Armed)return;
            IntPtr target=NativeSymbols.Functions(path,module,"check_fall_damage")["check_fall_damage"];
            try
            {
                hook=new NativeDetour(target,callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<FallCheck>();hook.Apply();
            }
            catch{Dispose();throw;}
        }
        internal static void Dispose()
        {
            lock(sync??typeof(MushroomFallGuard)){hook?.Dispose();hook=null;Forget();}
        }
    }
}
