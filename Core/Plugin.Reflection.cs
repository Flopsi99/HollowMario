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

        private static object ReadMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                try
                {
                    return property.GetValue(target, null);
                }
                catch (Exception)
                {
                }
            }
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                return null;
            }
            try
            {
                return field.GetValue(target);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FsmText(object value)
        {
            if (value == null)
            {
                return "";
            }
            object stored = Read(value, "Value") ?? Read(value, "Name");
            return Convert.ToString(stored ?? value);
        }

        private static void SetFsmValue(object variable, object value)
        {
            if (variable == null)
            {
                return;
            }
            try
            {
                PropertyInfo stored = variable.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (stored != null && stored.CanWrite)
                {
                    stored.SetValue(variable, value, null);
                }
            }
            catch (Exception)
            {
            }
        }

        private static object Read(object target, string member)
        {
            if (target == null || member == null)
            {
                return null;
            }
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                try
                {
                    return property.GetValue(target);
                }
                catch (Exception)
                {
                }
            }
            FieldInfo field = type.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(target);
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        private static string Events(object fsm)
        {
            if (Read(fsm, "Events") is not Array events)
            {
                return "nothing";
            }
            string known = "";
            for (int i = 0; i < events.Length && i < 14; i++)
            {
                object value = events.GetValue(i);
                if (value == null)
                {
                    continue;
                }
                known += (known.Length == 0 ? "" : ", ") + Convert.ToString(Read(value, "Name"));
            }
            return known.Length == 0 ? "nothing" : known;
        }

    }
}
