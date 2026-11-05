using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace HollowMario
{
    // All optional debug controls live here and in ONE [Debug] config section.
    // Never gate automatic recovery, ordinary movement, combat or test reporting.
    internal static class DebugSettings
    {
        private static ConfigEntry<bool> master, roomReset, benchReturn, bossHp, hitboxes, verbose,engineOverlay,presentationTiming,cameraTrace;
        private static ManualLogSource source;
        private static bool? healthDisplayOverride;
        private static bool? hitboxDisplayOverride;
        internal static bool Enabled => master?.Value ?? true;
        internal static bool RoomReset => Enabled && (roomReset?.Value ?? true);
        internal static bool BenchReturn => Enabled && (benchReturn?.Value ?? true);
        internal static bool ShowBossHp => Enabled && (healthDisplayOverride ?? bossHp?.Value ?? false);
        internal static bool ShowHitboxes => Enabled && (hitboxDisplayOverride ?? hitboxes?.Value ?? false);
        internal static bool DiagnosticsAvailable
        {
            get
            {
#if HOLLOW_MARIO_DIAGNOSTICS
                return true;
#else
                return false;
#endif
            }
        }
        internal static bool VerboseLogging => DiagnosticsAvailable && Enabled && (verbose?.Value ?? false);
        internal static bool ShowEngineOverlay => Enabled && (engineOverlay?.Value ?? false);
        internal static bool PresentationTiming => Enabled && VerboseLogging && (presentationTiming?.Value ?? true);
        internal static bool CameraTrace => DiagnosticsAvailable && Enabled && (cameraTrace?.Value ?? false);

        internal static void Bind(ConfigFile config)
        {
            healthDisplayOverride=null;
            hitboxDisplayOverride=null;
            const string restart = " Edit with the game closed; applies next launch.";
            master = config.Bind("Debug", "Enabled", true,
                "Master switch for debug hotkeys, boss HP and diagnostic observations/logs. Automatic out-of-bounds recovery and normal gameplay stay active. Warnings and errors remain visible." + restart);
            roomReset = config.Bind("Debug", "EnableRoomReset", true,
                "Allow F5 to return Mario to this room's first landing point. Requires Enabled." + restart);
            benchReturn = config.Bind("Debug", "EnableBenchReturn", true,
                "Allow F9 to return to the last bench. Requires Enabled." + restart);
            bossHp = config.Bind("Debug", "ShowBossHp", false,
                "Show live boss HP numbers at startup. Default off. F8 toggles the display for the current session in all builds. Requires Enabled. Read-only." + restart);
            hitboxes=config.Bind("Debug","ShowHitboxes",false,
                "Draw Mario's collision/hurt boxes, active move reach and stomp path. Default off. F7 toggles for the current session; rebind in the community API mod menu. Requires Enabled. Read-only."+restart);
            verbose = config.Bind("Debug", "VerboseLogging", false,
                "Enable companion info/debug logs, projectile tracing and terrain/trigger inspection. Default off for new installs; existing choices are preserved. Requires Enabled. Warnings, errors and automated test results remain available." + restart);
            cameraTrace=config.Bind("Debug","TraceCamera",false,
                "Temporary read-only hidden-room camera diagnostics. Records collider/region contacts and camera motion to two rotating 4 MiB files beside the host. No camera behavior changes; independent of VerboseLogging."+restart);
            presentationTiming=config.Bind("Debug","LogPresentationHitches",true,
                "Log gameplay frames >=50 ms or scoped work >=12 ms, at most once per five seconds, plus a 30-second performance summary. Includes collision, simulation, combat, rendering, camera/pose costs, per-frame GC and managed allocation when supported. Scope times overlap; unmeasured game/render/other-mod work is not attributed. Requires Enabled and VerboseLogging."+restart);
            engineOverlay = config.Bind("Debug", "ShowEngineOverlay", false,
                "Show the old engine's large control/status banner. Requires Enabled. ROM startup errors remain visible." + restart);
        }
        internal static void ToggleBossHp()
        {if(Enabled)healthDisplayOverride=!ShowBossHp;}
        internal static void ToggleHitboxes()
        {if(Enabled)hitboxDisplayOverride=!ShowHitboxes;}

        internal static void Arm(Harmony harmony, ConfigFile config, ManualLogSource logger)
        {
            Bind(config);
            source = logger;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(ManualLogSource), "Log", new[] { typeof(LogLevel), typeof(object) }),
                    prefix: new HarmonyMethod(typeof(DebugSettings), nameof(FilterLog)));
            }
            catch (Exception error) { logger.LogWarning("Debug log switch could not be armed: " + error.GetBaseException().Message); }
        }

        private static bool FilterLog(ManualLogSource __instance, LogLevel __0, object __1)
        {
            // Limit this hook to our exact log source, including the frozen
            // platform helpers. Never suppress another mod or critical messages.
            if (!ReferenceEquals(__instance, source)) return true;
            if ((__0 & (LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message)) != 0) return true;
            if (__1 is string text && text.StartsWith("melty test:", StringComparison.OrdinalIgnoreCase)) return true;
            return VerboseLogging || (__0 & (LogLevel.Info | LogLevel.Debug)) == 0;
        }
    }
}
