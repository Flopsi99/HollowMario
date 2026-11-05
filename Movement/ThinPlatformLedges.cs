using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // Fill only the missed lower-wall test on shallow platforms. All ordinary
    // movement runs first; successful fallback grabs use SM64's own ledge solver.
    internal static class ThinPlatformLedges
    {
        [StructLayout(LayoutKind.Sequential)]private struct Point{internal float X,Y,Z;}
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint AirStep(IntPtr mario,ref Point intended,uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate IntPtr Wall(ref Point point,float height,float radius);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate float Floor(float x,float y,float z,out IntPtr surface);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate float Ceiling(ref Point point,float floor,out IntPtr surface);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint Grab(IntPtr mario,IntPtr wall,ref Point intended,ref Point next);
        private static readonly AirStep callback=Step;
        private static AirStep original;
        private static Wall wall;
        private static Floor floor;
        private static Ceiling ceiling;
        private static Grab grab;
        private static NativeDetour hook;
        private static ConfigEntry<bool> enabled;
        private static ManualLogSource log;

        internal static void Configure(ConfigFile config,ManualLogSource logger)
        {
            log=logger;
            enabled=config.Bind("Movement","ThinPlatformLedgeGrabs",true,
                "Allow normal falling ledge grabs on shallow platforms missed by SM64's foot-height wall check. Does not enlarge collision shapes or grab reach. Edit with the game closed; applies next launch.");
        }
        internal static void Install(string path,IntPtr module)
        {
            if(hook!=null||!(enabled?.Value??true))return;
            var functions=NativeSymbols.Functions(path,module,"perform_air_quarter_step","resolve_and_return_wall_collisions",
                "find_floor","vec3f_find_ceil","check_ledge_grab");
            wall=(Wall)Marshal.GetDelegateForFunctionPointer(functions["resolve_and_return_wall_collisions"],typeof(Wall));
            floor=(Floor)Marshal.GetDelegateForFunctionPointer(functions["find_floor"],typeof(Floor));
            ceiling=(Ceiling)Marshal.GetDelegateForFunctionPointer(functions["vec3f_find_ceil"],typeof(Ceiling));
            grab=(Grab)Marshal.GetDelegateForFunctionPointer(functions["check_ledge_grab"],typeof(Grab));
            try
            {
                hook=new NativeDetour(functions["perform_air_quarter_step"],callback,new NativeDetourConfig{ManualApply=true});
                original=hook.GenerateTrampoline<AirStep>();hook.Apply();
                log?.LogInfo("Shallow platforms can use Mario's normal falling ledge grab.");
            }
            catch{Dispose();throw;}
        }
        private static unsafe uint Step(IntPtr mario,ref Point intended,uint flags)
        {
            uint result=original(mario,ref intended,flags);
            if(result!=0||(flags&1u)==0||!NativeMovementGates.GameplayTick||!(enabled?.Value??true))return result;
            // Same verified x64 MarioState velocity layout used by the movement
            // gates. Extra queries work on copies and never move a failed attempt.
            float vx=*(float*)((byte*)mario+0x48),vy=*(float*)((byte*)mario+0x4C),vz=*(float*)((byte*)mario+0x50);
            if(vy>0f)return result;
            float speed=(float)Math.Sqrt(vx*vx+vz*vz);
            if(!(speed>.01f))return result;
            Point probe=intended;
            if(wall(ref probe,150f,50f)!=IntPtr.Zero)return result;
            probe=intended;
            if(wall(ref probe,30f,50f)!=IntPtr.Zero)return result; // Native ledges already got their chance.
            float x=intended.X+vx/speed*60f,z=intended.Z+vz/speed*60f;
            IntPtr top;
            float height=floor(x,intended.Y+160f,z,out top),rise=height-intended.Y;
            if(top==IntPtr.Zero||!(rise>100f&&rise<=160f))return result;
            Point standing=new Point{X=x,Y=height,Z=z};IntPtr roof;
            if(ceiling(ref standing,height,out roof)-height<160f)return result;
            probe=intended;
            IntPtr edge=wall(ref probe,rise-1f,50f);
            if(edge==IntPtr.Zero||Marshal.ReadInt16(edge)==1)return result; // Burning walls remain hazardous.
            // This is the original displacement, floor, facing and placement
            // calculation. The caller still starts the normal ledge-grab action.
            return grab(mario,edge,ref intended,ref probe)!=0?3u:result;
        }
        internal static void Dispose(){hook?.Dispose();hook=null;}
    }
}
