using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // A short native shade state at long-jump launch; the long jump's physics stay native.
    internal static class ShadeLongJump
    {
        private const uint LongJump = 0x03000888u;
        private const float ProtectionMultiplier = 2f, RechargeSeconds = 2f;
        private const float ReadyCueTime = .22f;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type heroType;
        private static FieldInfo cooldown, duration, recharge, state, shadow, sound, rechargeEffect;
        private static Type fsmType;
        private static PropertyInfo fsmName;
        private static MethodInfo sendEvent;
        private static object owner, ownedState, cueOwner, pendingRecharge;
        private static float remaining, cueRemaining, rechargeWindow;
        private static ManualLogSource log;
        private static int failures, launches;
        internal static bool Active => owner != null && remaining > 0f;

        internal static void Arm(Type controller, ManualLogSource logger)
        {
            log = logger;
            MarioAppearance.Configure(controller);
            fsmType = AccessTools.TypeByName("PlayMakerFSM");
            fsmName = fsmType?.GetProperty("FsmName", Any);
            sendEvent = fsmType?.GetMethod("SendEvent", Any, null, new[] { typeof(string) }, null);
            NativeMovementGates.ActionInitialized += ActionChanged;
        }

        private static bool Cache(object hero)
        {
            if (hero == null) return false;
            if (hero.GetType() != heroType)
            {
                heroType = hero.GetType();
                cooldown = heroType.GetField("shadowDashTimer", Any);
                // The installed Knight's Dash() ends at DASH_TIME; ShadowDash() is empty.
                duration = heroType.GetField("DASH_TIME", Any);
                recharge = heroType.GetField("SHADOW_DASH_COOLDOWN", Any);
                state = heroType.GetField("cState", Any);
                shadow = state?.FieldType.GetField("shadowDashing", Any);
                sound = heroType.GetField("shadowDashClip", Any);
                rechargeEffect = heroType.GetField("shadowRechargePrefab", Any);
            }
            return cooldown != null && duration != null && recharge != null && shadow != null;
        }

        internal static void ActionChanged(uint action)
        {
            try
            {
                if (action != LongJump) { EndPhase(); return; }
                if (Active) return;
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                object controller = follow?.Controller(), hero = follow?.Hero(controller);
                if (controller == null || !follow.MarioAlive(controller) || !Cache(hero)
                    || !MovementUnlocks.HasShade(hero) || ShadeGateCollision.ExtraPassageActive
                    || Convert.ToSingle(cooldown.GetValue(hero)) > 0f) return;
                object flags = state.GetValue(hero);
                if (flags == null || (bool)shadow.GetValue(flags)) return;
                float time = Convert.ToSingle(duration.GetValue(hero)) * ProtectionMultiplier;
                float nativeWait = Convert.ToSingle(recharge.GetValue(hero));
                float wait = RechargeSeconds;
                if (time <= 0f || wait <= 0f || float.IsNaN(time) || float.IsNaN(wait)) return;
                StopReadyCue();
                owner = hero; ownedState = flags; remaining = time;
                cooldown.SetValue(hero, wait);
                shadow.SetValue(flags, true);
                // Do not set dashing, invulnerable or isInvincible and never disable the body.
                Tint(controller);
                ShadeGateCollision.Open(controller, follow);
                if (hero is Component part && sound?.GetValue(hero) is AudioClip clip)
                    part.GetComponent<AudioSource>()?.PlayOneShot(clip);
                pendingRecharge = hero; rechargeWindow = nativeWait;
                if (launches++ < 8) log?.LogInfo("Shade long jump: protection=" + time.ToString("0.000")
                    + "s, recharge=" + wait.ToString("0.000") + "s from launch; body collision retained.");
            }
            catch (Exception error) { Cancel(); Warn(error); }
        }

        // Called in the existing LateUpdate beside the native parry-timer mirror. The
        // Knight advances this same field itself whenever its own Update is enabled.
        internal static void Tick(object controller, KnightResetFollow follow, float dt)
        {
            try
            {
                object hero = follow?.Hero(controller);
                if (controller == null || !follow.MarioAlive(controller)) { AdvanceRechargeEffect(); Cancel(); return; }
                if (!Cache(hero)) return;
                if (hero is Behaviour behaviour && behaviour.enabled) { AdvanceRechargeEffect(); Cancel(); return; }
                if (cueOwner != null)
                {
                    cueRemaining -= Mathf.Max(0f, dt);
                    if (!ReferenceEquals(cueOwner, hero) || !MovementUnlocks.HasShade(hero) || cueRemaining <= 0f)
                        StopReadyCue();
                }
                if (dt > 0f)
                {
                    float before = Convert.ToSingle(cooldown.GetValue(hero));
                    float after = Mathf.Max(0f, before - dt);
                    cooldown.SetValue(hero, after);
                    if (before > 0f && after == 0f && MovementUnlocks.HasShade(hero) && !Active)
                    {
                        cueOwner = hero; cueRemaining = ReadyCueTime;
                        Tint(controller, true);
                    }
                }
                AdvanceRechargeEffect();
                if (!Active) return;
                if (!ReferenceEquals(owner, hero) || !MovementUnlocks.HasShade(hero)
                    || follow.Action(controller) != LongJump || !(bool)shadow.GetValue(ownedState)) { EndPhase(); return; }
                remaining -= Mathf.Max(0f, dt);
                if (remaining <= 0f) EndPhase();
            }
            catch (Exception error) { Cancel(); Warn(error); }
        }

        private static void AdvanceRechargeEffect()
        {
            object hero = pendingRecharge;
            if (hero == null) return;
            try
            {
                float wait = Convert.ToSingle(cooldown.GetValue(hero));
                if (!MovementUnlocks.HasShade(hero) || wait <= 0f) { pendingRecharge = null; return; }
                if (wait > rechargeWindow) return;
                // The native routine contains its own original recharge delay/audio.
                // Start it in the final native-length window, without editing its FSM
                // or the Knight's constants. Its cue then follows the longer timer.
                pendingRecharge = null;
                StartRechargeEffect(hero);
            }
            catch (Exception error) { pendingRecharge = null; Warn(error); }
        }

        private static void StartRechargeEffect(object hero)
        {
            try
            {
                // This is the same routine the native dash starts. Its own AudioPlay
                // actions play both recharge sounds; no second manually timed sound.
                if (!(rechargeEffect?.GetValue(hero) is GameObject effect) || fsmType == null || sendEvent == null) return;
                effect.SetActive(true);
                foreach (Component fsm in effect.GetComponents(fsmType))
                    if (Convert.ToString(fsmName?.GetValue(fsm, null)) == "Recharge Effect")
                    { sendEvent.Invoke(fsm, new object[] { "RESET" }); break; }
            }
            catch (Exception error) { Warn(error); }
        }

        private static void Tint(object controller, bool ready = false)
        {
            if(ready)MarioAppearance.Ready(controller,true);
            else MarioAppearance.Shade(controller,true);
        }

        internal static void Refill(object controller,KnightResetFollow follow)
        {
            object hero=follow?.Hero(controller);
            if(hero==null||!Cache(hero))return;
            Cancel();cooldown.SetValue(hero,0f);pendingRecharge=null;
            if(MovementUnlocks.HasShade(hero))
            {cueOwner=hero;cueRemaining=ReadyCueTime;Tint(controller,true);}
        }

        internal static void Cancel()
        {
            EndPhase();
            StopReadyCue();
        }

        private static void EndPhase()
        {
            if (ownedState == null) return;
            object flags = ownedState;
            owner = ownedState = null; remaining = 0f;
            try { if (flags != null) shadow?.SetValue(flags, false); }
            catch (Exception error) { Warn(error); }
            MarioAppearance.Shade(null,false);
            // The native recharge field is deliberately preserved through every handover.
        }

        private static void StopReadyCue()
        {
            if (cueOwner == null) return;
            cueOwner = null; cueRemaining = 0f;
            MarioAppearance.Ready(null,false);
        }

        private static void Warn(Exception error)
        {
            if (failures++ < 4) log?.LogWarning("Shade long jump: " + error.GetBaseException().Message);
        }

        internal static void Dispose() { Cancel(); pendingRecharge = null; NativeMovementGates.ActionInitialized -= ActionChanged; }
    }
}
