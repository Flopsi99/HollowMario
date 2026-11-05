using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Supply the disabled HeroController's look flags just before its native camera reads
    // them. The game still owns camera offset, smoothing, room limits and locked areas.
    internal static class KnightLook
    {
        private static FieldInfo cameraHero, lookTimer, lookDelay, lookingUp, lookingDown;
        private static FieldInfo targetHero, falling,currentVelocity;
        private static MethodInfo resetLook;
        private static object ownedHero;
        private static bool capCaptured;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony, ManualLogSource logger)
        {
            log = logger;
            try
            {
                Type camera = AccessTools.TypeByName("CameraController");
                cameraHero = AccessTools.Field(camera, "hero_ctrl");
                Type hero = cameraHero?.FieldType;
                Type flags = AccessTools.Field(hero, "cState")?.FieldType;
                Type target = AccessTools.TypeByName("CameraTarget");
                targetHero = AccessTools.Field(target, "hero_ctrl");
                currentVelocity=AccessTools.Field(hero,"current_velocity");
                falling = AccessTools.Field(flags, "falling");
                lookTimer = AccessTools.Field(hero, "lookDelayTimer");
                lookDelay = AccessTools.Field(hero, "LOOK_DELAY");
                lookingUp = AccessTools.Field(flags, "lookingUp");
                lookingDown = AccessTools.Field(flags, "lookingDown");
                resetLook = AccessTools.Method(hero, "ResetLook");
                MethodInfo tick = AccessTools.Method(camera, "LateUpdate");
                MethodInfo targetTick = AccessTools.Method(target, "Update");
                if (cameraHero == null || lookTimer == null || lookDelay == null || lookingUp == null
                    || lookingDown == null || resetLook == null || tick == null || targetHero == null
                    || falling == null || targetTick == null)
                    throw new MissingMemberException("the game's camera look controls weren't found");
                CapControls.Changed += CapChanged;
                harmony.Patch(tick, prefix: new HarmonyMethod(typeof(KnightLook), nameof(BeforeCamera)));
                harmony.Patch(targetTick,
                    prefix:new HarmonyMethod(typeof(KnightLook),nameof(BeforeTarget)),
                    finalizer:new HarmonyMethod(typeof(KnightLook),nameof(AfterTarget)));
            }
            catch (Exception error) { Warn(error); }
        }

        private static void CapChanged() { if (CapControls.Open) capCaptured = true; }

        internal struct FallState { internal object Flags,Hero; internal bool Falling; internal Vector2 Velocity; }
        private static void BeforeTarget(object __instance,out FallState __state)
        {
            __state=default;
            try
            {
                KnightResetFollow follow=Plugin.Instance?.knightFollow;
                object controller=follow?.Controller();
                object hero=controller==null?null:follow.Hero(controller);
                if(hero==null||!ReferenceEquals(hero,targetHero.GetValue(__instance))
                    ||!follow.MarioAlive(controller)||(hero is Behaviour native&&native.enabled))return;
                object flags=CapAccess.Read(hero,"cState");
                if(CapAccess.Flag(flags,"transitioning")||CapAccess.Flag(flags,"dead")
                    ||CapAccess.Flag(flags,"hazardDeath")||CapAccess.Flag(flags,"hazardRespawning"))return;
                __state=PrepareFalling(__instance,flags,
                    IsCameraFalling(follow.Grounded(controller),follow.VerticalSpeed(controller),follow.Action(controller)));
                if(currentVelocity!=null)
                {
                    __state.Hero=hero;__state.Velocity=(Vector2)currentVelocity.GetValue(hero);
                    currentVelocity.SetValue(hero,new Vector2(follow.SideSpeed(controller),follow.VerticalSpeed(controller))*(30f*.0105f));
                }
            }
            catch(Exception error){Warn(error);}
        }
        internal static bool IsCameraFalling(bool grounded,float vertical,uint action)
            =>!grounded&&vertical<0f&&!Swimming.UsesSwimControls(action);
        internal static FallState PrepareFalling(object target,object flags,bool value)
        {
            if(flags==null)return default;
            var state=new FallState{Flags=flags,Falling=(bool)falling.GetValue(flags)};
            falling.SetValue(flags,value);
            // The native target owns its descent latch, smoothing and reset.
            // Supply motion, rather than changing its internal camera state.
            return state;
        }
        internal static void AfterTarget(FallState __state)
        {
            // Supply current motion only to the camera. Other Knight routines and
            // interactions keep their own state, even if the camera tick throws.
            if(__state.Flags!=null)falling.SetValue(__state.Flags,__state.Falling);
            if(__state.Hero!=null)currentVelocity.SetValue(__state.Hero,__state.Velocity);
        }

        internal static void BeforeCamera(object __instance)
        {
            try
            {
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                object controller = follow?.Controller();
                object hero = controller == null ? null : follow.Hero(controller);
                if (hero != null && !ReferenceEquals(hero, cameraHero.GetValue(__instance))) return;
                if (controller == null || !follow.MarioAlive(controller) || hero == null
                    || (hero is Behaviour native && native.enabled)) { Clear(); return; }

                object handler = CapAccess.Read(hero, "inputHandler");
                object actions = CapAccess.Read(handler, "inputActions");
                object modifier = CapControls.Action;
                bool capInput = CapControls.Open || CapAccess.Flag(modifier, "IsPressed")
                    || CapAccess.Flag(modifier, "WasPressed");
                if (capInput) capCaptured = true;
                if (capCaptured)
                {
                    object stick = CapAccess.Read(actions, "rightStick");
                    bool neutral = !Held(actions, "up") && !Held(actions, "down")
                        && !Held(actions, "rs_up") && !Held(actions, "rs_down")
                        && Mathf.Abs(CapAccess.Number(stick, "X")) < .25f
                        && Mathf.Abs(CapAccess.Number(stick, "Y")) < .25f;
                    if (!capInput && neutral) capCaptured = false;
                    Clear(); return;
                }

                // Match the Knight's idle-only look. Mario's swim, attack and stun actions
                // cannot inherit an idle Knight flag just because his hidden body is still.
                const uint swimAttackOrStun = 0x00826000u; // Swimming, Metal-water, attacking, invulnerable action.
                if (actions == null || Time.timeScale <= 0f || FocusHeal.Frozen || follow.Busy(controller)
                    || follow.ControlHeld(controller) || !follow.AcceptingInput(controller)
                    || !CapAccess.Flag(handler, "acceptingInput") || !follow.Grounded(controller)
                    || Mathf.Abs(follow.ForwardSpeed(controller)) > 1f
                    || (follow.Action(controller) & swimAttackOrStun) != 0u) { Clear(); return; }

                bool up = Held(actions, "up") || Held(actions, "rs_up");
                bool down = !up && (Held(actions, "down") || Held(actions, "rs_down"));
                if (!up && !down) { Clear(); return; }
                if (!ReferenceEquals(ownedHero, hero))
                {
                    Clear();
                    ownedHero = hero;
                    resetLook.Invoke(hero, null);
                }
                float elapsed = (float)lookTimer.GetValue(hero);
                bool ready = elapsed >= (float)lookDelay.GetValue(hero)
                    || Held(actions, up ? "rs_up" : "rs_down");
                if (!ready) lookTimer.SetValue(hero, elapsed + Time.deltaTime);
                object flags = CapAccess.Read(hero, "cState");
                lookingUp.SetValue(flags, ready && up);
                lookingDown.SetValue(flags, ready && down);
            }
            catch (Exception error) { Clear(); Warn(error); }
        }

        private static bool Held(object actions, string name) => CapAccess.Flag(CapAccess.Read(actions, name), "IsPressed");
        private static void Clear()
        {
            object previous = ownedHero;
            ownedHero = null;
            if (previous == null) return;
            try { resetLook?.Invoke(previous, null); }
            catch (Exception error) { Warn(error); }
        }
        internal static void Forget() { Clear(); capCaptured = false; }
        internal static void Dispose() { CapControls.Changed -= CapChanged; Forget(); }
        private static void Warn(Exception error)
        { if (failures++ < 4) log?.LogWarning("Mario camera look: " + error.GetBaseException().Message); }
    }
}
