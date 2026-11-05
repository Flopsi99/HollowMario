using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using UnityEngine;

namespace HollowMario
{
    internal sealed class KnightResetFollow
    {
        private const float WorldScale = 0.0105f;
        internal const uint KnockedDownActionFlag = 0x00020000u;
        private const uint AirActionFlag = 0x00000800u;

        private readonly ManualLogSource log;
        private readonly FieldInfo instanceField;
        private readonly FieldInfo marioIdField;
        private readonly FieldInfo stateField;
        private readonly FieldInfo prevStateField;
        private readonly FieldInfo markPositionField;
        private readonly FieldInfo heroField;
        private readonly FieldInfo feetOffsetField;
        private readonly FieldInfo originField;
        private readonly MethodInfo recenterMethod;
        private readonly PropertyInfo heroTransformProperty;
        private readonly FieldInfo cStateField;
        private readonly FieldInfo transitionStateField;
        private readonly FieldInfo transitioningField;
        private readonly FieldInfo deadField;
        private readonly FieldInfo invulnerableField;
        private readonly FieldInfo facingRightField;
        private readonly FieldInfo recoilingField;
        private readonly FieldInfo freezeDownField;
        private readonly FieldInfo pulseField;
        private readonly MethodInfo pulseStartMethod;
        private readonly MethodInfo pulseStopMethod;
        private readonly FieldInfo statePx;
        private readonly FieldInfo statePy;
        private readonly FieldInfo statePz;
        private readonly FieldInfo stateActionField;
        private readonly FieldInfo stateAnimField;
        private readonly FieldInfo stateAnimFrame;
        private readonly FieldInfo stateForwardField;
        private readonly FieldInfo stateFaceField;
        private readonly FieldInfo stateVx;
        private readonly FieldInfo stateVy;
        private readonly FieldInfo stateVz;
        private readonly FieldInfo groundedField;
        private readonly FieldInfo swimmingField;
        private readonly FieldInfo hazardDeathField;
        private readonly FieldInfo attackingField;
        private readonly FieldInfo dashingField;
        private readonly FieldInfo backDashingField;
        private readonly FieldInfo heroStateField;
        private readonly FieldInfo acceptingInputField;
        private readonly FieldInfo controlRequinlishedField;
        private readonly MethodInfo canTalkMethod;
        private readonly MethodInfo canInteractMethod;
        private readonly FieldInfo prevPx;
        private readonly FieldInfo prevPy;
        private readonly FieldInfo prevPz;
        private readonly float thresholdSqr;
        private readonly MethodInfo setPositionMethod;
        private readonly MethodInfo setVelocityMethod;
        private readonly MethodInfo setActionMethod,setForwardMethod;
        private Forward nativeForward;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Forward(int id,float speed);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
        private int lastId = -2;
        private float activeAt;
        private float settleUntil;
        private int roomEntryScene = int.MinValue;
        private bool roomEntryPending;
        private bool roomEntrySaved;
        private Vector3 roomEntryGround;
        private object roomEntryHero;

        internal bool Armed { get; private set; }
        internal string Problem { get; private set; }
        internal int ResetsFollowed { get; private set; }

