using System;
using System.Reflection;

namespace HollowMario
{
    // Eight scheduled chunks fill a normal 99-point bar in 2.5 seconds. Use the scaled
    // game clock, rather than rounding 0.3125 seconds to a number of 30 Hz Mario steps.
    internal static class SurfaceSoul
    {
        private const int SoulPerBar = 99;
        // n64decomp/sm64: SOUND_MENU_POWER_METER, including SOUND_STATUS_WAITING.
        private const uint RefillSound = 0x700D0081u;
        private const double ChunkSeconds = 2.5 / 8;
        private static int chunks;
        private static double startedAt;
        private static bool active;
        private static object boundHero;
        private static Type heroType;
        private static MethodInfo spa;
        private static Func<int, bool> addSoul;
        private static Action<uint> playSound;
        internal static Func<bool> CanRefill;

        internal static void Arm(Type native)
        {
            MethodInfo sound = native?.GetMethod("sm64_play_sound_global", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(uint) }, null);
            playSound = sound == null ? null : (Action<uint>)Delegate.CreateDelegate(typeof(Action<uint>), sound);
        }

        internal static void Reset()
        {
            active = false;
            chunks = 0;
            boundHero = null;
            addSoul = null;
        }

        internal static void Tick(object hero, bool atSurface, double now)
        {
            if (!atSurface || hero == null || CanRefill?.Invoke()==false) { active = false; chunks = 0; return; }
            if (!ReferenceEquals(boundHero, hero))
            {
                boundHero = hero;
                active = false;
                chunks = 0;
                if (heroType != hero.GetType())
                {
                    heroType = hero.GetType();
                    spa = heroType.GetMethod("TryAddMPChargeSpa", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(int) }, null);
                }
                // The game's own spa soul method handles the normal caps, reserve and UI.
                // It never adds health; no hot-spring FSM or healing effect is activated.
                addSoul = spa == null ? null : (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), hero, spa);
            }
            if (addSoul == null) return;
            if (!active)
            {
                active = true;
                startedAt = now;
                return;
            }
            int due = (int)Math.Floor((now - startedAt) / ChunkSeconds);
            if (due <= chunks) return;
            // Cumulative rounding produces eight near-equal whole-point chunks totalling 99.
            // A missed render frame catches up to the original deadlines without timer drift
            // or an unbounded callback loop; at most one normal bar is granted in one update.
            int amount = (int)Math.Min(SoulPerBar, (long)due * SoulPerBar / 8 - (long)chunks * SoulPerBar / 8);
            chunks = due;
            // The native spa API handles partially filled primary/reserve meters and reports
            // whether soul really increased. A full meter must not produce a refill chime.
            if (addSoul(amount)) playSound?.Invoke(RefillSound);
        }
    }
}
