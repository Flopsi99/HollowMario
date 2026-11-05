using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Borrow the Knight's live nail settings: Set Slash Damage owns Strength and
    // bindings, Fury owns Multiplier, Enemy Recoil Up owns magnitudeMult. Their
    // native equipment/health/broken-charm rules keep running while Mario plays.
    internal static class NailCharmModifiers
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static FieldInfo normalSlash;
        private static object cachedSlash,damage,fury,recoil;
        private static PropertyInfo intValue,floatValue;

        private static bool Resolve(object hero)
        {
            if(hero==null)return false;
            if(normalSlash==null||normalSlash.DeclaringType!=hero.GetType())
                normalSlash=hero.GetType().GetField("normalSlash",Any);
            object slash=normalSlash?.GetValue(hero);
            if(slash==null)return false;
            if(ReferenceEquals(slash,cachedSlash)&&damage!=null&&fury!=null&&recoil!=null)return true;
            cachedSlash=null;damage=fury=recoil=null;
            if(!(slash is Component component))return false;
            Type fsmType=AccessTools.TypeByName("PlayMakerFSM");
            foreach(Component fsm in component.GetComponents(fsmType))
            {
                if(Convert.ToString(fsmType.GetProperty("FsmName").GetValue(fsm,null))!="damages_enemy")continue;
                object variables=fsmType.GetProperty("FsmVariables").GetValue(fsm,null);
                Type type=variables.GetType();
                damage=type.GetMethod("FindFsmInt").Invoke(variables,new object[]{"damageDealt"});
                MethodInfo find=type.GetMethod("FindFsmFloat");
                fury=find.Invoke(variables,new object[]{"Multiplier"});
                recoil=find.Invoke(variables,new object[]{"magnitudeMult"});
                if(damage==null||fury==null||recoil==null)return false;
                intValue=damage.GetType().GetProperty("Value");floatValue=fury.GetType().GetProperty("Value");
                cachedSlash=slash;return true;
            }
            return false;
        }
        internal static int BaseDamage(int original,object hero)
        {
            int value=Resolve(hero)?Convert.ToInt32(intValue.GetValue(damage,null)):0;
            return value>0?value:original;
        }
        internal static float DamageMultiplier(float original,object hero)
            =>Resolve(hero)?original*Convert.ToSingle(floatValue.GetValue(fury,null)):original;
        internal static float Knockback(float original,object hero)
            =>Resolve(hero)?original*Convert.ToSingle(floatValue.GetValue(recoil,null)):original;

        // Change the original builder's inputs, not its attack sheet or shared
        // PlayerData. Native move rounding, hit direction and accounting survive.
        internal static IEnumerable<CodeInstruction> BuildHits(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var result=new List<CodeInstruction>();int bases=0,multipliers=0,knockbacks=0;
            int heroArgument=__originalMethod.IsStatic?1:2;
            foreach(CodeInstruction instruction in instructions)
            {
                FieldInfo field=instruction.operand as FieldInfo;
                string resolver=null;
                if(instruction.opcode==OpCodes.Ldfld&&field?.Name=="nailDamage"&&field.DeclaringType.Name=="PlayerData")
                {result.Add(instruction);resolver=nameof(BaseDamage);bases++;}
                else if(instruction.opcode==OpCodes.Stfld&&field?.DeclaringType.Name=="HitInstance")
                {
                    if(field.Name=="Multiplier"){resolver=nameof(DamageMultiplier);multipliers++;}
                    if(field.Name=="MagnitudeMultiplier"){resolver=nameof(Knockback);knockbacks++;}
                }
                if(resolver!=null)
                {
                    var load=new CodeInstruction(OpCodes.Ldarg_S,(byte)heroArgument);
                    if(instruction.opcode==OpCodes.Stfld)
                    {
                        load.labels.AddRange(instruction.labels);instruction.labels.Clear();
                        load.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();
                    }
                    result.Add(load);result.Add(new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(NailCharmModifiers),resolver)));
                }
                if(instruction.opcode!=OpCodes.Ldfld||resolver==null)result.Add(instruction);
            }
            if(bases!=1||multipliers!=1||knockbacks!=1)
                throw new InvalidOperationException("Mario nail modifier assignments changed: "+bases+"/"+multipliers+"/"+knockbacks);
            return result;
        }
    }
}