        internal KnightResetFollow(ManualLogSource log, Type controllerType, int thresholdMilli)
        {
            this.log = log;
            float threshold = thresholdMilli / 1000f;
            thresholdSqr = threshold * threshold;
            try
            {
                instanceField = Find(controllerType, "Instance", BindingFlags.Static);
                marioIdField = Find(controllerType, "MarioId", BindingFlags.Instance);
                stateField = Find(controllerType, "State", BindingFlags.Instance);
                prevStateField = Find(controllerType, "PrevState", BindingFlags.Instance);
                markPositionField = Find(controllerType, "MarkPosition", BindingFlags.Instance);
                heroField = Find(controllerType, "hero", BindingFlags.Instance);
                feetOffsetField = Find(controllerType, "feetOffset", BindingFlags.Instance);
                recenterMethod = controllerType.GetMethod("Recenter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type mathType = controllerType.Assembly.GetType("MarioHollow.MarioMath", false);
                originField = mathType == null ? null : mathType.GetField("Origin", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (stateField != null)
                {
                    Type stateType = stateField.FieldType;
                    statePx = Find(stateType, "px", BindingFlags.Instance);
                    statePy = Find(stateType, "py", BindingFlags.Instance);
                    statePz = Find(stateType, "pz", BindingFlags.Instance);
                    stateActionField = Find(stateType, "action", BindingFlags.Instance);
                    stateAnimField = Find(stateType, "animID", BindingFlags.Instance);
                    stateAnimFrame = Find(stateType, "animFrame", BindingFlags.Instance);
                    stateForwardField = Find(stateType, "forwardVelocity", BindingFlags.Instance);
                    stateFaceField = Find(stateType, "faceAngle", BindingFlags.Instance);
                    stateVx = Find(stateType, "vx", BindingFlags.Instance);
                    stateVy = Find(stateType, "vy", BindingFlags.Instance);
                    stateVz = Find(stateType, "vz", BindingFlags.Instance);
                }
                if (prevStateField != null)
                {
                    Type prevType = prevStateField.FieldType;
                    prevPx = Find(prevType, "px", BindingFlags.Instance);
                    prevPy = Find(prevType, "py", BindingFlags.Instance);
                    prevPz = Find(prevType, "pz", BindingFlags.Instance);
                }
                if (heroField != null)
                {
                    Type heroType = heroField.FieldType;
                    heroTransformProperty = heroType.GetProperty("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    cStateField = Find(heroType, "cState", BindingFlags.Instance);
                    transitionStateField = Find(heroType, "transitionState", BindingFlags.Instance);
                    Type cStateType = cStateField == null ? null : cStateField.FieldType;
                    if (cStateType != null)
                    {
                        transitioningField = Find(cStateType, "transitioning", BindingFlags.Instance);
                        deadField = Find(cStateType, "dead", BindingFlags.Instance);
                        invulnerableField = Find(cStateType, "invulnerable", BindingFlags.Instance);
                        facingRightField = Find(cStateType, "facingRight", BindingFlags.Instance);
                        recoilingField = Find(cStateType, "recoiling", BindingFlags.Instance);
                        groundedField = Find(cStateType, "onGround", BindingFlags.Instance);
                        swimmingField = Find(cStateType, "swimming", BindingFlags.Instance);
                        hazardDeathField = Find(cStateType, "hazardDeath", BindingFlags.Instance);
                        attackingField = Find(cStateType, "attacking", BindingFlags.Instance);
                        dashingField = Find(cStateType, "dashing", BindingFlags.Instance);
                        backDashingField = Find(cStateType, "backDashing", BindingFlags.Instance);
                    }
                    heroStateField = Find(heroType, "hero_state", BindingFlags.Instance);
                    acceptingInputField = Find(heroType, "acceptingInput", BindingFlags.Instance);
                    controlRequinlishedField = Find(heroType, "controlReqlinquished", BindingFlags.Instance);
                    canTalkMethod = heroType.GetMethod("CanTalk", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    canInteractMethod = heroType.GetMethod("CanInteract", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    freezeDownField = Find(heroType, "DAMAGE_FREEZE_DOWN", BindingFlags.Instance);
                    pulseField = Find(heroType, "invPulse", BindingFlags.Instance);
                    Type pulseType = pulseField == null ? null : pulseField.FieldType;
                    pulseStartMethod = pulseType == null ? null : pulseType.GetMethod("startInvulnerablePulse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    pulseStopMethod = pulseType == null ? null : pulseType.GetMethod("stopInvulnerablePulse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                Type nativeType = controllerType.Assembly.GetType("MarioHollow.Native", false);
                BindingFlags instanceScope = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type[] moveArgs = { typeof(int), typeof(float), typeof(float), typeof(float) };
                setPositionMethod = nativeType == null ? null : FindMove(nativeType, "sm64_set_mario_position", moveArgs, instanceScope);
                setVelocityMethod = nativeType == null ? null : FindMove(nativeType, "sm64_set_mario_velocity", moveArgs, instanceScope);
                setActionMethod=nativeType?.GetMethod("sm64_set_mario_action",instanceScope);
                setForwardMethod=nativeType?.GetMethod("sm64_set_mario_forward_velocity",instanceScope);

                Armed = instanceField != null && marioIdField != null && stateField != null && prevStateField != null
                    && heroField != null && feetOffsetField != null && originField != null && recenterMethod != null
                    && heroTransformProperty != null && statePx != null && statePy != null && statePz != null
                    && prevPx != null && prevPy != null && setPositionMethod != null && setVelocityMethod != null;
                Problem = Armed ? "" : "a Mario controller member wasn't found";
            }
            catch (Exception exception)
            {
                Armed = false;
                Problem = exception.Message;
            }
        }

        private static FieldInfo Find(Type type, string name, BindingFlags scope)
        {
            return type.GetField(name, scope | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static MethodInfo FindMove(Type type, string name, Type[] args, BindingFlags scope)
        {
            return type.GetMethod(name, scope, null, args, null);
        }

        internal void NoteSceneChange()
        {
            settleUntil = Time.unscaledTime + 1.5f;
        }

        // Called only when an allowed Mario activation begins. Unity's scene
        // handle distinguishes a fresh visit/reload of the same named room.
        // Focus/map/character toggles in that same room never re-arm the point.
        internal void BeginRoomEntry(int scene)
        {
            if (roomEntryScene == scene) return;
            roomEntryScene = scene;
            roomEntryPending = true;
            roomEntrySaved = false;
            roomEntryHero = null;
        }

        internal bool RememberRoomEntry(object controller, int scene)
        {
            if (!roomEntryPending || roomEntrySaved || scene != roomEntryScene ||
                !Armed || !MarioAlive(controller) || Busy(controller) ||
                ControlHeld(controller) || !AcceptingInput(controller)) return false;
            uint move = Action(controller);
            uint group = move & 0x1C0u;
            // AIR absence alone also includes swimming, spawn/cutscene and
            // automatic actions. Save a real ground-action group, not those.
            if (move == 0 || (move & AirActionFlag) != 0 ||
                (group != 0u && group != 0x40u && group != 0x180u)) return false;
            Vector3? point = Ground(controller);
            if (!point.HasValue || !Finite(point.Value.x) || !Finite(point.Value.y)) return false;
            object hero = Hero(controller);
            if (hero == null || ReadBool(hazardDeathField, CState(hero))) return false;
            roomEntryGround = point.Value;
            roomEntryHero = hero;
            roomEntrySaved = true;
            roomEntryPending = false;
            log.LogInfo("F5 room entry saved at Mario's first landing: (" + point.Value.x.ToString("0.00") + ", " + point.Value.y.ToString("0.00") + ").");
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal bool ReturnToRoomEntry(object controller, int scene)
        {
            if (!Armed || controller == null || !roomEntrySaved || roomEntryScene != scene ||
                !ReferenceEquals(roomEntryHero, Hero(controller)) || Busy(controller) ||
                ReadBool(hazardDeathField, CState(Hero(controller)))) return false;
            Transform hero = HeroTransform(controller);
            if (hero == null) return false;
            float offset = (float)feetOffsetField.GetValue(controller);
            // Native Knight focus can change the solid box's pose while Mario
            // is parked. Align whichever body is current to the saved ground.
            if (!MarioAlive(controller))
            {
                BoxCollider2D box = hero.GetComponent<BoxCollider2D>();
                if (box != null && box.enabled && !box.isTrigger) offset = hero.position.y - box.bounds.min.y;
            }
            Vector3 target = new Vector3(roomEntryGround.x, roomEntryGround.y + offset, hero.position.z);
            if (hero.parent != null) hero.SetParent(null, true);
            hero.position = target;
            Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
            if (body != null) { body.position = new Vector2(target.x, target.y); body.velocity = Vector2.zero; }
            if (MarioAlive(controller))
                Teleport(controller, stateField.GetValue(controller), MarioId(controller), target, offset, Origin);
            // Never change the saved room point here or wait for the ordinary
            // distance/settling heuristic: F5 must work even nearby or just after landing.
            hasLastFeet = false;
            Physics2D.SyncTransforms();
            log.LogInfo("Returned to the fixed room-entry landing point.");
            return true;
        }

        internal object Controller()
        {
            return instanceField == null ? null : instanceField.GetValue(null);
        }

        internal bool MarioAlive(object controller)
        {
            if (controller == null || marioIdField == null)
            {
                return false;
            }
            try
            {
                return (int)marioIdField.GetValue(controller) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal bool InvincibilityArmed => invulnerableField != null && recoilingField != null;

        internal bool MarioInvincibleState(object hero = null)
        {
            object controller = Controller();
            return MarioAlive(controller) && (hero == null || ReferenceEquals(Hero(controller), hero))
                && MarioKnockedDown(controller);
        }

        internal void SetHeroInvulnerable(object hero, bool value)
        {
            object cState = CState(hero);
            SetBool(invulnerableField, cState, value);
        }

        internal void SetHeroRecoiling(object hero, bool value)
        {
            SetBool(recoilingField, CState(hero), value);
        }

        internal float FreezeDown(object hero)
        {
            return freezeDownField == null || hero == null ? 0f : (float)freezeDownField.GetValue(hero);
        }

        internal void Pulse(object hero, bool on)
        {
            object pulse = hero == null || pulseField == null ? null : pulseField.GetValue(hero);
            MethodInfo method = on ? pulseStartMethod : pulseStopMethod;
            if (pulse == null || method == null)
            {
                return;
            }
            try
            {
                method.Invoke(pulse, null);
            }
            catch (Exception)
            {
            }
        }

        private static void SetBool(FieldInfo field, object target, bool value)
        {
            if (field == null || target == null)
            {
                return;
            }
            if ((bool)field.GetValue(target) != value)
            {
                field.SetValue(target, value);
            }
        }

        private object CState(object hero)
        {
            return hero == null || cStateField == null ? null : cStateField.GetValue(hero);
        }

        // The game's water says "the Knight is swimming" through its own cState flag, set by the
        // scene's water trigger even while the Knight is hidden behind Mario. That is the signal for
        // letting Mario swim instead of handing the water back to the Knight.
        internal bool Swimming(object controller)
        {
            if (controller == null || heroField == null || swimmingField == null)
            {
                return false;
            }
            try
            {
                return ReadBool(swimmingField, CState(heroField.GetValue(controller)));
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal bool AcceptingInput(object controller)
        {
            if (controller == null || heroField == null || acceptingInputField == null)
            {
                return false;
            }
            try
            {
                return ReadBool(acceptingInputField, heroField.GetValue(controller));
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal bool ControlHeld(object controller)
        {
            if (controller == null || heroField == null || controlRequinlishedField == null)
            {
                return false;
            }
            try
            {
                return ReadBool(controlRequinlishedField, heroField.GetValue(controller));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Water can lock the Knight's controls away (held, not accepting input) while Mario is the
        // one playing. Since the Knight is parked and cannot use them anyway, they are handed back
        // so the original mod's test for "the game has the Knight" stays true and Mario keeps playing.
        internal bool RetakeControl(object controller)
        {
            if (controller == null || heroField == null)
            {
                return false;
            }
            try
            {
                object hero = heroField.GetValue(controller);
                if (hero == null)
                {
                    return false;
                }
                bool held = ReadBool(controlRequinlishedField, hero);
                bool listening = ReadBool(acceptingInputField, hero);
                if (!held && listening)
                {
                    return false;
                }
                SetBool(controlRequinlishedField, hero, false);
                SetBool(acceptingInputField, hero, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        internal int TransitionState(object controller)
        {
            if (controller == null || heroField == null || transitionStateField == null)
            {
                return -1;
            }
            try
            {
                object raw = transitionStateField.GetValue(heroField.GetValue(controller));
                return raw == null ? -1 : Convert.ToInt32(raw);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // Where the game itself puts Mario this tick, before any water level is worked out.
        internal Vector2 Origin => originField == null ? Vector2.zero : (Vector2)originField.GetValue(null);

        private static bool ReadBool(FieldInfo field, object target)
        {
            if (field == null || target == null)
            {
                return false;
            }
            try
            {
                object value = field.GetValue(target);
                return value != null && Convert.ToBoolean(value);
            }
            catch (Exception)
            {
                return false;
            }
        }


        internal bool MarioKnockedDown(object controller)
        {
            if (controller == null || stateField == null || stateActionField == null)
            {
                return false;
            }
            try
            {
                object state = stateField.GetValue(controller);
                if (state == null)
                {
                    return false;
                }
                object raw = stateActionField.GetValue(state);
                if (raw == null)
                {
                    return false;
                }
                uint action = Convert.ToUInt32(raw);
                return (action & KnockedDownActionFlag) != 0u;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal uint Action(object controller)
        {
            object state = State(controller);
            if (state == null || stateActionField == null)
            {
                return 0u;
            }
            try
            {
                return Convert.ToUInt32(stateActionField.GetValue(state));
            }
            catch (Exception)
            {
                return 0u;
            }
        }

        internal int Animation(object controller)
        {
            object state = State(controller);
            if (state == null || stateAnimField == null)
            {
                return -1;
            }
            try
            {
                return Convert.ToInt32(stateAnimField.GetValue(state));
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal bool FacingRight(object controller)
        {
            object state = CState(Hero(controller));
            if (state == null || facingRightField == null)
            {
                return true;
            }
            try
            {
                return facingRightField.GetValue(state) is bool right && right;
            }
            catch (Exception)
            {
                return true;
            }
        }

        internal void Face(object controller, bool right)
        {
            object hero = Hero(controller);
            object state = CState(hero);
            if (state == null || facingRightField == null)
            {
                return;
            }
            try
            {
                MethodInfo face = hero.GetType().GetMethod(right ? "FaceRight" : "FaceLeft",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (face != null)
                {
                    face.Invoke(hero, null);
                    return;
                }
                facingRightField.SetValue(state, right);
            }
            catch (Exception)
            {
            }
        }

        internal int AnimFrame(object controller)
        {
            object state = State(controller);
            if (state == null || stateAnimFrame == null)
            {
                return -1;
            }
            try
            {
                return Convert.ToInt32(stateAnimFrame.GetValue(state));
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal object Hero(object controller)
        {
            if (controller == null || heroField == null)
            {
                return null;
            }
            try
            {
                return heroField.GetValue(controller);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal float FaceAngle(object controller)
        {
            object state = State(controller);
            if (state == null || stateFaceField == null)
            {
                return 0f;
            }
            try
            {
                return Convert.ToSingle(stateFaceField.GetValue(state));
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        internal float VerticalSpeed(object controller)
        {
            return Speed(controller, stateVy);
        }

        internal float SideSpeed(object controller)
        {
            return Speed(controller, stateVx);
        }

        internal float ForwardSpeed(object controller)
        {
            return Speed(controller, stateForwardField);
        }

        internal float LaneSpeed(object controller)
        {
            return Speed(controller, stateVz);
        }

        internal int MarioId(object controller)
        {
            if (controller == null || marioIdField == null)
            {
                return -1;
            }
            try
            {
                return (int)marioIdField.GetValue(controller);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private float Speed(object controller, FieldInfo field)
        {
            object state = State(controller);
            if (state == null || field == null)
            {
                return 0f;
            }
            try
            {
                return Convert.ToSingle(field.GetValue(state));
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        internal bool Grounded(object controller)
        {
            uint action=Action(controller),group=action&0x1C0u;
            return action!=0&&(action&AirActionFlag)==0u&&(group==0u||group==0x40u||group==0x180u);
        }

        private object State(object controller)
        {
            if (controller == null || stateField == null)
            {
                return null;
            }
            try
            {
                return stateField.GetValue(controller);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The Knight's own animation controller is switched off while Mario plays, so the state it
        // was left in never changes again. If Mario was started in mid-air, or mid-swing, or while
        // the Knight was being held still, that stale state keeps the game from offering
        // conversations, doors and benches: they all ask whether the Knight can talk or interact.
        internal void Guard(object controller, float seconds)
        {
            guardUntil = Time.unscaledTime + seconds;
            guardedHero=Hero(controller);
            guarding = true;
            GuardTick(controller);
        }
        internal bool GuardProtects(object hero)
            =>guarding&&ReferenceEquals(guardedHero,hero)&&Time.unscaledTime<guardUntil;
        internal void RelinquishGuard(object hero)
        {if(ReferenceEquals(guardedHero,hero)){guarding=false;guardedHero=null;}}
        internal void CancelGuard()
        {
            object hero=guardedHero;guarding=false;guardedHero=null;
            if(hero!=null&&!Plugin.HasHitProtection(hero))SetHeroInvulnerable(hero,false);
        }

        internal void GuardTick(object controller)
        {
            if (!guarding || invulnerableField == null || cStateField == null || heroField == null)
            {
                return;
            }
            try
            {
                object hero = guardedHero;
                object state = hero == null ? null : cStateField.GetValue(hero);
                if (state == null)
                {
                    guarding=false;guardedHero=null;
                    return;
                }
                bool over = Time.unscaledTime >= guardUntil;
                if(!over||!Plugin.HasHitProtection(hero))invulnerableField.SetValue(state, !over);
                if (over)
                {
                    guarding = false;guardedHero=null;
                }
            }
            catch (Exception)
            {
                guarding = false;guardedHero=null;
            }
        }

        internal void KeepInteractable(object controller)
        {
            if (!Armed || controller == null)
            {
                return;
            }
            try
            {
                object hero = heroField.GetValue(controller);
                if (hero == null)
                {
                    return;
                }
                object cState = CState(hero);
                if (cState == null)
                {
                    return;
                }
                bool grounded = Grounded(controller);
                SetBool(groundedField, cState, grounded);
                SetBool(attackingField, cState, false);
                SetBool(dashingField, cState, false);
                SetBool(backDashingField, cState, false);
                if (heroStateField != null)
                {
                    float forwardVelocity = 0f;
                    if (stateForwardField != null)
                    {
                        object state = State(controller);
                        if (state != null)
                        {
                            forwardVelocity = Convert.ToSingle(stateForwardField.GetValue(state));
                        }
                    }
                    int wanted = !grounded ? 3 : (Mathf.Abs(forwardVelocity) > 1f ? 2 : 1);
                    if (Convert.ToInt32(heroStateField.GetValue(hero)) != wanted)
                    {
                        heroStateField.SetValue(hero, Enum.ToObject(heroStateField.FieldType, wanted));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private Rigidbody2D heroBody;
        private Vector3 lastFeet;
        private float lastFeetTime;
        private bool hasLastFeet;
        private float guardUntil;
        private bool guarding;
        private object guardedHero;
        private bool speedShown;

        // The Knight's body is carried along instead of walking, so its speed reads as zero even while
        // Mario runs. The floors that rumble when stepped on ask the Knight's body how fast it is
        // moving, so we hand it Mario's real speed each frame.
        internal void ShareMarioSpeed(object controller)
        {
            if (controller == null)
            {
                return;
            }
            try
            {
                if (heroBody == null)
                {
                    Transform hero = HeroTransform(controller);
                    if (hero == null)
                    {
                        return;
                    }
                    heroBody = hero.GetComponent<Rigidbody2D>();
                }
                Vector3? feet = Feet(controller);
                if (heroBody == null || !feet.HasValue)
                {
                    return;
                }
                float now = Time.unscaledTime;
                if (hasLastFeet)
                {
                    float dt = now - lastFeetTime;
                    if (dt > 0.0005f && dt < 0.25f)
                    {
                        Vector3 delta = feet.Value - lastFeet;
                        Vector2 speed = new Vector2(delta.x, delta.y) / dt;
                        if (speed.magnitude > 25f)
                        {
                            speed = Vector2.zero;
                        }
                        heroBody.velocity = speed;
                        if (!speedShown && speed.magnitude > 2f)
                        {
                            speedShown = true;
                            log.LogInfo("the Knight's body now moves at " + speed.magnitude.ToString("0.0") + " while Mario runs.");
                        }
                    }
                }
                lastFeet = feet.Value;
                lastFeetTime = now;
                hasLastFeet = true;
            }
            catch (Exception)
            {
            }
        }

        internal bool CanTalk(object controller)
        {
            return Ask(controller, canTalkMethod);
        }

        internal bool CanInteract(object controller)
        {
            return Ask(controller, canInteractMethod);
        }

        private bool Ask(object controller, MethodInfo method)
        {
            if (controller == null || method == null)
            {
                return false;
            }
            try
            {
                object hero = heroField.GetValue(controller);
                return hero != null && (bool)method.Invoke(hero, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal string GateReport(object controller)
        {
            if (controller == null)
            {
                return "no controller";
            }
            try
            {
                object hero = heroField.GetValue(controller);
                object cState = CState(hero);
                if (hero == null || cState == null)
                {
                    return "no knight";
                }
                object rawState = heroStateField == null ? null : heroStateField.GetValue(hero);
                string heroState = rawState == null ? "?" : Enum.GetName(heroStateField.FieldType, rawState);
                return "onGround=" + Read(groundedField, cState)
                    + " knightState=" + heroState
                    + " attacking=" + Read(attackingField, cState)
                    + " dashing=" + Read(dashingField, cState)
                    + " recoiling=" + Read(recoilingField, cState)
                    + " held=" + Read(controlRequinlishedField, hero)
                    + " acceptingInput=" + Read(acceptingInputField, hero)
                    + " transitioning=" + Read(transitioningField, cState)
                    + " canTalk=" + CanTalk(controller)
                    + " canInteract=" + CanInteract(controller);
            }
            catch (Exception exception)
            {
                return "unreadable: " + exception.Message;
            }
        }

        private static string Read(FieldInfo field, object target)
        {
            if (field == null || target == null)
            {
                return "?";
            }
            try
            {
                object value = field.GetValue(target);
                return value == null ? "?" : value.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        internal Transform HeroTransform(object controller)
        {
            if (controller == null || heroField == null || heroTransformProperty == null)
            {
                return null;
            }
            object hero = heroField.GetValue(controller);
            return hero == null ? null : heroTransformProperty.GetValue(hero, null) as Transform;
        }

        internal Vector3? Feet(object controller)
        {
            if (controller == null || stateField == null || originField == null || feetOffsetField == null)
            {
                return null;
            }
            object state = stateField.GetValue(controller);
            if (state == null)
            {
                return null;
            }
            Vector2 origin = (Vector2)originField.GetValue(null);
            float feetOffset = (float)feetOffsetField.GetValue(controller);
            float x = origin.x + (float)statePx.GetValue(state) * WorldScale;
            float y = origin.y + (float)statePy.GetValue(state) * WorldScale;
            return new Vector3(x, y + feetOffset, 0f);
        }

        internal Vector3? Ground(object controller)
        {
            if (controller == null || stateField == null || originField == null)
            {
                return null;
            }
            object state = stateField.GetValue(controller);
            if (state == null)
            {
                return null;
            }
            Vector2 origin = (Vector2)originField.GetValue(null);
            float x = origin.x + (float)statePx.GetValue(state) * WorldScale;
            float y = origin.y + (float)statePy.GetValue(state) * WorldScale;
            return new Vector3(x, y, 0f);
        }

        internal bool BeforeTick(object controller)
        {
            if (!Armed || controller == null)
            {
                return false;
            }
            try
            {
                int id = (int)marioIdField.GetValue(controller);
                if (id < 0)
                {
                    return false;
                }
                if (id != lastId)
                {
                    lastId = id;
                    activeAt = Time.unscaledTime;
                }
                if (Time.unscaledTime - activeAt < 1.5f || Time.unscaledTime < settleUntil || Busy(controller))
                {
                    return false;
                }
                Transform transform = HeroTransform(controller);
                object state = stateField.GetValue(controller);
                if (transform == null || state == null)
                {
                    return false;
                }
                Vector2 origin = (Vector2)originField.GetValue(null);
                float feetOffset = (float)feetOffsetField.GetValue(controller);
                Vector3 knight = transform.position;
                float expectedX = origin.x + (float)statePx.GetValue(state) * WorldScale;
                float expectedY = origin.y + (float)statePy.GetValue(state) * WorldScale + feetOffset;
                float dx = knight.x - expectedX;
                float dy = knight.y - expectedY;
                if (dx * dx + dy * dy <= thresholdSqr)
                {
                    return false;
                }
                Teleport(controller, state, id, knight, feetOffset, origin);
                ResetsFollowed++;
                log.LogInfo("knight reset followed: Mario moved to (" + knight.x.ToString("0.0") + ", " + knight.y.ToString("0.0") + ")");
                return true;
            }
            catch (Exception exception)
            {
                Problem = exception.Message;
                return false;
            }
        }

        internal bool Busy(object controller)
        {
            object hero = heroField.GetValue(controller);
            if (hero == null)
            {
                return true;
            }
            if (transitionStateField != null)
            {
                object transitionState = transitionStateField.GetValue(hero);
                if (transitionState != null && Convert.ToInt32(transitionState) != 0)
                {
                    return true;
                }
            }
            object cState = cStateField == null ? null : cStateField.GetValue(hero);
            if (cState != null)
            {
                if (transitioningField != null && (bool)transitioningField.GetValue(cState))
                {
                    return true;
                }
                if (deadField != null && (bool)deadField.GetValue(cState))
                {
                    return true;
                }
            }
            return false;
        }

        private void Teleport(object controller, object state, int id, Vector3 knight, float feetOffset, Vector2 origin)
        {
            float targetX = knight.x;
            float targetY = knight.y - feetOffset;

            statePx.SetValue(state, (targetX - origin.x) / WorldScale);
            statePy.SetValue(state, (targetY - origin.y) / WorldScale);
            statePz.SetValue(state, 0f);
            stateField.SetValue(controller, state);

            recenterMethod.Invoke(controller, null);

            object stateNow = stateField.GetValue(controller);
            if (stateNow != null)
            {
                statePx.SetValue(stateNow, 0f);
                statePy.SetValue(stateNow, 0f);
                statePz.SetValue(stateNow, 0f);
                stateField.SetValue(controller, stateNow);
            }
            object prev = prevStateField.GetValue(controller);
            if (prev != null)
            {
                prevPx.SetValue(prev, 0f);
                prevPy.SetValue(prev, 0f);
                if (prevPz != null)
                {
                    prevPz.SetValue(prev, 0f);
                }
                prevStateField.SetValue(controller, prev);
            }

            setPositionMethod.Invoke(null, new object[] { id, 0f, 0f, 0f });
            // Position/XYZ velocity were already reset here. Also leave the old knockback
            // action and its independent forward speed behind, at the same existing reset.
            const uint idleAction=0x0C400201u;
            MapFallFlourish.Forget();
            setActionMethod.Invoke(null,new object[]{id,idleAction});
            if(setForwardMethod!=null)setForwardMethod.Invoke(null,new object[]{id,0f});
            else
            {
                if(nativeForward==null)
                {
                    IntPtr entry=GetProcAddress(GetModuleHandle("sm64.dll"),"sm64_set_mario_forward_velocity");
                    if(entry==IntPtr.Zero)throw new MissingMethodException("Native reset forward velocity");
                    nativeForward=(Forward)Marshal.GetDelegateForFunctionPointer(entry,typeof(Forward));
                }
                nativeForward(id,0f);
            }
            setVelocityMethod.Invoke(null, new object[] { id, 0f, 0f, 0f });
            object neutral=stateField.GetValue(controller);
            stateActionField.SetValue(neutral,idleAction);stateForwardField?.SetValue(neutral,0f);
            stateVx?.SetValue(neutral,0f);stateVy?.SetValue(neutral,0f);stateVz?.SetValue(neutral,0f);
            stateField.SetValue(controller,neutral);prevStateField.SetValue(controller,neutral);
            hasLastFeet=false;
            Rigidbody2D body=HeroTransform(controller)?.GetComponent<Rigidbody2D>();
            if(body!=null)body.velocity=Vector2.zero;

            if (markPositionField != null)
            {
                markPositionField.SetValue(controller, new Vector3(targetX, targetY, knight.z));
            }
        }
    }
}
