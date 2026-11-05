using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace HollowMario
{
    // Native cap/triple-jump/flight actions own all lift, speed and collision. Only
    // flying gets a 2D steering adapter; land, takeoff, swimming and other caps do not.
    internal static class WingCap
    {
        private const uint Flying = 0x10880899u;
        private static Type handler;
        private static FieldInfo stickX,stickY,camX,camZ;
        private static ManualLogSource log;
        private static int failures;
        internal static bool Ready { get; private set; }

        internal static void Arm(Harmony harmony,Type native,ManualLogSource logger)
        {
            log=logger;Ready=false;
            try
            {
                handler=AccessTools.TypeByName("InputHandler");
                MethodInfo tick=AccessTools.Method(native,"sm64_mario_tick");
                Type inputs=tick?.GetParameters()[1].ParameterType.GetElementType();
                stickX=AccessTools.Field(inputs,"stickX");stickY=AccessTools.Field(inputs,"stickY");
                camX=AccessTools.Field(inputs,"camLookX");camZ=AccessTools.Field(inputs,"camLookZ");
                if(handler==null||tick==null||stickX==null||stickY==null||camX==null||camZ==null)
                    throw new MissingMemberException("Wing Cap's native flight controls were not found.");
                harmony.Patch(tick,prefix:new HarmonyMethod(typeof(WingCap),nameof(BeforeTick)){priority=Priority.Last});
                Ready=true;
            }
            catch(Exception error){Warn(error);}
        }

        internal static void BeforeTick(object[] __args)
        {
            if(!Ready||CapSystem.Current!=CapKind.Wing||!CapSystem.Active||__args==null||__args.Length<4)return;
            try
            {
                KnightResetFollow follow=Plugin.Instance?.knightFollow;
                object controller=follow?.Controller();
                if(controller==null||!follow.MarioAlive(controller)||Convert.ToInt32(__args[0])!=follow.MarioId(controller)
                    ||Convert.ToUInt32(CapAccess.Read(__args[2],"action")??0u)!=Flying)return;
                object actions=CapAccess.Read(CapAccess.Read(handler,"Instance"),"inputActions");
                object move=CapAccess.Read(actions,"moveVector");
                if(move==null)return;
                float y=CapAccess.Number(move,"Y");
                // Flight keeps its existing takeoff heading. Turn only after leaving
                // flight; horizontal input must never flip or yaw the flight path.
                object input=__args[1];
                stickX.SetValue(input,0f);
                // Verified installed update_flying_pitch: positive stickY pitches up.
                stickY.SetValue(input,Math.Abs(y)<.2f?0f:Math.Max(-1f,Math.Min(1f,y)));
                camX.SetValue(input,0f);camZ.SetValue(input,-1f);
                __args[1]=input; // Prefix input only; never write back state/geometry.
            }
            catch(Exception error){Warn(error);}
        }

        private static void Warn(Exception error)
        {if(failures++<4)log?.LogWarning("Wing Cap: "+error.GetBaseException().Message);}
    }
}
