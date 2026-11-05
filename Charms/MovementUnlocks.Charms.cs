using System;
using System.Reflection;

namespace HollowMario
{
    internal static partial class MovementUnlocks
    {
        private static FieldInfo sharpShadow;
        private static FieldInfo quickSlash;
        private static FieldInfo noBonk;

        internal static bool HasNoBonk(object hero)
        {
            object data=Data(hero);if(data==null)return false;
            if(noBonk==null||!noBonk.DeclaringType.IsInstanceOfType(data))
                noBonk=data.GetType().GetField("equippedCharm_14",Fields);
            return noBonk?.GetValue(data) is bool equipped&&equipped;
        }

        internal static bool HasQuickSlash(object hero)
        {
            object data = Data(hero);
            if (data == null) return false;
            if (quickSlash == null || !quickSlash.DeclaringType.IsInstanceOfType(data))
                quickSlash = data.GetType().GetField("equippedCharm_32", Fields);
            return quickSlash?.GetValue(data) is bool equipped && equipped;
        }

        // Share the existing dive's contact/damage path only during the real shade
        // phase. An ordinary long jump, cooldown or unequipped charm stays harmless.
        internal static uint AttackAction(uint action,bool shaded,object hero)
        {
            if(action!=0x03000888u||!shaded)return action;
            object data=Data(hero);if(data==null)return action;
            if(sharpShadow==null||!sharpShadow.DeclaringType.IsInstanceOfType(data))
                sharpShadow=data.GetType().GetField("equippedCharm_16",Fields);
            return sharpShadow?.GetValue(data) is bool equipped&&equipped?0x0188088Au:action;
        }

        internal static bool HasDashmaster(object hero)
        {
            object data = Data(hero);
            return data != null && dashmaster?.GetValue(data) is bool equipped && equipped;
        }

        internal static float RunMultiplier(object hero)
        {
            object data = Data(hero);
            if (data == null || !(sprintmaster?.GetValue(data) is bool equipped && equipped)) return 1f;
            object flags = state?.GetValue(hero);
            if ((acid?.GetValue(hero) is bool wet && wet)
                || (flags != null && walkZone?.GetValue(flags) is bool walking && walking)) return 1f;
            bool combo = dashmaster?.GetValue(data) is bool paired && paired;
            float normal = run == null ? 0f : Convert.ToSingle(run.GetValue(hero));
            FieldInfo boosted = combo ? comboRun : charmRun;
            float speed = boosted == null ? 0f : Convert.ToSingle(boosted.GetValue(hero));
            float ratio = speed / normal;
            return normal > 0f && ratio > 1f && !float.IsInfinity(ratio) ? ratio : 1f;
        }

    }
}
