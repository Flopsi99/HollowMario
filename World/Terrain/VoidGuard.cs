using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // Detect an endless/out-of-world fall, then use F5's fixed first-landing
    // room entry and shared teleport cleanup. No rolling last-floor checkpoint
    // or native hazard-respawn destination can replace that saved entry.
    internal static class VoidGuard
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo objectsField;
        private static FieldInfo stateField;
        private static FieldInfo pyField;
        private static bool looked;

        private static float fallingSince;
        private static float cooldownUntil;
        private static object fallOwner;
        private static float descentSince=-1f;
        private static float nextDescentCheck;

        internal static void Tick(object controller, KnightResetFollow follow)
        {
            if (controller == null || follow == null || !follow.Armed || !follow.MarioAlive(controller)
                || follow.Busy(controller) || follow.ControlHeld(controller) || !follow.AcceptingInput(controller)
                || FallDestination.NativeOwns(follow.Hero(controller)))
            {
                ResetDescent();
                fallingSince = 0f;
                return;
            }
            if(LongFall(controller,follow))return;
            // Reuse the static terrain snapshot's bottom, including negative-coordinate
            // rooms. Leave real room transitions to the game's normal ownership path.
            Vector3? metalFeet=CapSystem.Metallic?follow.Ground(controller):null;
            if(metalFeet.HasValue&&metalFeet.Value.y < TerrainConversion.LowestY-12f&&ReturnToEntry(controller))return;
            if (!looked)
            {
                Look(controller.GetType());
            }
            if (objectsField == null)
            {
                return;
            }
            int near = Convert.ToInt32(objectsField.GetValue(null));
            if (near > 0)
            {
                fallingSince = 0f;
                return;
            }
            if (Time.unscaledTime < cooldownUntil)
            {
                return;
            }
            if (fallingSince == 0f)
            {
                fallingSince = Time.time;
            }
            float py = 0f;
            object state = stateField == null ? null : stateField.GetValue(controller);
            if (state != null && pyField != null)
            {
                py = Convert.ToSingle(pyField.GetValue(state));
            }
            if (Time.time - fallingSince < 0.8f && py > -1200f)
            {
                return;
            }
            ReturnToEntry(controller);
        }

        private static bool ReturnToEntry(object controller)
        {
            if (Time.unscaledTime < cooldownUntil || Plugin.Instance == null) return false;
            var follow=Plugin.Instance.knightFollow;Vector3? feet=follow?.Ground(controller);
            object hero=follow?.Hero(controller);
            // Apply the same gate to the long-fall, missing-world and Metal
            // routes. Nearby converted terrain alone cannot describe an open shaft.
            if(!feet.HasValue||FallDestination.NativeOwns(hero)||FallDestination.HasBelow(feet.Value,hero)
                ||!Plugin.Instance.ReturnToRoomEntry(controller))return false;
            fallingSince = 0f;
            ResetDescent();
            FallDestination.Forget();
            cooldownUntil = Time.unscaledTime + 3f;
            Plugin.Instance.Log.LogInfo("Mario fell out of the world; returned to this room's fixed entry landing.");
            return true;
        }

        internal static void Forget()
        {
            fallingSince = 0f;
            ResetDescent();
            FallDestination.Forget();
        }

        private static void ResetDescent(){fallOwner=null;descentSince=-1f;nextDescentCheck=0f;}
        private static bool LongFall(object controller,KnightResetFollow follow)
        {
            uint action=follow.Action(controller);
            // Metal's underwater fall/jump actions lack SM64's ordinary AIR flag.
            bool metalAir=(action&~1u)==0x000042F4u||(action&~1u)==0x000044F8u;
            if(!follow.MarioAlive(controller)||follow.Busy(controller)||follow.ControlHeld(controller)
                ||!follow.AcceptingInput(controller)||(follow.Grounded(controller)&&!metalAir)
                ||follow.VerticalSpeed(controller)>=-1f||Swimming.UsesSwimControls(action))
            {ResetDescent();return false;}
            if(!ReferenceEquals(fallOwner,controller)){ResetDescent();fallOwner=controller;}
            if(descentSince<0f)descentSince=Time.time;
            if(Time.time-descentSince<5f||Time.time<nextDescentCheck)return false;
            nextDescentCheck=Time.time+.5f;
            if(!ReturnToEntry(controller))return false;
            ResetDescent();return true;
        }

        private static void Look(Type controllerType)
        {
            try
            {
                Type worldType = controllerType.GetField("collision", Any)?.FieldType;
                objectsField = worldType?.GetField("Objects", Static);
                stateField = controllerType.GetField("State", Any);
                pyField = stateField?.FieldType.GetField("py", Any);
            }
            catch (Exception)
            {
                objectsField = null;
            }
            if (objectsField == null)
            {
                looked = true;
            }
        }
    }
}
