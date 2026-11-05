using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HollowMario
{
    internal static class NailHitDirection
    {
        internal const float Down = 270f;

        internal static float ForMove(float original, string move) => move == "ground_pound" || move == "ground_pound_land"
            ? Down : original;

        // Amend only the direction written into the base attack's HitInstance. Keep its
        // receiver, damage, stats, native nail type and all original hit/dedup logic intact.
        internal static IEnumerable<CodeInstruction> BuildHits(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            ParameterInfo[] args = __originalMethod.GetParameters();
            if (args.Length < 4) throw new MissingMemberException("Mario attack row argument");
            Type row = args[3].ParameterType;
            FieldInfo id = row.GetField("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (id == null || id.FieldType != typeof(string)) throw new MissingFieldException(row.FullName, "Id");
            MethodInfo resolve = AccessTools.Method(typeof(NailHitDirection), nameof(ForMove));
            var result = new List<CodeInstruction>();
            int matched = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field
                    && field.Name == "Direction" && field.FieldType == typeof(float) && field.DeclaringType.Name == "HitInstance")
                {
                    var load = new CodeInstruction(row.IsValueType ? OpCodes.Ldarga_S : OpCodes.Ldarg_S,
                        (byte)(__originalMethod.IsStatic ? 3 : 4));
                    // The facing ternary branches to the field store. Its labels must enter
                    // our resolver too, or one facing direction can bypass the correction.
                    load.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                    result.Add(load);
                    result.Add(new CodeInstruction(OpCodes.Ldfld, id));
                    result.Add(new CodeInstruction(OpCodes.Call, resolve));
                    matched++;
                }
                result.Add(instruction);
            }
            if (matched != 1) throw new InvalidOperationException("Expected one Mario HitInstance.Direction assignment; found " + matched);
            return result;
        }
    }
}
