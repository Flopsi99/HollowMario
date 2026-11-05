using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // Some breakables (including grub bottles) listen for a tagged nail trigger rather than a
    // damage event. Let their own active action check its filters, store the collider and send
    // its configured event. No object names, rescue flags, or guessed event names belong here.
    internal static class NailTriggerSignals
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool Enabled(object action)
            =>action!=null&&!(Read(action,"Enabled") is bool enabled&&!enabled);
        internal static bool ClaimDamageEvent(object action,HashSet<object> delivered)
            =>Enabled(action)&&delivered.Add(action);
        internal static bool DeliverOnce(object action, Collider2D nail, HashSet<object> delivered)
        {
            if (action == null || delivered.Contains(action) || !Deliver(action, nail)) return false;
            // Track a matching active action, not its whole object. A contact while the
            // receiver is initializing or running another state must not consume its hit.
            delivered.Add(action);
            return true;
        }

        internal static bool Deliver(object action, Collider2D nail)
        {
            if (action == null || nail == null) return false;
            string name = action.GetType().Name;
            bool layerFilter = name == "Trigger2dEventLayer" || name == "Trigger2dEventToParent";
            if (name != "Trigger2dEvent" && !layerFilter) return false;
            if (!Enabled(action)) return false;

            object tag = Read(action, "collideTag");
            string wantedTag = Convert.ToString(Read(tag, "Value"));
            bool explicitTag = tag != null && !(Read(tag, "IsNone") is bool noTag && noTag)
                && !string.IsNullOrEmpty(wantedTag) && wantedTag != "Untagged";
            bool matchesTag = explicitTag && string.Equals(wantedTag, nail.gameObject.tag, StringComparison.Ordinal);
            object layer = layerFilter ? Read(action, "collideLayer") : null;
            bool explicitLayer = layer != null && !(Read(layer, "IsNone") is bool noLayer && noLayer)
                && Read(layer, "Value") is int;
            bool matchesLayer = explicitLayer && (int)Read(layer, "Value") == nail.gameObject.layer;

            // Do not turn a hit into a generic proximity/entry event (NPC prompts, doors, etc.).
            // The active action must explicitly opt into the nail's tag or layer first.
            if ((!explicitTag && !explicitLayer) || (explicitTag && !matchesTag) || (explicitLayer && !matchesLayer)) return false;
            string trigger = Convert.ToString(Read(action, "trigger"));
            string callback = trigger == "OnTriggerEnter2D" ? "DoTriggerEnter2D"
                : trigger == "OnTriggerStay2D" ? "DoTriggerStay2D" : null;
            if (callback == null) return false;
            MethodInfo handler = action.GetType().GetMethod(callback, Any, null, new[] { typeof(Collider2D) }, null);
            if (handler == null) return false;
            handler.Invoke(action, new object[] { nail });
            return true;
        }

        private static object Read(object target, string member)
        {
            if (target == null) return null;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(member, Any);
            return property != null ? property.GetValue(target, null) : type.GetField(member, Any)?.GetValue(target);
        }
    }
}
