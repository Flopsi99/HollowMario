using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Some of Hollow Knight's terrain exists only for enemies: a block in the middle of an elevator
    // shaft, for one. The game tells the Knight's own body to ignore those colliders, so he walks
    // straight through them, while Mario's world is built from every terrain collider and he hits a
    // wall the Knight never sees. This skips, for Mario, whatever the game tells the Knight's body
    // to ignore, so the two agree everywhere instead of room by room.
    internal static class HeroIgnores
    {
        private const int LogLimit = 8;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly HashSet<string> told = new HashSet<string>();
        private static readonly HashSet<long> ignored = new HashSet<long>();
        private static Type heroType;
        private static PropertyInfo heroInstance;
        private static FieldInfo heroInstanceField;
        private static Collider2D[] heroBodies = new Collider2D[0];
        private static int heroBodiesFrame = -1;
        private static ManualLogSource log;

        internal static bool Arm(Harmony harmony, ManualLogSource source)
        {
            log = source;
            Type world = AccessTools.TypeByName("MarioHollow.WorldCollision");
            MethodInfo create = world == null ? null : AccessTools.Method(world, "Create", new[] { typeof(Collider2D) });
            if (create == null)
            {
                return false;
            }
            harmony.Patch(create, prefix: new HarmonyMethod(typeof(HeroIgnores), nameof(CreatePrefix)));
            heroType = AccessTools.TypeByName("HeroController");
            heroInstance = heroType == null ? null : heroType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            heroInstanceField = heroType == null ? null : heroType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return true;
        }

        internal static void Forget()
        {
            ignored.Clear();
            heroBodies = new Collider2D[0];
            heroBodiesFrame = -1;
        }

        private static bool CreatePrefix(Collider2D c, ref object __result)
        {
            if (c == null || !Ignored(c))
            {
                return true;
            }
            __result = null;
            Note(c);
            return false;
        }

        private static bool Ignored(Collider2D c)
        {
            long key = ((Component)c).GetInstanceID();
            if (ignored.Contains(key))
            {
                return true;
            }
            try
            {
                if (MarkedForHero(c) || TheKnightIgnores(c))
                {
                    ignored.Add(key);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        internal static bool ShouldIgnore(Collider2D c) => c != null && Ignored(c);

        private static bool MarkedForHero(Collider2D c)
        {
            Component[] parts = ((Component)c).gameObject.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                Component part = parts[i];
                Type type = part == null ? null : part.GetType();
                while (type != null)
                {
                    if (type.Name == "IgnoreHeroCollision")
                    {
                        return true;
                    }
                    type = type.BaseType;
                }
            }
            return false;
        }

        private static bool TheKnightIgnores(Collider2D c)
        {
            Collider2D[] bodies = HeroBodies();
            for (int i = 0; i < bodies.Length; i++)
            {
                Collider2D body = bodies[i];
                if (body != null && Physics2D.GetIgnoreCollision(body, c))
                {
                    return true;
                }
            }
            return false;
        }

        private static Collider2D[] HeroBodies()
        {
            if (heroBodiesFrame == Time.frameCount)
            {
                return heroBodies;
            }
            heroBodiesFrame = Time.frameCount;
            heroBodies = new Collider2D[0];
            try
            {
                object hero = heroInstance != null ? heroInstance.GetValue(null, null)
                    : heroInstanceField == null ? null : heroInstanceField.GetValue(null);
                Component part = hero as Component;
                if (part != null)
                {
                    heroBodies = part.GetComponents<Collider2D>();
                }
            }
            catch (Exception)
            {
            }
            return heroBodies;
        }

        private static void Note(Collider2D c)
        {
            if (log == null || told.Count >= LogLimit)
            {
                return;
            }
            string name = ((UnityEngine.Object)c).name;
            if (!told.Add(name))
            {
                return;
            }
            log.LogInfo("Mario now passes " + name + " the way the Knight does: the game tells the Knight's body to ignore it.");
        }
    }
}
