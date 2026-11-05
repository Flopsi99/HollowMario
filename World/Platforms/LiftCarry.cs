using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // Hollow Knight carries the Knight on a lift by making him a child of it, and PlatformCarry
    // follows that for the lifts whose carriage is marked with the game's HeroPlatformStick. The
    // big lifts take the same road without that mark: the game still parents the Knight to the
    // carriage, there is just no stick to recognise it by. So the rule here is the parenting
    // itself and nothing else: while the game holds the Knight as a child of something that is not
    // a stuck platform, Mario is moved by that holder's own step, and the copies of the holder's
    // ground in Mario's world are kept where the holder really is. Nothing is ever picked up from
    // the floor, so enemies and dropped weapons cannot move him.
    internal static class LiftCarry
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Collider2D[] beneath = new Collider2D[16];
        private static readonly HashSet<string> told = new HashSet<string>();

        private static FieldInfo collisionField;
        private static FieldInfo trackedField;
        private static FieldInfo idField;
        private static MethodInfo createMethod;
        private static MethodInfo deleteMethod;
        private static bool looked;

        private static Transform held;
        private static Vector3 heldAt;
        private static Collider2D[] parts = new Collider2D[0];
        private static int refreshFailed;
        private static float lastReport;

        internal static void Tick(object controller, KnightResetFollow follow)
        {
            if (controller == null || follow == null || !follow.Armed)
            {
                return;
            }
            if (!looked)
            {
                Look(controller.GetType());
            }
            Transform hero = follow.HeroTransform(controller);
            if (hero == null)
            {
                held = null;
                return;
            }
            Transform holder = hero.parent;
            if (holder == null || StickIn(holder) != null)
            {
                held = null;
                return;
            }
            Watch(hero, follow, follow.Feet(controller));
            if (!ReferenceEquals(holder, held))
            {
                held = holder;
                heldAt = holder.position;
                parts = Parts(holder);
                Note(holder);
                return;
            }
            Vector3 now = holder.position;
            Vector3 step = now - heldAt;
            heldAt = now;
            if (step.sqrMagnitude < 1e-9f || step.sqrMagnitude > 1f)
            {
                return;
            }
            AutoMove.Prepare(controller);
            AutoMove.Shift(controller, step.x, step.y);
            Refresh(controller);
            if (Plugin.Instance != null && Time.unscaledTime - lastReport > 1f)
            {
                lastReport = Time.unscaledTime;
                Plugin.Instance.Log.LogInfo("riding " + holder.gameObject.name + ": step ("
                    + step.x.ToString("0.000") + ", " + step.y.ToString("0.000") + "), copies "
                    + (refreshFailed > 0 ? "failed " + refreshFailed : "kept up"));
            }
            refreshFailed = 0;
        }

        // The holder's own ground: its colliders, unless the holder is large enough that walking
        // its whole tree every tick would be wasteful, in which case only the ones it carries
        // directly are kept up.
        private static Collider2D[] Parts(Transform holder)
        {
            try
            {
                Collider2D[] all = holder.GetComponentsInChildren<Collider2D>();
                if (all.Length <= 24)
                {
                    return all;
                }
                return holder.GetComponents<Collider2D>();
            }
            catch (Exception)
            {
                return new Collider2D[0];
            }
        }

        private static Transform StickIn(Transform from)
        {
            for (Transform current = from; current != null; current = current.parent)
            {
                try
                {
                    MonoBehaviour[] on = current.GetComponents<MonoBehaviour>();
                    for (int i = 0; i < on.Length; i++)
                    {
                        if (on[i] != null && on[i].GetType().Name == "HeroPlatformStick")
                        {
                            return current;
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        private static void Refresh(object controller)
        {
            if (createMethod == null || trackedField == null || collisionField == null)
            {
                return;
            }
            try
            {
                object world = collisionField.GetValue(controller);
                IDictionary tracked = world == null ? null : trackedField.GetValue(world) as IDictionary;
                if (tracked == null)
                {
                    return;
                }
                for (int i = 0; i < parts.Length; i++)
                {
                    Collider2D shape = parts[i];
                    if (shape == null || shape.isTrigger || !shape.enabled || !shape.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    if (!tracked.Contains(shape))
                    {
                        continue;
                    }
                    object old = tracked[shape];
                    uint id = Convert.ToUInt32(idField.GetValue(old));
                    if (deleteMethod != null)
                    {
                        deleteMethod.Invoke(null, new object[] { id });
                    }
                    object fresh = createMethod.Invoke(world, new object[] { shape });
                    if (fresh == null)
                    {
                        tracked.Remove(shape);
                        refreshFailed++;
                    }
                    else
                    {
                        tracked[shape] = fresh;
                    }
                }
            }
            catch (Exception exception)
            {
                refreshFailed++;
                if (Plugin.Instance != null && refreshFailed == 1)
                {
                    Plugin.Instance.Log.LogWarning("couldn't keep the lift's ground under Mario: " + exception.Message);
                }
            }
        }

        private static void Note(Transform holder)
        {
            string name = holder.gameObject.name;
            if (told.Count >= 12 || !told.Add("ride:" + name) || Plugin.Instance == null)
            {
                return;
            }
            Plugin.Instance.Log.LogInfo("Mario is riding " + name + "; the game holds the Knight by it without a platform stick, so its step carries him.");
        }

        // Says once per holder what the game is holding the Knight with and what is solid under his
        // feet there, so a ride that goes wrong can be told apart from the others in a log. It only
        // writes a line; nothing here moves anything.
        private static void Watch(Transform hero, KnightResetFollow follow, Vector3? feet)
        {
            Transform holder = hero.parent;
            if (holder == null || Plugin.Instance == null)
            {
                return;
            }
            string name = holder.gameObject.name;
            if (told.Count >= 12 || !told.Add("hold:" + name))
            {
                return;
            }
            string under = "";
            if (feet.HasValue)
            {
                int count = Physics2D.OverlapCircleNonAlloc(new Vector2(feet.Value.x, feet.Value.y + 0.15f), 0.7f, beneath);
                for (int i = 0; i < count && i < 6; i++)
                {
                    Collider2D shape = beneath[i];
                    if (shape == null)
                    {
                        continue;
                    }
                    Bounds box = shape.bounds;
                    under += (under.Length == 0 ? "" : " | ") + shape.gameObject.name
                        + " " + box.size.x.ToString("0.0") + "x" + box.size.y.ToString("0.0")
                        + (shape.isTrigger ? " trigger" : "")
                        + (shape.transform == holder || shape.transform.IsChildOf(holder) ? " held" : "");
                }
            }
            Plugin.Instance.Log.LogInfo("the Knight is held by " + name + " (" + Names(holder)
                + "); solid under his feet: " + (under.Length == 0 ? "nothing" : under) + ".");
        }

        private static string Names(Transform of)
        {
            string names = "";
            try
            {
                MonoBehaviour[] on = of.GetComponents<MonoBehaviour>();
                for (int i = 0; i < on.Length && i < 5; i++)
                {
                    if (on[i] != null)
                    {
                        names += (names.Length == 0 ? "" : ", ") + on[i].GetType().Name;
                    }
                }
            }
            catch (Exception)
            {
            }
            return names.Length == 0 ? "no scripts" : names;
        }

        internal static void Forget()
        {
            held = null;
            parts = new Collider2D[0];
            refreshFailed = 0;
            lastReport = 0f;
        }

        private static void Look(Type controllerType)
        {
            try
            {
                collisionField = controllerType.GetField("collision", Any);
                Type worldType = collisionField?.FieldType;
                trackedField = worldType?.GetField("tracked", Any);
                Type trackedType = trackedField == null ? null : trackedField.FieldType.GetGenericArguments()[1];
                idField = trackedType?.GetField("Id", Any);
                createMethod = worldType?.GetMethod("Create", Any);
                Type nativeType = controllerType.Assembly.GetType("MarioHollow.Native", false);
                deleteMethod = nativeType?.GetMethod("sm64_surface_object_delete", Static);
            }
            catch (Exception)
            {
                createMethod = null;
            }
            looked = true;
        }
    }
}
