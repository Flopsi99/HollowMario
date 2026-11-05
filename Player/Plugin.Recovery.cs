using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {

        private void ArmMarioDefault()
        {
            try
            {
                defaultMarioWanted = AccessTools.Field(controllerType, "Wanted");
                MethodInfo awake = AccessTools.Method(controllerType, "Awake", Type.EmptyTypes);
                if (defaultMarioWanted == null || awake == null) throw new MissingMemberException("Mario's initial selection");
                harmony.Patch(awake, postfix: new HarmonyMethod(typeof(Plugin), nameof(DefaultMarioPostfix)));
                // The required base plugin normally created its controller before
                // our Awake. Future controllers get the same default via their Awake.
                object controller = knightFollow.Controller();
                if (controller != null) DefaultMarioPostfix(controller);
            }
            catch (Exception error) { Log.LogWarning("Mario's default selection could not be set: " + error.GetBaseException().Message); }
        }

        private static void DefaultMarioPostfix(object __instance)
        {
            // Initial preference only. The base controller still owns readiness,
            // activation and M's normal Toggle/SetWanted behavior afterwards.
            defaultMarioWanted.SetValue(__instance, true);
        }

        private void Update()
        {
            if(LakituDialogue.Active)return;
            if (!DebugSettings.Enabled) return;
            if (DebugKeyPressed(KeyCode.F8))DebugSettings.ToggleBossHp();
            if (DebugKeyPressed(KeyCode.F7))DebugSettings.ToggleHitboxes();
            if (DebugSettings.RoomReset && DebugKeyPressed(KeyCode.F5))
            {
                ResetRoom();
                return;
            }
            if (DebugSettings.BenchReturn && DebugKeyPressed(KeyCode.F9))
            {
                BackToBench();
            }
        }

        private static bool DebugKeyPressed(KeyCode key)
        {
            return ApiModSettings.Pressed(key);

        }

        private void BackToBench()
        {
            IEnumerator routine = null;
            try
            {
                if (benchReturn == null) benchReturn = new BenchReturn();
                if (benchReturn.Running) { BenchReturnNotice("The bench return is already in progress.", false); return; }
                Type gameType = AccessTools.TypeByName("GameManager");
                object game = gameType == null ? null
                    : gameType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                        ?? gameType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                routine = benchReturn.Request(game, () => Time.unscaledTime,
                    () => DebugSettings.BenchReturn, PrepareBenchReturn, BenchReturnNotice);
                if (routine != null) StartCoroutine(routine);
            }
            catch (Exception exception)
            {
                if (routine != null) benchReturn.Cancel(routine);
                BenchReturnNotice("F9 error: " + exception.GetBaseException().Message, true);
            }
        }

        private void PrepareBenchReturn()
        {
            FocusHeal.Remove();
            MushroomFallGuard.Forget();
            CapControls.Close();
            object controller = knightFollow?.Controller();
            if (controller != null && knightFollow.MarioAlive(controller))
                AccessTools.Method(controllerType, "Deactivate").Invoke(controller, null);
            // Use the existing handover/park cleanup, retaining Wanted/M choice.
            KnightFocus.Forget();
        }

        private void BenchReturnNotice(string text, bool failed)
        {
            if (failed) Log.LogWarning(text); else Log.LogMessage(text);
            CapHud.Message(text.StartsWith("F9 error:", StringComparison.Ordinal)
                ? "The bench return couldn't start. Try again after loading finishes." : text);
        }

        private void ResetRoom()
        {
            try
            {
                Type gameType = AccessTools.TypeByName("GameManager");
                object game = gameType == null ? null
                    : gameType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                        ?? gameType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                if (game == null)
                {
                    return;
                }
                FieldInfo stateField = gameType.GetField("gameState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (stateField != null && Convert.ToInt32(stateField.GetValue(game)) != 4)
                {
                    return;
                }
                object controller = knightFollow?.Controller();
                if (!ReturnToRoomEntry(controller))
                {
                    Log.LogInfo("F5: waiting for Mario's first landing in this room, or a native transition is still in progress.");
                }
            }
            catch (Exception exception)
            {
                Log.LogWarning("the room-entry point couldn't be reached: " + exception.GetBaseException().Message);
            }
        }

        // Manual F5 and automatic out-of-bounds recovery share one teleport and
        // its cleanup. Neither owns another checkpoint or a fallback destination.
        internal bool ReturnToRoomEntry(object controller)
        {
            try
            {
                int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
                if (knightFollow == null || !knightFollow.ReturnToRoomEntry(controller, scene)) return false;
                DiveSlideFollowup.Forget();
                QuickSlash.Forget();
                // Discard old location/attack/movement bookkeeping through its
                // existing owners. None of these can replace the fixed entry point.
                CapSystem.Stop();
                CapControls.Close();
                // A direct teleport has no Activate afterwards. Cancel the old
                // thorn burst while keeping Crest/Thorns bound for later play.
                if (controller != null && knightFollow.MarioAlive(controller)) NativeCharmEffects.Activated(controller);
                else NativeCharmEffects.Forget();
                StartupHurtbox.Forget();
                KnightLook.Forget();
                ShadeLongJump.Cancel();
                ShadeGateCollision.Suspend();
                KnightFocus.Forget();
                MushroomFallGuard.Forget();
                MapFallFlourish.Forget();
                TerrainRecovery.Forget();
                VoidGuard.Forget();
                Swimming.Forget();
                DoubleJump.Forget();
                PoundLongJump.Forget();
                PlatformCarry.Forget();
                LiftCarry.Forget();
                ConveyorCarry.Forget();
                stompTickStart = null;
                if (controller != null && knightFollow.MarioAlive(controller)) knightFollow.KeepInteractable(controller);
                return true;
            }
            catch (Exception exception)
            {
                Log.LogWarning("the room-entry point couldn't be reached: " + exception.GetBaseException().Message);
                return false;
            }
        }

    }
}
