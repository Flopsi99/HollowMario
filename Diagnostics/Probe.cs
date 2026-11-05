using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace HollowMario
{
    // Mario is stopped by something the Knight walks straight through, and no scene collider near
    // him explains it: his own world is built from the colliders the original mod hands to Super
    // Mario 64, which is a different set. This reads that set, the one Mario actually collides
    // with, and writes what is within a few steps of him: name, layer, size, how many triangles it
    // became and how far away it is. That names the surface acting as the invisible wall.
    internal static class Probe
    {
        private const int ProbeLimit = 4;
        private const float Near = 2.5f;
        private const float Show = 3f;
        private const float Interval = 0.6f;

        private static readonly List<string> seen = new List<string>();
        private static readonly List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        private static readonly StringBuilder text = new StringBuilder(512);
        private static FieldInfo collisionField;
        private static FieldInfo trackedField;
        private static FieldInfo trianglesField;
        private static FieldInfo seenAtField;
        private static bool looked;
        private static float nextAt;
        private static int probes;

        internal static void Tick(object controller, KnightResetFollow follow)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (follow == null || controller == null || probes >= ProbeLimit || Time.unscaledTime < nextAt)
            {
                return;
            }
            Vector3? feet = follow.Feet(controller);
            if (feet == null)
            {
                return;
            }
            if (!looked)
            {
                Look(controller);
            }
            IDictionary tracked = Tracked(controller);
            if (tracked == null)
            {
                return;
            }
            Vector2 here = new Vector2(feet.Value.x, feet.Value.y);
            rows.Clear();
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool touching = false;
            try
            {
                foreach (DictionaryEntry entry in tracked)
                {
                    Collider2D collider = entry.Key as Collider2D;
                    if (collider == null)
                    {
                        continue;
                    }
                    Bounds box = collider.bounds;
                    float gap = Vector2.Distance(here, box.ClosestPoint(here));
                    if (gap > Show)
                    {
                        continue;
                    }
                    if (gap <= Near)
                    {
                        touching = true;
                    }
                    int tris = trianglesField == null ? 0 : Convert.ToInt32(trianglesField.GetValue(entry.Value));
                    rows.Add(new KeyValuePair<float, string>(gap, Describe(collider, tris, gap, here)));
                }
            }
            catch (Exception)
            {
                return;
            }
            if (!touching || rows.Count == 0)
            {
                return;
            }
            nextAt = Time.unscaledTime + Interval;
            string key = scene + "|" + here.x.ToString("0") + "|" + here.y.ToString("0");
            if (seen.Contains(key))
            {
                return;
            }
            seen.Add(key);
            probes++;
            rows.Sort(delegate (KeyValuePair<float, string> a, KeyValuePair<float, string> b) { return a.Key.CompareTo(b.Key); });
            text.Length = 0;
            text.Append("probe ").Append(scene).Append(" at (").Append(here.x.ToString("0.00")).Append(", ")
                .Append(here.y.ToString("0.00")).Append("), Mario's world has ").Append(rows.Count).Append(" surface(s) within ")
                .Append(Show.ToString("0")).Append(":");
            for (int i = 0; i < rows.Count; i++)
            {
                text.Append("\n  ").Append(rows[i].Value);
            }
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Log.LogInfo(text.ToString());
            }
        }

        internal static void Reset()
        {
            seen.Clear();
            nextAt = 0f;
        }

        private static void Look(object controller)
        {
            looked = true;
            try
            {
                collisionField = controller.GetType().GetField("collision", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type world = collisionField == null ? null : collisionField.FieldType;
                trackedField = world == null ? null : world.GetField("tracked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type trackedType = trackedField == null ? null : trackedField.FieldType.GetGenericArguments()[1];
                trianglesField = trackedType == null ? null : trackedType.GetField("Tris", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                seenAtField = trackedType == null ? null : trackedType.GetField("SeenAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                collisionField = null;
            }
        }

        private static IDictionary Tracked(object controller)
        {
            if (collisionField == null || trackedField == null)
            {
                return null;
            }
            try
            {
                object world = collisionField.GetValue(controller);
                return world == null ? null : trackedField.GetValue(world) as IDictionary;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Describe(Collider2D collider, int tris, float gap, Vector2 here)
        {
            GameObject owner = collider.gameObject;
            StringBuilder line = new StringBuilder(150);
            line.Append(owner.name).Append(" layer=").Append(owner.layer)
                .Append("(").Append(LayerMask.LayerToName(owner.layer)).Append(")")
                .Append(" tag=").Append(SafeTag(owner))
                .Append(" trigger=").Append(collider.isTrigger)
                .Append(" size=").Append(collider.bounds.size.x.ToString("0.00")).Append("x")
                .Append(collider.bounds.size.y.ToString("0.00"))
                .Append(" at=(").Append(collider.bounds.center.x.ToString("0.00")).Append(", ")
                .Append(collider.bounds.center.y.ToString("0.00")).Append(")")
                .Append(" tris=").Append(tris)
                .Append(" gap=").Append(gap.ToString("0.00"))
                .Append(" type=").Append(collider.GetType().Name);
            EdgeCollider2D edge = collider as EdgeCollider2D;
            if (edge != null)
            {
                line.Append(EdgeShape(edge, here));
            }
            try
            {
                Component[] parts = owner.GetComponents<Component>();
                int shown = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    Component part = parts[i];
                    if (part == null || part is Transform || part is Collider2D)
                    {
                        continue;
                    }
                    line.Append(shown == 0 ? " comps=" : ",").Append(part.GetType().Name);
                    shown++;
                    string fsm = FsmName(part);
                    if (!string.IsNullOrEmpty(fsm))
                    {
                        line.Append("[").Append(fsm).Append("]");
                    }
                }
            }
            catch (Exception)
            {
            }
            return line.ToString();
        }

        // The original turns a terrain line (EdgeCollider2D) into one thin slab per segment, so a
        // vertical segment in the line is a thin wall for Mario wherever it sits. This prints the
        // line's own points that are close to Mario, so a wall found right beside him can be told
        // apart from the floor he stands on.
        private static string EdgeShape(EdgeCollider2D edge, Vector2 here)
        {
            try
            {
                Vector2[] points = edge.points;
                if (points == null || points.Length < 2)
                {
                    return "";
                }
                StringBuilder line = new StringBuilder(120);
                int shown = 0;
                int total = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 at = edge.transform.TransformPoint(points[i]);
                    if (Vector2.Distance(at, here) > 4.5f)
                    {
                        continue;
                    }
                    total++;
                    if (shown >= 12)
                    {
                        continue;
                    }
                    if (shown == 0)
                    {
                        line.Append(" edge=").Append(points.Length).Append("pts near:");
                    }
                    shown++;
                    line.Append(" (").Append(at.x.ToString("0.0")).Append(", ").Append(at.y.ToString("0.0")).Append(")");
                }
                if (total > shown)
                {
                    line.Append(" +").Append(total - shown);
                }
                return line.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }
        private static string FsmName(Component part)
        {
            if (part.GetType().Name != "PlayMakerFSM")
            {
                return null;
            }
            try
            {
                FieldInfo field = part.GetType().GetField("FsmName", BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                {
                    return Convert.ToString(field.GetValue(part));
                }
                PropertyInfo property = part.GetType().GetProperty("FsmName", BindingFlags.Instance | BindingFlags.Public);
                return property == null ? null : Convert.ToString(property.GetValue(part, null));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeTag(GameObject owner)
        {
            try
            {
                return owner.tag;
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
