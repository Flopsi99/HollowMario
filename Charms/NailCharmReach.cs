using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    internal static class NailCharmReach
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type heroType, dataType;
        private static FieldInfo playerData, longnail, pride;

        internal static float Multiplier(object hero)
        {
            if (hero == null) return 1f;
            if (heroType != hero.GetType())
            {
                heroType = hero.GetType();
                playerData = heroType.GetField("playerData", Fields);
            }
            object data = playerData?.GetValue(hero);
            if (data == null) return 1f;
            if (dataType != data.GetType())
            {
                dataType = data.GetType();
                longnail = dataType.GetField("equippedCharm_18", Fields);
                pride = dataType.GetField("equippedCharm_13", Fields);
            }
            bool hasLongnail = longnail?.GetValue(data) is bool l && l;
            bool hasPride = pride?.GetValue(data) is bool p && p;
            // NailSlash.StartSlash scales both local X and Y by these exact values.
            // They are additive when combined, not compounded. Read equipment live;
            // never change the attack sheets or cache an equipped-charm result.
            return hasPride ? (hasLongnail ? 1.4f : 1.25f) : (hasLongnail ? 1.15f : 1f);
        }

        internal static float ScaleDistance(float distance, object hero) => distance * Multiplier(hero);

        internal static Bounds Scale(Bounds reach, Vector3 origin, float multiplier)
        {
            if (multiplier == 1f) return reach;
            return new Bounds(new Vector3(origin.x + (reach.center.x-origin.x)*multiplier,
                origin.y + (reach.center.y-origin.y)*multiplier, reach.center.z),
                new Vector3(reach.size.x*multiplier, reach.size.y*multiplier, reach.size.z));
        }
    }
}
