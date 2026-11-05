using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace HollowMario
{
    // HarmonyX versions compatible with API v77 predate __args. Translate only
    // OUR six array-prefix callbacks into exact typed argument references. The
    // gameplay callbacks themselves, their priorities and return values stay put.
    internal static class ApiArgumentAdapter
    {
        internal const string Owner = "gg.melty.hollowmario.api-arguments";
        private static readonly Dictionary<Tuple<MethodBase, MethodInfo>, MethodInfo> cache =
            new Dictionary<Tuple<MethodBase, MethodInfo>, MethodInfo>();
        private static readonly ModuleBuilder factories = AppDomain.CurrentDomain.DefineDynamicAssembly(
            new AssemblyName("HollowMario.ApiArgumentFactories"), AssemblyBuilderAccess.Run).DefineDynamicModule("Factories");

        internal static void Install()
        {
            var signature = new[] { typeof(MethodBase), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod) };
            new Harmony(Owner).Patch(AccessTools.Method(typeof(Harmony), nameof(Harmony.Patch), signature),
                prefix: new HarmonyMethod(typeof(ApiArgumentAdapter), nameof(Prepare)));
        }

        private static void Prepare(MethodBase __0, ref HarmonyMethod __1)
        {
            MethodInfo callback = __1?.method;
            if (callback == null || !callback.GetParameters().Any(p => p.Name == "__args")) return;
            Assembly owner = callback.DeclaringType?.Assembly;
            if (owner != typeof(Plugin).Assembly && owner?.GetName().Name != "MarioHollow") return;
            __1.method = Wrap(__0, callback);
        }

        internal static MethodInfo Wrap(MethodBase target, MethodInfo callback)
        {
            var key = Tuple.Create(target, callback);
            if (cache.TryGetValue(key, out MethodInfo existing)) return existing;
            var parameters = target.GetParameters();
            var callbackParameters = callback.GetParameters();
            if (!callback.IsStatic || (callback.ReturnType != typeof(void) && callback.ReturnType != typeof(bool)) ||
                callbackParameters.Any(p => p.Name != "__args" && p.Name != "__instance"))
                throw new NotSupportedException("Unsupported API argument prefix: " + callback);
            bool instance = !target.IsStatic;
            var types = parameters.Select(p => p.ParameterType.IsByRef ? p.ParameterType : p.ParameterType.MakeByRefType()).ToList();
            if (instance) types.Add(target.DeclaringType);
            var wrapper = new DynamicMethod("ApiArgs_" + callback.Name + "_" + cache.Count,
                callback.ReturnType, types.ToArray(), typeof(ApiArgumentAdapter).Module, true);
            for (int i = 0; i < parameters.Length; i++) wrapper.DefineParameter(i + 1, ParameterAttributes.None, "__" + i);
            if (instance) wrapper.DefineParameter(types.Count, ParameterAttributes.None, "__instance");
            var il = wrapper.GetILGenerator();
            var arguments = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Ldc_I4, parameters.Length); il.Emit(OpCodes.Newarr, typeof(object)); il.Emit(OpCodes.Stloc, arguments);
            for (int i = 0; i < parameters.Length; i++)
            {
                Type element = types[i].GetElementType();
                il.Emit(OpCodes.Ldloc, arguments); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Ldobj, element);
                if (element.IsValueType) il.Emit(OpCodes.Box, element);
                il.Emit(OpCodes.Stelem_Ref);
            }
            foreach (var parameter in callbackParameters)
            {
                if (parameter.Name == "__args") il.Emit(OpCodes.Ldloc, arguments);
                else if (instance) il.Emit(OpCodes.Ldarg, parameters.Length);
                else il.Emit(OpCodes.Ldnull);
            }
            il.Emit(OpCodes.Call, callback);
            LocalBuilder result = callback.ReturnType == typeof(void) ? null : il.DeclareLocal(callback.ReturnType);
            if (result != null) il.Emit(OpCodes.Stloc, result);
            for (int i = 0; i < parameters.Length; i++)
            {
                Type element = types[i].GetElementType();
                il.Emit(OpCodes.Ldarg, i); il.Emit(OpCodes.Ldloc, arguments); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldelem_Ref);
                if (element.IsValueType) il.Emit(OpCodes.Unbox_Any, element);
                else il.Emit(OpCodes.Castclass, element);
                il.Emit(OpCodes.Stobj, element);
            }
            if (result != null) il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            // Harmony serializes patch identities and requires a real factory
            // method for DynamicMethod callbacks. The factory itself needs no
            // access to any private game/mod type; its returned thunk has it.
            var factoryType = factories.DefineType("ArgumentFactory" + cache.Count, TypeAttributes.Public | TypeAttributes.Sealed);
            var thunk = factoryType.DefineField("Thunk", typeof(MethodInfo), FieldAttributes.Public | FieldAttributes.Static);
            var factory = factoryType.DefineMethod("Build", MethodAttributes.Public | MethodAttributes.Static,
                typeof(MethodInfo), new[] { typeof(MethodBase) });
            var factoryIl = factory.GetILGenerator();
            factoryIl.Emit(OpCodes.Ldsfld, thunk); factoryIl.Emit(OpCodes.Ret);
            Type made = factoryType.CreateType();
            made.GetField("Thunk").SetValue(null, wrapper);
            MethodInfo method = made.GetMethod("Build");
            cache.Add(key, method);
            return method;
        }
    }
}
