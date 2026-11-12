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
        // A move's damage circle is anchored to Mario, but the thing it lands on only has to touch
        // that circle. A big or oddly placed collider (a room-wide shape, or an enemy whose hurtbox
        // sits far from its body) can therefore take a hit from well away. A hit is only allowed when
        // the target actually has body within the move's own reach of Mario.
        // Every move lands the same way: its own reach is a box around Mario's body (his body plus how
        // far that move swings), and a hit only counts when that box touches the enemy's own hurtbox,
        // the part a nail and the pogo would hit. Awareness zones are triggers on other layers and are
        // never looked at.
        private struct EnemyStrike
        {
            internal Component Target;
            internal int Health;
            internal int ObjectId, ReceiverId;
            internal object Controller;
            internal string Name;
            internal bool Bounce;
            internal bool Blocked;
            internal uint Action;
            internal string Move;
            internal NativeDamageTimer.DiveHit Dive;
        }

        private static bool StrikePrefix(object target, float dir, ref object row, out EnemyStrike __state)
        {
            __state = default(EnemyStrike);
            if (Instance == null)
            {
                return true;
            }
            try
            {
                Component component = target as Component;
                if (component == null || Instance.knightFollow == null)
                {
                    return true;
                }
                object controller = Instance.knightFollow.Controller();
                uint action = Instance.knightFollow.Action(controller);
                int animation = Instance.knightFollow.Animation(controller);
                if (!NailCollisionSignals.AllowMoveDamage(action, animation, component.gameObject.layer)) return false;
                // This is the original Strike's value-type row argument, not the shared
                // attack table. Pass the startup multiplier into its normal hit builder.
                if (row != null && rowDamageField != null && NailCollisionSignals.IsPoundStartup(action, animation))
                    rowDamageField.SetValue(row, 1f);
                Transform knight = Instance.knightFollow.HeroTransform(controller);
                Collider2D body = knight == null ? null : knight.GetComponent<Collider2D>();
                if (body == null)
                {
                    return true;
                }
                string id = rowIdField == null || row == null ? "" : Convert.ToString(rowIdField.GetValue(row));
                float pad;
                float forward;
                float up;
                if (!MoveShape(id, out pad, out forward, out up))
                {
                    return true;
                }
                if (!SwingAllowed(id, controller))
                {
                    return false;
                }
                float multiplier = NailCharmReach.Multiplier(Instance.knightFollow.Hero(controller));
                Bounds box = NailCharmReach.Scale(body.bounds, body.bounds.center, multiplier);
                Bounds reach = SceneryContact.Reach(body.bounds, dir, pad, forward, up, multiplier);
                Vector3 motion = AttackMotion(id, controller);
                bool enemy = IsEnemy(component);
                if (!TouchesHurtbox(component, reach, enemy ? motion : Vector3.zero, action, animation))
                {
                    if (strikeLogs < StrikeLogLimit)
                    {
                        strikeLogs++;
                        Instance.Log.LogInfo("move " + id + " didn't reach " + component.name);
                    }
                    return false;
                }
                if (enemy ? IsGone(component) || hitThisSwing.Contains(component.GetInstanceID()) || !CanLand(component, LandingGap)
                    : !LandsOnce(component))
                {
                    return false;
                }
                if (enemy)
                    __state = new EnemyStrike { Target=component, Health=(int)hpField.GetValue(component), Controller=controller,
                        Name=component.name, ObjectId=component.gameObject.GetInstanceID(), ReceiverId=component.GetInstanceID(), Action=action, Move=id,
                        Blocked=CapAccess.Flag(component,"IsInvincible") };
                // Remember top contact before Hit can remove a dying enemy's colliders.
                // Blocked nail hits still allow the existing pogo on armoured enemies.
                if (id == "ground_pound" && IsEnemy(component)
                    && !NailCollisionSignals.IsPoundStartup(action, animation)
                    && Instance.knightFollow.VerticalSpeed(controller) < -4f)
                {
                    Vector3? feet = Instance.knightFollow.Ground(controller);
                    if (feet.HasValue)
                    {
                        Collider2D[] hurtboxes = component.GetComponentsInChildren<Collider2D>(true);
                        for (int i = 0; i < hurtboxes.Length; i++)
                        {
                            Collider2D shape = hurtboxes[i];
                            if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy
                                || shape.gameObject.layer != EnemiesLayer) continue;
                            if (StompContact.PoundStrikeTouchesTop(box, stompTickStart ?? feet.Value, feet.Value,
                                shape.bounds, reach.min.y, reach.max.y))
                            {
                                __state.Bounce = true;
                                break;
                            }
                        }
                    }
                }
                if (strikeLogs < StrikeLogLimit)
                {
                    strikeLogs++;
                    Instance.Log.LogInfo("move " + id + " hit " + component.name);
                }
                if ((id == "dive" || id == "dive_slide") && IsEnemy(component))
                    __state.Dive = NativeDamageTimer.BeginDiveHit(component, Instance.knightFollow.Hero(controller), hpField, action);
            }
            catch (Exception)
            {
            }
            return true;
        }

        private static void StrikePostfix(EnemyStrike __state)
        {
            if (ReferenceEquals(__state.Target, null)) return;
            long epoch=GameplayEpoch(__state.Controller);
            // A native blocked hit also emits HIT/particles. Do not replay it on
            // every simulation tick just because an armoured boss lost no HP.
            if(__state.Blocked)hitThisSwing.Add(__state.ReceiverId);
            if (EnemyLostHealth(__state.Target, __state.Health))
            {
                RecordLanding(__state.ObjectId);
                hitThisSwing.Add(__state.ReceiverId);
                QuickSlash.Confirmed(__state.Controller, __state.Action, __state.Move);
                NotifyEnemyNailHit(__state.Target);
            }
            if(!GameplayTickCurrent(__state.Controller,epoch))return;
            if (__state.Bounce) Pogo(__state.Controller, Instance.knightFollow, __state.Name);
            NativeDamageTimer.FinishDiveHit(__state.Dive);
        }

        private static void NotifyEnemyNailHit(Component target)
        {
            if(target==null||sendFsmEvent==null)return;
            // Use the game's normal nail reaction after damage. Reactions may
            // withdraw a hurtbox or start a cutscene synchronously.
            bool outer=deliveringTopHit;
            try
            {
                deliveringTopHit=true;
                sendFsmEvent.Invoke(null,new object[]{target.gameObject,"TAKE DAMAGE",false});
            }
            catch(Exception error){Instance?.Log.LogWarning("Enemy nail reaction: "+error.GetBaseException().Message);}
            finally{deliveringTopHit=outer;}
        }

        internal static void QuickSlashNeutral(object controller)
        {
            // This runs only on the owned native neutral transition, before
            // attack queries for the next completed simulation output. Do not
            // clear per-enemy contact/evasion cooldowns or hit sets mid-query.
            swingAction = lastSwingAction = uint.MaxValue;
            MetalCap.ResetDirectAttack(); // A genuinely new attack can pay its normal hit cost; hazard grace is separate.
            try
            {
                object attacks = marioAttacksField?.GetValue(controller);
                if (attacks == null) return;
                if (quickSlashBaseLastAction == null || !quickSlashBaseLastAction.DeclaringType.IsInstanceOfType(attacks))
                    quickSlashBaseLastAction = AccessTools.Field(attacks.GetType(), "lastAction");
                quickSlashBaseLastAction?.SetValue(attacks, uint.MaxValue);
            }
            catch (Exception error) { Instance?.Log.LogWarning("Quick Slash's new attack boundary: " + error.GetBaseException().Message); }
        }

        private static void BindIndependentAttackGeometry(Type attacksType)
        {
            FieldInfo query=AccessTools.Field(attacksType,"CandidateBounds");
            if(query==null)throw new MissingFieldException("Shared attack geometry binding");
            query.SetValue(null,Delegate.CreateDelegate(query.FieldType,AccessTools.Method(typeof(Plugin),nameof(IndependentAttackBounds))));
        }
        private static Bounds IndependentAttackBounds(object controller,object hero,string move,float direction)
        {
            Collider2D body=Instance.knightFollow.HeroTransform(controller)?.GetComponent<Collider2D>();
            if(body==null||!MoveShape(move,out float pad,out float forward,out float up))
                throw new InvalidOperationException("Shared attack body unavailable: "+move);
            Bounds reach=SceneryContact.Reach(body.bounds,direction,pad,forward,up,NailCharmReach.Multiplier(hero));
            return SceneryContact.SweepBounds(reach,AttackMotion(move,controller));
        }

        private static Vector3 AttackMotion(string move, object controller)
        {
            if (move != "dive" && move != "dive_slide") return Vector3.zero;
            Vector3? end = Instance.knightFollow.Ground(controller);
            return end.HasValue && stompTickStart.HasValue ? end.Value-stompTickStart.Value : Vector3.zero;
        }

        private static bool EnemyLostHealth(Component target, int before)
        {
            if(CapSystem.Vanished)return false;
            if (hpField != null && Convert.ToInt32(hpField.GetValue(target)) < before) return true;
            if (rejectedEnemyHits++ < 8)
                Instance?.Log.LogInfo("Enemy rejected Mario's hit: " + target.name + "; health=" + before
                    + ", invincible=" + Read(target, "IsInvincible") + ", evasion=" + Read(target, "evasionByHitRemaining") + ".");
            return false;
        }

        private static bool SwingAllowed(string id, object controller)
        {
            float window = HitWindow(id);
            uint action = Instance.knightFollow.Action(controller);
            int anim = Instance.knightFollow.Animation(controller);
            int frame = Instance.knightFollow.AnimFrame(controller);
            float now = Time.unscaledTime;
            if (action != swingAction || anim != swingAnim || frame < swingFrame)
            {
                swingAction = action;
                swingAnim = anim;
                swingStart = now;
                hitThisSwing.Clear();
            }
            swingFrame = frame;
            if (window < 0f)
            {
                return true;
            }
            return now - swingStart <= window;
        }

        private static float HitWindow(string id)
        {
            switch (id)
            {
                case "punch":
                case "move_punch":
                    return 0.3f;
                case "jump_kick":
                case "slide_kick":
                case "slide_kick_slide":
                    return 0.4f;
                case "dive":
                case "dive_slide":
                case "ground_pound":
                    // Travelling attacks can reach a new enemy throughout their motion.
                    // Existing per-target tracking still prevents continuous repeat damage.
                    return -1f;
                case "ground_pound_land":
                    return 0.35f;
                default:
                    return 0f;
            }
        }

        private static bool MoveShape(string id, out float pad, out float forward, out float up)
        {
            pad = 0f;
            forward = 0f;
            up = 0f;
            switch (id)
            {
                case "punch":
                case "move_punch":
                    pad = 0.35f;
                    forward = 0.55f;
                    up = 0.05f;
                    return true;
                case "jump_kick":
                case "slide_kick":
                case "slide_kick_slide":
                    pad = 0.35f;
                    forward = 0.7f;
                    up = -0.05f;
                    return true;
                case "dive":
                case "dive_slide":
                    pad = 0.6f;
                    forward = 0.35f;
                    return true;
                case "ground_pound":
                    pad = 0.6f;
                    up = -0.1f;
                    return true;
                case "ground_pound_land":
                    pad = 1.7f;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TouchesHurtbox(Component target, Bounds reach, Vector3 motion, uint action, int animation)
            =>TryHurtbox(target,reach,motion,action,animation,out _);

        internal static float PoundHitDirection(string move,object target)
        {
            var component=target as Component;
            KnightResetFollow follow=Instance?.knightFollow;
            object controller=follow?.Controller();
            Collider2D body=controller==null?null:follow.HeroTransform(controller)?.GetComponent<Collider2D>();
            if(component==null||body==null||!MoveShape(move,out float pad,out float forward,out float up))return NailHitDirection.Down;
            Bounds reach=SceneryContact.Reach(body.bounds,1f,pad,forward,up,NailCharmReach.Multiplier(follow.Hero(controller)));
            return TryHurtbox(component,reach,Vector3.zero,follow.Action(controller),follow.Animation(controller),out Bounds contact)
                ?NailHitDirection.ForContact(NailHitDirection.Down,move,reach,contact):NailHitDirection.Down;
        }

        private static bool TryHurtbox(Component target,Bounds reach,Vector3 motion,uint action,int animation,out Bounds contact)
        {
            contact=default(Bounds);
            bool enemy = IsEnemy(target);
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                int layer = collider.gameObject.layer;
                if (!NailCollisionSignals.AllowMoveDamage(action, animation, layer)) continue;
                if (enemy ? layer == EnemiesLayer || (!collider.isTrigger && layer != 9 && layer != 20)
                    : SceneryContact.IsResponderShape(collider, target.gameObject))
                {
                    if (SceneryContact.Sweeps(reach, motion, collider.bounds))
                    {
                        contact=collider.bounds;
                        return true;
                    }
                }
            }
            return false;
        }
        // The original only offers a hit once its own small circle finds something, and that circle
        // sits where the move's sprite is, not where Mario is. The dive's circle is up and in front of
        // him, so an enemy beside or under him never reached it. This walks the same move box against
        // the enemies in it and lands through the original's own Strike, so damage and its counters
        // stay the same.
        private static void ObserveAttackCharms(object controller)
        {
            var rows=sheetAttacksField?.GetValue(null) as Array;
            object row=rows==null||Instance?.knightFollow==null?null:FindRow(rows,Instance.knightFollow.Action(controller));
            AttackCharmEffects.Observe(controller,row==null?null:Convert.ToString(rowIdField?.GetValue(row)));
        }
        private static void TickEnemyHits(object controller)
        {
            if (Instance == null || Instance.knightFollow == null || sheetAttacksField == null)
            {
                return;
            }
            if (marioAttacksField == null || strikeMethod == null)
            {
                Type controllerType = controller.GetType();
                marioAttacksField = controllerType.GetField("Attacks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type attacksType = marioAttacksField == null ? null : marioAttacksField.FieldType;
                strikeMethod = attacksType == null ? null : attacksType.GetMethod("Strike", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (marioAttacksField == null || strikeMethod == null)
                {
                    return;
                }
            }
            Array rows = sheetAttacksField.GetValue(null) as Array;
            if (rows == null || rowIdField == null)
            {
                return;
            }
            uint action = Instance.knightFollow.Action(controller);
            object row = FindRow(rows, action);
            if (row == null)
            {
                return;
            }
            string id = Convert.ToString(rowIdField.GetValue(row));
            float pad;
            float forward;
            float up;
            if (!MoveShape(id, out pad, out forward, out up))
            {
                return;
            }
            if (!SwingAllowed(id, controller))
            {
                return;
            }
            Transform knight = Instance.knightFollow.HeroTransform(controller);
            Collider2D body = knight == null ? null : knight.GetComponent<Collider2D>();
            object hero = Instance.knightFollow.Hero(controller);
            object attacks = marioAttacksField.GetValue(controller);
            if (body == null || hero == null || attacks == null)
            {
                return;
            }
            float dir = Mathf.Sin(Instance.knightFollow.FaceAngle(controller)) >= 0f ? 1f : -1f;
            Bounds reach = SceneryContact.Reach(body.bounds, dir, pad, forward, up, NailCharmReach.Multiplier(hero));
            Vector3 motion = AttackMotion(id, controller);
            Bounds search = reach;
            if(NailCollisionSignals.AllowMoveDamage(action,Instance.knightFollow.Animation(controller),EnemiesLayer))
                HitboxOverlay.Attack(reach,motion);
            Bounds startReach = reach;
            startReach.center -= motion;
            search.Encapsulate(startReach);
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(1 << EnemiesLayer);
            int count = Physics2D.OverlapBox(search.center, search.size, 0f, filter, nearby);
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = nearby[i];
                nearby[i] = null;
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (knight != null && collider.transform.IsChildOf(knight))
                {
                    continue;
                }
                if (!SceneryContact.Sweeps(reach, motion, collider.bounds))
                {
                    continue;
                }
                MonoBehaviour[] candidates = collider.GetComponentsInParent<MonoBehaviour>(true);
                for (int c = 0; c < candidates.Length; c++)
                {
                    Component responder = candidates[c];
                    if (!IsEnemy(responder) || IsGone(responder))
                    {
                        continue;
                    }
                    if (hitThisSwing.Contains(responder.GetInstanceID()))
                    {
                        break;
                    }
                    string targetName = responder.name;
                    try
                    {
                        strikeMethod.Invoke(attacks, new object[] { responder, hero, dir, row });
                    }
                    catch (Exception)
                    {
                    }
                    if (hitThisSwing.Contains(responder.GetInstanceID()) && strikeLogs < StrikeLogLimit)
                    {
                        strikeLogs++;
                        Instance.Log.LogInfo("move " + id + " landed on " + targetName + " by body");
                    }
                    // StrikePostfix commits successful damage and the remembered top bounce.
                    break;
                }
            }
        }

        // A move is one hit. Mario's punch keeps its hitbox out for several frames of the same swing,
        // and the original lets every one of those frames land again, so a single punch used to count
        // two or three times on the same enemy.
        private static bool LandsOnce(Component target)
        {
            return LandsOnce(target, LandingGap);
        }

        private static bool LandsOnce(Component target, float gap)
        {
            if (!CanLand(target, gap)) return false;
            RecordLanding(target);
            return true;
        }

        private static bool CanLand(Component target, float gap)
        {
            int id = target.gameObject.GetInstanceID();
            float now = Time.unscaledTime;
            return !lastLanding.TryGetValue(id, out float when) || now-when >= gap;
        }

        private static void RecordLanding(Component target)
        {
            RecordLanding(target.gameObject.GetInstanceID());
        }

        private static void RecordLanding(int objectId)
        {
            if (lastLanding.Count > 256)
            {
                lastLanding.Clear();
            }
            lastLanding[objectId] = Time.unscaledTime;
        }

        private static string Describe(float distance)
        {
            return distance >= float.MaxValue * 0.5f ? "nowhere" : distance.ToString("0.0");
        }

        private static float ReachOf(object row)
        {
            if (row == null || attackRadiusField == null)
            {
                return 1f;
            }
            try
            {
                return Convert.ToSingle(attackRadiusField.GetValue(row)) * 0.0105f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }
        // The real body of the target: its own colliders, ignoring triggers (detection zones) and
        // anything switched off. If it has none, its anchor point stands in for the body.
        private static float NearestSolid(Component target, Vector3 point)
        {
            Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
            float best = float.MaxValue;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                {
                    continue;
                }
                float candidate = BoxDistance(collider.bounds, point);
                if (candidate < best)
                {
                    best = candidate;
                }
            }
            if (best >= float.MaxValue * 0.5f)
            {
                best = Vector2.Distance(target.transform.position, point);
            }
            return Mathf.Max(best, 0f);
        }
        private static float BoxDistance(Bounds bounds, Vector3 point)
        {
            float dx = Mathf.Max(Mathf.Max(bounds.min.x - point.x, 0f), point.x - bounds.max.x);
            float dy = Mathf.Max(Mathf.Max(bounds.min.y - point.y, 0f), point.y - bounds.max.y);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static void ResolveSmacks()
        {
            try
            {
                Type sheetsType = AccessTools.TypeByName("MarioHollow.Sheets");
                sheetAttacksField = sheetsType == null ? null : sheetsType.GetField("Attacks", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Type rowType = sheetAttacksField == null ? null : sheetAttacksField.FieldType.GetElementType();
                if (rowType != null)
                {
                    rowIdField = rowType.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rowActionField = rowType.GetField("Action", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rowRadiusField = rowType.GetField("Radius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rowForwardField = rowType.GetField("Forward", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rowUpField = rowType.GetField("Up", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    rowDamageField = rowType.GetField("Damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    TuneDamage();
                }
                fsmType = AccessTools.TypeByName("PlayMakerFSM");
                responderType = AccessTools.TypeByName("IHitResponder");
                Type utility = AccessTools.TypeByName("FSMUtility");
                sendFsmEvent = utility == null ? null : AccessTools.Method(utility, "SendEventToGameObject", new[] { typeof(GameObject), typeof(string), typeof(bool) });
                if (sheetAttacksField == null || fsmType == null || responderType == null || sendFsmEvent == null || rowActionField == null)
                {
                    Instance.Log.LogWarning("The game's own damage event wasn't found; breakable things in the world stay unresponsive to Mario's moves.");
                }
            }
            catch (Exception exception)
            {
                Instance.Log.LogWarning("The game's own damage event wasn't set up: " + exception.Message);
            }
        }

        // How hard each move hits, as a multiple of the knight's nail damage. A punch or a kick is
        // the usual way to fight, so it is the nail exactly; the big committed moves hit twice.
        private static void TuneDamage()
        {
            try
            {
                if (sheetAttacksField == null || rowIdField == null || rowDamageField == null)
                {
                    return;
                }
                Array rows = sheetAttacksField.GetValue(null) as Array;
                if (rows == null)
                {
                    return;
                }
                for (int i = 0; i < rows.Length; i++)
                {
                    object row = rows.GetValue(i);
                    if (row == null)
                    {
                        continue;
                    }
                    string id = Convert.ToString(rowIdField.GetValue(row));
                    float damage;
                    if (id == "punch" || id == "move_punch" || id == "jump_kick" || id == "slide_kick" || id == "slide_kick_slide")
                    {
                        damage = 1f;
                    }
                    else if (id == "dive" || id == "dive_slide" || id == "ground_pound" || id == "ground_pound_land")
                    {
                        damage = 2f;
                    }
                    else
                    {
                        continue;
                    }
                    rowDamageField.SetValue(row, damage);
                    rows.SetValue(row, i);
                }
                Instance.Log.LogInfo("Mario's moves: punches, kicks and pound startup 1x nail damage; dive, dive slide, falling pound and pound landing 2x.");
            }
            catch (Exception exception)
            {
                Instance.Log.LogWarning("move damage stays as the original sets it: " + exception.Message);
            }
        }

        private static object FindRow(Array rows, uint action)
        {
            KnightResetFollow follow=Instance?.knightFollow;
            object controller=follow?.Controller();
            action=MovementUnlocks.AttackAction(action,ShadeLongJump.Active,controller==null?null:follow.Hero(controller));
            if (rows == null)
            {
                return null;
            }
            for (int i = 0; i < rows.Length; i++)
            {
                object row = rows.GetValue(i);
                if (row == null)
                {
                    continue;
                }
                if (Convert.ToUInt32(rowActionField.GetValue(row)) == action)
                {
                    return row;
                }
            }
            return null;
        }

        private static int MoveDamage(object row)
        {
            try
            {
                object player = playerDataInstance == null ? null : playerDataInstance.GetValue(null, null);
                int nail = player == null || nailDamageField == null ? 5 : Convert.ToInt32(nailDamageField.GetValue(player));
                float multiplier = rowDamageField == null ? 1f : Convert.ToSingle(rowDamageField.GetValue(row));
                KnightResetFollow follow = Instance?.knightFollow;
                object controller = follow?.Controller();
                if (controller != null)
                    multiplier = NailCollisionSignals.DamageMultiplier(multiplier, follow.Action(controller), follow.Animation(controller));
                object hero=controller==null?null:follow.Hero(controller);
                int damage=Mathf.Max(1,Mathf.RoundToInt(NailCharmModifiers.BaseDamage(Mathf.Max(1,nail),hero)*multiplier));
                return Mathf.Max(1,Mathf.RoundToInt(damage*NailCharmModifiers.DamageMultiplier(1f,hero)));
            }
            catch (Exception)
            {
                return 5;
            }
        }

    }
}
