using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // An event-only use of the enemy's existing flash animation. No renderer
    // snapshot, material replacement, polling or special damage type is needed.
    internal static class WeakSlideFlash
    {
        private static Type flashType;
        private static MethodInfo initialize, flash;
        private static FieldInfo hitEffects;
        private static ManualLogSource log;
        private static int failures;

        internal static void Configure(Type health, ManualLogSource logger)
        {
            log = logger;
            try
            {
                flashType = AccessTools.TypeByName("SpriteFlash");
                initialize = AccessTools.Method(flashType, "Start", Type.EmptyTypes);
                flash = AccessTools.Method(flashType, "flash", new[] { typeof(Color), typeof(float), typeof(float), typeof(float), typeof(float) });
                hitEffects = AccessTools.Field(health, "hitEffectReceiver");
                if (initialize == null || flash == null || hitEffects == null)
                    throw new MissingMemberException("Native enemy flash API");
            }
            catch (Exception error) { flash = null; Warn(error); }
        }

        internal static void Apply(object target)
        {
            if (flash == null || target == null) return;
            try
            {
                // Follow the native effect receiver, which can be on a different
                // object from HealthManager. Include its active visual children
                // just as the ordinary native flash methods do.
                Component visual = hitEffects.GetValue(target) as Component ?? target as Component;
                if (visual == null) return;
                Component[] parts = visual.GetComponentsInChildren(flashType, false);
                foreach (Component part in parts)
                {
                    if (part == null) continue;
                    try
                    {
                        // flash(Color, ...) assumes its native property block is
                        // initialized. Start is idempotent and sets up only that.
                        initialize.Invoke(part, null);
                        flash.Invoke(part, new object[] { new Color(.55f, .55f, .55f, 1f), .75f, .001f, .05f, .1f });
                    }
                    catch (Exception error) { Warn(error); }
                }
            }
            catch (Exception error) { Warn(error); }
        }

        private static void Warn(Exception error)
        { if (failures++ < 3) log?.LogWarning("Mario's weak slide flash: " + error.GetBaseException().Message); }
    }
}
