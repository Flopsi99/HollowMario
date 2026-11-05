using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // One physical liquid source supplies SM64's surface. A small geometric edge allowance
    // tolerates probe rounding; no timed latch or hidden-Knight swimming flag retains water.
    //
    // The water then has its own controls in SM64, and they are not the ones the original mod feeds
    // with: on land the stick's up is "forward" and its left/right is never used, which in water
    // means Mario cannot turn and has no up or down. Only during a SM64 swimming action are inputs
    // replaced by the ones swimming actually reads: left/right turns him, and holding up (or cast)
    // pitches him up while holding down (or dash) pitches him down. Jump is not touched, so it stays
    // the swim stroke it is in SM64, and with up held at the surface it is SM64's own jump out.
    internal static class Swimming
    {
        private const float NoWater = -1000000f;
        private const uint SwimmingActionFlag = 0x00002000u;
        private const uint ActionGroupMask = 0x000001C0u;
        private const uint SubmergedActionGroup = 0x000000C0u;
        private const uint WaterJump = 0x01000889u;
        private const uint HoldWaterJump = 0x010008A3u;
        private const uint NormalJump = 0x03000880u;
        private const uint HoldNormalJump = 0x030008A0u;
        private const int LogLimit = 12;
        private const int LogEvery = 30;

        private static MethodInfo setWaterLevel;
        private static Action<int, int> setIntegerLevel;
        private static Action<int, float> setFloatLevel;
        private static bool integerLevel;
        private static Collider2D[] nearbyLiquids = new Collider2D[64];
        private static readonly ContactFilter2D liquidFilter = new ContactFilter2D { useTriggers = true };
        private static object sensedHero;
        private static BoxCollider2D heroBox;
        private static MethodInfo setAction, setVelocity, setForward;
        private static SetForwardDelegate nativeForward;
        private static object nativeSync;
        private static FieldInfo stickX;
        private static FieldInfo stickY;
        private static FieldInfo camLookX;
        private static FieldInfo camLookZ;
        private static FieldInfo marioAction;
        private static float surface = float.NaN;
        private static bool wasInWater;
        private static bool acidSurface;
        private static Collider2D liquid;
        private static int sinceLog;
        private static int logs;
        private static bool wroteInputs;
        private static int exitLogs;
        private static FieldInfo marioWanted;
        private static PropertyInfo waterFsmName, waterFsmOwner, waterStateName;
        private static MethodInfo findWaterState;
        private static PropertyInfo positionFsm;
        private static FieldInfo positionTarget;
        private static MethodInfo resolvePositionTarget,finishPosition;
        private static int positionLogs;

        internal static MethodInfo ArmKnightWater(Type controller, Type fsm)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            marioWanted = controller?.GetField("Wanted", any);
            waterFsmName = fsm?.GetProperty("Name", any);
            waterFsmOwner = fsm?.GetProperty("GameObject", any);
            findWaterState = fsm?.GetMethod("GetState", any, null, new[] { typeof(string) }, null);
            waterStateName = findWaterState?.ReturnType.GetProperty("Name", any);
            if (marioWanted == null || waterFsmName == null || waterFsmOwner == null || waterStateName == null) return null;
            return fsm.GetMethod("SwitchState", any, null, new[] { findWaterState.ReturnType }, null);
        }

        // The native Surface Water FSM owns the Knight's swim animation, velocity and
        // control lock. In Mario mode it must never enter Take Control in the first place.
        // Redirect through its normal idle state so PlayMaker clears its pending transition;
        // merely skipping SwitchState would leave UpdateStateChanges looping forever.
        internal static void BeforeKnightWaterState(object __instance, ref object __0)
        {
            // Installed HarmonyX writes ref arguments back, but not replacements of a
            // by-value class parameter through __args. Use the actual argument by reference.
            if (__0 == null
                || Convert.ToString(waterStateName.GetValue(__0, null)) != "Take Control"
                || Convert.ToString(waterFsmName.GetValue(__instance, null)) != "Surface Water") return;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || !(marioWanted.GetValue(controller) is bool wanted && wanted)) return;
            Component knight = follow.Hero(controller) as Component;
            if (knight != null && ReferenceEquals(waterFsmOwner.GetValue(__instance, null), knight.gameObject))
                __0 = findWaterState.Invoke(__instance, new object[] { "Inactive" });
        }

        internal static MethodInfo ArmWaterPosition(Type action)
        {
            const BindingFlags any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            positionFsm=action?.GetProperty("Fsm",any);positionTarget=action?.GetField("gameObject",any);
            finishPosition=action?.GetMethod("Finish",any,null,Type.EmptyTypes,null);
            resolvePositionTarget=positionTarget==null?null:positionFsm?.PropertyType.GetMethod("GetOwnerDefaultTarget",any,null,new[]{positionTarget.FieldType},null);
            if(marioWanted==null||waterFsmName==null||positionFsm==null||resolvePositionTarget==null||finishPosition==null)return null;
            return action.GetMethod("OnEnter",any);
        }

        // The region's splash action snaps the Knight to its surface BEFORE the Knight
        // swimming FSM takes control. That move looks like a respawn to reset-follow and
        // removes caps. Finish only this position action; retain the splash/state flow.
        internal static bool BeforeWaterPosition(object __instance)
        {
            object fsm=positionFsm.GetValue(__instance,null);
            if(fsm==null||!Equals(waterFsmName.GetValue(fsm,null),"Surface Water Region"))return true;
            KnightResetFollow follow=Plugin.Instance?.knightFollow;object controller=follow?.Controller();
            if(controller==null||!follow.MarioAlive(controller)||!(marioWanted.GetValue(controller) is bool wanted&&wanted))return true;
            Component hero=follow.Hero(controller) as Component;
            if(hero==null||!ReferenceEquals(resolvePositionTarget.Invoke(fsm,new[]{positionTarget.GetValue(__instance)}),hero.gameObject))return true;
            finishPosition.Invoke(__instance,null);
            if(positionLogs++<4)Plugin.Instance?.Log.LogInfo("Water entry kept Mario's position; the splash cannot snap the Knight to the surface.");
            return false;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetForwardDelegate(int id, float speed);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal static bool Armed => setWaterLevel != null;

        internal static bool InWater => !float.IsNaN(surface);
        internal static bool TouchingLiquid => touchingLiquid;

        internal static MethodInfo TickMethod { get; private set; }

        internal static void Arm(Type nativeType)
        {
            setWaterLevel = nativeType == null ? null : Find(nativeType, "sm64_set_mario_water_level", 2);
            setIntegerLevel = null;
            setFloatLevel = null;
            if (setWaterLevel != null)
            {
                integerLevel = setWaterLevel.GetParameters()[1].ParameterType == typeof(int);
                try
                {
                    if (integerLevel) setIntegerLevel = (Action<int, int>)Delegate.CreateDelegate(typeof(Action<int, int>), setWaterLevel);
                    else setFloatLevel = (Action<int, float>)Delegate.CreateDelegate(typeof(Action<int, float>), setWaterLevel);
                }
                catch (ArgumentException) { } // Retain the existing reflective fallback for other bridges.
            }
            setAction = nativeType == null ? null : Find(nativeType, "sm64_set_mario_action", 2);
            setVelocity = nativeType == null ? null : Find(nativeType, "sm64_set_mario_velocity", 4);
            setForward = nativeType == null ? null : Find(nativeType, "sm64_set_mario_forward_velocity", 2);
            nativeSync = nativeType?.GetField("Sync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ?? new object();
            if (nativeType != null && setForward == null)
            {
                try
                {
                    IntPtr module = GetModuleHandle("sm64.dll");
                    IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "sm64_set_mario_forward_velocity");
                    if (entry != IntPtr.Zero)
                        nativeForward = (SetForwardDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(SetForwardDelegate));
                }
                catch (Exception) { }
            }
            TickMethod = nativeType == null ? null : Find(nativeType, "sm64_mario_tick", 4);
            if (TickMethod != null)
            {
                ParameterInfo[] parameters = TickMethod.GetParameters();
                if (parameters.Length < 2 || !parameters[1].ParameterType.IsByRef)
                {
                    TickMethod = null;
                }
            }
        }

        internal static void Forget()
        {
            surface = float.NaN;
            wasInWater = false;
            acidSurface = false;
            liquid = null;
            sinceLog = 0;
            logs = 0;
            wroteInputs = false;
            exitLogs = 0;
            sensedHero = null;
            heroBox = null;
            Array.Clear(nearbyLiquids, 0, nearbyLiquids.Length);
            touchingLiquid = false;
            SurfaceSoul.Reset();
        }

        // The game's liquids are Water Surface triggers, plus Acid after Isma's Tear. Their top edge is the surface, so
        // Mario is only told about water while he is actually inside one of those triggers: not a
        // guessed height, and never the water of a pool somewhere above or beside him.
        internal static void Sense(object hero)
        {
            bool wasTouchingLiquid = touchingLiquid;
            touchingLiquid = false;
            if (hero == null)
            {
                return;
            }
            try
            {
                if (!ReferenceEquals(sensedHero, hero) || heroBox == null)
                {
                    sensedHero = hero;
                    heroBox = ((Component)hero).GetComponent<BoxCollider2D>();
                }
            }
            catch (Exception)
            {
            }
            if (heroBox == null)
            {
                return;
            }
            Bounds bounds = heroBox.bounds;
            Vector2 point = new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller != null && follow.MarioAlive(controller) && ReferenceEquals(follow.Hero(controller), hero))
            {
                Vector3? feet = follow.Ground(controller);
                if (feet.HasValue) point = new Vector2(feet.Value.x, feet.Value.y + 0.05f);
            }
            if (acidSurface && !AcidSafety.CanEnterAcid(hero))
            {
                wasInWater = acidSurface = false;
                surface = float.NaN;
                liquid = null;
            }
            float top;
            bool fromAcid;
            Collider2D found;
            bool inside = Pooled(point, hero, out top, out fromAcid, out found);
            bool confirmed = inside;
            // A tiny geometric edge allowance replaces the old frame timers and the hidden
            // Knight's stale swim flag. Retention cannot extend above/outside this real pool.
            if (!inside && liquid != null && liquid.enabled && liquid.isTrigger && liquid.gameObject.activeInHierarchy
                && (!acidSurface || AcidSafety.CanEnterAcid(hero)))
            {
                Bounds pool = liquid.bounds;
                Vector2 nearest = liquid.ClosestPoint(point);
                float dx = nearest.x - point.x, dy = nearest.y - point.y;
                inside = dx * dx + dy * dy <= .0025f && point.y <= pool.max.y + .05f;
                if (inside) { found = liquid; top = pool.max.y; fromAcid = acidSurface; }
            }
            if (inside)
            {
                touchingLiquid = confirmed;
                if (confirmed && !wasTouchingLiquid)
                {
                    // Refill both of the Knight's real air-move flags on each actual entry.
                    DoubleJump.ResetHero(hero);
                    PoundLongJump.ResetHero(hero);
                }
                if (!wasInWater)
                {
                    wasInWater = true;
                    if (Plugin.Instance != null)
                    {
                        Plugin.Instance.Log.LogInfo("Mario is in the game's water; its surface is at " + top.ToString("0.00") + ".");
                    }
                }
                surface = top;
                acidSurface = fromAcid;
                liquid = found;
                return;
            }
            wasInWater = acidSurface = false;
            surface = float.NaN;
            liquid = null;
        }

        internal static void Tick(object controller, KnightResetFollow follow)
        {
            if (setWaterLevel == null || follow == null || controller == null)
            {
                return;
            }
            int id = follow.MarioId(controller);
            if (id < 0)
            {
                return;
            }
            Apply(id, wasInWater ? (surface - follow.Origin.y) / 0.0105f : NoWater);
            if (!wasInWater)
            {
                return;
            }
            Note(controller, follow);
        }

        private static bool touchingLiquid;

        internal static void RegenerateSoul(object controller, KnightResetFollow follow)
        {
            if (!touchingLiquid || !wasInWater || follow == null || controller == null)
            {
                SurfaceSoul.Tick(null, false, Time.time);
                return;
            }
            uint action = follow.Action(controller);
            // Hurt/death/drowning actions are submerged too, but are not a resting surface.
            if (!UsesSwimControls(action) || (action & (0x00020000u | 0x00001000u)) != 0 || follow.Busy(controller))
            {
                SurfaceSoul.Tick(null, false, Time.time);
                return;
            }
            Vector3? feet = follow.Ground(controller);
            // Native SM64 floating puts the feet 80 units (0.84 here) below the water plane.
            // A small allowance covers bobbing without granting soul to deep swimmers.
            bool atSurface = feet.HasValue && Mathf.Abs(surface - feet.Value.y - 0.84f) <= 0.16f;
            SurfaceSoul.Tick(atSurface ? follow.Hero(controller) : null, atSurface, Time.time);
        }

        private static bool Pooled(Vector2 point, object hero, out float top, out bool fromAcid, out Collider2D volume)
        {
            top = float.NegativeInfinity;
            fromAcid = false;
            volume = null;
            try
            {
                Vector2 low = point + new Vector2(-0.5f, -0.5f);
                Vector2 high = point + new Vector2(0.5f, 1.7f);
                int count;
                // Reuse the buffer at the exact same probe frequency. Grow only on overflow
                // and retry, so a busy trigger area cannot silently hide its water/acid pool.
                while ((count = Physics2D.OverlapArea(low, high, liquidFilter, nearbyLiquids)) == nearbyLiquids.Length)
                    Array.Resize(ref nearbyLiquids, nearbyLiquids.Length * 2);
                for (int i = 0; i < count; i++)
                {
                    Collider2D hit = nearbyLiquids[i];
                    if (hit == null || !hit.enabled || !hit.gameObject.activeInHierarchy || !hit.isTrigger || !hit.OverlapPoint(point))
                    {
                        continue;
                    }
                    string tag;
                    try
                    {
                        tag = hit.gameObject.tag;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    bool acid = tag == "Acid";
                    // Only the game's actual liquid tags count. Decorative pipes, fountains,
                    // waterfalls and other objects named "water" are not swimming volumes.
                    if (acid ? !AcidSafety.CanEnterAcid(hero)
                        : tag != "Water Surface")
                    {
                        continue;
                    }
                    float edge = hit.bounds.max.y;
                    if (edge > top)
                    {
                        top = edge;
                        fromAcid = acid;
                        volume = hit;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return !float.IsNegativeInfinity(top);
        }

        // Replaces the stick only while Mario's current action is actually swimming. The original sends "forward" as the stick's
        // up so that walking goes where the Knight faces; in water that same stick is what pitches
        // Mario down, and the stick's left/right (the one swimming uses to turn) is never sent.
        internal static bool WaterInputs(object[] __args)
        {
            if (__args == null || __args.Length < 3)
            {
                return true;
            }
            object inputs = __args[1];
            if (inputs == null)
            {
                return true;
            }
            try
            {
                // One native-tick authority, including catch-up ticks and an origin recenter.
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                object controller = follow?.Controller();
                if (controller != null && follow.MarioAlive(controller) && follow.MarioId(controller) == Convert.ToInt32(__args[0]))
                    Tick(controller, follow);
                // The contact latch is deliberately not the controls gate. Water jumps and land
                // actions must receive the untouched land input on the very next native tick,
                // even when a puddle or the surface probe still says that water is nearby.
                object state = __args[2];
                if (state == null) return true;
                if (marioAction == null || marioAction.DeclaringType != state.GetType())
                    marioAction = state.GetType().GetField("action", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (marioAction == null) return true;
                uint action = Convert.ToUInt32(marioAction.GetValue(state));
                if (action == WaterJump || action == HoldWaterJump)
                {
                    RegularizeSurfaceJump(Convert.ToInt32(__args[0]), state, action);
                    return true;
                }
                // Mario's actual move decides the controls, independently of probe/latch misses.
                // Otherwise horizontal land input (stickY = abs(x)) becomes upward swim input
                // and A can accidentally satisfy the engine's surface-exit check.
                if (!UsesSwimControls(action)) return true;
                Type type = inputs.GetType();
                if (stickX == null || stickX.DeclaringType != type)
                {
                    Cache(type);
                }
                if (stickX == null || stickY == null)
                {
                    return true;
                }
                float x;
                float y;
                bool dash;
                bool cast;
                ReadInput(out x, out y, out dash, out cast);
                stickX.SetValue(inputs, Mathf.Clamp(x, -1f, 1f));
                stickY.SetValue(inputs, Vertical(y, dash, cast));
                if (camLookX != null)
                {
                    camLookX.SetValue(inputs, 0f);
                }
                if (camLookZ != null)
                {
                    camLookZ.SetValue(inputs, 0f);
                }
                __args[1] = inputs;
                if (!wroteInputs)
                {
                    wroteInputs = true;
                    if (Plugin.Instance != null)
                    {
                        Plugin.Instance.Log.LogInfo("Mario's water controls are on: left/right turns him, dash dives, cast rises (hold it with jump at the surface to jump out).");
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        internal static bool UsesSwimControls(uint action)
        {
            // Water jump/freefall are airborne actions; metal-water walking also uses normal
            // directional controls. Neither should be mistaken for a swimming action.
            return (action & ActionGroupMask) == SubmergedActionGroup && (action & SwimmingActionFlag) != 0u;
        }

        private static void RegularizeSurfaceJump(int id, object state, uint action)
        {
            if (setAction == null || setVelocity == null || (setForward == null && nativeForward == null)) return;
            Type type = state.GetType();
            float x = Convert.ToSingle(type.GetField("vx").GetValue(state));
            float y = Convert.ToSingle(type.GetField("vy").GetValue(state));
            float z = Convert.ToSingle(type.GetField("vz").GetValue(state));
            float forward = Convert.ToSingle(type.GetField("forwardVelocity").GetValue(state));
            lock (nativeSync)
            {
                setAction.Invoke(null, new object[] { id, action == HoldWaterJump ? HoldNormalJump : NormalJump });
                // Changing to a normal jump normally re-launches and trims forward speed.
                // Restore the surface jump's existing momentum; only its move options change.
                if (setForward != null) setForward.Invoke(null, new object[] { id, forward });
                else nativeForward(id, forward);
                setVelocity.Invoke(null, new object[] { id, x, y, z });
            }
            if (exitLogs++ < LogLimit)
                Plugin.Instance?.Log.LogInfo("Mario's surface water jump became a regular jump with its existing momentum.");
        }

        // Up (or cast) rises, down (or dash) dives. Jump is left alone: in water it is the swim
        // stroke, and with a rise held at the surface it is SM64's own jump out of the water.
        private static float Vertical(float y, bool dash, bool cast)
        {
            // The shipped libsm64 uses +64 stickY for upward pitch and checks stickY > 60
            // for a surface jump. Its signs differ from the old upstream source notes.
            if (cast || y > 0.35f)
            {
                return 1f;
            }
            if (dash || y < -0.35f)
            {
                return -1f;
            }
            return 0f;
        }

        private static void Cache(Type type)
        {
            stickX = type.GetField("stickX");
            stickY = type.GetField("stickY");
            camLookX = type.GetField("camLookX");
            camLookZ = type.GetField("camLookZ");
        }

        private static void Note(object controller, KnightResetFollow follow)
        {
            if (++sinceLog < LogEvery)
            {
                return;
            }
            sinceLog = 0;
            if (logs >= LogLimit || Plugin.Instance == null)
            {
                return;
            }
            Vector3? at = follow.Ground(controller);
            if (!at.HasValue)
            {
                return;
            }
            logs++;
            float depth = surface - at.Value.y;
            Plugin.Instance.Log.LogInfo(
                "Water: Mario's feet are " + depth.ToString("0.00") + " below the water line, action "
                + follow.Action(controller).ToString("X") + (follow.Grounded(controller) ? ", on the floor" : "") + ".");
        }

        private static void Apply(int id, float level)
        {
            try
            {
                if (setIntegerLevel != null) { setIntegerLevel(id, Mathf.RoundToInt(level)); return; }
                if (setFloatLevel != null) { setFloatLevel(id, level); return; }
                object value = integerLevel
                    ? (object)Mathf.RoundToInt(level)
                    : level;
                setWaterLevel.Invoke(null, new[] { (object)id, value });
            }
            catch (Exception)
            {
            }
        }

        private static MethodInfo Find(Type nativeType, string name, int parameters)
        {
            MethodInfo[] methods = nativeType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != name || method.GetParameters().Length != parameters)
                {
                    continue;
                }
                ParameterInfo[] found = method.GetParameters();
                if (found[0].ParameterType != typeof(int))
                {
                    continue;
                }
                return method;
            }
            return null;
        }

        private static bool Pressed(object control)
        {
            if (control == null)
            {
                return false;
            }
            if (pressedField == null || pressedField.DeclaringType != control.GetType())
            {
                pressedField = control.GetType().GetProperty("IsPressed");
            }
            return pressedField != null && pressedField.GetValue(control, null) is bool on && on;
        }

        private static FieldInfo moveVector;
        private static FieldInfo dashAction;
        private static FieldInfo castAction;
        private static PropertyInfo moveXField;
        private static PropertyInfo moveYField;
        private static PropertyInfo pressedField;

        private static void ReadInput(out float x, out float y, out bool dash, out bool cast)
        {
            x = 0f;
            y = 0f;
            dash = false;
            cast = false;
            try
            {
                Type input = AccessTools.TypeByName("InputHandler");
                object handler = input?.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                    ?? input?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                object actions = handler?.GetType().GetField("inputActions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(handler);
                if (actions == null) return;
                if (moveVector == null || !moveVector.DeclaringType.IsInstanceOfType(actions))
                {
                    Type type = actions.GetType();
                    moveVector = type.GetField("moveVector");
                    dashAction = type.GetField("dash");
                    castAction = type.GetField("cast");
                }
                object stick = moveVector?.GetValue(actions);
                if (stick != null)
                {
                    if (moveXField == null || moveXField.DeclaringType != stick.GetType())
                    {
                        moveXField = stick.GetType().GetProperty("X");
                        moveYField = stick.GetType().GetProperty("Y");
                    }
                    x = moveXField == null ? 0f : (float)moveXField.GetValue(stick, null);
                    y = moveYField == null ? 0f : (float)moveYField.GetValue(stick, null);
                }
                dash = Pressed(dashAction?.GetValue(actions));
                cast = Pressed(castAction?.GetValue(actions));
            }
            catch (Exception)
            {
            }
        }
    }
}
