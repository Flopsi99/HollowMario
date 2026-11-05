using System;
using System.Reflection;
using HarmonyLib;

namespace HollowMario
{
    internal static class AcidSafety
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [ThreadStatic] private static bool protectedDamage;
        private static Type heroType, dataType, playerDataType;
        private static FieldInfo heroData, tear, playerDataInstance;
        private static PropertyInfo playerDataInstanceProperty;
        private static bool playerDataResolved;
        internal static Func<object,bool> AdditionalProtection;

        // Read the current save's inventory every time. Picking up the Tear (or loading a
        // different save) must take effect without cached permission or edited player data.
        internal static bool HasTear(object hero)
        {
            if (hero != null && heroType != hero.GetType())
            {
                heroType = hero.GetType();
                heroData = heroType.GetField("playerData", Any);
            }
            object data = hero == null ? null : heroData?.GetValue(hero);
            if (data == null)
            {
                if (!playerDataResolved)
                {
                    playerDataType = AccessTools.TypeByName("PlayerData");
                    playerDataInstance = playerDataType?.GetField("instance", Any);
                    playerDataInstanceProperty = playerDataType?.GetProperty("instance", Any);
                    playerDataResolved = true;
                }
                data = playerDataInstance?.GetValue(null) ?? playerDataInstanceProperty?.GetValue(null, null);
            }
            if (data == null) return false;
            if (dataType != data.GetType())
            {
                dataType = data.GetType();
                tear = dataType.GetField("hasAcidArmour", Any);
            }
            return tear?.GetValue(data) is bool on && on;
        }

        internal static bool CanEnterAcid(object hero) => HasTear(hero)||AdditionalProtection?.Invoke(hero)==true;
        internal static bool Protects(object hero, int hazard) => hazard == 3 && CanEnterAcid(hero);

        internal static bool BeforeDamage(object __instance, int __3, out bool __state)
        {
            __state = protectedDamage;
            protectedDamage = false;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (__3 != 3 || controller == null || !follow.MarioAlive(controller)
                || !ReferenceEquals(follow.Hero(controller), __instance)) return true;
            protectedDamage = CanEnterAcid(__instance);
            return !protectedDamage;
        }

        // Finalizers run after every postfix, even when damage was skipped or threw. Keep the
        // protected call's context through the base mod's unconditional AfterHeroHit callback,
        // then restore the outer call so nested real enemy hits retain their own reaction.
        internal static void AfterDamage(bool __state) { protectedDamage = __state; }

        internal static bool SuppressReaction => protectedDamage;
    }
}
