using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Register only native ShadowGateColliderControl blockers (layer 13). Their own
    // FixedUpdate and the existing world tick drive this bridge; no scene/physics scan.
    internal static class ShadeGateCollision
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private sealed class Gate { internal Component Source; internal Collider2D Shape; internal uint? Id; internal bool Phased; internal float EntrySide; }
        private static readonly Dictionary<Component, Gate> gates = new Dictionary<Component, Gate>();
        private static FieldInfo collider, objectId, origin;
        private static MethodInfo create, delete;
        private static object world;
        private static Vector2 basis;
        private static ManualLogSource log;
        private static int failures;
        internal static Func<bool> AdditionalPassage;
        internal static bool ExtraPassageActive=>AdditionalPassage?.Invoke()==true;
        private static bool Phasing=>ShadeLongJump.Active||ExtraPassageActive;

        internal static void Arm(Harmony harmony, Type native, ManualLogSource logger)
        {
            log = logger;
            Type gate = AccessTools.TypeByName("ShadowGateColliderControl");
            Type collision = AccessTools.TypeByName("MarioHollow.WorldCollision");
            collider = gate?.GetField("disableCollider", Any);
            create = collision?.GetMethod("Create", Any);
            objectId = create?.ReturnType.GetField("Id", Any);
            delete = native?.GetMethod("sm64_surface_object_delete", Any);
            origin = AccessTools.TypeByName("MarioHollow.MarioMath")?.GetField("Origin", Any);
            MethodInfo start = gate?.GetMethod("Start", Any), step = gate?.GetMethod("FixedUpdate", Any);
            MethodInfo scan = collision?.GetMethod("Tick", Any), destroy = gate?.GetMethod("OnDestroy", Any);
            if (collider == null || objectId == null || delete == null || origin == null || start == null || step == null || scan == null)
            { log.LogWarning("Shade-gate geometry bridge could not be armed."); return; }
            harmony.Patch(start, postfix: new HarmonyMethod(typeof(ShadeGateCollision), nameof(Register)));
            harmony.Patch(step, prefix: new HarmonyMethod(typeof(ShadeGateCollision), nameof(BeforeGateStep)));
            if (destroy != null) harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(ShadeGateCollision), nameof(Remove)));
            harmony.Patch(scan, postfix: new HarmonyMethod(typeof(ShadeGateCollision), nameof(AfterWorldTick)));
        }

        private static void Register(Component __instance)
        {
            if (__instance != null && collider.GetValue(__instance) is Collider2D shape && shape != null && !shape.isTrigger)
                gates[__instance] = new Gate { Source = __instance, Shape = shape };
        }

        private static bool BeforeGateStep(Component __instance)
        {
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || !follow.MarioAlive(controller)) return true;
            if (gates.TryGetValue(__instance, out Gate gate) && gate.Shape != null)
                gate.Shape.enabled = !Phasing;
            return false; // Native push/visual FSMs already read the actual shadowDashing flag.
        }

        internal static void Open(object controller, KnightResetFollow follow)
        {
            Vector3? feet = follow.Ground(controller);
            foreach (Gate gate in gates.Values)
            {
                if (gate.Shape == null) continue;
                gate.EntrySide = feet.HasValue && feet.Value.x < gate.Shape.bounds.center.x ? -1f : 1f;
                gate.Phased = true;
                Drop(gate);
                gate.Shape.enabled = false;
            }
        }

        private static void AfterWorldTick(object __instance)
        {
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (gates.Count == 0 || controller == null || !follow.MarioAlive(controller)) return;
            try
            {
                Vector2 now = (Vector2)origin.GetValue(null);
                if (!ReferenceEquals(world, __instance) || now != basis) { Suspend(); world = __instance; basis = now; }
                Vector3? feet = follow.Ground(controller);
                if (!feet.HasValue) return;
                foreach (Gate gate in gates.Values)
                {
                    Collider2D shape = gate.Shape;
                    if (gate.Source == null || shape == null || !shape.gameObject.activeInHierarchy
                        || (gate.Source is Behaviour script && !script.isActiveAndEnabled)) { Drop(gate); continue; }
                    if (Phasing) { Drop(gate); shape.enabled = false; continue; }
                    shape.enabled = true;
                    if (gate.Phased) { gate.Phased = false; FinishCrossing(gate, controller, follow); }
                    Bounds bounds = shape.bounds;
                    if (feet.Value.x < bounds.min.x-28f || feet.Value.x > bounds.max.x+28f
                        || feet.Value.y < bounds.min.y-28f || feet.Value.y > bounds.max.y+28f) { Drop(gate); continue; }
                    if (!gate.Id.HasValue)
                    {
                        object entry = create.Invoke(__instance, new object[] { shape });
                        if (entry != null) gate.Id = Convert.ToUInt32(objectId.GetValue(entry));
                    }
                }
            }
            catch (Exception error) { if (failures++ < 4) log?.LogWarning("Shade-gate geometry: " + error.GetBaseException().Message); }
        }

        private static void FinishCrossing(Gate gate, object controller, KnightResetFollow follow)
        {
            Transform hero = follow.HeroTransform(controller);
            Collider2D body = hero == null ? null : hero.GetComponent<Collider2D>();
            if (body == null) return;
            Bounds barrier = gate.Shape.bounds;
            // If the charge expires inside the blocker, return to its entry face rather
            // than creating solid geometry through Mario or granting extra immunity.
            Bounds contact = body.bounds;
            contact.extents = new Vector3(Mathf.Max(contact.extents.x, .525f), contact.extents.y, contact.extents.z);
            if (contact.max.x < barrier.min.x || contact.min.x > barrier.max.x
                || contact.max.y < barrier.min.y || contact.min.y > barrier.max.y) return;
            float edge = gate.EntrySide < 0f ? barrier.min.x-contact.extents.x-.02f : barrier.max.x+contact.extents.x+.02f;
            float step = edge-contact.center.x;
            AutoMove.Prepare(controller); AutoMove.Shift(controller, step, 0f);
            hero.position += new Vector3(step, 0f, 0f);
            Rigidbody2D rigidbody = hero.GetComponent<Rigidbody2D>();
            if (rigidbody != null) rigidbody.position = new Vector2(hero.position.x, hero.position.y);
        }

        private static void Drop(Gate gate)
        {
            if (!gate.Id.HasValue) return;
            delete.Invoke(null, new object[] { gate.Id.Value }); gate.Id = null;
        }
        private static void Remove(Component __instance)
        {
            if (gates.TryGetValue(__instance, out Gate gate)) { Drop(gate); gates.Remove(__instance); }
        }
        internal static void Suspend()
        {
            foreach (Gate gate in gates.Values) { Drop(gate); if (gate.Shape != null) gate.Shape.enabled = true; }
            world = null;
        }
        internal static void Forget() { Suspend(); gates.Clear(); }
    }
}
