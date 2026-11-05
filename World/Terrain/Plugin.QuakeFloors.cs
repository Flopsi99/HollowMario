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

        private void WatchQuakeFloors(object controller)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (knightFollow == null || fsmType == null || Time.unscaledTime < nextFloorScan)
            {
                return;
            }
            nextFloorScan = Time.unscaledTime + 0.5f;
            Vector3? feet = knightFollow.Feet(controller);
            if (!feet.HasValue)
            {
                return;
            }
            if (fsmNameProp == null)
            {
                fsmNameProp = fsmType.GetProperty("FsmName");
                fsmStateProp = fsmType.GetProperty("ActiveStateName");
            }
            ReadKnightFlags();
            UnityEngine.Object[] all;
            try
            {
                all = Resources.FindObjectsOfTypeAll(fsmType);
            }
            catch (Exception)
            {
                return;
            }
            for (int i = 0; i < all.Length; i++)
            {
                Component fsm = all[i] as Component;
                if (fsm == null)
                {
                    continue;
                }
                GameObject host = fsm.gameObject;
                if (host == null || !host.activeInHierarchy)
                {
                    continue;
                }
                string lower = host.name.ToLowerInvariant();
                if (lower.IndexOf("quake") < 0 && lower.IndexOf("crack") < 0 && lower.IndexOf("break") < 0 && lower.IndexOf("floor") < 0)
                {
                    continue;
                }
                Vector3 at = host.transform.position;
                if (Mathf.Abs(at.x - feet.Value.x) > 80f || Mathf.Abs(at.y - feet.Value.y) > 50f)
                {
                    continue;
                }
                string fsmName = fsmNameProp == null ? "?" : Convert.ToString(fsmNameProp.GetValue(fsm, null));
                string state = fsmStateProp == null ? "?" : Convert.ToString(fsmStateProp.GetValue(fsm, null));
                if (lower.IndexOf("quake") >= 0)
                {
                    DumpQuakeFsm(fsm, host, "quake floor");
                    LogKnightSpellParts(controller);
                }
                else if (lower.IndexOf("loose") >= 0 || lower.IndexOf("crumbl") >= 0)
                {
                    DumpQuakeFsm(fsm, host, "crumbling floor");
                }
                string key = host.GetInstanceID() + "/" + fsmName;
                string was;
                if (seenFloorStates.TryGetValue(key, out was) && was == state)
                {
                    continue;
                }
                seenFloorStates[key] = state;
                Log.LogInfo("floor watch: " + host.name + " [" + fsmName + "] is in state " + state + " at (" + at.x.ToString("0.0") + ", " + at.y.ToString("0.0") + ")" + QuakeVars(fsm, lower.IndexOf("quake") >= 0) + LastTrigger(host));
            }
        }

        private void ReadKnightFlags()
        {
            try
            {
                Type heroType = AccessTools.TypeByName("HeroController");
                if (heroType == null)
                {
                    return;
                }
                if (heroInstanceField == null)
                {
                    heroInstanceField = heroType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                }
                object hero = heroInstanceField != null ? heroInstanceField.GetValue(null) : null;
                if (hero == null)
                {
                    PropertyInfo instanceProperty = heroType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    hero = instanceProperty == null ? null : instanceProperty.GetValue(null, null);
                }
                if (hero == null)
                {
                    return;
                }
                FieldInfo cStateField = heroType.GetField("cState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object cState = cStateField == null ? null : cStateField.GetValue(hero);
                if (cState == null)
                {
                    return;
                }
                bool spellQuake = ReadBoolFlag(cState, "spellQuake");
                bool downAttack = ReadBoolFlag(cState, "downAttacking");
                bool castPlaying = ReadBoolFlag(cState, "castPlaying");
                if (!flagsKnown || spellQuake != lastSpellQuake || downAttack != lastDownAttack || castPlaying != lastCastPlaying)
                {
                    flagsKnown = true;
                    lastSpellQuake = spellQuake;
                    lastDownAttack = downAttack;
                    lastCastPlaying = castPlaying;
                    Log.LogInfo("floor watch: knight flags spellQuake=" + spellQuake + " downAttacking=" + downAttack + " castPlaying=" + castPlaying);
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool ReadBoolFlag(object cState, string name)
        {
            try
            {
                FieldInfo field = cState.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                {
                    return false;
                }
                object value = field.GetValue(cState);
                return value is bool && (bool)value;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
