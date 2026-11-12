using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HollowMario
{
    internal static class NativeMovementGates
    {
        private const uint LongJump = 0x03000888u, Jump = 0x03000880u;
        // Native MarioState.input: u16 at byte 2, verified against the installed x64 engine.
        private const int InputOffset = 2;
        private const short APressed = 2;
        [ThreadStatic] private static Policy current;
        internal struct Policy { internal bool Active, Claws, Dash, Dashmaster, NoBonk; internal float RunMultiplier; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int WallAction(IntPtr mario);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AirStep(IntPtr mario,uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint SetAction(IntPtr mario, uint action, uint argument);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void WalkingSpeed(IntPtr mario);

        private static readonly WallAction wallCallback = WallCheck, bonkCallback = AirHitWall;
        private static readonly WallAction freefallCallback = Freefall;
        private static readonly AirStep airStepCallback = StepAir;
        private static readonly SetAction actionCallback = ChooseAction;
        private static readonly WalkingSpeed walkingCallback = UpdateWalkingSpeed;
        private static NativeDetour wallHook, bonkHook, actionHook, airStepHook, freefallHook;
        private static WallAction originalWall, originalBonk, originalFreefall;
        private static AirStep originalAirStep;
        private static SetAction originalAction;
        private static NativeDetour walkingHook;
        private static WalkingSpeed originalWalking;
        private static string library;
        private static object sync;
        private static bool attempted;
        internal static bool Armed => actionHook != null && wallHook != null && bonkHook != null && airStepHook != null && freefallHook != null;
        internal static bool GameplayTick => current.Active;
        internal static event Action<uint> ActionInitialized;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        internal static void Configure(Type native)
        {
            library = Path.Combine(Path.GetDirectoryName(native.Assembly.Location), "sm64.dll");
            sync = native.GetField("Sync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) ?? new object();
        }

        internal static void BeforeTick(int __0, out Policy __state)
        {
            __state = current;
            current = default;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || __0 != follow.MarioId(controller)) return;
            if (!Armed && !attempted)
            {
                IntPtr module = GetModuleHandle(library);
                if (module != IntPtr.Zero)
                {
                    attempted = true;
                    try
                    {
                        lock (sync) Install(library, module);
                        Plugin.Instance?.Log.LogInfo("Mario's wall jumps and long jumps now check the Knight's upgrades before starting.");
                    }
                    catch (Exception exception)
                    {
                        Plugin.Instance?.Log.LogWarning("Mario's movement upgrade gates could not be armed: " + exception.Message);
                    }
                }
            }
            object hero = follow.Hero(controller);
            current = new Policy { Active = Armed, Claws = MovementUnlocks.HasClaws(hero), Dash = MovementUnlocks.HasDash(hero),
                Dashmaster = MovementUnlocks.HasDashmaster(hero), NoBonk=MovementUnlocks.HasNoBonk(hero), RunMultiplier = MovementUnlocks.RunMultiplier(hero) };
            NoBonk.BeginTick(current.Active&&current.NoBonk,current.Claws);
        }

        // Finalizer restores nesting even if another prefix or the native tick throws. No ref
        // state/geometry arguments are read or written by this hook.
        internal static void AfterTick(Policy __state) { current = __state; }

        internal static void Install(string path, IntPtr module)
        {
            if (Armed) return;
            var functions = NativeSymbols.Functions(path, module, "set_mario_action", "check_wall_kick", "act_air_hit_wall", "perform_air_step", "act_freefall");
            try
            {
                var manual = new NativeDetourConfig { ManualApply = true };
                actionHook = new NativeDetour(functions["set_mario_action"], actionCallback, manual);
                originalAction = actionHook.GenerateTrampoline<SetAction>();
                wallHook = new NativeDetour(functions["check_wall_kick"], wallCallback, manual);
                originalWall = wallHook.GenerateTrampoline<WallAction>();
                bonkHook = new NativeDetour(functions["act_air_hit_wall"], bonkCallback, manual);
                originalBonk = bonkHook.GenerateTrampoline<WallAction>();
                airStepHook = new NativeDetour(functions["perform_air_step"], airStepCallback, manual);
                originalAirStep = airStepHook.GenerateTrampoline<AirStep>();
                freefallHook = new NativeDetour(functions["act_freefall"], freefallCallback, manual);
                originalFreefall = freefallHook.GenerateTrampoline<WallAction>();
                actionHook.Apply(); wallHook.Apply(); bonkHook.Apply(); airStepHook.Apply(); freefallHook.Apply();
            }
            catch { Dispose(); throw; }
            // Optional running bonus: a missing walking symbol must not disable the
            // already-working wall/long-jump gates.
            try
            {
                IntPtr target = NativeSymbols.Functions(path, module, "update_walking_speed")["update_walking_speed"];
                walkingHook = new NativeDetour(target, walkingCallback, new NativeDetourConfig { ManualApply = true });
                originalWalking = walkingHook.GenerateTrampoline<WalkingSpeed>();
                walkingHook.Apply();
            }
            catch (Exception error)
            {
                walkingHook?.Dispose(); walkingHook = null;
                Plugin.Instance?.Log.LogWarning("Mario's running-speed charm bonus could not be armed: " + error.Message);
            }
            try{ThinPlatformLedges.Install(path,module);}
            catch(Exception error){Plugin.Instance?.Log.LogWarning("Shallow-platform ledge grabs unavailable: "+error.Message);}
            try{GroundStepEdges.Install(path,module);}
            catch(Exception error){Plugin.Instance?.Log.LogWarning("Flat-ledge falling fix unavailable: "+error.Message);}
        }

        private static unsafe void UpdateWalkingSpeed(IntPtr mario)
        {
            float ratio = current.Active ? current.RunMultiplier : 1f;
            if (ratio <= 1f) { originalWalking(mario); return; }
            // Verified installed MarioState: forwardVel 0x54, slideVelXZ 0x58/0x5C,
            // velXZ 0x48/0x50. Let the original solver work in normal running-speed units.
            // Rescale its locomotion only: wind/sand offsets added to vel remain unchanged.
            float* forward = (float*)((byte*)mario + 0x54);
            *forward /= ratio;
            originalWalking(mario);
            float* slideX = (float*)((byte*)mario + 0x58), slideZ = (float*)((byte*)mario + 0x5C);
            *(float*)((byte*)mario + 0x48) += *slideX * (ratio - 1f);
            *(float*)((byte*)mario + 0x50) += *slideZ * (ratio - 1f);
            *slideX *= ratio; *slideZ *= ratio; *forward *= ratio;
        }

        private static int WallCheck(IntPtr mario)
        {
            if(current.Active&&!current.Claws)return 0;
            return originalWall(mario);
        }

        private static int StepAir(IntPtr mario,uint flags)
        {
            uint action=unchecked((uint)Marshal.ReadInt32(mario,0xC));
            int result=originalAirStep(mario,flags);
            if(current.Active&&current.NoBonk)NoBonk.AirStep(mario,result,action);
            return result;
        }

        private static int TryBonkWallKick(IntPtr mario)
        {
            if(!current.Active||!current.NoBonk||!current.Claws||!NoBonk.PrepareKick(mario,out NoBonk.Saved saved))return 0;
            try{return originalWall(mario);}
            finally{NoBonk.RestoreKick(mario,saved);}
        }

        private static int Freefall(IntPtr mario)
        {
            int kick=TryBonkWallKick(mario);
            return kick!=0?kick:originalFreefall(mario);
        }

        private static int AirHitWall(IntPtr mario)
        {
            if (!current.Active || current.Claws) return originalBonk(mario);
            // The immediate wall-kick branch changes facing and upward velocity BEFORE it
            // calls set_mario_action. Withhold only its pressed-edge flag at that decision;
            // A-held (variable jump height), ordinary jumps and other inputs are untouched.
            short input = Marshal.ReadInt16(mario, InputOffset);
            Marshal.WriteInt16(mario, InputOffset, (short)(input & ~APressed));
            try { return originalBonk(mario); }
            finally
            {
                short after = Marshal.ReadInt16(mario, InputOffset);
                Marshal.WriteInt16(mario, InputOffset, (short)((after & ~APressed) | (input & APressed)));
            }
        }

        private static unsafe uint ChooseAction(IntPtr mario, uint action, uint argument)
        {
            uint before = *(uint*)((byte*)mario + 0xC);
            // Substitute before the engine initializes long-jump velocity/animation/sounds.
            // Its own normal jump initializer handles the still-valid jump input instead.
            if (current.Active && !current.Dash && action == LongJump) action = Jump;
            if (current.Active && current.NoBonk)
            {
                action = NoBonk.RemapAction(mario,before,action,current.Claws);
                int kick=TryBonkWallKick(mario);
                if(kick!=0)return unchecked((uint)kick);
            }
            if (current.Active) action = QuickSlash.RemapAction(mario, action);
            uint result = originalAction(mario, action, argument);
            NoBonk.ActionInitialized(mario,*(uint*)((byte*)mario+0xC));
            // Both grounded and aerial long jumps pass through this one initializer. Boost
            // its completed horizontal launch once, after the normal acceleration/cap,
            // so no tick-by-tick compounding or extra upward speed is introduced.
            if (current.Active && current.Dashmaster && action == LongJump
                && *(uint*)((byte*)mario + 0xC) == LongJump)
                *(float*)((byte*)mario + 0x54) *= 1.2f;
            if (current.Active)
            {
                uint initialized = *(uint*)((byte*)mario + 0xC);
                QuickSlash.ActionInitialized(mario, before, initialized);
                ActionInitialized?.Invoke(initialized);
            }
            return result;
        }

        internal static void Dispose()
        {
            lock (sync ?? typeof(NativeMovementGates))
            {
                walkingHook?.Dispose(); walkingHook = null;
                ThinPlatformLedges.Dispose();
                GroundStepEdges.Dispose();
                NoBonk.Forget();
                freefallHook?.Dispose(); airStepHook?.Dispose(); bonkHook?.Dispose(); wallHook?.Dispose(); actionHook?.Dispose();
                freefallHook = airStepHook = bonkHook = wallHook = actionHook = null;
            }
        }
    }
}
