using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Replace the result of the actual charm inventory's language action only.
    // No shared language table or equipment is changed. Steady Body's slot
    // also receives its Mario-specific badge name in native language actions.
    internal static class CharmDescriptions
    {
        private static FieldInfo key, sheet, stored;
        private static PropertyInfo value, actionFsm, fsmName, fsmOwner;
        private static MethodInfo currentLanguage;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony, ManualLogSource logger)
        {
            log = logger;
            try
            {
                Type action = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.GetLanguageString");
                key = AccessTools.Field(action, "convName");
                sheet = AccessTools.Field(action, "sheetName");
                stored = AccessTools.Field(action, "storeValue");
                value = AccessTools.Property(stored?.FieldType, "Value");
                actionFsm = AccessTools.Property(action, "Fsm");
                fsmName = AccessTools.Property(actionFsm?.PropertyType, "Name");
                fsmOwner = AccessTools.Property(actionFsm?.PropertyType, "GameObject");
                currentLanguage = AccessTools.Method(AccessTools.TypeByName("Language.Language"), "CurrentLanguage", Type.EmptyTypes);
                MethodInfo enter = AccessTools.Method(action, "OnEnter");
                if (key == null || sheet == null || stored == null || value == null ||
                    fsmName == null || fsmOwner == null || currentLanguage == null || enter == null)
                    throw new MissingMemberException("Charm description inventory metadata");
                harmony.Patch(enter, postfix: new HarmonyMethod(typeof(CharmDescriptions), nameof(AfterLanguage)));
            }
            catch (Exception error) { Warn(error); }
        }

        // UI Charms builds keys and forwards them to the separate Update Text
        // FSM on the same Charms object. That second FSM reads and displays them.
        internal static string Find(string name, string table, string language, bool selected, string fsm, string owner)
        {
            if (!selected || table != "UI" || language != "EN" || name == null)
                return null;
            if(name==NoBonk.NameKey)return NoBonk.Name;
            if(fsm!="Update Text"||owner!="Charms")return null;
            return CharmDescriptionText.Entries.TryGetValue(name, out string replacement) ? replacement : null;
        }

        private static string Read(FieldInfo field, object action)
        {
            object parameter = field.GetValue(action);
            return parameter == null ? null : value.GetValue(parameter, null) as string;
        }

        private static void AfterLanguage(object __instance)
        {
            try
            {
                string name = Read(key, __instance);
                // Ignore the many other language actions before consulting mode/owner.
                if (name == null || (name!=NoBonk.NameKey&&!CharmDescriptionText.Entries.ContainsKey(name))) return;
                object fsm = actionFsm.GetValue(__instance, null);
                if (fsm == null) return;
                GameObject owner = fsmOwner.GetValue(fsm, null) as GameObject;
                if (owner == null) return;
                string replacement = Find(name, Read(sheet, __instance),
                    Convert.ToString(currentLanguage.Invoke(null, null)), PickupArtwork.Selected(),
                    fsmName.GetValue(fsm, null) as string, owner.name);
                if (replacement == null) return;
                object parameter = stored.GetValue(__instance);
                if (parameter != null) value.SetValue(parameter, replacement, null);
            }
            catch (Exception error) { Warn(error); }
        }

        private static void Warn(Exception error)
        {
            if (failures++ < 3) log?.LogWarning("Mario's charm descriptions: " + error.GetBaseException().Message);
        }
    }
}
