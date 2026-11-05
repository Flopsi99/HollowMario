using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // One dive and its immediate landing slide share only the enemies that
    // actually lost HP to that dive. Enemy blocking, SOUL, recoil and successful
    // hit protection still use their original native paths.
    internal static class DiveSlideFollowup
    {
        private const uint Dive = 0x0188088Au, Slide = 0x00880456u;
        private sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        private static readonly HashSet<object> diveTargets = new HashSet<object>(new Identity());
        private static FieldInfo hp, source, attackType, rowId;
        private static object controllerOwner, heroOwner;
        private static uint phase;
        private static int generation, failures;
        private static ManualLogSource log;
        internal static bool Ready { get; private set; }

        private struct Context
        {
            internal object Target, Source;
            internal int Generation;
            internal bool Reduce, Quartered;
        }
        private struct StrikeState
        {
            internal Context Previous;
            internal object Target;
            internal int Health, Generation;
            internal uint Action;
        }
        [ThreadStatic] private static Context current;

        internal static void Arm(Harmony harmony, Type attacks, Type health, ManualLogSource logger)
        {
            log = logger;
            Ready = false;
            try
            {
                MethodInfo strike = AccessTools.Method(attacks, "Strike");
                MethodInfo damage = AccessTools.Method(health, "TakeDamage");
                Type payload = damage?.GetParameters()[0].ParameterType;
                hp = AccessTools.Field(health, "hp");
                source = AccessTools.Field(payload, "Source");
                attackType = AccessTools.Field(payload, "AttackType");
                rowId = AccessTools.Field(strike?.GetParameters()[3].ParameterType, "Id");
                if (strike == null || damage == null || hp == null || source == null || attackType == null || rowId == null)
                    throw new MissingMemberException("Dive/slide native damage metadata");
                WeakSlideFlash.Configure(health, logger);
                harmony.Patch(damage, transpiler: new HarmonyMethod(typeof(DiveSlideFollowup), nameof(FinalDamage)));
                harmony.Patch(strike,
                    prefix: new HarmonyMethod(typeof(DiveSlideFollowup), nameof(BeforeStrike)) { priority = Priority.First + 500 },
                    finalizer: new HarmonyMethod(typeof(DiveSlideFollowup), nameof(FinishStrike)));
                NativeMovementGates.ActionInitialized += ActionChanged;
                Ready = true;
            }
            catch (Exception error) { Warn(error); }
        }

        private static void ActionChanged(uint action)
        {
            try
            {
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                object controller = follow?.Controller(), hero = controller == null ? null : follow.Hero(controller);
                if (controller == null || !follow.MarioAlive(controller) || hero == null) { Forget(); return; }
                if (action == Dive)
                {
                    Forget();
                    controllerOwner = controller;
                    heroOwner = hero;
                    phase = Dive;
                }
                else if (action == Slide && phase == Dive && ReferenceEquals(controllerOwner, controller) && ReferenceEquals(heroOwner, hero))
                    phase = Slide;
                else Forget();
            }
            catch (Exception error) { Forget(); Warn(error); }
        }

        private static void BeforeStrike(object __0, object __1, object __3, out StrikeState __state)
        {
            __state = new StrikeState { Previous = current };
            // An unrelated or nested strike must not borrow an outer slide scope.
            current = default(Context);
            try
            {
                if (!Ready || __0 == null || !hp.DeclaringType.IsInstanceOfType(__0) || !(__1 is Component body)) return;
                KnightResetFollow follow = Plugin.Instance?.knightFollow;
                object controller = follow?.Controller();
                if (controller == null || !follow.MarioAlive(controller) || !ReferenceEquals(controllerOwner, controller)
                    || !ReferenceEquals(heroOwner, __1) || !ReferenceEquals(follow.Hero(controller), __1)) return;
                uint action = follow.Action(controller);
                if ((action != Dive && action != Slide) || action != phase) return;
                string move = __3 == null ? null : Convert.ToString(rowId.GetValue(__3));
                if (move != (action == Dive ? "dive" : "dive_slide")) return;
                __state.Target = __0;
                __state.Health = Convert.ToInt32(hp.GetValue(__0));
                __state.Generation = generation;
                __state.Action = action;
                current = new Context { Target = __0, Source = body.gameObject, Generation = generation,
                    Reduce = action == Slide && diveTargets.Contains(__0) };
            }
            catch (Exception error) { Warn(error); }
        }

        private static void FinishStrike(StrikeState __state)
        {
            try
            {
                if (__state.Target != null && __state.Generation == generation && __state.Health > 0
                    && Convert.ToInt32(hp.GetValue(__state.Target)) < __state.Health)
                {
                    if (__state.Action == Dive) diveTargets.Add(__state.Target);
                    if (__state.Action == Slide && current.Quartered && current.Generation == __state.Generation
                        && ReferenceEquals(current.Target, __state.Target)) WeakSlideFlash.Apply(__state.Target);
                }
            }
            catch (Exception error) { Warn(error); }
            finally { current = __state.Previous; }
        }

        private static int Quarter<THit>(int damage, object target, THit hit)
        {
            if (!current.Reduce || current.Generation != generation || !ReferenceEquals(current.Target, target) || damage <= 0)
                return damage;
            // Only box a qualifying follow-up. Nested spell/scenery/foreign-source
            // damage must not inherit the direct Mario nail hit's reduction.
            try
            {
                object payload = hit;
                if (!ReferenceEquals(source.GetValue(payload), current.Source) || Convert.ToInt32(attackType.GetValue(payload)) != 0)
                    return damage;
                int reduced = Math.Max(1, Mathf.RoundToInt(damage * .25f));
                current.Quartered = true;
                return reduced;
            }
            catch (Exception error) { Warn(error); return damage; }
        }

        internal static IEnumerable<CodeInstruction> FinalDamage(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            Type health = __originalMethod.DeclaringType;
            FieldInfo healthField = AccessTools.Field(health, "hp");
            Type payload = __originalMethod.GetParameters()[0].ParameterType;
            MethodInfo quarter = AccessTools.Method(typeof(DiveSlideFollowup), nameof(Quarter)).MakeGenericMethod(payload);
            int found = 0;
            var result = new List<CodeInstruction>();
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (i >= 2 && instruction.opcode == OpCodes.Sub && code[i - 2].LoadsField(healthField) && LocalLoad(code[i - 1]))
                {
                    // Native calculation/rounding, Fury, Metal and damageOverride
                    // are already finished. Replace just the final damage operand
                    // of hp - damage, before native clamping/death/nonfatal logic.
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    owner.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    owner.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    result.Add(owner);
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Call, quarter));
                    found++;
                }
                result.Add(instruction);
            }
            if (found != 1) throw new InvalidOperationException("Expected one final native HP subtraction, found " + found + ".");
            return result;
        }

        private static bool LocalLoad(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S || instruction.opcode == OpCodes.Ldloc_0
                || instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3;

        internal static void Forget()
        {
            diveTargets.Clear();
            controllerOwner = heroOwner = null;
            phase = 0;
            unchecked { generation++; }
        }
        internal static void Dispose()
        {
            NativeMovementGates.ActionInitialized -= ActionChanged;
            Forget();
            current = default(Context);
            Ready = false;
        }
        private static void Warn(Exception error)
        { if (failures++ < 4) log?.LogWarning("Mario's dive follow-up: " + error.GetBaseException().Message); }
    }
}
