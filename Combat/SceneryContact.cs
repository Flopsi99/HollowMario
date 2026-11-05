using UnityEngine;

namespace HollowMario
{
    internal static class SceneryContact
    {
        internal static Bounds SweepBounds(Bounds end, Vector3 motion)
        {
            return new Bounds(end.center - motion * .5f,
                end.size + new Vector3(Mathf.Abs(motion.x), Mathf.Abs(motion.y), 0f));
        }

        // Sweep the existing attack box along one simulation step. The broad query can
        // use its enclosing box; this exact segment test rejects empty diagonal corners.
        internal static bool Sweeps(Bounds end, Vector3 motion, Bounds target)
        {
            Vector3 start = end.center - motion;
            float enter = 0f, leave = 1f;
            return Clip(start.x, motion.x, target.min.x-end.extents.x, target.max.x+end.extents.x, ref enter, ref leave)
                && Clip(start.y, motion.y, target.min.y-end.extents.y, target.max.y+end.extents.y, ref enter, ref leave);
        }

        private static bool Clip(float start, float delta, float min, float max, ref float enter, ref float leave)
        {
            if (Mathf.Abs(delta) < .000001f) return start >= min && start <= max;
            float a = (min-start)/delta, b = (max-start)/delta;
            enter = Mathf.Max(enter, Mathf.Min(a,b));
            leave = Mathf.Min(leave, Mathf.Max(a,b));
            return enter <= leave;
        }

        internal static Bounds Reach(Bounds body, float facing, float pad, float forward, float up, float multiplier = 1f)
        {
            Bounds reach = new Bounds(body.center + new Vector3(facing >= 0 ? forward : -forward, up, 0),
                body.size + new Vector3(pad * 2, pad * 2, 0));
            return NailCharmReach.Scale(reach, body.center, multiplier);
        }

        internal static bool IsHitShape(Collider2D hit) => hit != null && hit.enabled
            && hit.gameObject.activeInHierarchy && hit.gameObject.layer != 13; // Native Hero Detector layer.

        internal static bool IsResponderShape(Collider2D contact, GameObject receiver)
        {
            // Native IHitResponder props can legitimately use a trigger as their nail shape.
            // Player bodies and awareness sensors still cannot stand in for a hit on a prop.
            return contact != null && contact.gameObject.layer != 9 && contact.gameObject.layer != 20
                && CanReceive(contact, receiver);
        }

        internal static bool CanReceive(Collider2D contact, GameObject receiver)
        {
            if (!IsHitShape(contact) || receiver == null) return false;
            if (contact.gameObject == receiver) return true;
            // A child sensor is not permission to hit its parent. If that receiver owns a
            // physical nail-hit shape, the attack query must overlap that shape directly.
            // Receivers with actual child hitboxes and no own collider retain their route.
            Collider2D[] own = receiver.GetComponents<Collider2D>();
            for (int i = 0; i < own.Length; i++)
                if (IsHitShape(own[i])) return false;
            return true;
        }
    }
}
