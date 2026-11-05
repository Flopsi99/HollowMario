using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // DropPlatform expects a dynamic Knight collision strictly above its top. Mario's
    // kinematic body can rest exactly on it without that entry. Reuse his existing foot
    // trigger and the native Flip coroutine, only while actually supported from above.
    public sealed class DropPlatformContact : MonoBehaviour
    {
        private static FieldInfo surfaceField, routineField;
        private static MethodInfo flip;
        private MonoBehaviour platform;

        internal static bool Arm(Harmony harmony, Type nativeType)
        {
            surfaceField = nativeType == null ? null : AccessTools.Field(nativeType, "collider");
            routineField = nativeType == null ? null : AccessTools.Field(nativeType, "flipRoutine");
            flip = nativeType == null ? null : AccessTools.Method(nativeType, "Flip", Type.EmptyTypes);
            MethodInfo awake = nativeType == null ? null : AccessTools.Method(nativeType, "Awake", Type.EmptyTypes);
            if (surfaceField == null || routineField == null || flip == null || awake == null) return false;
            harmony.Patch(awake, postfix: new HarmonyMethod(typeof(DropPlatformContact), nameof(AfterAwake)));
            return true;
        }

        internal static void AfterAwake(object __instance)
        {
            MonoBehaviour source = __instance as MonoBehaviour;
            if (source == null) return;
            DropPlatformContact contact = source.GetComponent<DropPlatformContact>();
            if (contact == null) contact = source.gameObject.AddComponent<DropPlatformContact>();
            contact.platform = source;
        }

        private void OnTriggerEnter2D(Collider2D other) => Touch(other);
        private void OnTriggerStay2D(Collider2D other) => Touch(other);

        internal void Touch(Collider2D other)
        {
            if (platform == null || !platform.isActiveAndEnabled || other == null || other.gameObject.layer != 9
                || routineField.GetValue(platform) != null) return;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || !follow.MarioAlive(controller) || follow.Busy(controller) || !follow.Grounded(controller)) return;
            Component hero = follow.Hero(controller) as Component;
            if (hero == null || other.gameObject != hero.gameObject) return;
            Collider2D surface = surfaceField.GetValue(platform) as Collider2D;
            Vector3? feet = follow.Ground(controller);
            if (surface == null || !surface.enabled || surface.isTrigger || !feet.HasValue) return;
            Bounds top = surface.bounds, foot = other.bounds;
            float height = feet.Value.y - top.max.y;
            if (height < -.08f || height > .12f
                || Mathf.Min(foot.max.x, top.max.x) - Mathf.Max(foot.min.x, top.min.x) <= .02f) return;
            routineField.SetValue(platform, platform.StartCoroutine((IEnumerator)flip.Invoke(platform, null)));
        }
    }
}
