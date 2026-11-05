using System;
using System.Reflection;

namespace HollowMario
{
    internal static partial class MovementUnlocks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type heroType, dataType;
        private static FieldInfo playerData, claws, dash, shade;
        private static FieldInfo sprintmaster, dashmaster, run, charmRun, comboRun, state, walkZone, acid;

        private static object Data(object hero)
        {
            if (hero == null) return null;
            if (heroType != hero.GetType())
            {
                heroType = hero.GetType();
                playerData = heroType.GetField("playerData", Fields);
                run = heroType.GetField("RUN_SPEED", Fields);
                charmRun = heroType.GetField("RUN_SPEED_CH", Fields);
                comboRun = heroType.GetField("RUN_SPEED_CH_COMBO", Fields);
                state = heroType.GetField("cState", Fields);
                walkZone = state?.FieldType.GetField("inWalkZone", Fields);
                acid = heroType.GetField("inAcid", Fields);
            }
            object data = playerData?.GetValue(hero);
            if (data != null && dataType != data.GetType())
            {
                dataType = data.GetType();
                claws = dataType.GetField("hasWalljump", Fields);
                dash = dataType.GetField("hasDash", Fields);
                shade = dataType.GetField("hasShadowDash", Fields);
                sprintmaster = dataType.GetField("equippedCharm_37", Fields);
                dashmaster = dataType.GetField("equippedCharm_31", Fields);
            }
            return data;
        }

        internal static bool HasClaws(object hero)
        {
            object data = Data(hero);
            return data != null && claws?.GetValue(data) is bool on && on;
        }

        internal static bool HasDash(object hero)
        {
            object data = Data(hero);
            return data != null && ((dash?.GetValue(data) is bool normal && normal)
                || (shade?.GetValue(data) is bool shadow && shadow));
        }

        internal static bool HasShade(object hero)
        {
            object data = Data(hero);
            return data != null && shade?.GetValue(data) is bool unlocked && unlocked;
        }

    }
}
