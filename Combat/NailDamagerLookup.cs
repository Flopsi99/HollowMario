using UnityEngine;

namespace HollowMario
{
    // PlayMaker's normal FindFsmOnGameObject searches fsmList, which excludes disabled
    // FSMs. Our inert nail clone must remain disabled, but native nail receivers still
    // need its damage/direction metadata. Only this owned source gets a local fallback.
    internal static class NailDamagerLookup
    {
        private static GameObject source;
        private static Component damager;

        internal static void Register(GameObject ownedSource, Component ownedDamager)
        {
            if (ownedSource != null && ownedDamager != null && ownedDamager.gameObject == ownedSource)
            { source = ownedSource; damager = ownedDamager; }
        }

        internal static bool Find(object __0, string __1, ref object __result)
        {
            if (source == null || damager == null || !(__0 is GameObject asked) || asked != source || __1 != "damages_enemy") return true;
            __result = damager;
            return false;
        }

        internal static void Clear() { source = null; damager = null; }
    }
}
