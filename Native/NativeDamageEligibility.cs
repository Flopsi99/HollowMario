using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // The pinned upstream runtime caches interaction protection BEFORE advancing
    // Mario's action. An external hit arrives AFTER that tick, so use the live
    // action/timer for its eligibility, including the first frame after get-up.
    internal static class NativeDamageEligibility
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint Damage(IntPtr mario,uint amount,uint subtype,float x,float y,float z);
        private static readonly Damage callback=Apply;
        private static Damage original;
        private static NativeDetour hook;
        private static IntPtr globalState;
        [ThreadStatic]private static bool confirmedHit;
        internal static bool BeginConfirmedHit(){bool previous=confirmedHit;confirmedHit=true;return previous;}
        internal static void EndConfirmedHit(bool previous){confirmedHit=previous;}
        [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
        internal static void Install(string library)
        {
            if(hook!=null)return;
            IntPtr module=GetModuleHandle(library);
            IntPtr target=NativeSymbols.Functions(library,module,"fake_damage_knock_back")["fake_damage_knock_back"];
            globalState=NativeSymbols.Data(library,module,"g_state")["g_state"];
            try
            {
                hook=new NativeDetour(target,callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<Damage>();hook.Apply();
            }
            catch{Dispose();throw;}
        }
        private static uint Apply(IntPtr mario,uint amount,uint subtype,float x,float y,float z)
        {
            // Layout from pinned upstream MarioState and GlobalState headers:
            // action 0x0C, invincTimer 0x26; GlobalState.msInvulnerable 0x02.
            // Native calls already run under the host's shared simulation lock.
            IntPtr state=Marshal.ReadIntPtr(globalState);
            if(state==IntPtr.Zero)return original(mario,amount,subtype,x,y,z);
            short previous=Marshal.ReadInt16(state,2);
            uint action=unchecked((uint)Marshal.ReadInt32(mario,0x0C));
            // HK already accepted a confirmed hit. A native wall/fall recovery
            // must not veto its knockback after masks have actually been lost.
            bool protectedNow=!confirmedHit&&((action&0x20000u)!=0||Marshal.ReadInt16(mario,0x26)!=0);
            Marshal.WriteInt16(state,2,(short)(protectedNow?1:0));
            try{return original(mario,amount,subtype,x,y,z);}
            finally{Marshal.WriteInt16(state,2,previous);}
        }
        internal static void Dispose(){hook?.Dispose();hook=null;original=null;globalState=IntPtr.Zero;confirmedHit=false;}
    }
}
