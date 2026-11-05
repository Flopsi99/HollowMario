using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    // Successful punches cancel their remaining ground recovery. A successful
    // low kick returns to the nearby real floor immediately; other jump kicks
    // use the configurable first-hit delay into normal, actionable fall.
    internal static class QuickSlash
    {
        private const uint Punch = 0x00800380u, MovingPunch = 0x00800457u, Kick = 0x018008ACu;
        private const uint Idle = 0x0C400201u, Fall = 0x0100088Cu, Land = 0x04000471u, Dive = 0x0188088Au;
        private const int InputOffset = 2, ActionOffset = 0xC, ForwardOffset = 0x54;
        private const int PositionOffset = 0x3C, FloorOffset = 0x70, FloorHeightOffset = 0x7C;
        private const int OffFloor = 0x0004, AttackPressed = 0x2000;
        private const int ShortHopStartTicks = 3;
        private const float ShortHopStartHeight = 120f, ShortHopReturnHeight = 200f;
        internal const float DefaultAirRecoverySeconds = 0.2f;
        private const float TickSeconds = 1f / 30f; // Existing Mario simulation cadence, not rendering frames.
        private static ConfigEntry<float> airRecoverySeconds;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ActionStep(IntPtr mario);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StopAtFloor(IntPtr mario);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float FindFloor(float x, float y, float z, out IntPtr floor);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SetAction(IntPtr mario, uint action, uint argument);
        private static readonly ActionStep punchCallback = PunchStep, movingCallback = MovingPunchStep, kickCallback = KickStep;
        private static NativeDetour punchHook, movingHook, kickHook;
        private static ActionStep originalPunch, originalMoving, originalKick;
        private static StopAtFloor stopAtFloor;
        private static FindFloor findFloor;
        private static SetAction setAction;
        private static string library;
        private static object sync;
        private static ManualLogSource log;
        private static bool attempted;
        private static int failures;
        private static object owner, heroOwner;
        private static uint pendingPunch;
        private static bool confirmedKick, airReady;
        private static int connectedKickTicks;
        private static object flightOwner, flightHero;
        private static bool sawGround, shortHopKick;
        private static int airTicks = ShortHopStartTicks + 1;
        [ThreadStatic] private static Scope current;
        [ThreadStatic] private static bool completingKick;
        internal struct Scope { internal object Controller, Hero; internal bool Active; }
        internal static bool Armed => punchHook != null && movingHook != null && kickHook != null;

        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

        internal static void Bind(ConfigFile config)
        {
            airRecoverySeconds = config.Bind("Charms", "QuickSlashAirRecoverySeconds", DefaultAirRecoverySeconds,
                "Seconds from a jump kick's first damaging enemy hit until Quick Slash allows another air action. Default 0.2; smaller is faster, 0 is immediate on the next simulation step. Uses Mario's 30 Hz simulation, pauses with it, and never restarts for more enemies hit by the same kick. Edit while the game is closed; the next launch reads the value.");
        }

        private static float AirRecoverySeconds
        {
            get
            {
                float seconds = airRecoverySeconds?.Value ?? DefaultAirRecoverySeconds;
                return seconds >= 0f && !float.IsInfinity(seconds) ? seconds : DefaultAirRecoverySeconds;
            }
        }

        internal static void Configure(Type native, ManualLogSource logger)
        {
            log = logger;
            library = Path.Combine(Path.GetDirectoryName(native.Assembly.Location), "sm64.dll");
            sync = native.GetField("Sync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ?? new object();
        }

        internal static void BeforeTick(int __0, out Scope __state)
        {
            __state = current;
            current = default(Scope);
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || __0 != follow.MarioId(controller)) return;
            object hero = follow.Hero(controller);
            if (CapAccess.Flag(controller,"DamagePending") || !Eligible(controller, hero, follow)) { Forget(); return; }
            if (!Armed && !attempted)
            {
                IntPtr module = GetModuleHandle(library);
                if (module != IntPtr.Zero)
                {
                    attempted = true;
                    try { lock (sync) Install(library, module); }
                    catch (Exception error) { Warn(error); }
                }
            }
            current = new Scope { Controller = controller, Hero = hero, Active = Armed && NativeMovementGates.Armed };
            if (current.Active) ObserveFlight(controller, hero, follow.Action(controller));
        }

        private static bool GroundAction(uint action)
        {
            uint group = action & 0x1C0u;
            return action != 0 && (group == 0 || group == 0x40u || group == 0x180u);
        }

        private static void ObserveFlight(object controller, object hero, uint action)
        {
            if (!ReferenceEquals(flightOwner, controller) || !ReferenceEquals(flightHero, hero))
            {
                if (flightOwner != null) Forget();
                flightOwner = controller; flightHero = hero;
            }
            if (GroundAction(action)) { sawGround = true; airTicks = 0; }
            else if ((action & 0x800u) != 0 && sawGround) airTicks = Math.Min(airTicks + 1, ShortHopStartTicks + 1);
            else { sawGround = false; airTicks = ShortHopStartTicks + 1; }
        }

        // No ref output structs/__args postfix: native simulation remains the
        // sole author of action, velocity, animation and interpolated output.
        internal static void FinishTick(Scope __state) { current = __state; }

        private static bool Eligible(object controller, object hero, KnightResetFollow follow)
            => hero != null && follow.MarioAlive(controller) && !follow.Busy(controller)
                && !follow.ControlHeld(controller) && follow.AcceptingInput(controller) && MovementUnlocks.HasQuickSlash(hero);

        internal static void Confirmed(object controller, uint action, string move)
        {
            try
            {
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                if (controller == null || follow == null || !ReferenceEquals(controller, follow.Controller())) return;
                object hero = follow.Hero(controller);
                if (!Armed || !NativeMovementGates.Armed || !Eligible(controller, hero, follow) || follow.Action(controller) != action) return;
                if (!((action == Punch && move == "punch") || (action == MovingPunch && move == "move_punch")
                    || (action == Kick && move == "jump_kick"))) return;
                if ((owner != null && !ReferenceEquals(owner, controller)) || (heroOwner != null && !ReferenceEquals(heroOwner, hero))) Forget();
                owner = controller; heroOwner = hero;
                if (action == Kick)
                {
                    if (!confirmedKick) connectedKickTicks = 0;
                    confirmedKick = true;
                }
                else pendingPunch = action;
            }
            catch (Exception error) { Forget(); Warn(error); }
        }

        private static bool OwnsScope()
            => current.Active && ReferenceEquals(owner, current.Controller) && ReferenceEquals(heroOwner, current.Hero)
                && MovementUnlocks.HasQuickSlash(current.Hero);

        private static int PunchStep(IntPtr mario) => GroundStep(mario, Punch, originalPunch);
        private static int MovingPunchStep(IntPtr mario) => GroundStep(mario, MovingPunch, originalMoving);
        private static int GroundStep(IntPtr mario, uint move, ActionStep original)
        {
            if (OwnsScope() && pendingPunch == move)
            {
                if ((Marshal.ReadInt16(mario, InputOffset) & OffFloor) == 0)
                {
                    // Return cancel=TRUE so the engine dispatches its normal idle
                    // input handling in this same simulation step.
                    setAction(mario, Idle, 0);
                    return 1;
                }
                Forget(); // The floor moved away; let native falling win.
            }
            return original(mario);
        }

        private static int KickStep(IntPtr mario)
        {
            if (OwnsScope() && confirmedKick && shortHopKick && ReturnShortHopToFloor(mario))
                return 1; // Dispatch native idle/input now, without another kick/air step.
            int result = originalKick(mario);
            if (OwnsScope() && confirmedKick && (uint)Marshal.ReadInt32(mario, ActionOffset) == Kick
                && ++connectedKickTicks * TickSeconds >= AirRecoverySeconds)
            {
                // Count only real native kick steps after its first accepted hit.
                // Fall's initializer adds no jump or movement impulse. Another
                // enemy hit by this same kick cannot postpone the earned release.
                bool previous = completingKick;
                completingKick = true;
                try { setAction(mario, Fall, 0); }
                finally { completingKick = previous; }
            }
            return result; // Do not run a second air-physics step this tick.
        }

        private static unsafe bool NearFloor(IntPtr mario, float limit)
        {
            float height = *(float*)((byte*)mario + PositionOffset + 4) - *(float*)((byte*)mario + FloorHeightOffset);
            return Marshal.ReadIntPtr(mario, FloorOffset) != IntPtr.Zero && height >= 0f && height <= limit;
        }

        private static unsafe bool ReturnShortHopToFloor(IntPtr mario)
        {
            float* position = (float*)((byte*)mario + PositionOffset);
            float height = findFloor(position[0], position[1], position[2], out IntPtr floor);
            float distance = position[1] - height;
            // Requery at the current X/Z: never return to a remembered takeoff
            // position, a missing/moved platform, or the distant safety plane.
            if (floor == IntPtr.Zero || !(distance >= 0f && distance <= ShortHopReturnHeight)) return false;
            Marshal.WriteIntPtr(mario, FloorOffset, floor);
            *(float*)((byte*)mario + FloorHeightOffset) = height;
            stopAtFloor(mario); // Native helper stops motion and syncs gfx/position to floor height.
            Marshal.WriteInt16(mario, InputOffset, (short)(Marshal.ReadInt16(mario, InputOffset) & ~OffFloor));
            setAction(mario, Idle, 0);
            return true;
        }

        // Called inside the existing single set_mario_action detour. Never stack
        // a second detour at that shared initializer.
        internal static unsafe uint RemapAction(IntPtr mario, uint requested)
        {
            if (!OwnsScope()) return requested;
            uint before = (uint)Marshal.ReadInt32(mario, ActionOffset);
            if (requested == Land && ((before == Kick && confirmedKick) || (before == Fall && airReady)))
                return Idle; // Only a real native AIR_STEP_LANDED requests this.
            if (requested == Dive && before == Fall && airReady && (Marshal.ReadInt16(mario, InputOffset) & AttackPressed) != 0)
            {
                return *(float*)((byte*)mario + ForwardOffset) > 28f ? Dive : Kick; // Native jump's normal attack choice.
            }
            return requested;
        }

        internal static void ActionInitialized(IntPtr mario, uint before, uint action)
        {
            if (!current.Active) return;
            bool recovered = completingKick && action == Fall && OwnsScope();
            bool neutral = OwnsScope() && action == Idle && (pendingPunch != 0 || confirmedKick || airReady);
            // Classify at kick START. A late descending kick close to the floor
            // is still an aerial kick, as is a follow-up in an earned air combo.
            bool earlyJump = before == 0x03000880u || before == 0x03000881u || before == 0x01000882u
                || (before == Fall && !airReady);
            bool shortStart = action == Kick && (GroundAction(before) || (earlyJump && sawGround && airTicks <= ShortHopStartTicks))
                && NearFloor(mario, ShortHopStartHeight);
            ClearRecovery();
            shortHopKick = shortStart;
            if (recovered) { owner = current.Controller; heroOwner = current.Hero; airReady = true; }
            if (recovered || neutral) Plugin.QuickSlashNeutral(current.Controller);
        }

        internal static void Install(string path, IntPtr module)
        {
            if (Armed) return;
            var functions = NativeSymbols.Functions(path, module, "act_punching", "act_move_punching", "act_jump_kick", "set_mario_action", "find_floor", "stop_and_set_height_to_floor");
            try
            {
                setAction = (SetAction)Marshal.GetDelegateForFunctionPointer(functions["set_mario_action"], typeof(SetAction));
                findFloor = (FindFloor)Marshal.GetDelegateForFunctionPointer(functions["find_floor"], typeof(FindFloor));
                stopAtFloor = (StopAtFloor)Marshal.GetDelegateForFunctionPointer(functions["stop_and_set_height_to_floor"], typeof(StopAtFloor));
                var manual = new NativeDetourConfig { ManualApply = true };
                punchHook = new NativeDetour(functions["act_punching"], punchCallback, manual);
                originalPunch = punchHook.GenerateTrampoline<ActionStep>();
                movingHook = new NativeDetour(functions["act_move_punching"], movingCallback, manual);
                originalMoving = movingHook.GenerateTrampoline<ActionStep>();
                kickHook = new NativeDetour(functions["act_jump_kick"], kickCallback, manual);
                originalKick = kickHook.GenerateTrampoline<ActionStep>();
                punchHook.Apply(); movingHook.Apply(); kickHook.Apply();
            }
            catch { Dispose(); throw; }
        }

        private static void ClearRecovery() { owner = heroOwner = null; pendingPunch = 0; confirmedKick = airReady = shortHopKick = false; connectedKickTicks = 0; }
        internal static void Forget() { ClearRecovery(); flightOwner = flightHero = null; sawGround = false; airTicks = ShortHopStartTicks + 1; }
        internal static void Dispose()
        {
            lock (sync ?? typeof(QuickSlash))
            {
                kickHook?.Dispose(); movingHook?.Dispose(); punchHook?.Dispose();
                kickHook = movingHook = punchHook = null;
                Forget(); current = default(Scope);
            }
        }
        private static void Warn(Exception error)
        { if (failures++ < 4) log?.LogWarning("Mario's Quick Slash: " + error.GetBaseException().Message); }
    }
}
