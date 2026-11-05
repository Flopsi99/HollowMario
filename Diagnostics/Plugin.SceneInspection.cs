using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {

        private void WatchBreakables()
        {
            try
            {
                Type breakable = null;
                Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i].GetName().Name == "Assembly-CSharp")
                    {
                        breakable = loaded[i].GetType("Breakable");
                        break;
                    }
                }
                MethodInfo hit = breakable == null ? null : breakable.GetMethod("Hit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (hit == null)
                {
                    return;
                }
                MethodInfo watch = typeof(Plugin).GetMethod(nameof(BreakableHitPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(hit, prefix: new HarmonyMethod(watch));
                MethodInfo breakIt = breakable.GetMethod("Break", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (breakIt != null)
                {
                    MethodInfo breakWatch = typeof(Plugin).GetMethod(nameof(BreakableBreakPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(breakIt, prefix: new HarmonyMethod(breakWatch));
                }
                Log.LogInfo("watching breakable floors for hits that break them on their own.");
            }
            catch (Exception exception)
            {
                Log.LogWarning("couldn't watch breakable floors: " + exception.Message);
            }
        }

        private static void BreakableHitPrefix(object __instance, object[] __args)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (Instance == null)
            {
                return;
            }
            string name = "?";
            Component part = __instance as Component;
            if (part != null)
            {
                name = part.name;
            }
            string source = "nothing";
            string type = "?";
            try
            {
                object hit = __args != null && __args.Length > 0 ? __args[0] : null;
                if (hit != null)
                {
                    FieldInfo sourceField = hit.GetType().GetField("Source");
                    GameObject from = sourceField == null ? null : sourceField.GetValue(hit) as GameObject;
                    if (from != null)
                    {
                        source = from.name;
                    }
                    FieldInfo typeField = hit.GetType().GetField("AttackType");
                    object attackType = typeField == null ? null : typeField.GetValue(hit);
                    if (attackType != null)
                    {
                        type = attackType.ToString();
                    }
                }
            }
            catch (Exception)
            {
            }
            Instance.Log.LogWarning("a breakable was hit: " + name + " by " + source + " (" + type + ")");
            Instance.Log.LogWarning(Environment.StackTrace);
        }

        private static void BreakableBreakPrefix(object __instance)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (Instance == null)
            {
                return;
            }
            string name = "?";
            Component part = __instance as Component;
            if (part != null)
            {
                name = part.name;
            }
            Instance.Log.LogWarning("a breakable broke: " + name);
            Instance.Log.LogWarning(Environment.StackTrace);
        }

        private void WatchHeroState()
        {
            try
            {
                Type statesType = AccessTools.TypeByName("HeroControllerStates");
                MethodInfo getState = statesType == null ? null : AccessTools.Method(statesType, "GetState", new[] { typeof(string) });
                if (getState == null)
                {
                    Log.LogWarning("couldn't watch which knight state the world asks about.");
                }
                else
                {
                    MethodInfo watch = typeof(Plugin).GetMethod(nameof(HeroStateAskedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(getState, postfix: new HarmonyMethod(watch));
                }
                Type proxyType = AccessTools.TypeByName("PlayMakerUnity2DProxy");
                MethodInfo enter = proxyType == null ? null : AccessTools.Method(proxyType, "OnTriggerEnter2D", new[] { typeof(Collider2D) });
                if (enter == null)
                {
                    Log.LogWarning("couldn't watch what touches the quake floors.");
                }
                else
                {
                    MethodInfo watch = typeof(Plugin).GetMethod(nameof(ProxyTriggerEnterPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(enter, prefix: new HarmonyMethod(watch));
                }
                Type touchType = AccessTools.TypeByName("TouchShake");
                MethodInfo touchEnter = touchType == null ? null : AccessTools.Method(touchType, "OnTriggerEnter2D", new[] { typeof(Collider2D) });
                if (touchEnter == null)
                {
                    Log.LogWarning("couldn't watch what makes the floors shake.");
                }
                else
                {
                    MethodInfo watch = typeof(Plugin).GetMethod(nameof(TouchShakePrefix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(touchEnter, prefix: new HarmonyMethod(watch));
                }
            }
            catch (Exception exception)
            {
                Log.LogWarning("couldn't watch the quake floors' trigger: " + exception.Message);
            }
        }

        private static void HeroStateAskedPostfix(string stateName, bool __result)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (Instance == null || stateName == null)
            {
                return;
            }
            string key = stateName + "=" + __result;
            int seen;
            heroStateSeen.TryGetValue(key, out seen);
            heroStateSeen[key] = seen + 1;
            if (seen > 0 || heroStateLines >= 60)
            {
                return;
            }
            heroStateLines++;
            Instance.Log.LogInfo("the world asks: is the knight '" + stateName + "'? " + __result);
        }

        private static void ProxyTriggerEnterPrefix(object __instance, Collider2D __0)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (Instance == null || __0 == null || proxyEnterLines >= 40)
            {
                return;
            }
            Component part = __instance as Component;
            if (part == null || part.gameObject == null)
            {
                return;
            }
            string host = part.gameObject.name.ToLowerInvariant();
            if (host.IndexOf("quake") < 0 && host.IndexOf("crack") < 0 && host.IndexOf("floor") < 0)
            {
                return;
            }
            proxyEnterLines++;
            string chain = __0.name;
            Transform parent = __0.transform.parent;
            for (int i = 0; i < 3 && parent != null; i++)
            {
                chain = parent.name + "/" + chain;
                parent = parent.parent;
            }
            Instance.Log.LogInfo("quake floor " + part.gameObject.name + " was touched by " + chain + " tag=" + __0.tag + " layer=" + LayerMask.LayerToName(__0.gameObject.layer) + " trigger=" + __0.isTrigger);
        }

        private static void TouchShakePrefix(object __instance, Collider2D __0)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (Instance == null || __0 == null || shakeLines >= 30)
            {
                return;
            }
            Component part = __instance as Component;
            if (part == null || part.gameObject == null)
            {
                return;
            }
            shakeLines++;
            string chain = __0.name;
            Transform parent = __0.transform.parent;
            for (int i = 0; i < 3 && parent != null; i++)
            {
                chain = parent.name + "/" + chain;
                parent = parent.parent;
            }
            Instance.Log.LogInfo("the little shake on " + part.gameObject.name + " was touched by " + chain + " tag=" + __0.tag);
        }

        private void DumpQuakeFsm(Component fsm, GameObject host, string label)
        {
            string fsmName = fsmNameProp == null ? "?" : Convert.ToString(fsmNameProp.GetValue(fsm, null));
            if (!dumpedQuake.Add(label + "/" + host.GetInstanceID() + "/" + fsmName))
            {
                return;
            }
            EnableProxyDebug(host);
            try
            {
                Log.LogInfo(label + " " + host.name + " [" + fsmName + "] is at (" + host.transform.position.x.ToString("0.0") + ", " + host.transform.position.y.ToString("0.0") + ") on layer " + LayerMask.LayerToName(host.layer));
                Component[] parts = host.GetComponents<Component>();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null)
                    {
                        continue;
                    }
                    string extra = "";
                    Collider2D collider = parts[i] as Collider2D;
                    if (collider != null)
                    {
                        extra = " trigger=" + collider.isTrigger + " size=" + collider.bounds.size + " layer=" + LayerMask.LayerToName(collider.gameObject.layer);
                    }
                    Log.LogInfo(label + " " + host.name + ": has " + parts[i].GetType().Name + extra);
                }
                object fsmObject = ReadMember(fsm, "Fsm");
                if (fsmObject == null)
                {
                    return;
                }
                Array states = ReadMember(fsmObject, "States") as Array;
                if (states == null)
                {
                    return;
                }
                foreach (object stateObject in states)
                {
                    Log.LogInfo(label + " " + host.name + " [" + fsmName + "]: state '" + Convert.ToString(ReadMember(stateObject, "Name")) + "'");
                    Array actions = ReadMember(stateObject, "Actions") as Array;
                    if (actions != null)
                    {
                        foreach (object action in actions)
                        {
                            Log.LogInfo(label + " " + host.name + " [" + fsmName + "]:   does " + DescribeAction(action));
                        }
                    }
                    Array transitions = ReadMember(stateObject, "Transitions") as Array;
                    if (transitions != null)
                    {
                        foreach (object transition in transitions)
                        {
                            object fsmEvent = ReadMember(transition, "FsmEvent");
                            string eventName = fsmEvent == null ? "?" : Convert.ToString(ReadMember(fsmEvent, "Name"));
                            Log.LogInfo(label + " " + host.name + " [" + fsmName + "]:   when '" + eventName + "' -> " + Convert.ToString(ReadMember(transition, "ToState")));
                        }
                    }
                }
                DumpVariables(ReadMember(fsmObject, "Variables"), label + " " + host.name + " [" + fsmName + "]");
            }
            catch (Exception exception)
            {
                Log.LogWarning("couldn't read why the floor breaks: " + exception.Message);
            }
        }

        private static string DescribeAction(object action)
        {
            Type type = action.GetType();
            string text = type.Name;
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            int shown = 0;
            for (int i = 0; i < fields.Length && shown < 14; i++)
            {
                object value;
                try
                {
                    value = fields[i].GetValue(action);
                }
                catch (Exception)
                {
                    continue;
                }
                string shownValue = DescribeValue(value);
                if (shownValue != null)
                {
                    text = text + " " + fields[i].Name + "=" + shownValue;
                    shown++;
                }
            }
            return text;
        }

        private static string DescribeValue(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (value is Array array)
            {
                string text = "[";
                for (int i = 0; i < array.Length; i++)
                {
                    if (i > 0)
                    {
                        text += ", ";
                    }
                    text += DescribeValue(array.GetValue(i)) ?? "?";
                }
                return text + "]";
            }
            if (value is UnityEngine.Object unit)
            {
                return unit == null ? "null" : unit.name;
            }
            string simple = Simple(value);
            if (simple != null)
            {
                return simple;
            }
            string typeName = value.GetType().Name;
            if (typeName == "FsmOwnerDefault")
            {
                object option = ReadMember(value, "OwnerOption");
                object target = ReadMember(value, "GameObject");
                object targetName = target == null ? null : ReadMember(target, "Name");
                object targetValue = target == null ? null : ReadMember(target, "Value");
                GameObject targetGo = targetValue as GameObject;
                string name = Convert.ToString(targetName);
                return Convert.ToString(option) + (string.IsNullOrEmpty(name) ? "" : " " + name) + "=" + (targetGo == null ? "null" : targetGo.name);
            }
            if (typeName == "FsmEvent" || typeName == "FsmEventTarget")
            {
                return Convert.ToString(ReadMember(value, "Name"));
            }
            if (typeName.StartsWith("Fsm"))
            {
                string name = Convert.ToString(ReadMember(value, "Name"));
                object inner = ReadMember(value, "Value");
                string innerText = Simple(inner);
                if (!string.IsNullOrEmpty(name))
                {
                    return name + (innerText == null ? "" : "=" + innerText);
                }
                if (innerText != null)
                {
                    return innerText;
                }
            }
            string variableName = ReadMember(value, "variableName") as string;
            if (string.IsNullOrEmpty(variableName))
            {
                variableName = ReadMember(value, "stringValue") as string;
            }
            if (string.IsNullOrEmpty(variableName))
            {
                variableName = Convert.ToString(ReadMember(value, "boolValue"));
            }
            return string.IsNullOrEmpty(variableName) ? null : variableName;
        }

        private static string Simple(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (value is UnityEngine.Object unity)
            {
                return unity == null ? "null" : unity.name;
            }
            if (value is string || value is bool || value is int || value is float || value.GetType().IsEnum)
            {
                return value.ToString();
            }
            return null;
        }

        private static void EnableProxyDebug(GameObject host)
        {
            Component[] parts = host.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                Component part = parts[i];
                if (part == null || part.GetType().Name != "PlayMakerUnity2DProxy")
                {
                    continue;
                }
                FieldInfo debug = part.GetType().GetField("debug", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (debug != null)
                {
                    debug.SetValue(part, true);
                }
            }
        }

        private static string QuakeVars(object fsm, bool quakeHost)
        {
            if (!quakeHost)
            {
                return "";
            }
            try
            {
                object variables = ReadMember(ReadMember(fsm, "Fsm"), "Variables");
                string text = "";
                Array bools = ReadMember(variables, "BoolVariables") as Array;
                if (bools != null)
                {
                    for (int i = 0; i < bools.Length; i++)
                    {
                        object variable = bools.GetValue(i);
                        string name = Convert.ToString(ReadMember(variable, "Name"));
                        if (name == "Quaking" || name == "Activated")
                        {
                            text = text + " " + name + "=" + (Simple(ReadMember(variable, "Value")) ?? "?");
                        }
                    }
                }
                Array objects = ReadMember(variables, "ObjectVariables") as Array;
                if (objects != null)
                {
                    for (int i = 0; i < objects.Length; i++)
                    {
                        object variable = objects.GetValue(i);
                        string name = Convert.ToString(ReadMember(variable, "Name"));
                        if (name == "Hero Obj")
                        {
                            object value = ReadMember(variable, "Value");
                            UnityEngine.Object unit = value as UnityEngine.Object;
                            text = text + " Hero Obj=" + (unit == null ? "null" : unit.name);
                        }
                    }
                }
                Array gameObjects = ReadMember(variables, "GameObjectVariables") as Array;
                if (gameObjects != null)
                {
                    for (int i = 0; i < gameObjects.Length; i++)
                    {
                        object variable = gameObjects.GetValue(i);
                        string name = Convert.ToString(ReadMember(variable, "Name"));
                        if (name == "Hero Obj")
                        {
                            object value = ReadMember(variable, "Value");
                            GameObject unit = value as GameObject;
                            text = text + " Hero Obj=" + (unit == null ? "null" : unit.name);
                        }
                    }
                }
                return text;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string LastTrigger(GameObject host)
        {
            try
            {
                Component[] parts = host.GetComponents<Component>();
                for (int i = 0; i < parts.Length; i++)
                {
                    Component part = parts[i];
                    if (part == null || part.GetType().Name != "PlayMakerUnity2DProxy")
                    {
                        continue;
                    }
                    Collider2D last = ReadMember(part, "lastTrigger2DInfo") as Collider2D;
                    if (last != null)
                    {
                        return " last trigger: " + last.name + "/" + last.tag + " on " + LayerMask.LayerToName(last.gameObject.layer);
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        private void DumpVariables(object variables, string label)
        {
            DumpVariableArray(variables, "BoolVariables", label);
            DumpVariableArray(variables, "IntVariables", label);
            DumpVariableArray(variables, "StringVariables", label);
            DumpVariableArray(variables, "GameObjectVariables", label);
            DumpVariableArray(variables, "ObjectVariables", label);
        }

        private void DumpVariableArray(object variables, string name, string label)
        {
            Array values = ReadMember(variables, name) as Array;
            if (values == null)
            {
                return;
            }
            for (int i = 0; i < values.Length; i++)
            {
                object variable = values.GetValue(i);
                if (variable == null)
                {
                    continue;
                }
                object current = ReadMember(variable, "Value");
                object global = ReadMember(variable, "IsGlobal");
                string mark = global is bool && (bool)global ? " [global]" : "";
                Log.LogInfo(label + ": " + name + " " + Convert.ToString(ReadMember(variable, "Name")) + " = " + (Simple(current) ?? "?") + mark);
            }
        }

        private void LogKnightSpellParts(object controller)
        {
            if (knightSpellPartsShown || knightFollow == null)
            {
                return;
            }
            Transform knight = knightFollow.HeroTransform(controller);
            if (knight == null)
            {
                return;
            }
            knightSpellPartsShown = true;
            Log.LogInfo("knight spell parts, while Mario plays:");
            int shown = 0;
            Transform[] all = knight.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length && shown < 80; i++)
            {
                Transform part = all[i];
                if (part == null)
                {
                    continue;
                }
                string lower = part.name.ToLowerInvariant();
                bool interesting = lower.IndexOf("quake") >= 0 || lower.IndexOf("spell") >= 0
                    || lower.IndexOf("dive") >= 0 || lower.IndexOf("down") >= 0
                    || lower.IndexOf("attack") >= 0 || lower.IndexOf("hit") >= 0;
                if (!interesting)
                {
                    continue;
                }
                string collider = "";
                Collider2D shape = part.GetComponent<Collider2D>();
                if (shape != null)
                {
                    collider = " collider trigger=" + shape.isTrigger + " enabled=" + shape.enabled + " layer=" + LayerMask.LayerToName(part.gameObject.layer) + " tag=" + part.tag;
                }
                Log.LogInfo("  " + part.name + " activeSelf=" + part.gameObject.activeSelf + " activeInHierarchy=" + part.gameObject.activeInHierarchy + collider);
                shown++;
            }
        }

    }
}
