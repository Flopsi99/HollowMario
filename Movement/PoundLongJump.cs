using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HollowMario
{
    // Ground-pound windup (or descent with Dashmaster) -> B -> the engine's long jump. The Knight's real airDashed
    // flag and CanDash gate own eligibility; only Mario's pre-pound motion is stored locally.
    internal static class PoundLongJump
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const uint Pound = 0x008008A9u;
        private const uint LongJump = 0x03000888u;
        private const uint WallKick = 0x03000886u;
        private const int StartPoundAnimation = 0x3C;
        private const int TriplePoundAnimation = 0x3B;
        private const int FallingPoundAnimation = 0x3D;
        private const float TickSeconds = 1f / 30f;
        private const float DirectionThreshold = 0.2f;

        private static MethodInfo setAction, setFace, setForward;
        private static SetForwardDelegate nativeForward;
        private static object nativeSync;
        private static Type stateType, inputType, heroType;
        private static FieldInfo stateAction, animation, vx, vy, vz, face, buttonB;
        private static FieldInfo airDashed, cooldown, normalCooldown, playerData, cState;
        private static FieldInfo wallSliding;
        private static MethodInfo canDash;
        private static bool lastB, saved, wasPound, hadWaterContact;
        private static Vector3 candidateVelocity, savedVelocity;
        private static float candidateFace, savedFace;
        private static uint lastResetAction;
        private static int logs, failures;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetForwardDelegate(int id, float speed);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal static bool Armed => setAction != null && setFace != null && (setForward != null || nativeForward != null);

        internal static void Arm(Type native)
        {
            if (native == null) return;
            setAction = native.GetMethod("sm64_set_mario_action", Static);
            setFace = native.GetMethod("sm64_set_mario_faceangle", Static);
            setForward = native.GetMethod("sm64_set_mario_forward_velocity", Static);
            nativeSync = native.GetField("Sync", Static)?.GetValue(null) ?? new object();
            if (setForward == null)
            {
                IntPtr module = GetModuleHandle("sm64.dll");
                IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "sm64_set_mario_forward_velocity");
                if (entry != IntPtr.Zero)
                    nativeForward = (SetForwardDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(SetForwardDelegate));
            }
        }

        internal static void Forget()
        {
            lastB = false;
            saved = false;
            wasPound = false;
            hadWaterContact = false;
            lastResetAction = 0u;
            logs = 0;
            failures = 0;
        }

        internal static void TickPrefix(object[] __args)
        {
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (!Armed || __args == null || __args.Length < 3 || controller == null
                || Convert.ToInt32(__args[0]) != follow.MarioId(controller)) return;
            try
            {
                object inputs = __args[1];
                object state = __args[2];
                if (inputs == null || state == null) return;
                CacheState(state.GetType());
                if (inputType != inputs.GetType())
                {
                    inputType = inputs.GetType();
                    buttonB = inputType.GetField("buttonB", Any);
                }
                if (buttonB == null) throw new MissingFieldException("Mario buttonB");
                bool held = Convert.ToBoolean(buttonB.GetValue(inputs));
                bool pressed = held && !lastB;
                lastB = held;
                Sync(controller, follow);
                object hero = follow.Hero(controller);
                CacheHero(hero);
                // HeroController.Update is disabled while Mario plays. Advance its real dash
                // cooldown here instead of introducing a second timer that can diverge from it.
                if (hero is Behaviour behaviour && !behaviour.enabled && cooldown != null)
                    cooldown.SetValue(hero, Mathf.Max(0f, Convert.ToSingle(cooldown.GetValue(hero)) - TickSeconds));

                if(CapAccess.Flag(controller,"DamagePending")) { saved=false; wasPound=false; return; }
                wasPound = Convert.ToUInt32(stateAction.GetValue(state)) == Pound;
                if (!wasPound)
                {
                    saved = false;
                    candidateVelocity = new Vector3(Convert.ToSingle(vx.GetValue(state)), Convert.ToSingle(vy.GetValue(state)), Convert.ToSingle(vz.GetValue(state)));
                    candidateFace = Convert.ToSingle(face.GetValue(state));
                    return;
                }
                int anim = Convert.ToInt32(animation.GetValue(state));
                // A real ground-pound startup is already an airborne, non-swimming action.
                // Nearby/sticky water contact (or the hidden Knight's swim flag) must not veto
                // that move after a surface jump or above a shallow puddle.
                bool cancelWindow = anim == StartPoundAnimation || anim == TriplePoundAnimation
                    || (anim == FallingPoundAnimation && MovementUnlocks.HasDashmaster(hero));
                if (!saved || !pressed || !cancelWindow
                    || !DashReady(hero)) return;

                int id = follow.MarioId(controller);
                float forward = savedVelocity.x * (float)Math.Sin(savedFace) + savedVelocity.z * (float)Math.Cos(savedFace);
                float launchFace = savedFace;
                float direction = DoubleJump.MoveInput();
                if (Mathf.Abs(direction) >= DirectionThreshold)
                {
                    // Use the same stick/buttons as the Wings reversal. Choose direction now,
                    // while keeping the saved speed magnitude from before the ground pound.
                    launchFace = direction > 0f ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;
                    forward = Mathf.Abs(forward);
                }
                lock (nativeSync)
                {
                    setFace.Invoke(null, new object[] { id, launchFace });
                    if (setForward != null) setForward.Invoke(null, new object[] { id, forward });
                    else nativeForward(id, forward);
                    // Let the engine apply its standard long-jump launch, including its normal
                    // horizontal multiplier/cap and upward speed, as if this speed were grounded.
                    setAction.Invoke(null, new object[] { id, LongJump });
                }
                airDashed.SetValue(hero, true);
                cooldown.SetValue(hero, Convert.ToSingle(normalCooldown.GetValue(hero)));
                saved = false;
                // B is consumed by this move. A stays untouched, including a continued hold.
                buttonB.SetValue(inputs, (byte)0);
                __args[1] = inputs;
                if (logs++ < 12)
                    Plugin.Instance.Log.LogInfo("Mario cancelled the ground pound into a long jump; saved velocity="
                        + savedVelocity + ", grounded forward speed=" + forward.ToString("0.00")
                        + ", held direction=" + direction.ToString("0.00")
                        + ", launch facing=" + launchFace.ToString("0.00") + "; Knight air dash spent.");
            }
            catch (Exception exception) { Failed(exception); }
        }

        // Read completed output by value. With this installed HarmonyX version, __args shared
        // with a prefix still contains the pre-tick ref structs; a postfix using it writes those
        // stale state/geometry values back and erases the entire native simulation result.
        internal static void TickPostfix(int __0, object __2)
        {
            if (!Armed || __2 == null) return;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || __0 != follow.MarioId(controller)) return;
            try
            {
                object state = __2;
                CacheState(state.GetType());
                uint action = Convert.ToUInt32(stateAction.GetValue(state));
                if (action == Pound && !wasPound)
                {
                    savedVelocity = candidateVelocity;
                    savedFace = candidateFace;
                    saved = true;
                }
                else if (action != Pound) saved = false;
            }
            catch (Exception exception) { Failed(exception); }
        }

        internal static void Sync(object controller, KnightResetFollow follow)
        {
            object hero = follow?.Hero(controller);
            if (hero == null) return;
            CacheHero(hero);
            uint action = follow.Action(controller);
            object state = cState?.GetValue(hero);
            bool sliding = state != null && wallSliding != null && Convert.ToBoolean(wallSliding.GetValue(state));
            bool waterContact = Swimming.TouchingLiquid;
            bool enteredWater = waterContact && !hadWaterContact;
            hadWaterContact = waterContact;
            // Entering water restores the shared dash once. Actual swimming also keeps it
            // ready, but a lingering probe/hidden-Knight flag cannot refund a spent air cancel.
            if (follow.Grounded(controller) || Swimming.UsesSwimControls(action) || enteredWater || sliding
                || (action == WallKick && lastResetAction != WallKick)) Reset(controller);
            lastResetAction = action;
        }

        internal static void Reset(object controller)
        {
            object hero = Plugin.Instance?.knightFollow?.Hero(controller);
            ResetHero(hero);
        }

        internal static void ResetHero(object hero)
        {
            if (hero == null) return;
            CacheHero(hero);
            airDashed?.SetValue(hero, false);
        }

        private static bool DashReady(object hero)
        {
            if (hero == null || airDashed == null || cooldown == null || normalCooldown == null
                || playerData == null || canDash == null) return false;
            object data = playerData.GetValue(hero);
            return data != null && MovementUnlocks.HasDash(hero)
                && !Convert.ToBoolean(airDashed.GetValue(hero)) && Convert.ToBoolean(canDash.Invoke(hero, null));
        }

        private static void CacheHero(object hero)
        {
            if (hero == null || heroType == hero.GetType()) return;
            heroType = hero.GetType();
            airDashed = heroType.GetField("airDashed", Any);
            cooldown = heroType.GetField("dashCooldownTimer", Any);
            normalCooldown = heroType.GetField("DASH_COOLDOWN", Any);
            canDash = heroType.GetMethod("CanDash", Any);
            playerData = heroType.GetField("playerData", Any);
            cState = heroType.GetField("cState", Any);
            wallSliding = cState?.FieldType.GetField("wallSliding", Any);
        }

        private static void CacheState(Type type)
        {
            if (stateType == type) return;
            stateType = type;
            stateAction = type.GetField("action", Any);
            animation = type.GetField("animID", Any);
            vx = type.GetField("vx", Any);
            vy = type.GetField("vy", Any);
            vz = type.GetField("vz", Any);
            face = type.GetField("faceAngle", Any);
        }

        private static void Failed(Exception exception)
        {
            if (failures++ < 5) Plugin.Instance?.Log.LogWarning("ground-pound long jump: " + exception);
        }
    }
}
