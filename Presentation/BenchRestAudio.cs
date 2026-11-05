using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // Keep the seated sleep animation and closed eyes, without its repeating voice.
    // The scope covers only our private bench actor's synchronous simulation tick.
    internal static class BenchRestAudio
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PlaySound(uint sound,IntPtr position);
        private static readonly PlaySound callback=Play;
        private static PlaySound original;
        private static PlaySound stop;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void ProcessRequests();
        private static ProcessRequests processRequests;
        private static object sync;
        private static readonly HashSet<IntPtr> sleepingSources=new HashSet<IntPtr>();
        private static NativeDetour hook;
        [ThreadStatic]private static bool quiet;
        [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);

        internal static void Install(string library)
        {
            if(hook!=null)return;
            IntPtr module=GetModuleHandle(library);
            var symbols=NativeSymbols.Functions(library,module,"play_sound","stop_sound","process_all_sound_requests");
            IntPtr target=symbols["play_sound"];
            sync=AccessTools.Field(AccessTools.TypeByName("MarioHollow.Native"),"Sync").GetValue(null);
            stop=(PlaySound)Marshal.GetDelegateForFunctionPointer(symbols["stop_sound"],typeof(PlaySound));
            processRequests=(ProcessRequests)Marshal.GetDelegateForFunctionPointer(symbols["process_all_sound_requests"],typeof(ProcessRequests));
            try
            {
                hook=new NativeDetour(target,callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<PlaySound>();hook.Apply();
            }
            catch{Dispose();throw;}
        }
        internal static bool Begin(){bool previous=quiet;quiet=true;return previous;}
        internal static void End(bool previous){quiet=previous;}
        internal static bool ShouldMute(uint sound)
            =>quiet&&IsSleepSound(sound);
        private static bool IsSleepSound(uint sound)
        {
            // Compare bank/sample only; SM64 can vary a sound's priority/flags.
            uint sample=sound&0xF0FF0000u;
            return sample==0x200E0000u||sample==0x200F0000u||sample==0x20350000u;
        }
        private static void Play(uint sound,IntPtr position)
        {
            if(ShouldMute(sound))return;
            if(IsSleepSound(sound))sleepingSources.Add(position);
            original(sound,position);
        }
        internal static void StopSleeping()
        {
            if(sync==null||stop==null)return;
            lock(sync)
            {
                if(sleepingSources.Count==0)return;
                // A just-requested snore may not be in its sound bank yet. Resolve
                // pending requests before stopping only these three voice samples.
                processRequests();
                foreach(IntPtr source in sleepingSources)
                {
                    stop(0x240E8080u,source);stop(0x240F8080u,source);stop(0x2435FF80u,source);
                }
                sleepingSources.Clear();
            }
        }
        internal static void Forget(){if(sync!=null)lock(sync)sleepingSources.Clear();}
        internal static void Dispose(){hook?.Dispose();hook=null;quiet=false;Forget();stop=null;processRequests=null;}
    }
}
