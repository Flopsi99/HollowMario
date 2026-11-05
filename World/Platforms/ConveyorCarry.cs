using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HollowMario
{
    // Hollow Knight gives a belt's speed to the Knight after his normal movement has already run.
    // Mario gets a post-movement step that gently approaches 70% of the game's belt speed.
    // Only this added belt contribution ramps up; Mario's own movement remains independent.
    internal static class ConveyorCarry
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const uint AirborneAction = 0x800u;
        private const float TickSeconds = 1f / 30f;
        private const float WorldScale = 0.0105f;
        private const float BeltStrength = 0.7f;
        private const float RampSeconds = 0.5f;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Collider2D[] nearby = new Collider2D[24];

        private static FieldInfo beltSpeedField;
        private static FieldInfo beltVerticalField;
        private static Type beltType;
        private static float appliedBeltSpeed;
        private static float takeoffSpeed;
        private static bool hasTakeoffSpeed;
        private static float tickStartX;
        private static bool hasTickStart;
        private static FieldInfo marioState;
        private static FieldInfo marioId;
        private static FieldInfo velocityX;
        private static FieldInfo velocityY;
        private static FieldInfo velocityZ;
        private static FieldInfo faceAngle;
        private static FieldInfo forwardVelocity;
        private static MethodInfo setVelocity;
        private static MethodInfo setForward;
        private static SetForwardDelegate setForwardNative;
        private static object nativeSync;
        private static float reportAt;
        private static int errors;
        private static int ticks;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetForwardDelegate(int id, float speed);

        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string module);

        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal static void BeforeMarioTick(object controller, KnightResetFollow follow)
        {
            Vector3? ground = follow == null ? null : follow.Ground(controller);
            hasTickStart = ground.HasValue;
            if (hasTickStart)
            {
                tickStartX = ground.Value.x;
            }
        }

        internal static void AfterMarioTick(object controller, KnightResetFollow follow)
        {
            if (controller == null || follow == null)
            {
                return;
            }
            try
            {
                // The platform callers prepare this shared helper before using it. A conveyor
                // can be reached without ever riding a platform, so it must prepare it too.
                AutoMove.Prepare(controller);
                ticks++;
                bool airborne = (follow.Action(controller) & AirborneAction) != 0u;
                float speed = 0f;
                bool onBelt = false;
                bool hasPad = Plugin.TryGetFeetPadBounds(out Bounds reach);
                if (!airborne && hasPad)
                {
                    onBelt = BeltInside(reach, out speed);
                }
                string mode;
                float step = 0f;
                Vector3? before = follow.Ground(controller);
                if (onBelt)
                {
                    float targetSpeed = speed * BeltStrength;
                    appliedBeltSpeed = Mathf.MoveTowards(appliedBeltSpeed, targetSpeed,
                        Mathf.Abs(targetSpeed) * TickSeconds / RampSeconds);
                    step = appliedBeltSpeed * TickSeconds;
                    // Remember Mario's actual combined travel over this grounded tick. The belt
                    // is already present in that total; takeoff must copy it, not add it again.
                    hasTakeoffSpeed = hasTickStart && before.HasValue;
                    if (hasTakeoffSpeed)
                    {
                        takeoffSpeed = (before.Value.x - tickStartX + step) / TickSeconds;
                    }
                    mode = "belt";
                }
                else if (airborne && hasTakeoffSpeed)
                {
                    // Copy the complete ground speed at departure exactly once, including zero.
                    // From this point on the normal SM64 air update owns the velocity, steering,
                    // drag, wall contact, and any later changes of action.
                    float transfer = takeoffSpeed;
                    appliedBeltSpeed = 0f;
                    hasTakeoffSpeed = false;
                    takeoffSpeed = 0f;
                    ApplyTakeoff(controller, transfer);
                    mode = "takeoff transfer";
                }
                else
                {
                    appliedBeltSpeed = 0f;
                    hasTakeoffSpeed = false;
                    takeoffSpeed = 0f;
                    mode = "no belt";
                }

                hasTickStart = false;
                if (Mathf.Abs(step) > 0.000001f)
                {
                    AutoMove.Shift(controller, step, 0f);
                    // Follow already ran inside Mario's tick. Keep the stand-in at the corrected
                    // position, otherwise reset-follow can mistake the belt step for a teleport.
                    Transform hero = follow.HeroTransform(controller);
                    if (hero != null)
                    {
                        hero.position += new Vector3(step, 0f, 0f);
                        Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
                        if (body != null)
                        {
                            body.position = new Vector2(hero.position.x, hero.position.y);
                        }
                    }
                }
                Vector3? after = follow.Ground(controller);
                if (Time.unscaledTime >= reportAt && Plugin.Instance != null)
                {
                    reportAt = Time.unscaledTime + 1f;
                    if(DebugSettings.VerboseLogging&&(Mathf.Abs(speed)>.0001f||Mathf.Abs(appliedBeltSpeed)>.0001f))
                    {
                    string actual = before.HasValue && after.HasValue
                        ? (after.Value.x - before.Value.x).ToString("0.0000") : "unavailable";
                    Plugin.Instance.Log.LogInfo("conveyor trace: ticks=" + ticks + " mode=" + mode
                        + " action=" + follow.Action(controller).ToString("X") + " pad=" + hasPad
                        + " rawSpeed=" + speed.ToString("0.000") + " appliedSpeed=" + appliedBeltSpeed.ToString("0.000")
                        + " requestedStep=" + step.ToString("0.0000")
                        + " appliedStep=" + actual + " ground=" + before + " padCenter=" + reach.center
                        + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    }
                    ticks = 0;
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Instance != null && errors++ < 5)
                {
                    Plugin.Instance.Log.LogWarning("conveyor carry failed: " + exception);
                }
            }
        }

        internal static void Forget()
        {
            appliedBeltSpeed = 0f;
            takeoffSpeed = 0f;
            hasTakeoffSpeed = false;
            hasTickStart = false;
            reportAt = 0f;
            ticks = 0;
            errors = 0;
        }

        private static void ApplyTakeoff(object controller, float groundSpeed)
        {
            if (marioState == null)
            {
                Type type = controller.GetType();
                marioState = type.GetField("State", Any);
                marioId = type.GetField("MarioId", Any);
                Type stateType = marioState?.FieldType;
                velocityX = stateType?.GetField("vx", Any);
                velocityY = stateType?.GetField("vy", Any);
                velocityZ = stateType?.GetField("vz", Any);
                faceAngle = stateType?.GetField("faceAngle", Any);
                forwardVelocity = stateType?.GetField("forwardVelocity", Any);
                Type native = type.Assembly.GetType("MarioHollow.Native", false);
                setVelocity = native?.GetMethod("sm64_set_mario_velocity", Static);
                setForward = native?.GetMethod("sm64_set_mario_forward_velocity", Static);
                nativeSync = native?.GetField("Sync", Static)?.GetValue(null) ?? new object();
                if (setForward == null)
                {
                    // The base mod does not wrap this export, but its bundled SM64 engine has
                    // it. Writing vx alone would be overwritten from forwardVel next air tick.
                    IntPtr module = GetModuleHandle("sm64.dll");
                    IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "sm64_set_mario_forward_velocity");
                    if (entry != IntPtr.Zero)
                    {
                        setForwardNative = (SetForwardDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(SetForwardDelegate));
                    }
                }
            }
            if (marioState == null || marioId == null || velocityX == null || velocityY == null || velocityZ == null
                || faceAngle == null || forwardVelocity == null || setVelocity == null
                || (setForward == null && setForwardNative == null))
            {
                throw new InvalidOperationException("Mario's takeoff momentum setters were not found.");
            }
            object state = marioState.GetValue(controller);
            int id = Convert.ToInt32(marioId.GetValue(controller));
            float oldX = Convert.ToSingle(velocityX.GetValue(state));
            float vx = groundSpeed * TickSeconds / WorldScale;
            float vy = Convert.ToSingle(velocityY.GetValue(state));
            float vz = Convert.ToSingle(velocityZ.GetValue(state));
            float angle = Convert.ToSingle(faceAngle.GetValue(state));
            // Preserve facing and vertical speed. Signed forward speed also supports taking off
            // backwards if the current overcomes Mario's own movement in the other direction.
            float forward = vx * (float)Math.Sin(angle) + vz * (float)Math.Cos(angle);
            lock (nativeSync)
            {
                if (setForward != null)
                {
                    setForward.Invoke(null, new object[] { id, forward });
                }
                else
                {
                    setForwardNative(id, forward);
                }
                setVelocity.Invoke(null, new object[] { id, vx, vy, vz });
            }
            velocityX.SetValue(state, vx);
            forwardVelocity.SetValue(state, forward);
            marioState.SetValue(controller, state);
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Log.LogInfo("conveyor takeoff: measuredGroundSpeed=" + groundSpeed.ToString("0.000")
                    + " horizontalBefore=" + (oldX * WorldScale / TickSeconds).ToString("0.000")
                    + " horizontalAfter=" + (vx * WorldScale / TickSeconds).ToString("0.000")
                    + "; ground speed copied once, no additional belt impulse.");
            }
        }

        private static bool BeltInside(Bounds reach, out float speed)
        {
            speed = 0f;
            int count = Physics2D.OverlapBoxNonAlloc(reach.center, reach.size, 0f, nearby, Physics2D.AllLayers);
            for (int i = 0; i < count; i++)
            {
                Collider2D shape = nearby[i];
                nearby[i] = null;
                if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (TryReadBelt(shape.transform, out speed))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadBelt(Transform from, out float speed)
        {
            speed = 0f;
            for (Transform current = from; current != null; current = current.parent)
            {
                MonoBehaviour[] scripts;
                try
                {
                    scripts = current.GetComponents<MonoBehaviour>();
                }
                catch (Exception)
                {
                    continue;
                }
                for (int i = 0; i < scripts.Length; i++)
                {
                    MonoBehaviour script = scripts[i];
                    if (script == null)
                    {
                        continue;
                    }
                    string typeName = script.GetType().Name;
                    if (typeName != "ConveyorBelt" && typeName != "ConveyorZone")
                    {
                        continue;
                    }
                    CacheBelt(script.GetType());
                    if (beltSpeedField == null || beltVerticalField == null
                        || beltVerticalField.GetValue(script) is not bool vertical || vertical)
                    {
                        continue;
                    }
                    speed = Convert.ToSingle(beltSpeedField.GetValue(script));
                    return Mathf.Abs(speed) > 0.0001f;
                }
            }
            return false;
        }

        private static void CacheBelt(Type type)
        {
            if (type == beltType)
            {
                return;
            }
            beltType = type;
            beltSpeedField = type.GetField("speed", Any);
            beltVerticalField = type.GetField("vertical", Any);
        }

    }
}
