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

        private static bool ActivatePrefix(object __instance)
        {
            Instance?.knightFollow?.BeginRoomEntry(UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle);
            return true;
        }

        private static void ActivatePostfix(object __instance)
        {
            AudioTicksAllowed = HasLiveMario();
            if (Instance?.knightFollow == null || !Instance.knightFollow.MarioAlive(__instance)) return;
            AddFeetPad(__instance);
            KnightFocus.Activated(__instance);
            NativeCharmEffects.Activated(__instance);
        }

        private static void AddFeetPad(object controller)
        {
            if (Instance == null || Instance.knightFollow == null || controller == null)
            {
                return;
            }
            MakeFeetPad(Instance.knightFollow.HeroTransform(controller));
        }
        // The probe has no Player tag, HeroController, or shared player physics
        // body. Only explicit interaction routes receive the real hero in its
        // place; camera/room detectors never see a second Player object.
        private static void MakeFeetPad(Transform knight)
        {
            if (knight == null || feetPad != null)
            {
                return;
            }
            try
            {
                BoxCollider2D box = knight.GetComponent<BoxCollider2D>();
                if (box == null)
                {
                    return;
                }
                BoxCollider2D shape = InteractionProbe.Create(knight,box);
                feetPadId=shape.GetInstanceID();feetPad = shape;feetPadBody=box;
                shape.gameObject.SetActive(true);
                shape.enabled=true;
                foreach(Collider2D own in knight.GetComponentsInChildren<Collider2D>(true))
                    if(own!=shape)Physics2D.IgnoreCollision(shape,own,true);
                if(knight.GetComponent<CrumblingFloorContact>()==null)knight.gameObject.AddComponent<CrumblingFloorContact>();
                Instance.Log.LogInfo("Interaction probe has no Player tag or shared Knight physics body; only approved interaction contacts are forwarded.");
                Instance.Log.LogInfo("the pad under him spans " + shape.bounds.min.y.ToString("0.00") + " to " + shape.bounds.max.y.ToString("0.00") + ", the Knight stands at " + knight.position.y.ToString("0.00") + ".");
            }
            catch (Exception exception)
            {
                Instance.Log.LogWarning("the box under the Knight's feet couldn't be made: " + exception.Message);
            }
        }

        private static void RemoveFeetPad()
        {
            if (feetPad == null)
            {
                return;
            }
            feetPad.enabled = false;
            retiredFeetPad=feetPad; // Unity can deliver its queued exits after removal.
            retiredFeetPadId=feetPadId;feetPadId=0;
            retiredFeetPadBody=feetPadBody;
            UnityEngine.Object.Destroy(feetPad.gameObject);
            feetPad = feetPadBody = null;
        }

        internal static bool TryGetFeetPadBounds(out Bounds bounds)
        {
            if (feetPad != null && feetPad.enabled && feetPad.gameObject.activeInHierarchy)
            {
                bounds = feetPad.bounds;
                return true;
            }
            bounds = default(Bounds);
            return false;
        }

        internal static bool IsFeetPad(Collider2D collider)
        {
            if(ReferenceEquals(collider,null))return false;
            if(ReferenceEquals(collider,feetPad)||ReferenceEquals(collider,retiredFeetPad))return true;
            // This Unity version's == also compares managed wrappers. Match the
            // cached native ID so physics callback wrappers cannot bypass filters.
            int id=collider.GetInstanceID();
            return id!=0&&(id==feetPadId||id==retiredFeetPadId);
        }

        internal static Collider2D InteractionCollider(Collider2D sensor)
        {
            if(!IsFeetPad(sensor))return sensor;
            bool current=ReferenceEquals(sensor,feetPad)||(feetPadId!=0&&sensor.GetInstanceID()==feetPadId);
            return current?feetPadBody:retiredFeetPadBody;
        }
        internal static Collider2D InteractionSensor=>feetPad;

        private static void RecenterPrefix()
        {
            // A normal simulation recenter preserves world-space mesh history.
            // Direct teleports still discard interpolation from the old location.
            snapNextRender = !MarioView.HasPendingGeometry;
        }

        private static void RenderPrefix(ref float alpha)
        {
            if (snapNextRender)
            {
                snapNextRender = false;
                alpha = 1f;
            }
        }

        // The Knight's spells leave from whichever way the Knight is facing, so he is turned to the way
        // Mario is facing every tick; otherwise a fireball flies off the wrong way after a turn.
        private static void TickFacing(object controller)
        {
            if (Instance == null || Instance.knightFollow == null || controller == null)
            {
                return;
            }
            KnightResetFollow follow = Instance.knightFollow;
            bool wantRight = Mathf.Sin(follow.FaceAngle(controller)) >= 0f;
            if (follow.FacingRight(controller) == wantRight)
            {
                return;
            }
            follow.Face(controller, wantRight);
            if (facingLogs < 12)
            {
                facingLogs++;
                Transform knight = follow.HeroTransform(controller);
                Instance.Log.LogInfo("turning the Knight to face " + (wantRight ? "right" : "left")
                    + ": Mario angle " + follow.FaceAngle(controller).ToString("0.00")
                    + ", knight scale x now " + (knight == null ? "?" : knight.localScale.x.ToString("0.00"))
                    + ", knight says facing right " + follow.FacingRight(controller) + ".");
            }
        }

        // Physical water detection only supplies Mario's liquid plane. Native Knight swimming
        // is blocked at its FSM entry; never reinterpret a control lock based on pool detection.
        private static void KnightFreePostfix(ref bool __result, object __0)
        {
            Swimming.Sense(__0);
        }

    }
}
