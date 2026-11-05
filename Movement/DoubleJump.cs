using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Hollow Knight already has one private flag which says whether the Monarch Wings has
    // been spent: HeroController.doubleJumped. Mario uses that very flag, rather than a
    // second copy of the state. The Knight's normal reset moments are mirrored by Mario's
    // ground, wall-kick, water and bounce moments below.
    internal static class DoubleJump
    {
        private const uint AirAction = 0x00000800u;
        private const uint AttackingAction = 0x00800000u;
        private const uint WallKickAction = 0x03000886u;
        private const uint DoubleJumpAction = 0x03000881u;
        private const float ReverseInput = 0.2f;
        private const float Moving = 1f;

        private static MethodInfo setAction;
        private static MethodInfo setFace;
        private static MethodInfo setForward;
        private static SetForwardDelegate setForwardNative;
        private static WallProbeDelegate wallProbe;
        private static object nativeSync;
        private static readonly AirJumpPriority priority = new AirJumpPriority();
        private static Type probeStateType;
        private static FieldInfo px, py, pz, vx, vy, vz;
        private static FieldInfo buttonA;
        private static Type inputType;
        private static FieldInfo heroField;
        private static Type heroType;
        private static FieldInfo doubleJumped;
        private static FieldInfo heroPlayerData;
        private static Type playerDataType;
        private static PropertyInfo playerDataInstance;
        private static FieldInfo playerDataInstanceField;
        private static FieldInfo hasDoubleJump;
        private static FieldInfo moveVector;
        private static PropertyInfo moveX;
        private static bool lastA;
        private static uint lastAction;
        private static bool haveLastAction;
        private static int logs;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetForwardDelegate(int marioId, float velocity);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WallProbeDelegate(ref float x, ref float y, ref float z, float offsetY, float radius);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        internal static bool Armed => setAction != null;

        internal static MethodInfo TickMethod { get; private set; }

        internal static void Arm(Type nativeType)
        {
            if (nativeType == null)
            {
                return;
            }
            setAction = AccessTools.Method(nativeType, "sm64_set_mario_action", new[] { typeof(int), typeof(uint) });
            setFace = AccessTools.Method(nativeType, "sm64_set_mario_faceangle", new[] { typeof(int), typeof(float) });
            setForward = AccessTools.Method(nativeType, "sm64_set_mario_forward_velocity", new[] { typeof(int), typeof(float) });
            nativeSync = nativeType.GetField("Sync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ?? new object();
            MethodInfo probe = AccessTools.Method(nativeType, "sm64_surface_find_wall_collision");
            if (probe != null) wallProbe = (WallProbeDelegate)Delegate.CreateDelegate(typeof(WallProbeDelegate), probe);
            else
            {
                try
                {
                    IntPtr module = GetModuleHandle("sm64.dll");
                    IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "sm64_surface_find_wall_collision");
                    if (entry != IntPtr.Zero) wallProbe = (WallProbeDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(WallProbeDelegate));
                }
                catch (Exception) { }
            }
            if (setForward == null)
            {
                try
                {
                    IntPtr module = GetModuleHandle("sm64.dll");
                    IntPtr entry = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "sm64_set_mario_forward_velocity");
                    if (entry != IntPtr.Zero)
                    {
                        setForwardNative = (SetForwardDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(SetForwardDelegate));
                    }
                }
                catch (Exception)
                {
                }
            }
            TickMethod = AccessTools.Method(nativeType, "sm64_mario_tick");
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
            lastA = false;
            haveLastAction = false;
            lastAction = 0u;
            priority.Forget();
        }

        // Runs after each Mario frame too, so landing, wall-kicking and entering water put
        // the exact Knight flag back to its ready state even though the hidden Knight's own
        // Update loop is not running while Mario is on screen.
        internal static void Sync(object controller, KnightResetFollow follow)
        {
            if (controller == null || follow == null)
            {
                return;
            }
            object hero = Hero(controller);
            if (hero == null)
            {
                return;
            }
            uint action = follow.Action(controller);
            if (Swimming.UsesSwimControls(action) || follow.Grounded(controller))
            {
                ResetHero(hero);
            }
            else if (action == WallKickAction && (!haveLastAction || lastAction != action))
            {
                ResetHero(hero);
            }
            lastAction = action;
            haveLastAction = true;
        }

        internal static void Reset(object controller)
        {
            ResetHero(Hero(controller));
        }

        internal static void ResetHero(object hero)
        {
            if (hero == null)
            {
                return;
            }
            CacheHero(hero);
            if (doubleJumped == null)
            {
                return;
            }
            try
            {
                if ((bool)doubleJumped.GetValue(hero))
                {
                    doubleJumped.SetValue(hero, false);
                }
            }
            catch (Exception)
            {
            }
        }

        // This is deliberately a native-tick prefix. It changes Mario's action before the
        // simulation advances. ACT_DOUBLE_JUMP does not start another jump from A_PRESSED;
        // leave A held so the engine's ordinary variable-height gravity sees the real button.
        internal static bool TickPrefix(object[] __args)
        {
            if (!Armed || __args == null || __args.Length < 2 || Plugin.Instance == null || Plugin.Instance.knightFollow == null)
            {
                return true;
            }
            object inputs = __args[1];
            if (inputs == null)
            {
                return true;
            }
            KnightResetFollow follow = Plugin.Instance.knightFollow;
            object controller = follow.Controller();
            if (controller == null || __args[0] == null || follow.MarioId(controller) != Convert.ToInt32(__args[0]))
            {
                return true;
            }
            bool pressed = ReadA(inputs);
            if(CapAccess.Flag(controller,"HardLanding"))pressed=CapAccess.Flag(controller,"RecoveryJumpHeld");
            bool newPress = pressed && !lastA;
            // These are simulation samples, not screen frames: release/press changes must be
            // tracked even when several 30 Hz ticks run during a single rendered frame.
            lastA = pressed;
            priority.Sample(follow.Action(controller));
            if (!newPress)
            {
                return true;
            }
            Sync(controller, follow);
            object hero = Hero(controller);
            bool recovery=RecoveryAction(follow.Action(controller))||CapAccess.Flag(controller,"HardLanding");
            if (!Ready(hero, controller, follow) || (!recovery&&priority.Reserved) || NoBonk.Pending)
            {
                return true;
            }

            try
            {
                // Keep A unchanged so the native engine can execute its own wall kick. Query
                // its collision world, including the same radius/heights as perform_air_step;
                // only a correction opposing our travel counts, not a wall behind Mario.
                object state = __args.Length > 2 ? __args[2]
                    : controller.GetType().GetField("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(controller);
                if (!recovery&&ApproachingWall(state)) return true;
                float move = MoveInput();
                float horizontal = follow.SideSpeed(controller);
                bool reverse = Mathf.Abs(horizontal) > Moving && Mathf.Sign(move) != Mathf.Sign(horizontal) && Mathf.Abs(move) >= ReverseInput;
                float forward = follow.ForwardSpeed(controller);
                int id = follow.MarioId(controller);
                if (reverse)
                {
                    // In SM64's 2D orientation, positive sine is right. Keep the speed but
                    // flip the facing direction before starting the Wings jump.
                    setFace?.Invoke(null, new object[] { id, move > 0f ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f });
                    forward = Mathf.Abs(forward);
                }
                SetForward(id, forward);
                if(CapAccess.Flag(controller,"HardLanding"))
                {
                    // The host masks ordinary input during this get-up. Only
                    // an accepted Wings escape restores A for native jump height.
                    buttonA.SetValue(inputs,Convert.ChangeType(1,buttonA.FieldType));
                    AccessTools.Method(controller.GetType(),"CancelHardLanding").Invoke(controller,null);
                }
                setAction.Invoke(null, new object[] { id, DoubleJumpAction });
                // libsm64 gives its ordinary double jump an 80% speed trim. Monarch Wings is
                // a mid-air jump, so restore the momentum it had when the player pressed jump.
                SetForward(id, forward);
                MarkUsed(hero);
                if (logs < 8)
                {
                    logs++;
                    Plugin.Instance.Log.LogInfo("Mario used the Knight's double jump" + (reverse ? " and reversed in mid-air." : "."));
                }
            }
            catch (Exception exception)
            {
                if (logs < 8)
                {
                    logs++;
                    Plugin.Instance.Log.LogWarning("Mario's double jump could not start: " + exception.Message);
                }
            }
            return true;
        }

        private static bool Ready(object hero, object controller, KnightResetFollow follow)
        {
            if (hero == null || CapAccess.Flag(controller,"DamagePending") || !HasWings(hero) || Spent(hero))
            {
                return false;
            }
            uint action = follow.Action(controller);
            if(RecoveryAction(action)||CapAccess.Flag(controller,"HardLanding"))return true;
            if(follow.Grounded(controller))return false;
            // Deliberate attacks keep their commitment; movement stuns above
            // can be escaped with the same existing Wings charge.
            return (action & AirAction) != 0u && (action & (AttackingAction | 0x00020000u)) == 0u && !Swimming.UsesSwimControls(action);
        }
        internal static bool RecoveryAction(uint action)
        {
            switch(action)
            {
                case 0x010208B0u:case 0x010208B1u:case 0x010208B2u:case 0x010208B3u:case 0x010208B6u:
                case 0x00020460u:case 0x00020461u:case 0x00020462u:case 0x00020463u:case 0x00020464u:case 0x00020465u:
                case 0x00020226u:case 0x0002033Au:case 0x0002033Bu:case 0x0002033Cu:return true;
                default:return false;
            }
        }

        private static bool ApproachingWall(object state)
        {
            if (wallProbe == null || state == null) return false;
            if (probeStateType != state.GetType())
            {
                probeStateType = state.GetType();
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                px = probeStateType.GetField("px", fields); py = probeStateType.GetField("py", fields); pz = probeStateType.GetField("pz", fields);
                vx = probeStateType.GetField("vx", fields); vy = probeStateType.GetField("vy", fields); vz = probeStateType.GetField("vz", fields);
            }
            if (px == null || py == null || pz == null || vx == null || vy == null || vz == null) return false;
            float dx = Convert.ToSingle(vx.GetValue(state)), dz = Convert.ToSingle(vz.GetValue(state));
            if (dx*dx + dz*dz < 1f) return false;
            float nextX = Convert.ToSingle(px.GetValue(state)) + dx;
            float nextY = Convert.ToSingle(py.GetValue(state)) + Convert.ToSingle(vy.GetValue(state));
            float nextZ = Convert.ToSingle(pz.GetValue(state)) + dz;
            lock (nativeSync)
            {
                return OpposesTravel(nextX, nextY, nextZ, dx, dz, 150f)
                    || OpposesTravel(nextX, nextY, nextZ, dx, dz, 30f);
            }
        }

        private static bool OpposesTravel(float x, float y, float z, float dx, float dz, float height)
        {
            float originalX = x, originalZ = z;
            return wallProbe(ref x, ref y, ref z, height, 50f) > 0
                && (x-originalX)*dx + (z-originalZ)*dz < -0.01f;
        }

        private static object Hero(object controller)
        {
            if (controller == null)
            {
                return null;
            }
            try
            {
                if (heroField == null || heroField.DeclaringType != controller.GetType())
                {
                    heroField = controller.GetType().GetField("hero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return heroField == null ? null : heroField.GetValue(controller);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void CacheHero(object hero)
        {
            if (hero == null || heroType == hero.GetType())
            {
                return;
            }
            heroType = hero.GetType();
            doubleJumped = heroType.GetField("doubleJumped", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            heroPlayerData = heroType.GetField("playerData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type dataType = heroPlayerData == null ? AccessTools.TypeByName("PlayerData") : heroPlayerData.FieldType;
            playerDataType = dataType;
            hasDoubleJump = dataType == null ? null : dataType.GetField("hasDoubleJump", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            playerDataInstance = dataType == null ? null : dataType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            playerDataInstanceField = dataType == null ? null : dataType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static bool HasWings(object hero)
        {
            CacheHero(hero);
            if (hasDoubleJump == null)
            {
                return false;
            }
            try
            {
                object data = heroPlayerData == null ? null : heroPlayerData.GetValue(hero);
                if (data == null && playerDataInstance != null)
                {
                    data = playerDataInstance.GetValue(null, null);
                }
                if (data == null && playerDataInstanceField != null)
                {
                    data = playerDataInstanceField.GetValue(null);
                }
                return data != null && Convert.ToBoolean(hasDoubleJump.GetValue(data));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Spent(object hero)
        {
            CacheHero(hero);
            try
            {
                return doubleJumped != null && Convert.ToBoolean(doubleJumped.GetValue(hero));
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static void MarkUsed(object hero)
        {
            CacheHero(hero);
            if (doubleJumped != null)
            {
                doubleJumped.SetValue(hero, true);
            }
        }

        private static bool ReadA(object inputs)
        {
            try
            {
                Type type = inputs.GetType();
                if (buttonA == null || inputType != type)
                {
                    inputType = type;
                    buttonA = type.GetField("buttonA", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return buttonA != null && Convert.ToBoolean(buttonA.GetValue(inputs));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SetForward(int id, float velocity)
        {
            if (setForward != null)
            {
                setForward.Invoke(null, new object[] { id, velocity });
            }
            else
            {
                setForwardNative?.Invoke(id, velocity);
            }
        }

        // Shared horizontal intent for the Wings reversal and ground-pound long-jump cancel.
        internal static float MoveInput()
        {
            try
            {
                // InputHandler destroys/replaces HeroActions when controls are recreated. Cache
                // only member metadata; always resolve the current singleton and its live set.
                Type input = AccessTools.TypeByName("InputHandler");
                object handler = input?.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                    ?? input?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null);
                object actions = handler?.GetType().GetField("inputActions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(handler);
                if (actions == null) return 0f;
                if (moveVector == null || !moveVector.DeclaringType.IsInstanceOfType(actions))
                    moveVector = actions.GetType().GetField("moveVector", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object stick = moveVector?.GetValue(actions);
                if (stick == null)
                {
                    return 0f;
                }
                if (moveX == null || moveX.DeclaringType != stick.GetType())
                {
                    moveX = stick.GetType().GetProperty("X", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                return moveX == null ? 0f : Convert.ToSingle(moveX.GetValue(stick, null));
            }
            catch (Exception)
            {
                return 0f;
            }
        }
    }
}
