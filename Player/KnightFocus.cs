using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // Bridge native action gates, immediate focus/map ownership and grounded body returns.
    // The game's own FSMs retain input timing, costs, animation and cancellation rules.
    internal static class KnightFocus
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static FieldInfo instance, hero, marioId, brain, cState;
        private static FieldInfo body, box, feetOffset;
        private static PropertyInfo gameObject;
        private static MethodInfo findFsm;
        private static object mapHero, mapBrain;
        private static MethodInfo deactivate;
        private static object focusController, focusHero;
        private enum NativeAction { None, Focus, Map, Interaction }
        private static FieldInfo velocityTarget, velocityEveryFrame;
        private static PropertyInfo velocityFsm, movementOwner, movementState;
        private static MethodInfo movementTarget;
        private static object walkingFsm, walkingState;
        private static NativeAction owner;
        private static int logs;
        private static FieldInfo listenerInput, inputActions, castAction;
        private static PropertyInfo listenerFsm, brainFsm, castHeld, castPressed;
        private static PropertyInfo fsmVariables, floatValue;
        private static MethodInfo findFloat, canFocus, sendEvent;
        private static object heldHero, heldAction, holdDelay;
        private static float heldSince;
        private static bool holding, retrying;
        private static int pressFrame = -1, retryFrame = -1;

        internal static void Arm(Type controller)
        {
            instance = controller.GetField("Instance", Any);
            hero = controller.GetField("hero", Any);
            marioId = controller.GetField("MarioId", Any);
            deactivate = controller.GetMethod("Deactivate", Any);
            brain = hero?.FieldType.GetField("spellControl", Any);
            cState = hero?.FieldType.GetField("cState", Any);
            body = controller.GetField("body", Any);
            box = controller.GetField("box", Any);
            feetOffset = controller.GetField("feetOffset", Any);
            gameObject = hero?.FieldType.GetProperty("gameObject", Any);
            findFsm = gameObject == null ? null : brain?.FieldType.GetMethod("FindFsmOnGameObject", Any,
                null, new[] { gameObject.PropertyType, typeof(string) }, null);
            canFocus = hero?.FieldType.GetMethod("CanFocus", Any, null, Type.EmptyTypes, null);
            RemoveHealCrouch(controller.Assembly.GetType("MarioHollow.Sheets"));
        }

        internal static bool ArmCastInput(Type listener)
        {
            listenerInput = listener?.GetField("inputHandler", Any);
            inputActions = listenerInput?.FieldType.GetField("inputActions", Any);
            castAction = inputActions?.FieldType.GetField("cast", Any);
            castHeld = castAction?.FieldType.GetProperty("IsPressed", Any);
            castPressed = castAction?.FieldType.GetProperty("WasPressed", Any);
            listenerFsm = listener?.GetProperty("Fsm", Any);
            brainFsm = brain?.FieldType.GetProperty("Fsm", Any);
            fsmVariables = brain?.FieldType.GetProperty("FsmVariables", Any);
            findFloat = fsmVariables?.PropertyType.GetMethod("FindFsmFloat", Any, null, new[] { typeof(string) }, null);
            floatValue = findFloat?.ReturnType.GetProperty("Value", Any);
            sendEvent = brain?.FieldType.GetMethod("SendEvent", Any, null, new[] { typeof(string) }, null);
            return listenerInput != null && inputActions != null && castAction != null && castHeld != null
                && castPressed != null && listenerFsm != null && brainFsm != null && fsmVariables != null
                && findFloat != null && floatValue != null && sendEvent != null && canFocus != null;
        }

        internal static bool ArmScriptedWalk(Type action)
        {
            velocityTarget=action?.GetField("gameObject",Any);velocityEveryFrame=action?.GetField("everyFrame",Any);
            velocityFsm=action?.GetProperty("Fsm",Any);
            Type fsm=velocityFsm?.PropertyType;
            movementOwner=fsm?.GetProperty("GameObject",Any);movementState=fsm?.GetProperty("ActiveState",Any);
            movementTarget=velocityTarget==null?null:fsm?.GetMethod("GetOwnerDefaultTarget",Any,null,new[]{velocityTarget.FieldType},null);
            return movementOwner!=null&&movementState!=null&&movementTarget!=null&&velocityEveryFrame!=null;
        }

        // The native NPC/shop/stag walk action is already executing. Give it the body before
        // it sets velocity; retain ownership only while that same native state is active.
        internal static void BeforeScriptedWalk(object __instance)
        {
            if(velocityFsm==null||!(velocityEveryFrame.GetValue(__instance) is bool repeat&&repeat))return;
            object controller=instance?.GetValue(null),knight=controller==null?null:hero?.GetValue(controller);
            if(knight==null)return;
            object fsm=velocityFsm.GetValue(__instance,null),target=movementTarget.Invoke(fsm,new[]{velocityTarget.GetValue(__instance)});
            object actual=gameObject.GetValue(knight,null);
            if(!ReferenceEquals(target,actual)||ReferenceEquals(movementOwner.GetValue(fsm,null),actual))return;
            object nextState=movementState.GetValue(fsm,null);
            bool newState=!ReferenceEquals(walkingFsm,fsm)||!ReferenceEquals(walkingState,nextState);
            walkingFsm=fsm;walkingState=nextState;
            focusController=controller;focusHero=knight;owner=NativeAction.Interaction;
            if(newState)Plugin.Instance?.Log.LogInfo("Interaction movement owns the Knight until its native state finishes: "
                +Convert.ToString(nextState?.GetType().GetProperty("Name",Any)?.GetValue(nextState,null))+".");
            if(Convert.ToInt32(marioId.GetValue(controller))<0)return;
            AlignBody(controller);deactivate.Invoke(controller,null);
            Told("The game's scripted interaction walk owns the Knight until its movement state finishes.");
        }

        // Reuse the native cast listener's existing input polling. A failed focus attempt can
        // leave the FSM Inactive while the button remains held; its one-time entry check has
        // already been used. Retry its ordinary ALREADY DOWN branch once the hold is mature
        // and Mario is grounded/allowed, without forcing Focus Start or replacing any healing.
        internal static void AfterCastInput(object __instance)
        {
            if (retrying || castHeld == null || castPressed == null || sendEvent == null || canFocus == null
                || listenerFsm == null || brainFsm == null || floatValue == null) return;
            object controller = instance?.GetValue(null);
            object knight = controller == null ? null : hero?.GetValue(controller);
            object spell = knight == null ? null : brain?.GetValue(knight);
            if (spell == null || !ReferenceEquals(listenerFsm.GetValue(__instance, null), brainFsm.GetValue(spell, null))) return;
            object handler = listenerInput.GetValue(__instance);
            object controls = handler == null ? null : inputActions.GetValue(handler);
            object button = controls == null ? null : castAction.GetValue(controls);
            if (!ReferenceEquals(heldHero, knight) || !ReferenceEquals(heldAction, button))
            {
                ResetHeldInput();
                heldHero = knight; heldAction = button;
                object variables = fsmVariables.GetValue(spell, null);
                holdDelay = variables == null ? null : findFloat.Invoke(variables, new object[] { "Button Down Time" });
            }
            if (button == null || !(castHeld.GetValue(button, null) is bool held && held))
            { holding = false; return; }
            bool fresh = castPressed.GetValue(button, null) is bool pressed && pressed;
            if (!holding || (fresh && pressFrame != Time.frameCount))
            { heldSince = Time.time; holding = true; pressFrame = Time.frameCount; }
            if (fresh || holdDelay == null || retryFrame == Time.frameCount || SpellState(knight) != "Inactive") return;
            float delay = Convert.ToSingle(floatValue.GetValue(holdDelay, null));
            if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < 0f || Time.time-heldSince < delay) return;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            if (!CanPrepare(controller, follow, knight) || !follow.Grounded(controller)) return;
            PrepareFocus(knight);
            if (!(canFocus.Invoke(knight, null) is bool allowed && allowed)) return;
            retryFrame = Time.frameCount; // Set before dispatch: state entry can poll recursively.
            retrying = true;
            try
            {
                sendEvent.Invoke(spell, new object[] { "ALREADY DOWN" });
                Told("A held heal input reached the game's normal focus check as soon as Mario was ready.");
            }
            finally { retrying = false; }
        }

        private static void ResetHeldInput()
        {
            heldHero = heldAction = holdDelay = null;
            holding = false; heldSince = 0f; pressFrame = retryFrame = -1;
        }

        internal static void RemoveHealCrouch(Type sheets)
        {
            if (!(sheets?.GetField("Controls", Any)?.GetValue(null) is Array rows)) return;
            for (int i = 0; i < rows.Length; i++)
            {
                object row = rows.GetValue(i);
                Type type = row.GetType();
                if (Convert.ToString(type.GetField("Field", Any)?.GetValue(row)) != "buttonZ") continue;
                FieldInfo inputs = type.GetField("HkInputs", Any);
                if (!(inputs?.GetValue(row) is string[] names)) continue;
                // Crouch/pound uses its dedicated button only. Cast belongs to healing,
                // and Down stays directional (including descending during flight).
                inputs.SetValue(row, Array.FindAll(names, name => name != "cast" && name != "stickDown"));
                rows.SetValue(row, i); // ControlRow is a value type.
            }
        }

        private static bool Flag(object target, string name) => target != null
            && target.GetType().GetField(name, Any)?.GetValue(target) is bool on && on;

        private static object HeroState(object target) => cState?.GetValue(target);
        private static string SpellState(object target)
        {
            object fsm = brain?.GetValue(target);
            return Convert.ToString(fsm?.GetType().GetProperty("ActiveStateName", Any)?.GetValue(fsm, null));
        }

        private static bool Focusing(object target)
        {
            string name = SpellState(target);
            return Flag(HeroState(target), "focusing") || name.StartsWith("Focus", StringComparison.Ordinal)
                || name == "Set Focus Speed" || name == "Start Slug Anim";
        }

        private static string MapState(object target)
        {
            if (!ReferenceEquals(target, mapHero))
            {
                mapHero = target;
                mapBrain = target == null || findFsm == null ? null
                    : findFsm.Invoke(null, new[] { gameObject.GetValue(target, null), "Map Control" });
            }
            return Convert.ToString(mapBrain?.GetType().GetProperty("ActiveStateName", Any)?.GetValue(mapBrain, null));
        }

        private static bool ReadingMap(object target)
        {
            // These are the installed Map Control's body-owning and closing states.
            // Button checks and Regain Control are deliberately outside this interval.
            switch (MapState(target))
            {
                case "Open Map": case "Map Idle": case "Walk Left": case "Walk Right":
                case "Turn L?": case "Turn R?": case "Close Map": case "Close Doub Check":
                case "Double Fail": case "Get Inventory": case "Extra Check": return true;
                default: return false;
            }
        }

        // Both the initial cast check and later focus/map checks need current state. Fixing
        // CanFocus alone is too late when CanCast has already rejected the button press.
        internal static void PrepareFocus(object __instance)
        {
            object controller = instance?.GetValue(null);
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            if (CanPrepare(controller, follow, __instance)) follow.KeepInteractable(controller);
        }

        private static bool CanPrepare(object controller, KnightResetFollow follow, object knight)
        {
            if (controller == null || follow == null || !ReferenceEquals(hero?.GetValue(controller), knight)
                || !follow.MarioAlive(controller) || follow.Busy(controller)
                || follow.ControlHeld(controller) || !follow.AcceptingInput(controller)
                || follow.MarioKnockedDown(controller) || Swimming.UsesSwimControls(follow.Action(controller))) return false;
            object flags = HeroState(knight);
            if (Flag(flags, "recoiling") || Flag(flags, "recoilFrozen") || Flag(flags, "hazardDeath")
                || Flag(flags, "hazardRespawning") || Flag(flags, "dead")) return false;
            return true;
        }

        // Focus Start and Open Map both call RelinquishControl before animating the Knight.
        // Give them the real body in that call, before another Mario update can move it.
        internal static void ControlTaken(object __instance)
        {
            if(LakituDialogue.OwnsHero(__instance))return;
            object controller = instance?.GetValue(null);
            if (controller == null || !ReferenceEquals(hero?.GetValue(controller), __instance)
                || Convert.ToInt32(marioId?.GetValue(controller) ?? -1) < 0
                || !Flag(__instance, "controlReqlinquished")) return;
            NativeAction action = Focusing(__instance) ? NativeAction.Focus
                : ReadingMap(__instance) ? NativeAction.Map : NativeAction.None;
            focusController = controller;
            focusHero = __instance;
            owner = action == NativeAction.None ? NativeAction.Interaction : action;
            AlignBody(controller);
            deactivate.Invoke(controller, null);
            Told("The game's " + owner + " routine has the Knight's body immediately, aligned with Mario's feet.");
        }

        // Presentation can take the body before Open Map's RelinquishControl action.
        // Remember its owner first so the existing airborne map return still spins.
        internal static void MapPresentation(object controller,object knight)
        {
            if(controller==null||!ReferenceEquals(hero?.GetValue(controller),knight))return;
            focusController=controller;focusHero=knight;owner=NativeAction.Map;
            if(Convert.ToInt32(marioId?.GetValue(controller)??-1)>=0)AlignBody(controller);
        }

        private static void AlignBody(object controller)
        {
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            Transform transform = follow?.HeroTransform(controller);
            Vector3? ground = follow?.Ground(controller);
            Collider2D shape = box?.GetValue(controller) as Collider2D;
            Rigidbody2D rigidbody = body?.GetValue(controller) as Rigidbody2D;
            if (transform == null || !ground.HasValue || shape == null || !shape.enabled || shape.isTrigger) return;
            // Follow's old feetOffset may belong to a different collider pose. Recompute it
            // from the real solid box; never use the extra interaction trigger as the feet.
            Vector3 position = transform.position;
            float offset = position.y - shape.bounds.min.y;
            position.x = ground.Value.x;
            position.y = ground.Value.y + offset;
            feetOffset?.SetValue(controller, offset);
            transform.position = position;
            if (rigidbody != null)
            {
                rigidbody.position = new Vector2(position.x, position.y);
                rigidbody.velocity = Vector2.zero;
            }
            Physics2D.SyncTransforms();
        }

        // Track other native handovers too, so any safely grounded return uses the same
        // zero-gap placement. This does not request a handover or change lift/water ownership.
        internal static void SteppedAside(object controller)
        {
            if (Convert.ToInt32(marioId?.GetValue(controller) ?? -1) < 0) return;
            if (!ReferenceEquals(focusController, controller)
                || !ReferenceEquals(focusHero, hero?.GetValue(controller))) owner = NativeAction.None;
            focusController = controller;
            focusHero = hero?.GetValue(controller);
        }

        internal static bool KeepOut(object controller)
        {
            if (owner == NativeAction.None || !ReferenceEquals(controller, focusController)
                || !ReferenceEquals(hero?.GetValue(controller), focusHero)) return false;
            object flags = HeroState(focusHero);
            if (Flag(flags, "dead") || Flag(flags, "hazardDeath") || Flag(flags, "transitioning")) return false;
            return Flag(focusHero, "controlReqlinquished") || !Flag(focusHero, "acceptingInput")
                || (owner == NativeAction.Focus ? Focusing(focusHero) : owner == NativeAction.Map ? ReadingMap(focusHero)
                    : walkingFsm != null && walkingState != null && ReferenceEquals(movementState.GetValue(walkingFsm,null),walkingState));
        }

        internal static void Activated(object controller)
        {
            if (ReferenceEquals(controller, focusController)
                && Convert.ToInt32(marioId?.GetValue(controller) ?? -1) >= 0) Forget();
        }

        internal static void Forget()
        {
            focusController = null; focusHero = null; owner = NativeAction.None;
            walkingFsm = walkingState = null;
            mapHero = null; mapBrain = null;
            ResetHeldInput();
        }
        private static void Told(string line)
        {
            if (logs++ < 12) Plugin.Instance?.Log.LogInfo(line);
        }
    }
}
