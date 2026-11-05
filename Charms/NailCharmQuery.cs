using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HollowMario
{
    internal static class NailCharmQuery
    {
        // Enlarge the base attack's candidate search as well as our final contact checks.
        // Adjust values on the stack, leaving the shared attack rows completely untouched.
        internal static IEnumerable<CodeInstruction> BuildQuery(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            if (__originalMethod.GetParameters().Length != 2)
                throw new InvalidOperationException("Expected Mario attack Tick(controller, hero)");
            MethodInfo scale = AccessTools.Method(typeof(NailCharmReach), nameof(NailCharmReach.ScaleDistance));
            var result = new List<CodeInstruction>();
            var fields = new HashSet<string>();
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.opcode != OpCodes.Ldfld || !(instruction.operand is FieldInfo field)
                    || field.DeclaringType.Name != "AttackRow" || field.FieldType != typeof(float)
                    || (field.Name != "Forward" && field.Name != "Up" && field.Name != "Radius")) continue;
                result.Add(new CodeInstruction(__originalMethod.IsStatic ? OpCodes.Ldarg_1 : OpCodes.Ldarg_2));
                result.Add(new CodeInstruction(OpCodes.Call, scale));
                fields.Add(field.Name);
                count++;
            }
            if (count != 3 || fields.Count != 3)
                throw new InvalidOperationException("Expected Mario attack Forward, Up and Radius reads; found " + count);
            return result;
        }
    }
}
