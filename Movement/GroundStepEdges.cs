using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // Keep upstream wall/floor resolution, but turn a flat ledge drop into its
    // normal LEFT_GROUND result instead of accepting the 100-unit down-step.
    internal static class GroundStepEdges
    {
        [StructLayout(LayoutKind.Sequential)]private struct Point {internal float X,Y,Z;}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int GroundStep(IntPtr mario,ref Point intended);
        private static readonly GroundStep callback=Step;
        private static GroundStep original;
        private static NativeDetour hook;
        private static int reports;
        internal static void Install(string path,IntPtr module)
        {
            if(hook!=null)return;
            IntPtr target=NativeSymbols.Functions(path,module,"perform_ground_quarter_step")["perform_ground_quarter_step"];
            try
            {
                hook=new NativeDetour(target,callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<GroundStep>();hook.Apply();
            }
            catch{Dispose();throw;}
        }
        private static unsafe int Step(IntPtr mario,ref Point intended)
        {
            // Pinned upstream x64 MarioState layout: pos at 0x3C, floor at
            // 0x70. Public SM64SurfaceCollisionData.normal.y is at byte 56.
            byte* state=(byte*)mario;float before=*(float*)(state+0x40);
            IntPtr prior=*(IntPtr*)(state+0x70);
            bool flat=prior!=IntPtr.Zero&&*(float*)((byte*)prior+56)>.999f;
            int result=original(mario,ref intended);
            if(!NativeMovementGates.GameplayTick||!flat||(result!=1&&result!=3))return result;
            float drop=before-*(float*)(state+0x40);
            IntPtr floor=*(IntPtr*)(state+0x70);
            if(drop<=4f||floor==IntPtr.Zero||*(float*)((byte*)floor+56)<=.999f)return result;
            // The original solver has selected the real lower floor. Keep it
            // for airborne landing, restore takeoff height, and let the native
            // caller start freefall before any further grounded quarter-steps.
            *(float*)(state+0x40)=before;
            if(reports++<12)Plugin.Instance?.Log.LogInfo("Mario walked off a flat ledge: falling instead of snapping down "+(drop*.0105f)+" world units.");
            return 0; // GROUND_STEP_LEFT_GROUND, from upstream sm64.h.
        }
        internal static void Dispose(){hook?.Dispose();hook=null;}
    }
}
