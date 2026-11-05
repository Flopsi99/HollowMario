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

        // Resolve the earliest top contact before ordinary attack delivery can kill its target.
        // Normal falls stomp; ground pounds use the existing stronger hit and pogo bounce.
        private static void TickStomps(object controller)
        {
            if (Instance == null || Instance.knightFollow == null || !ArmedNailHit())
            {
                return;
            }
            KnightResetFollow follow = Instance.knightFollow;
            if(CapAccess.Flag(controller,"DamagePending") || (follow.Action(controller)&0x00020000u)!=0u
                || NailCollisionSignals.IsPoundStartup(follow.Action(controller),follow.Animation(controller)))return;
            float fall = follow.VerticalSpeed(controller);
            Vector3? feet = follow.Ground(controller);
            Transform knight = follow.HeroTransform(controller);
            Collider2D body = knight == null ? null : knight.GetComponent<Collider2D>();
            if (feet == null || body == null)
            {
                return;
            }
            Bounds bodyBounds = NailCharmReach.Scale(body.bounds, body.bounds.center,
                NailCharmReach.Multiplier(follow.Hero(controller)));
            Vector3 start = stompTickStart ?? feet.Value;
            bool enemyFall = fall <= -4f;
            // A mushroom's small solid support may already have stopped native velocity.
            // Keep the swept approach for that target; ordinary enemy gating is unchanged.
            if (!enemyFall && feet.Value.y >= start.y - 0.0001f) return;
            uint action = follow.Action(controller);
            bool pounding = ActionIs(action, "ground_pound") || ActionIs(action, "ground_pound_land");
            Vector3 next=enemyFall?AnticipatedStompFeet(controller,follow,feet.Value):feet.Value;
            Bounds search = StompContact.SearchStompBounds(bodyBounds, start, feet.Value,next);
            if(enemyFall)HitboxOverlay.Stomp(bodyBounds,feet.Value,next);
            Bounds poundReach = bodyBounds;
            if (pounding && MoveShape(ActionIs(action,"ground_pound_land")?"ground_pound_land":"ground_pound",out float pad,out float forward,out float up))
            {
                poundReach = SceneryContact.Reach(body.bounds,1f,pad,forward,up,NailCharmReach.Multiplier(follow.Hero(controller)));
                search.Encapsulate(poundReach);
            }
            ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask((1 << EnemiesLayer) | (1 << 19) | (1 << 16));
            int count = Physics2D.OverlapBox(search.center, search.size, 0f, filter, nearby);
            Component chosen = null;
            bool chosenMushroom = false;
            float firstContact = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = nearby[i];
                if (collider == null || collider.transform.IsChildOf(knight))
                {
                    continue;
                }
                bool mushroom = MushroomBounce.TryTarget(collider,out Component spring);
                if (!mushroom && (!enemyFall || collider.gameObject.layer != EnemiesLayer)) continue;
                float contact;
                bool contactFound=mushroom
                    ? StompContact.TryContact(bodyBounds,start,feet.Value,collider.bounds,out contact)
                    : StompContact.TryStomp(bodyBounds,start,feet.Value,next,collider.bounds,out contact);
                if (!contactFound && mushroom && pounding && StompContact.PoundStrikeTouchesTop(bodyBounds,start,feet.Value,
                    collider.bounds,poundReach.min.y,poundReach.max.y)) {contactFound=true;contact=1f;}
                if (!contactFound || contact >= firstContact) continue;
                if (mushroom)
                {
                    chosen=spring;chosenMushroom=true;firstContact=contact;continue;
                }
                MonoBehaviour[] candidates = collider.GetComponentsInParent<MonoBehaviour>(true);
                for (int r = 0; r < candidates.Length; r++)
                {
                    Component responder = candidates[r];
                    Behaviour behaviour = responder as Behaviour;
                    if (!IsEnemy(responder) || (behaviour != null && !behaviour.isActiveAndEnabled) || IsGone(responder))
                    {
                        continue;
                    }
                    chosen = responder;
                    chosenMushroom = false;
                    firstContact = contact;
                    break;
                }
            }
            if (chosen == null || !CanLand(chosen, StompGap)) return;
            if (chosenMushroom)
            {
                RecordLanding(chosen);
                PlaceStompContact(controller,follow,start,feet.Value,firstContact);
                float speed=MushroomBounce.UpSpeed(follow.Hero(controller),pounding?PogoBounce:StompBounce);
                if (!pounding) speed *= .65f;
                if (Pogo(controller,follow,chosen.name,speed,MushroomBounce.SpringAction,pounding?.5f:1f))
                {
                    MushroomFallGuard.Grant(follow.MarioId(controller));
                    try { MushroomBounce.React(chosen); }
                    catch(Exception error) { Instance.Log.LogWarning("Mushroom bounce effects: "+error.GetBaseException().Message); }
                }
                return;
            }
            int stompDamage = (int)NailCollisionSignals.DamageMultiplier(pounding ? 2f : 1f, action, follow.Animation(controller));
            string targetName = chosen.name;
            if (!HitEnemyFromAbove(chosen, knight.gameObject, stompDamage, !pounding)) return;
            PlaceStompContact(controller, follow, start, feet.Value, firstContact);
            if (pounding) Pogo(controller, follow, targetName);
            if (stompLogs++ < 20)
                Instance.Log.LogInfo("Mario landed on " + targetName + " (swept top contact) and bounces off.");
            if (!pounding)
            {
                Bounce(controller, follow);
            }
        }

        private static bool deliveringTopHit;
        private static bool HitEnemyFromAbove(Component enemy, GameObject source, int multiplier, bool protectStomp = false)
        {
            if (!ArmedNailHit() || !IsEnemy(enemy) || IsGone(enemy)) return false;
            object hit = HitFromAbove(source, multiplier);
            if (hit == null) return false;
            int before = (int)hpField.GetValue(enemy);
            int objectId = enemy.gameObject.GetInstanceID();
            // An enemy can explode synchronously inside Hit. Protection must already
            // be active on that confirmed top contact, not added after the explosion.
            NativeDamageTimer.StompWindow protection=protectStomp
                ? NativeDamageTimer.BeginStomp(source.GetComponent(AccessTools.TypeByName("HeroController")))
                : default(NativeDamageTimer.StompWindow);
            bool outer=deliveringTopHit;
            try { deliveringTopHit=true; responderType.GetMethod("Hit").Invoke(enemy, new[] { hit }); }
            catch (Exception error)
            {
                NativeDamageTimer.CancelStomp(protection);
                if (stompLogs++ < 20) Instance.Log.LogWarning("enemy top hit: " + error.GetBaseException().Message);
                return false;
            }
            finally { deliveringTopHit=outer; }
            EnemyLostHealth(enemy, before); // Diagnose native rejections; retain ordinary armour pogos.
            RecordLanding(objectId);
            return true;
        }

        private static bool StompsBeforeAttacks(object __0)
        {
            long epoch=GameplayEpoch(__0);
            if(!GameplayTickCurrent(__0,epoch))return false;
            try
            {
                ObserveAttackCharms(__0);
                if(!GameplayTickCurrent(__0,epoch))return false;
                // Offer each actual attack tick to native prop listeners before a stomp
                // or hazard bounce can replace the move. Catch-up ticks must not be lost.
                DeliverSceneryHits(__0);
                if(!GameplayTickCurrent(__0,epoch))return false;
                stompChecked = true;
                TickStomps(__0);
            }
            catch (Exception exception)
            {
                stompChecked = false;
                if (stompLogs++ < 20) Instance?.Log.LogWarning("enemy top contact: " + exception.Message);
            }
            return GameplayTickCurrent(__0,epoch);
        }

        private static void PlaceStompContact(object controller, KnightResetFollow follow, Vector3 start, Vector3 end, float fraction)
        {
            Vector3 contact = Vector3.Lerp(start, end, Mathf.Clamp01(fraction));
            Vector3 step = contact-end;
            AutoMove.Prepare(controller);
            AutoMove.Shift(controller, step.x, step.y);
            Transform hero = follow.HeroTransform(controller);
            if (hero == null) return;
            hero.position += step;
            Rigidbody2D body = hero.GetComponent<Rigidbody2D>();
            if (body != null) body.position = new Vector2(hero.position.x, hero.position.y);
        }

        private static readonly RaycastHit2D[] stompTerrain=new RaycastHit2D[16];
        private static int stompContactTraces;
        private static Vector3 AnticipatedStompFeet(object controller,KnightResetFollow follow,Vector3 feet)
        {
            Vector3 next=StompContact.AnticipationEnd(feet,new Vector3(follow.SideSpeed(controller),follow.VerticalSpeed(controller),0f));
            Vector2 step=next-feet;
            if(step.sqrMagnitude<.000001f)return feet;
            // Anticipation must stop at room geometry, not hit enemies through a floor.
            var filter=new ContactFilter2D{useTriggers=false};
            filter.SetLayerMask(LayerMask.GetMask("Terrain","Soft Terrain"));
            int count=Physics2D.Raycast(feet,step.normalized,filter,stompTerrain,step.magnitude);
            if(count==stompTerrain.Length)return feet;
            float distance=step.magnitude;
            for(int n=0;n<count;n++)distance=Math.Min(distance,stompTerrain[n].distance);
            return feet+(Vector3)(step.normalized*distance);
        }

        private static bool ContactBounceOffEnemy(GameObject source)
        {
            if (deliveringTopHit || source == null || lastController == null || Instance == null || Instance.knightFollow == null)
            {
                return false;
            }
            if (!BelongsToEnemy(source))
            {
                return false;
            }
            KnightResetFollow follow = Instance.knightFollow;
            uint move = follow.Action(lastController);
            if(NailCollisionSignals.IsPoundStartup(move,follow.Animation(lastController)))return false;
            bool pounding = move == ActGroundPound || move == ActGroundPoundLand
                || ActionIs(move, "ground_pound") || ActionIs(move, "ground_pound_land")
                || Environment.TickCount - lastPoundAt < 400;
            if (CapAccess.Flag(lastController,"DamagePending") || (move&0x00020000u)!=0u || follow.VerticalSpeed(lastController) > (pounding ? -2f : -4f))
            {
                return false;
            }
            Vector3? feet = follow.Ground(lastController);
            if (feet == null)
            {
                return false;
            }
            Transform hero = follow.HeroTransform(lastController);
            Collider2D body = hero == null ? null : hero.GetComponent<Collider2D>();
            Component enemy = FindParent(source.transform, healthManagerType);
            if (body == null || enemy == null || IsGone(enemy) || !CanLand(enemy, StompGap)) return false;
            Bounds attackBody = NailCharmReach.Scale(body.bounds, body.bounds.center,
                NailCharmReach.Multiplier(follow.Hero(lastController)));
            // Only actual enemy bodies qualify. Explosion/awareness triggers must
            // never turn a nearby blast into a free stomp.
            float contact=float.PositiveInfinity;
            Vector3 next=AnticipatedStompFeet(lastController,follow,feet.Value);
            foreach(Collider2D hit in source.GetComponentsInChildren<Collider2D>())
            {
                if(!hit.enabled||!hit.gameObject.activeInHierarchy||hit.gameObject.layer!=EnemiesLayer)continue;
                bool found=StompContact.TryStomp(attackBody,stompTickStart??feet.Value,feet.Value,next,hit.bounds,out float at);
                if(found)contact=Math.Min(contact,at);
                if(DebugSettings.VerboseLogging&&stompContactTraces++<24)
                    Instance.Log.LogInfo("Stomp contact arbitration: accepted="+found+" enemy="+enemy.name
                        +" feet="+feet.Value+" previous="+(stompTickStart??feet.Value)+" predicted="+next
                        +" foot region="+StompContact.AnticipationFoot(attackBody,feet.Value)+" target="+hit.bounds
                        +" native fall="+follow.VerticalSpeed(lastController));
            }
            if(float.IsPositiveInfinity(contact))return false;
            string targetName = enemy.name;
            int damage = (int)NailCollisionSignals.DamageMultiplier(pounding ? 2f : 1f, move, follow.Animation(lastController));
            // Physics can report this contact before the attack tick. Send the same native
            // downward hit here; bouncing first changes Mario's action and loses that hit.
            if (!HitEnemyFromAbove(enemy, hero.gameObject, damage, !pounding)) return false;
            PlaceStompContact(lastController, follow, stompTickStart ?? feet.Value, feet.Value, contact);
            if(pounding) Pogo(lastController, follow, targetName);
            else Bounce(lastController, follow);
            return true;
        }

        private static bool SpikePogo()
        {
            if (lastController == null || Instance == null || Instance.knightFollow == null)
            {
                return false;
            }
            KnightResetFollow follow = Instance.knightFollow;
            uint move = follow.Action(lastController);
            if(NailCollisionSignals.IsPoundStartup(move,follow.Animation(lastController)))return false;
            // The pound may already have become its landing (or just ended) by the time the spikes
            // report their hit, so a pound that happened a moment ago counts too.
            bool pounding = move == ActGroundPound || move == ActGroundPoundLand
                || ActionIs(move, "ground_pound") || ActionIs(move, "ground_pound_land")
                || Environment.TickCount - lastPoundAt < 400;
            // Only a pound that is still coming down can land on spikes; once he is rising again
            // the bounce has happened, and the touch underneath is just the spikes he left.
            if (!pounding || follow.VerticalSpeed(lastController) > -2f)
            {
                return false;
            }
            Pogo(lastController, follow, "the spikes");
            return true;
        }

        private static void TickPogos(object controller)
        {
            if (Instance == null || Instance.knightFollow == null || nativeMarioVelocity == null || nativeMarioAction == null)
            {
                return;
            }
            KnightResetFollow follow = Instance.knightFollow;
            uint move = follow.Action(controller);
            if (move != ActGroundPound && !ActionIs(move, "ground_pound"))
            {
                return;
            }
            // SM64 sets vy=-50 during its suspended windup too. It is not a fall:
            // do not scan/pogo or mark a recent descending pound until the spin ends.
            if(NailCollisionSignals.IsPoundStartup(move,follow.Animation(controller)))return;
            lastPoundAt = Environment.TickCount;
            if (pogoSightings < 1)
            {
                pogoSightings++;
                Instance.Log.LogInfo("Mario is ground pounding; looking for spikes under him.");
            }
            if (follow.VerticalSpeed(controller) > -4f)
            {
                return;
            }
            Vector3? feet = follow.Ground(controller);
            Transform knight = follow.HeroTransform(controller);
            Collider2D body = knight == null ? null : knight.GetComponent<Collider2D>();
            if (feet == null || body == null)
            {
                return;
            }
            Bounds bodyBounds = body.bounds;
            float multiplier = NailCharmReach.Multiplier(follow.Hero(controller));
            bodyBounds = NailCharmReach.Scale(bodyBounds, bodyBounds.center, multiplier);
            Vector2 middle = new Vector2(feet.Value.x, feet.Value.y + 0.8f * multiplier);
            Vector2 size = new Vector2(Mathf.Max(body.bounds.size.x, 0.6f) * multiplier, 1.9f * multiplier);
            bool couldHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            int count = Physics2D.OverlapBoxNonAlloc(middle, size, 0f, nearby, Physics2D.AllLayers);
            Physics2D.queriesHitTriggers = couldHitTriggers;
            int tried = 0;
            for (int i = 0; i < count; i++)
            {
                Collider2D collider = nearby[i];
                if (collider == null || collider.transform.IsChildOf(knight))
                {
                    continue;
                }
                bool pogoable = Pogoable(collider);
                if (pogoable)
                {
                    tried++;
                }
                if (!pogoable)
                {
                    continue;
                }
                Bounds bounds = collider.bounds;
                if (feet.Value.y > bounds.max.y + 0.7f * multiplier || feet.Value.y < bounds.min.y - 0.9f * multiplier)
                {
                    continue;
                }
                float overlap = Mathf.Min(bodyBounds.max.x, bounds.max.x) - Mathf.Max(bodyBounds.min.x, bounds.min.x);
                if (overlap < 0.02f && Mathf.Abs(bounds.center.x - feet.Value.x) > 0.6f * multiplier)
                {
                    continue;
                }
                Pogo(controller, follow, collider.name);
                return;
            }
            if (pogoProbes < 6)
            {
                pogoProbes++;
                string names = "";
                for (int i = 0; i < count && i < 6; i++)
                {
                    Collider2D one = nearby[i];
                    names += (one == null ? "?" : one.name + "(L" + one.gameObject.layer + (one.isTrigger ? ",T" : "") + ")") + " ";
                }
                Instance.Log.LogInfo("ground pound over " + count + " things (" + (couldHitTriggers ? "triggers seen" : "triggers hidden") + "), "
                    + tried + " pogoable: " + names);
            }
        }

        private static bool Pogoable(Collider2D collider)
        {
            // Mushrooms now use the shared swept stomp/pound path and their native response.
            // The generic hazard scan must not turn a support box or a side sensor into one.
            if (MushroomBounce.BelongsTo(collider)) return false;
            if (healthManagerType == null)
            {
                healthManagerType = AccessTools.TypeByName("HealthManager");
                if (healthManagerType != null)
                {
                    hpField = healthManagerType.GetField("hp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }
            // A creature is never something to pogo from: its hurtbox, its contact damage and its
            // awareness zone all hang off the same enemy, so the pound falls through to the stomp
            // (or to nothing) instead of springing off a vision cone.
            if (healthManagerType != null && FindParent(collider, healthManagerType) != null)
            {
                return false;
            }
            int layer = collider.gameObject.layer;
            if (layer == EnemiesLayer || layer == 13 || layer == 14 || layer == 15)
            {
                return false;
            }
            if (damageHeroType == null)
            {
                damageHeroType = AccessTools.TypeByName("DamageHero");
                hazardTypeField = damageHeroType?.GetField("hazardType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                nonBouncerType = AccessTools.TypeByName("NonBouncer");
                bigBouncerType = AccessTools.TypeByName("BigBouncer");
            }
            Component hazard = damageHeroType == null ? null : FindParent(collider, damageHeroType);
            if (hazard != null)
            {
                int type = 1;
                try
                {
                    type = hazardTypeField == null ? 1 : Convert.ToInt32(hazardTypeField.GetValue(hazard));
                }
                catch (Exception)
                {
                }
                // 1 is spikes; acid and pits are no place to bounce from.
                if (type != 1)
                {
                    return false;
                }
                return nonBouncerType == null || FindParent(collider, nonBouncerType) == null;
            }
            if (bigBouncerType != null && FindParent(collider, bigBouncerType) != null)
            {
                return true;
            }
            // Some hazards are run by a "damages_hero" brain instead of a DamageHero part.
            try
            {
                if (fsmContains == null)
                {
                    Type fsmUtility = AccessTools.TypeByName("FSMUtility");
                    fsmContains = fsmUtility == null ? null : AccessTools.Method(fsmUtility, "ContainsFSM", new[] { typeof(GameObject), typeof(string) });
                    fsmLocate = fsmUtility == null ? null : AccessTools.Method(fsmUtility, "LocateFSM", new[] { typeof(GameObject), typeof(string) });
                    fsmGetInt = fsmUtility == null ? null : AccessTools.Method(fsmUtility, "GetInt", new[] { AccessTools.TypeByName("PlayMakerFSM"), typeof(string) });
                }
                if (fsmContains == null || fsmLocate == null || fsmGetInt == null)
                {
                    return false;
                }
                if (!(bool)fsmContains.Invoke(null, new object[] { collider.gameObject, "damages_hero" }))
                {
                    return false;
                }
                object hurt = fsmLocate.Invoke(null, new object[] { collider.gameObject, "damages_hero" });
                return hurt != null && Convert.ToInt32(fsmGetInt.Invoke(null, new object[] { hurt, "hazardType" })) == 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Pogo(object controller, KnightResetFollow follow, string what,
            float upward = PogoBounce, uint action = ActDoubleJump, float sideways = .5f)
        {
            // One bounce per moment, however many things report a touch in the same frame.
            if (Environment.TickCount - lastPogoAt < 150)
            {
                return false;
            }
            int id = follow.MarioId(controller);
            if (id < 0)
            {
                return false;
            }
            lastPogoAt = Environment.TickCount;
            // A bounce is never a hurt: the base mod's own reaction to the hit is skipped for the
            // moment after it, so the touches that come with the bounce stay quiet.
            pogoedAt = Time.unscaledTime;
            try
            {
                PogoLaunch.Apply(nativeMarioAction, nativeMarioVelocity, id, action,
                    follow.SideSpeed(controller) * sideways, upward, follow.LaneSpeed(controller) * sideways);
                DoubleJump.Reset(controller);
                PoundLongJump.Reset(controller);
            }
            catch (Exception exception)
            {
                if (pogoLogs < 5)
                {
                    pogoLogs++;
                    Instance.Log.LogWarning("the pogo couldn't be passed on: " + exception.Message);
                }
                return false;
            }
            follow.Guard(controller, StompGuard);
            if (pogoLogs < 12)
            {
                pogoLogs++;
                Instance.Log.LogInfo("Mario pogoed off " + what + "; upward speed=" + upward.ToString("0.0") + ".");
            }
            return true;
        }

        private static Component FindParent(Component start, Type type)
        {
            Transform transform = start == null ? null : start.transform;
            for (int depth = 0; transform != null && depth < 8; depth++)
            {
                Component found = transform.GetComponent(type);
                if (found != null)
                {
                    return found;
                }
                transform = transform.parent;
            }
            return null;
        }

        private static bool IsEnemy(Component responder)
        {
            if (responder == null)
            {
                return false;
            }
            if (healthManagerType == null)
            {
                healthManagerType = AccessTools.TypeByName("HealthManager");
                if (healthManagerType != null)
                {
                    hpField = healthManagerType.GetField("hp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }
            return healthManagerType != null && healthManagerType.IsInstanceOfType(responder);
        }

        private static bool IsGone(Component responder)
        {
            try
            {
                if (responder == null) return true;
                if (enemyDeadField == null)
                    enemyDeadField = healthManagerType?.GetField("isDead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                // HealthManager.Hit checks isDead, not hp. Scripted deaths deliberately
                // leave isDead false; refusing their zero-health bodies can strand them.
                bool dead = enemyDeadField != null && (bool)enemyDeadField.GetValue(responder);
                if (!dead && hpField != null && (int)hpField.GetValue(responder) <= 0)
                    TraceEnemyContact(responder, true);
                return dead;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private static void Bounce(object controller, KnightResetFollow follow)
        {
            if (nativeMarioVelocity == null)
            {
                return;
            }
            int id = follow.MarioId(controller);
            if (id < 0)
            {
                return;
            }
            try
            {
                nativeMarioVelocity.Invoke(null, new object[] { id, follow.SideSpeed(controller), StompBounce, follow.LaneSpeed(controller) });
                DoubleJump.Reset(controller);
                PoundLongJump.Reset(controller);
            }
            catch (Exception)
            {
            }
        }

        private static bool ActionIs(uint action, string id)
        {
            try
            {
                Array rows = sheetAttacksField == null ? null : sheetAttacksField.GetValue(null) as Array;
                if (rows == null || rowIdField == null || rowActionField == null)
                {
                    return false;
                }
                for (int i = 0; i < rows.Length; i++)
                {
                    object row = rows.GetValue(i);
                    if (row != null && Convert.ToString(rowIdField.GetValue(row)) == id)
                    {
                        return Convert.ToUInt32(rowActionField.GetValue(row)) == action;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool ArmedNailHit()
        {
            if (hitInstanceType != null)
            {
                return true;
            }
            try
            {
                hitInstanceType = AccessTools.TypeByName("HitInstance");
                if (hitInstanceType == null)
                {
                    return false;
                }
                hitSourceField = hitInstanceType.GetField("Source", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitAttackField = hitInstanceType.GetField("AttackType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitDamageField = hitInstanceType.GetField("DamageDealt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitDirectionField = hitInstanceType.GetField("Direction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitMultiplierField = hitInstanceType.GetField("Multiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitMagnitudeField = hitInstanceType.GetField("MagnitudeMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                hitIgnoreField = hitInstanceType.GetField("IgnoreInvulnerable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Type playerDataType = AccessTools.TypeByName("PlayerData");
                playerDataInstance = playerDataType == null ? null : playerDataType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                nailDamageField = playerDataType == null ? null : playerDataType.GetField("nailDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (hitDamageField == null || nailDamageField == null || playerDataInstance == null)
                {
                    Instance.Log.LogWarning("landing on enemies stays off: the game's damage numbers weren't found");
                    hitInstanceType = null;
                    return false;
                }
                Instance.Log.LogInfo("Mario can now land on enemies.");
                return true;
            }
            catch (Exception exception)
            {
                Instance.Log.LogWarning("landing on enemies stays off: " + exception.Message);
                hitInstanceType = null;
                return false;
            }
        }

        private static object HitFromAbove(GameObject source, int multiplier = 1)
        {
            try
            {
                object playerData = playerDataInstance.GetValue(null, null);
                int damage = playerData == null ? 0 : (int)nailDamageField.GetValue(playerData);
                if (damage <= 0)
                {
                    damage = 5;
                }
                KnightResetFollow follow=Instance?.knightFollow;
                object controller=follow?.Controller(),hero=controller==null?null:follow.Hero(controller);
                damage=NailCharmModifiers.BaseDamage(damage,hero);
                object hit = Activator.CreateInstance(hitInstanceType);
                hitSourceField.SetValue(hit, source);
                hitAttackField.SetValue(hit, Enum.ToObject(hitAttackField.FieldType, 0));
                hitDamageField.SetValue(hit, damage * multiplier);
                hitDirectionField.SetValue(hit, 270f);
                hitMultiplierField.SetValue(hit, NailCharmModifiers.DamageMultiplier(1f,hero));
                hitMagnitudeField.SetValue(hit, NailCharmModifiers.Knockback(1f,hero));
                if (hitIgnoreField != null)
                {
                    hitIgnoreField.SetValue(hit, false);
                }
                return hit;
            }
            catch (Exception exception)
            {
                if (stompLogs < 20)
                {
                    stompLogs++;
                    Instance.Log.LogWarning("a stomp couldn't be put together: " + exception.Message);
                }
                return null;
            }
        }

    }
}
