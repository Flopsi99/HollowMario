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
        // Some of the world's scripts do not take an event at face value. They inspect the attacking
        // object's "damages_enemy" FSM first, just as a normal NailSlash does. Mario therefore keeps
        // an inert copy of the Knight's own nail damager, with its live direction and damage filled
        // in for the move being delivered.
        private static GameObject StrikeSource(int damage, float direction)
        {
            try
            {
                if (!strikeSourceTried)
                {
                    strikeSourceTried = true;
                    if (fsmType == null)
                    {
                        fsmType = AccessTools.TypeByName("PlayMakerFSM");
                    }
                    GameObject source = FindNailDamager();
                    if (source == null)
                    {
                        Instance.Log.LogWarning("the Knight's nail damager wasn't found; Mario can still use ordinary hit receivers.");
                        return null;
                    }
                    strikeSource = (GameObject)UnityEngine.Object.Instantiate(source);
                    strikeSource.name = "Mario's nail strike";
                    UnityEngine.Object.DontDestroyOnLoad(strikeSource);
                    strikeSource.SetActive(false);
                    Collider2D[] shapes = strikeSource.GetComponentsInChildren<Collider2D>(true);
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        if (shapes[i] != null)
                        {
                            shapes[i].enabled = false;
                        }
                    }
                    MonoBehaviour[] parts = strikeSource.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] != null)
                        {
                            parts[i].enabled = false;
                        }
                    }
                    Instance.Log.LogInfo("Mario's scenery strikes now use the Knight's own nail damager.");
                }
                if(strikeSource==null)return null;
                GameObject selected=strikeSource;
                if(direction==NailHitDirection.Up)
                {
                    // Queued native receivers must not see their upward source
                    // changed to downward by another contact in this same tick.
                    if(upwardStrikeSource==null)
                    {
                        upwardStrikeSource=(GameObject)UnityEngine.Object.Instantiate(strikeSource);
                        upwardStrikeSource.name="Mario's upward nail strike";
                        UnityEngine.Object.DontDestroyOnLoad(upwardStrikeSource);
                        upwardStrikeSource.SetActive(false);
                    }
                    selected=upwardStrikeSource;
                }
                ConfigureStrikeSource(selected,damage,direction);
                return selected;
            }
            catch (Exception exception)
            {
                Instance.Log.LogWarning("a nail-like strike couldn't be prepared: " + exception.Message);
                strikeSource = null;
                NailDamagerLookup.Clear();
            }
            return strikeSource;
        }

        private static GameObject FindNailDamager()
        {
            if (fsmType == null)
            {
                return null;
            }
            Type nailSlash = AccessTools.TypeByName("NailSlash");
            UnityEngine.Object[] scripts = Resources.FindObjectsOfTypeAll(fsmType);
            GameObject fallback = null;
            for (int i = 0; i < scripts.Length; i++)
            {
                Component script = scripts[i] as Component;
                if (script == null || FsmText(Read(script, "FsmName")) != "damages_enemy")
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = script.gameObject;
                }
                if (nailSlash != null && (script.GetComponent(nailSlash) != null || script.GetComponentInParent(nailSlash) != null))
                {
                    return script.gameObject;
                }
            }
            return fallback;
        }

        private static void ConfigureStrikeSource(GameObject source,int damage, float direction)
        {
            if (source == null || fsmType == null)
            {
                return;
            }
            Component[] scripts = source.GetComponentsInChildren(fsmType, true);
            for (int i = 0; i < scripts.Length; i++)
            {
                Component script = scripts[i];
                if (script == null || FsmText(Read(script, "FsmName")) != "damages_enemy")
                {
                    continue;
                }
                SetScriptInt(script, "attackType", 0);
                SetScriptInt(script, "damageDealt", damage);
                SetScriptFloat(script, "direction", direction);
                // MoveDamage already includes Fury. Never inherit a second multiplier
                // from whichever native slash was cloned to provide prop metadata.
                SetScriptFloat(script,"Multiplier",1f);
                KnightResetFollow follow=Instance?.knightFollow;
                object controller=follow?.Controller();
                SetScriptFloat(script,"magnitudeMult",NailCharmModifiers.Knockback(1f,controller==null?null:follow.Hero(controller)));
                NailDamagerLookup.Register(source, script);
            }
        }

        // A real nail damages through two ordinary game routes: DamageEnemies sends TAKE DAMAGE to
        // the collider it touched, while IHitResponder receives the HitInstance the base mod already
        // constructs. A ReceivedDamage action can additionally choose its own event (for example a
        // crystal shot chooses HIT). Tagged nail triggers use their active action too (grub jars).
        // Follow the game's receivers rather than naming individual objects or inventing events.
        private static void DeliverSceneryHits(object controller)
        {
            if (sheetAttacksField == null || sendFsmEvent == null || responderType == null || fsmType == null)
            {
                return;
            }
            if (Instance == null || Instance.knightFollow == null)
            {
                return;
            }
            KnightResetFollow follow = Instance.knightFollow;
            try
            {
                uint action = follow.Action(controller);
                int anim = follow.Animation(controller);
                if (action != lastSwingAction || anim != lastSwingAnim)
                {
                    lastSwingAction = action;
                    lastSwingAnim = anim;
                    struckThisSwing.Clear();
                    nativeStruckThisSwing.Clear();
                    taggedStruckThisSwing.Clear();
                }
                Vector3? feet = follow.Ground(controller);
                if (feet == null)
                {
                    return;
                }
                Array rows = sheetAttacksField.GetValue(null) as Array;
                object row = FindRow(rows, action);
                if (row == null)
                {
                    return;
                }
                string moveId = rowIdField == null ? "" : Convert.ToString(rowIdField.GetValue(row));
                float pad, forward, up;
                if (!MoveShape(moveId, out pad, out forward, out up)) return;
                Transform knight = follow.HeroTransform(controller);
                Collider2D body = knight == null ? null : knight.GetComponent<Collider2D>();
                if (body == null) return;
                float direction = Mathf.Sin(follow.FaceAngle(controller)) >= 0f ? 1f : -1f;
                int damage = MoveDamage(row);
                float hitAngle = NailHitDirection.ForMove(direction > 0f ? 0f : 180f, moveId);
                Bounds reach = SceneryContact.Reach(body.bounds, direction, pad, forward, up,
                    NailCharmReach.Multiplier(follow.Hero(controller)));
                Vector2 centre = reach.center;
                Vector3 motion = stompTickStart.HasValue ? feet.Value - stompTickStart.Value : Vector3.zero;
                Bounds query = SceneryContact.SweepBounds(reach, motion);
                int count = Physics2D.OverlapBoxNonAlloc(query.center, query.size, 0f, nearby);
                ProjectileTrace.Frame trace=ProjectileFrame(controller,moveId,reach,motion,true,count,hitAngle);
                ProjectileTrace.Query(trace,nearby.Length);
                if (swingLogs < 60)
                {
                    string rowId = rowIdField == null ? "?" : Convert.ToString(rowIdField.GetValue(row));
                    int seenSwings;
                    swingSeen.TryGetValue(rowId, out seenSwings);
                    if (seenSwings < 3)
                    {
                        swingSeen[rowId] = seenSwings + 1;
                        swingLogs++;
                        string found = "";
                        for (int k = 0; k < count; k++)
                        {
                            found += " " + (nearby[k] == null ? "?" : nearby[k].name);
                        }
                        Instance.Log.LogInfo("move " + rowId + " scenery reach at (" + centre.x.ToString("0.0") + ", " + centre.y.ToString("0.0") + ") touched " + count + ":" + found);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    Collider2D collider = nearby[i];
                    if (!SceneryContact.IsHitShape(collider))
                    {
                        ProjectileTrace.Scan(trace,collider,"query shape rejected","disabled/inactive/Hero Detector");
                        continue;
                    }
                    GameObject target = collider.gameObject;
                    if (knight != null && target.transform.IsChildOf(knight))
                    {
                        continue;
                    }
                    GameObject projectile = ProjectileRoot(collider,out string classification);
                    ProjectileTrace.Scan(trace,collider,"query found",classification);
                    // Sweep only projectile contacts. Scenery retains its exact previous reach,
                    // so a moving attack cannot extend a switch's or NPC object's hit range.
                    if (projectile != null)
                    {
                        string blocked=CapSystem.Vanished?"Vanish active":NailCollisionSignals.IsPoundStartup(action,anim)?"pound startup"
                            :!SwingAllowed(moveId,controller)?"attack window expired":struckThisSwing.Contains(projectile.GetInstanceID())?"already accepted this swing"
                            :!SceneryContact.Sweeps(reach,motion,collider.bounds)?"outside swept attack":null;
                        if(blocked!=null){ProjectileTrace.Scan(trace,collider,"projectile gate rejected",blocked);continue;}
                    }
                    // Physics2D ignores Z; a prop's draw-order offset is not gameplay distance.
                    else if (!SceneryContact.Sweeps(reach, Vector3.zero, collider.bounds))
                    {ProjectileTrace.Scan(trace,collider,"nonprojectile gate rejected",classification+"; outside endpoint attack");continue;}
                    if (!NailCollisionSignals.AllowMoveDamage(action, anim, target.layer))
                    {ProjectileTrace.Scan(trace,collider,"damage gate rejected","move/layer exclusion");continue;}
                    if(CapSystem.Vanished&&(target.layer==11||target.layer==12||target.layer==22))
                    {ProjectileTrace.Scan(trace,collider,"damage gate rejected","Vanish enemy/projectile exclusion");continue;}
                    DeliverSceneryResponderHits(controller, collider, row, moveId, direction);
                    if (!NailCollisionSignals.AllowMoveContact(action, anim, collider.gameObject.layer))
                    {ProjectileTrace.Scan(trace,collider,"contact gate rejected","move/layer exclusion");continue;}
                    if (!SceneryContact.IsHitShape(collider))
                    {ProjectileTrace.Result(trace,collider,target,"response changed target","inactive/destroyed after native Hit");continue;}
                    int acceptedNailContacts = nativeStruckThisSwing.Count + taggedStruckThisSwing.Count;
                    float contactAngle=NailHitDirection.ForContact(hitAngle,moveId,reach,collider.bounds);
                    trace.Angle=contactAngle;
                    GameObject striker = StrikeSource(damage, contactAngle);
                    if (striker != null)
                    {
                        striker.transform.position = new Vector3(centre.x, centre.y, knight.position.z);
                        ProjectileTrace.NailOffer(trace,collider,target,striker);
                        SendNativeNailContacts(collider, striker.GetComponent<Collider2D>());
                        SendReceivedDamageSignals(collider, striker, true);
                    }
                    if (projectile != null)
                    {
                        bool received = acceptedNailContacts != nativeStruckThisSwing.Count + taggedStruckThisSwing.Count;
                        string route=received?"nail trigger/C# callback":null;
                        if (!received && striker != null && SceneryContact.IsHitShape(collider))
                        {
                            received = SendReceivedDamageSignals(collider, striker);
                            if(received)route="ReceivedDamage callback";
                        }
                        // Only shots without a native nail receiver need their own player-impact
                        // route. Mark accepted contacts once; unready listeners can retry later.
                        if (!received && SceneryContact.IsHitShape(collider))
                        {
                            received = SendProjectileImpact(collider, projectile, knight, follow.Hero(controller),trace);
                            if(received)route="player-impact callback";
                        }
                        ProjectileTrace.Result(trace,collider,target,received?"native response accepted":"no native response accepted",
                            received?route:striker==null?"nail source unavailable; impact filters did not accept":"nail/ReceivedDamage/impact filters did not accept");
                        if (received)
                        {
                            struckThisSwing.Add(projectile.GetInstanceID());
                            continue;
                        }
                    }
                    if (!SceneryContact.IsHitShape(collider))
                    {ProjectileTrace.Result(trace,collider,target,"response changed target","inactive/destroyed after nail/impact callbacks");continue;}
                    Component host = SceneryHost(collider.transform);
                    if (host != null && !SceneryContact.CanReceive(collider, host.gameObject)) continue;
                    // Enemy damage has its own Strike route. A prop-style TAKE DAMAGE
                    // here can make a Grimmkin cancel its dash and disable its collider
                    // before that route even queries it. Notify enemies after Strike
                    // actually removes health instead; keep prop/projectile handling above.
                    if (healthManagerType != null && FindParent(collider.transform, healthManagerType) != null) continue;
                    int key = host == null ? target.GetInstanceID() : host.gameObject.GetInstanceID();
                    if (!struckThisSwing.Add(key))
                    {
                        continue;
                    }
                    SendNailSignals(target, collider, striker);
                    if (smackLogs < 60)
                    {
                        smackLogs++;
                        Instance.Log.LogInfo("move delivered a nail strike to " + (host == null ? target.name : host.gameObject.name)
                            + " (" + (rowIdField == null ? "move" : Convert.ToString(rowIdField.GetValue(row))) + ")");
                    }
                }
            }
            catch (Exception exception)
            {
                if (smackLogs < 60)
                {
                    smackLogs++;
                    Instance.Log.LogWarning("a move couldn't be passed on: " + exception.Message);
                }
            }
        }

        private static GameObject ProjectileRoot(Collider2D contact,out string reason)
        {
            int layer = contact.gameObject.layer;
            if(layer!=12&&layer!=22&&layer!=19){reason="unrecognized projectile layer";return null;}
            if(BelongsToEnemy(contact.gameObject)){reason="owned by HealthManager/enemy";return null;}
            Rigidbody2D moving = contact.attachedRigidbody;
            // Interactive props share layer19 with crystal shots. Only their moving,
            // damaging phase qualifies; a grown crystal remains an ordinary breakable.
            if(layer==19)
            {
                if(moving==null){reason="layer19 without Rigidbody2D";return null;}
                if(moving.velocity.sqrMagnitude<.01f){reason="layer19 stationary";return null;}
                if(!Pogoable(contact)){reason="layer19 rejected as non-pogoable";return null;}
            }
            if(layer==22&&moving==null){reason="layer22 without Rigidbody2D";return null;}
            reason="recognized projectile";
            return moving == null ? contact.gameObject : moving.gameObject;
        }

        private static bool SendProjectileImpact(Collider2D contact, GameObject projectile, Transform knight, object hero,ProjectileTrace.Frame trace)
        {
            GameObject contactOwner=contact.gameObject;
            Collider2D[] bodies = knight.GetComponentsInChildren<Collider2D>(true);
            GameObject previousRoot = projectileImpactRoot;
            object previousHero = projectileImpactHero;
            projectileImpactRoot = projectile;
            projectileImpactHero = hero;
            try
            {
                if(NativeProjectileImpact.TryImpact(projectile,knight))
                {
                    ProjectileTrace.Result(trace,contact,contactOwner,"native C# bullet impact accepted","EnemyBullet shield-impact handler");
                    return true;
                }
                for (Transform current = contact.transform; current != null; current = current.parent)
                {
                    Component[] scripts = current.GetComponents(fsmType);
                    for (int i = 0; i < scripts.Length; i++)
                    {
                        Component script = scripts[i];
                        if (script is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
                        if (FsmText(Read(script, "FsmName")) == "damages_hero") continue; // Damage delivery is not a projectile impact.
                        object state = Read(Read(script, "Fsm"), "ActiveState");
                        Array actions = Read(state, "Actions") as Array;
                        if (actions == null) continue;
                        foreach (object action in actions)
                        foreach (Collider2D body in bodies)
                        {
                            // Offer only the real player's explicitly filtered contact. Never
                            // synthesize an unfiltered trigger, a guessed END event or a Destroy.
                            if (body == null || (body.gameObject.layer != 9 && body.gameObject.layer != 20)) continue;
                            bool wasRecorded=taggedStruckThisSwing.Contains(action);
                            bool delivered=NailTriggerSignals.DeliverOnce(action,body,taggedStruckThisSwing);
                            ProjectileTrace.ImpactResult(trace,contact,contactOwner,action,body,delivered,wasRecorded);
                            if(!delivered)continue;
                            if (projectileImpactLogs++ < 8)
                                Instance.Log.LogInfo("Mario's attack reached a projectile's native impact: " + projectile.name + ".");
                            return true;
                        }
                    }
                    if (current == projectile.transform) break;
                }
                return false;
            }
            finally
            {
                projectileImpactRoot = previousRoot;
                projectileImpactHero = previousHero;
            }
        }

        private static bool IsProjectileImpactDamage(object hero, GameObject source)
        {
            // Suppress only a synchronous self-hit caused by the impact callback above.
            // This scope cannot grant a timer or protect against another enemy/projectile.
            return projectileImpactRoot != null && source != null && ReferenceEquals(hero, projectileImpactHero)
                && source.transform.IsChildOf(projectileImpactRoot.transform);
        }

        // Solid/trigger breakables implement IHitResponder instead of listening to FSM events.
        // Offer them the same existing move-box contacts, then use the original Strike builder
        // for every HitInstance property, damage and accounting. Enemy delivery stays separate.
        private static void DeliverSceneryResponderHits(object controller, Collider2D contact, object row, string moveId, float direction)
        {
            if (strikeMethod == null || marioAttacksField == null || !SwingAllowed(moveId, controller)) return;
            object attacks = marioAttacksField.GetValue(controller);
            object hero = Instance.knightFollow.Hero(controller);
            if (attacks == null || hero == null) return;
            MonoBehaviour[] candidates = contact.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                MonoBehaviour receiver = candidates[i];
                if (receiver == null || !receiver.isActiveAndEnabled || !responderType.IsInstanceOfType(receiver)
                    || IsEnemy(receiver) || !SceneryContact.IsResponderShape(contact, receiver.gameObject)
                    || !hitThisSwing.Add(receiver.GetInstanceID())) continue;
                try { strikeMethod.Invoke(attacks, new object[] { receiver, hero, direction, row }); }
                catch (Exception exception)
                {
                    if (smackLogs++ < 60)
                        Instance.Log.LogWarning("A scenery nail hit could not be passed on: " + exception.Message);
                }
            }
        }
        // The object that owns the scenery's script: the game event is only understood by a script
        // sitting on the same object, so a collider on a child part is followed up to its script.
        private static Component SceneryHost(Transform from)
        {
            for (Transform current = from; current != null; current = current.parent)
            {
                Component script = current.GetComponent(fsmType);
                if (script != null && script.gameObject.activeInHierarchy)
                {
                    return script;
                }
            }
            return null;
        }

        private static void SendNailSignals(GameObject target, Collider2D contact, GameObject striker)
        {
            try
            {
                // This is the direct event sent by Hollow Knight's DamageEnemies component.
                sendFsmEvent.Invoke(null, new object[] { target, "TAKE DAMAGE", false });
            }
            catch (Exception)
            {
            }
            if (striker != null)
            {
                SendReceivedDamageSignals(contact, striker);
            }
        }

        private static void SendNativeNailContacts(Collider2D contact, Collider2D nail)
        {
            if (nail == null) return;
            for (Transform current = contact.transform; current != null; current = current.parent)
            {
                if (!SceneryContact.CanReceive(contact, current.gameObject)) continue;
                MonoBehaviour[] scripts = current.GetComponents<MonoBehaviour>();
                for (int i = 0; i < scripts.Length; i++)
                {
                    try { NailCollisionSignals.Deliver(scripts[i], nail, nativeStruckThisSwing); }
                    catch (Exception exception)
                    {
                        if (nativeNailFailures++ < 4)
                            Instance.Log.LogWarning("A native nail contact could not be passed on: " + exception.GetBaseException().Message);
                    }
                }
            }
        }

        private static bool SendReceivedDamageSignals(Collider2D contact, GameObject striker, bool triggerOnly = false)
        {
            bool delivered = false;
            Collider2D nail = striker.GetComponent<Collider2D>();
            for (Transform current = contact.transform; current != null; current = current.parent)
            {
                if (!SceneryContact.CanReceive(contact, current.gameObject)) continue;
                Component[] scripts;
                try
                {
                    scripts = current.GetComponents(fsmType);
                }
                catch (Exception)
                {
                    continue;
                }
                for (int i = 0; i < scripts.Length; i++)
                {
                    Component script = scripts[i];
                    object fsm = Read(script, "Fsm");
                    object state = Read(fsm, "ActiveState");
                    Array actions = Read(state, "Actions") as Array;
                    if (actions == null)
                    {
                        continue;
                    }
                    for (int a = 0; a < actions.Length; a++)
                    {
                        object action = actions.GetValue(a);
                        if (!NailTriggerSignals.Enabled(action))
                        {
                            continue;
                        }
                        if (triggerOnly)
                        {
                            if (NailTriggerSignals.DeliverOnce(action, nail, taggedStruckThisSwing))
                            {
                                delivered = true;
                                if (nailTriggerLogs++ < 12)
                                    Instance.Log.LogInfo("Mario's nail contact reached " + script.gameObject.name + " in "
                                        + Convert.ToString(Read(state, "Name")) + ".");
                            }
                            continue;
                        }
                        string actionName = action.GetType().Name;
                        if (actionName != "ReceivedDamage" && actionName != "ReceivedDamageStay")
                        {
                            continue;
                        }
                        if (FsmText(Read(action, "fsmName")) != "damages_enemy")
                        {
                            continue;
                        }
                        string eventName = FsmText(Read(action, "sendEvent"));
                        if (string.IsNullOrEmpty(eventName)||!NailTriggerSignals.ClaimDamageEvent(action,taggedStruckThisSwing))
                        {
                            continue;
                        }
                        SetFsmValue(Read(action, "storeGameObject"), striker);
                        sendFsmEvent.Invoke(null, new object[] { script.gameObject, eventName, false });
                        delivered = true;
                    }
                }
            }
            return delivered;
        }

        private static void SetScriptFloat(Component host, string name, float value)
        {
            try
            {
                object bag = Read(Read(host, "Fsm"), "Variables");
                if (Read(bag, "FloatVariables") is not Array values)
                {
                    return;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    object variable = values.GetValue(i);
                    if (variable == null || Convert.ToString(Read(variable, "Name")) != name)
                    {
                        continue;
                    }
                    PropertyInfo stored = variable.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (stored != null && stored.CanWrite)
                    {
                        stored.SetValue(variable, value);
                    }
                    return;
                }
            }
            catch (Exception)
            {
            }
        }

        private static void SetScriptInt(Component host, string name, int value)
        {
            try
            {
                object bag = Read(Read(host, "Fsm"), "Variables");
                if (Read(bag, "IntVariables") is not Array values)
                {
                    return;
                }
                for (int i = 0; i < values.Length; i++)
                {
                    object variable = values.GetValue(i);
                    if (variable == null || Convert.ToString(Read(variable, "Name")) != name)
                    {
                        continue;
                    }
                    PropertyInfo number = variable.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (number != null && number.CanWrite)
                    {
                        number.SetValue(variable, value);
                    }
                    return;
                }
            }
            catch (Exception)
            {
            }
        }

    }
}
