using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // The game carries the Knight on lifts and other moving platforms by parenting him to them
    // (HeroPlatformStick does it on contact). The Knight is only a stand-in for Mario here, so this
    // watches for that kind of platform under Mario and, while he is on one, moves him along by the
    // same step it takes  Eand keeps the platform's copy in Mario's world at the platform's position,
    // so he has something to stand on at every step. Nothing else in the world is touched.
    internal static class PlatformCarry
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo collisionField;
        private static FieldInfo trackedField;
        private static FieldInfo idField;
        private static MethodInfo createMethod;
        private static MethodInfo deleteMethod;
        private static bool looked;

        private static Transform carrier;
        private static Collider2D carrierShape;
        private static Vector3 carrierAt;
        private static int refreshFailed;
        private static float lastReport;
        private static readonly Collider2D[] beneath = new Collider2D[16];

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
            Vector3? feet = follow.Feet(controller);
            if (hero == null || !feet.HasValue)
            {
                return;
            }
            Transform nowOn = Carrier(hero, feet.Value);
            if (nowOn == null)
            {
                carrier = null;
                carrierShape = null;
                return;
            }
            Vector3 now = nowOn.position;
            if (!ReferenceEquals(nowOn, carrier))
            {
                carrier = nowOn;
                carrierAt = now;
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.Log.LogInfo("Mario is riding " + nowOn.name + "; it will carry him.");
                }
                return;
            }
            Vector3 step = now - carrierAt;
            carrierAt = now;
            if (step.sqrMagnitude < 1e-8f || step.sqrMagnitude > 1f)
            {
                return;
            }
            AutoMove.Prepare(controller);
            AutoMove.Shift(controller, step.x, step.y);
            Refresh(controller, nowOn);
            if (Plugin.Instance != null && Time.unscaledTime - lastReport > 1f)
            {
                lastReport = Time.unscaledTime;
                Plugin.Instance.Log.LogInfo("riding " + nowOn.name + ": step ("
                    + step.x.ToString("0.000") + ", " + step.y.ToString("0.000") + "), copies "
                    + (refreshFailed > 0 ? "failed " + refreshFailed : "kept up"));
            }
            refreshFailed = 0;
        }

        private static Transform Carrier(Transform hero, Vector3 feet)
        {
            if (carrier != null)
            {
                if (hero.parent == carrier)
                {
                    return carrier;
                }
                if (carrierShape != null && carrierShape.enabled && carrier.gameObject.activeInHierarchy
                    && StillOn(carrierShape.bounds, feet))
                {
                    return carrier;
                }
                carrier = null;
                carrierShape = null;
            }
            Transform parent = hero.parent;
            if (parent != null)
            {
                Transform stick = StickIn(parent);
                if (stick != null)
                {
                    return stick;
                }
            }
            int count = Physics2D.OverlapCircleNonAlloc(new Vector2(feet.x, feet.y + 0.15f), 0.7f, beneath);
            for (int i = 0; i < count; i++)
            {
                Collider2D shape = beneath[i];
                if (shape == null || shape.isTrigger || !shape.enabled || !shape.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (shape.transform.IsChildOf(hero) || shape.transform == hero)
                {
                    continue;
                }
                if (!StillOn(shape.bounds, feet))
                {
                    continue;
                }
                Transform stick = StickIn(shape.transform);
                if (stick != null)
                {
                    carrierShape = shape;
                    return stick;
                }
            }
            return null;
        }
        private static bool StillOn(Bounds box, Vector3 feet)
        {
            return feet.x >= box.min.x - 0.4f
                && feet.x <= box.max.x + 0.4f
                && feet.y >= box.min.y - 0.4f
                && feet.y <= box.max.y + 0.5f;
        }

        private static Transform StickIn(Transform from)
        {
            for (Transform current = from; current != null; current = current.parent)
            {
                try
                {
                    MonoBehaviour[] parts = current.GetComponents<MonoBehaviour>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] != null && parts[i].GetType().Name == "HeroPlatformStick")
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

        private static void Refresh(object controller, Transform parent)
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
                Collider2D[] shapes = parent.GetComponentsInChildren<Collider2D>();
                for (int i = 0; i < shapes.Length; i++)
                {
                    Collider2D shape = shapes[i];
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
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.Log.LogWarning("couldn't keep the platform's ground under Mario: " + exception.Message);
                }
            }
        }

        internal static void Forget()
        {
            carrier = null;
            carrierShape = null;
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
                Type trackedType = trackedField?.FieldType.GetGenericArguments()[1];
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
