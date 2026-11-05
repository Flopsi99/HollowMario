using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace HollowMario
{
    // Both menus edit the engine's original setting, not a second volume setting.
    internal static class MarioVolume
    {
        private static ConfigEntry<float> setting;
        private static FieldInfo output;
        internal static bool Available => setting != null && output != null;
        internal static float Value => Available ? Clamp(setting.Value) : .8f;
        internal static float Clamp(float value) => float.IsNaN(value) ? .8f : Math.Max(0f, Math.Min(1f, value));

        internal static void Arm(Harmony harmony, Assembly engine)
        {
            setting=AccessTools.Field(engine.GetType("MarioHollow.Plugin"),"Volume")?.GetValue(null) as ConfigEntry<float>;
            output=AccessTools.Field(engine.GetType("MarioHollow.MarioAudio"),"Volume");
            if(!Available)throw new InvalidOperationException("Mario's existing volume setting was not found.");
        }

        internal static void Set(float value)
        {
            if(!Available)return;
            value=Clamp(value);
            setting.Value=value; // Existing config saves automatically.
            output.SetValue(null,value); // The audio thread reads this for every buffer.
        }
        internal static int Percent => (int)Math.Round(Value*100f);
        internal static void SetPercent(int value) => Set(value/100f);
    }
}
