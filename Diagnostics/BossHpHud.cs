using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Read-only debug text, driven by the plugin's existing LateUpdate/OnGUI.
    // Register on native health lifecycle events, never scan the scene each frame.
    internal static class BossHpHud
    {
        private static FieldInfo hp, dead, specialDeath, bossScene, bossController;
        private static PropertyInfo bossLookup;
        private static ManualLogSource log;
        private static readonly List<Component> targets = new List<Component>();
        private static readonly List<int> values = new List<int>();
        private static readonly StringBuilder buffer = new StringBuilder(64);
        private static string label = "";
        private static GUIStyle style;
        private static int failures;
        internal static bool Enabled => DebugSettings.ShowBossHp;

        internal static void Arm(Harmony harmony, ManualLogSource logger)
        {
            log = logger;
            // Always subscribe, including shipped builds with the HUD hidden.
            // F8 must work for a boss that was already present before toggling on.
            try
            {
                Type health = AccessTools.TypeByName("HealthManager");
                Type controller = AccessTools.TypeByName("BossSceneController");
                hp = AccessTools.Field(health, "hp");
                dead = AccessTools.Field(health, "isDead");
                specialDeath = AccessTools.Field(health, "hasSpecialDeath");
                bossScene = AccessTools.Field(health, "unlockBossScene");
                bossController = AccessTools.Field(controller, "Instance");
                bossLookup = AccessTools.Property(controller, "BossHealthLookup");
                if (hp == null || dead == null || bossController == null || bossLookup == null)
                    throw new MissingMemberException("Boss HP debug metadata");
                foreach (string name in new[] { "OnEnable", "Start" })
                    harmony.Patch(AccessTools.Method(health, name), postfix: new HarmonyMethod(typeof(BossHpHud), nameof(HealthReady)));
                harmony.Patch(AccessTools.Method(controller, "ReportHealth"), postfix: new HarmonyMethod(typeof(BossHpHud), nameof(HealthReported)));
            }
            catch (Exception error) { Warn(error); }
        }

        private static bool RegisteredBoss(object health)
        {
            object controller = bossController?.GetValue(null);
            return controller != null && bossLookup.GetValue(controller, null) is IDictionary lookup && lookup.Contains(health);
        }

        private static bool BossMarker(Component health)
        {
            if (RegisteredBoss(health)) return true;
            // Story fights don't all have a Godhome controller or a Boss tag.
            // Their scripted death / Godfinder unlock metadata stays on their
            // own health component, rather than on ordinary summoned enemies.
            if (specialDeath?.GetValue(health) is bool scripted && scripted) return true;
            if (bossScene?.GetValue(health) is UnityEngine.Object scene && scene != null) return true;
            for (Transform owner = health.transform; owner != null; owner = owner.parent)
                if (owner.CompareTag("Boss")) return true;
            return false;
        }

        private static void HealthReady(object __instance)
        {
            try
            {
                Component health = __instance as Component;
                if (health != null && !targets.Contains(health) && BossMarker(health)) targets.Add(health);
            }
            catch (Exception error) { Warn(error); }
        }

        private static void HealthReported(object __0)
        {
            try
            {
                Component health = __0 as Component;
                if (health != null && !targets.Contains(health) && RegisteredBoss(health)) targets.Add(health);
            }
            catch (Exception error) { Warn(error); }
        }

        // Read the real field every existing rendered tick: scripted healing,
        // damage from companions, phase resets and difficulty scaling all count.
        internal static int ReadHealth(object health)
        {
            if (health == null || hp == null || dead?.GetValue(health) is bool killed && killed) return 0;
            return Math.Max(0, (int)hp.GetValue(health));
        }

        internal static void Tick()
        {
            if (!Enabled) { label = ""; return; }
            try
            {
                bool changed = false;
                int count = 0;
                object controller = bossController?.GetValue(null);
                IDictionary encounter = controller == null ? null : bossLookup.GetValue(controller, null) as IDictionary;
                for (int i = 0; i < targets.Count;)
                {
                    Component health = targets[i];
                    if (health == null) { targets.RemoveAt(i); continue; }
                    i++;
                    // In Godhome the game's explicit boss roster takes priority
                    // over story-fight markers, so summoned enemies can't add numbers.
                    if (encounter != null && encounter.Count > 0 && !encounter.Contains(health)) continue;
                    if (!health.gameObject.activeInHierarchy) continue;
                    int current = ReadHealth(health);
                    if (current <= 0) continue;
                    if (count >= values.Count) { values.Add(current); changed = true; }
                    else if (values[count] != current) { values[count] = current; changed = true; }
                    count++;
                }
                if (values.Count != count) { values.RemoveRange(count, values.Count - count); changed = true; }
                if (!changed && (label.Length > 0 || count == 0)) return;
                buffer.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (i != 0) buffer.Append("   |   ");
                    buffer.Append(values[i].ToString(CultureInfo.InvariantCulture));
                }
                label = buffer.ToString();
            }
            catch (Exception error) { label = ""; Warn(error); }
        }

        internal static void Draw()
        {
            if (!Enabled || label.Length == 0 || CapControls.Open) return;
            float scale = Mathf.Clamp(Screen.height / 1080f, .65f, 1.5f);
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            style.fontSize = Mathf.RoundToInt(26f * scale);
            Rect rect = new Rect(Screen.width * .5f - 300f * scale, Screen.height - 72f * scale, 600f * scale, 48f * scale);
            Color previous = GUI.color;
            try
            {
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + scale, rect.y + scale, rect.width, rect.height), label, style);
                GUI.color = Color.white;
                GUI.Label(rect, label, style);
            }
            finally { GUI.color = previous; }
        }

        internal static void Clear() { targets.Clear(); values.Clear(); label = ""; }
        private static void Warn(Exception error)
        { if (failures++ < 3) log?.LogWarning("Boss HP debug label: " + error.GetBaseException().Message); }
    }
}
