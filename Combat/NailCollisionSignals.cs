using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Native C# receivers such as HealthCocoon do not consume damage FSM events. Deliver
    // their real trigger callback only when its code explicitly checks the incoming nail
    // tag. Never broadcast a simulated entry to arbitrary player/proximity/hazard scripts.
    internal static class NailCollisionSignals
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, MethodInfo> receivers = new Dictionary<Type, MethodInfo>();

        internal static bool IsPoundStartup(uint action, int animation)
            => action == 0x008008A9u && (animation == 0x3B || animation == 0x3C);

        internal static float DamageMultiplier(float original, uint action, int animation)
            => IsPoundStartup(action, animation) ? 1f : original;

        // A startup hit must not destroy a projectile through either IHitResponder or
        // nail-contact events. Enemy-body/prop damage still works; melee clashes stay off.
        internal static bool AllowMoveDamage(uint action, int animation, int layer)
            => layer != 12 || !IsPoundStartup(action, animation);

        internal static bool AllowMoveContact(uint action, int animation, int layer)
            => (layer != 12 && layer != 22) || !IsPoundStartup(action, animation);

        internal static bool Deliver(MonoBehaviour script, Collider2D nail, HashSet<int> struck)
        {
            if (script == null || !script.isActiveAndEnabled || nail == null || nail.tag != "Nail Attack") return false;
            Type type = script.GetType();
            if (!receivers.TryGetValue(type, out MethodInfo method))
            {
                try
                {
                    method = type.GetMethod("OnTriggerEnter2D", Any, null, new[] { typeof(Collider2D) }, null);
                    if (method != null && !AcceptsNail(method)) method = null;
                }
                catch (Exception) { method = null; } // Unsupported native/dynamic script: cache and skip.
                receivers[type] = method; // Cache unsupported types too; no repeated IL scans.
            }
            if (method == null || !struck.Add(script.GetInstanceID())) return false;
            method.Invoke(script, new object[] { nail });
            return true;
        }

        internal static bool AcceptsNail(MethodInfo method)
        {
            if (method.IsStatic || method.ReturnType != typeof(void) || method.GetMethodBody() == null) return false;
            // Recognize direct tag/CompareTag checks on callback argument 1, optionally via
            // its gameObject. This is evidence of nail contact support, not a class-name list.
            int step = 0;
            foreach (CodeInstruction code in PatchProcessor.GetOriginalInstructions(method))
            {
                if (code.opcode == OpCodes.Nop) continue;
                if (code.opcode == OpCodes.Ldarg_1) { step = 1; continue; }
                bool call = code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt;
                MethodInfo called = call ? code.operand as MethodInfo : null;
                string declaring = called?.DeclaringType?.FullName;
                bool unityObject = declaring == "UnityEngine.Component" || declaring == "UnityEngine.GameObject";
                if (step == 1 && unityObject && called.Name == "get_gameObject") continue;
                if (step == 1 && unityObject && called.Name == "get_tag") { step = 2; continue; }
                if ((step == 1 || step == 2) && code.opcode == OpCodes.Ldstr && (string)code.operand == "Nail Attack")
                { step = step == 1 ? 3 : 4; continue; }
                if (step == 3 && unityObject && called.Name == "CompareTag") return true;
                if (step == 4 && declaring == "System.String" && called.Name == "op_Equality") return true;
                step = 0;
            }
            return false;
        }
    }
}
