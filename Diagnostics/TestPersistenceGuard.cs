using System;
using System.Reflection;
using HarmonyLib;

namespace HollowMario
{
    // Test-only: the existing gameplay routine loads a save and changes position
    // and health. Keep the player's disk/cloud saves and achievements unchanged.
    internal static class TestPersistenceGuard
    {
        internal static bool IndependentRun
        {
            get
            {
                return Array.IndexOf(Environment.GetCommandLineArgs(),"--hollow-mario-no-save")>=0;
            }
        }
        internal static void Arm(Harmony harmony)
        {
            if(!MeltyTest.Active&&!IndependentRun)return;
            if(MeltyTest.Active)
            {
                Type engineTest=AccessTools.TypeByName("MarioHollow.MeltyTest");
                MethodInfo engineReport=engineTest==null?null:AccessTools.Method(engineTest,"Report",new[]{typeof(string),typeof(bool),typeof(string)});
                if(engineReport!=null)harmony.Patch(engineReport,prefix:new HarmonyMethod(typeof(TestPersistenceGuard),nameof(NormalizeCheck)));
            }
            Type platform=AccessTools.TypeByName("DesktopPlatform");
            if(platform==null)throw new MissingMemberException("Test save protection could not find the desktop platform.");
            foreach(string name in new[]{"WriteSaveSlot","ClearSaveSlot"})
            {
                MethodInfo method=AccessTools.Method(platform,name);
                if(method==null)throw new MissingMethodException("Test save protection: "+name);
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(TestPersistenceGuard),nameof(SkipSave)));
            }
            foreach(string name in new[]{"PushAchievementUnlock","ResetAchievements","AddSocialStat"})
            {
                MethodInfo method=AccessTools.Method(platform,name);
                if(method!=null)harmony.Patch(method,prefix:new HarmonyMethod(typeof(TestPersistenceGuard),nameof(Skip)));
            }
            MeltyTest.Report("save-protected",true,null);
            if(IndependentRun)Plugin.Instance.Log.LogInfo("Independent host test: disk/cloud saves and achievements are protected.");
        }
        private static bool Skip() => false;
        private static void NormalizeCheck(ref string __0)
        {
            // Hiding the Knight belongs to Mario activation. Keep a failure as
            // a failure of that same check, rather than dropping any evidence.
            if(__0=="knight-hidden")__0="mario-on-m";
        }
        private static bool SkipSave(object[] __args)
        {
            // Report completion to the game's save UI, but perform no disk/cloud
            // write or deletion. This guard is never armed in ordinary play.
            foreach(object value in __args)if(value is Action<bool> callback)callback(true);
            return false;
        }
    }
}
