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
        private static readonly HitProtectionLease hitProtection=new HitProtectionLease();
        private static object protectedHero;
        internal static bool HasHitProtection(object hero)=>ReferenceEquals(protectedHero,hero)&&hitProtection.Active;
        private static int BeginHitProtection(KnightResetFollow follow,object hero,float duration)
        {
            if(!ReferenceEquals(protectedHero,hero))hitProtection.Cancel();
            protectedHero=hero;
            // Preserve the native duration, plus a bounded pause for Mario's stun
            // and a scheduling margin. A paused game advances none of this clock.
            return hitProtection.Begin(Math.Max(0f,duration)+Math.Max(0f,follow.FreezeDown(hero))
                +HitProtectionLease.StunPauseSeconds+1f,()=>
                {
                    follow.Pulse(hero,false);
                    follow.SetHeroInvulnerable(hero,follow.GuardProtects(hero));
                    follow.SetHeroRecoiling(hero,false);
                    protectedHero=null;
                });
        }
        private static void TickHitProtection(float delta)
        {
            if(hitProtection.Tick(delta))TraceInvincibility("recovered an overdue protection routine; owned invulnerability and recoil cleared.");
        }
        // The knight's invincibility after a hit is a countdown. While Mario is knocked down and getting
        // up, the game engine itself says he cannot be hurt, so the knight's countdown is paused for
        // through normal recovery instead of running out under him. A stale action flag
        // cannot pause that countdown indefinitely.
        // Nothing is granted when he was not hit: bonking a wall or falling into acid is untouched.
        private static bool InvulnerablePrefix(object __instance, float duration, ref IEnumerator __result)
        {
            KnightResetFollow follow = Instance == null ? null : Instance.knightFollow;
            if (follow == null || !follow.InvincibilityArmed)
            {
                return true;
            }
            object controller = follow.Controller();
            if(EndsHazardProtectionWithRespawn(CapAccess.Flag(controller,"Wanted"),
                ReferenceEquals(follow.Hero(controller),__instance),CapAccess.Flag(CapAccess.Read(__instance,"cState"),"hazardRespawning")))
            {
                __result=HazardRespawnProtection(follow,__instance,duration);
                return false;
            }
            if (!follow.MarioAlive(controller) || !ReferenceEquals(follow.Hero(controller), __instance))
            {
                if(ReferenceEquals(follow.Hero(controller),__instance))
                {
                    follow.RelinquishGuard(__instance);
                    if(HasHitProtection(__instance))hitProtection.Cancel();
                }
                return true;
            }
            __result = PausedInvincibility(follow, __instance, duration);
            return false;
        }

        internal static bool EndsHazardProtectionWithRespawn(bool wanted,bool ownHero,bool respawning)
            =>wanted&&ownHero&&respawning;
        private static IEnumerator HazardRespawnProtection(KnightResetFollow follow,object hero,float duration)
        {
            int ticket=BeginHitProtection(follow,hero,duration);
            try
            {
                follow.SetHeroInvulnerable(hero,true);
                while(hitProtection.Current(ticket)&&CapAccess.Flag(CapAccess.Read(hero,"cState"),"hazardRespawning"))yield return null;
            }
            finally
            {
                hitProtection.End(ticket);
            }
        }

        private static void TraceInvincibility(string text)
        {
            Instance?.Log.LogInfo("Hit protection: " + text);
        }

        private static IEnumerator PausedInvincibility(KnightResetFollow follow, object hero, float duration)
        {
            bool completed = false, paused = false, warned = false;
            float left = duration, pausedAt = 0;
            float stunBudget=HitProtectionLease.StunPauseSeconds;
            int ticket=BeginHitProtection(follow,hero,duration);
            TraceInvincibility("started; remaining=" + duration.ToString("0.000") + ".");
            try
            {
                follow.SetHeroInvulnerable(hero, true);
                yield return new WaitForSeconds(follow.FreezeDown(hero));
                if(!hitProtection.Current(ticket))yield break;
                follow.Pulse(hero, true);
                while (hitProtection.Current(ticket)&&left > 0f)
                {
                    yield return null;
                    if(!hitProtection.Current(ticket))yield break;
                    if (HitProtectionLease.PauseForStun(follow.MarioInvincibleState(hero),Time.deltaTime,ref stunBudget))
                    {
                        if (!paused) { paused = true; pausedAt = Time.unscaledTime; TraceInvincibility("countdown paused for Mario's stun."); }
                        if (!warned && Time.unscaledTime - pausedAt > 3f)
                        {
                            warned = true;
                            TraceInvincibility("still paused after 3 seconds; action=" + follow.Action(follow.Controller()).ToString("X")
                                +", remaining=" + left.ToString("0.000") + ".");
                        }
                        continue;
                    }
                    if (paused) { paused = false; TraceInvincibility("countdown resumed; remaining=" + left.ToString("0.000") + "."); }
                    left -= Time.deltaTime;
                }
                completed = hitProtection.Current(ticket)&&left<=0f;
            }
            finally
            {
                bool owns=hitProtection.Current(ticket);
                hitProtection.End(ticket);
                TraceInvincibility(!owns?"superseded; newer protection left unchanged."
                    :(completed?"expired":"interrupted")+"; owned hit protection released.");
            }
        }

        // A ground pound onto spikes - or anything else the Knight can pogo on - bounces Mario off it
        // instead of hurting him, and leaves him in his second jump, so a jump right after makes the
        // triple jump. Nothing else bounces off hazards; running or falling into them still hurts.
        // Spikes hurt the Knight the instant his body touches them, which also ends a ground pound.
        // When that blow comes from spikes while Mario is coming down in a ground pound, it is turned
        // into the pogo instead: no damage, Mario bounces, and he is left in his second jump.
        private static bool TakeDamagePrefix(object __instance, object[] __args)
        {
            if (__args != null && __args.Length > 0 && IsProjectileImpactDamage(__instance, __args[0] as GameObject)) return false;
            if(MetalCap.Recovering)return true; // A real forced pit recovery cannot become a pogo.
            if (__args == null || __args.Length < 4)
            {
                return true;
            }
            int hazard;
            try
            {
                hazard = Convert.ToInt32(__args[3]);
            }
            catch (Exception)
            {
                return true;
            }
            KnightResetFollow ledgeFollow=Instance?.knightFollow;
            object ledgeController=ledgeFollow?.Controller();
            // No invulnerability flag or timer: protection ends with this exact
            // native action. Enemy contact (type 1) remains ordinary damage.
            if(ledgeController!=null&&ledgeFollow.MarioAlive(ledgeController)
                &&ReferenceEquals(ledgeFollow.Hero(ledgeController),__instance)
                &&StartupHurtbox.BlocksLedgeHazard(ledgeFollow.Action(ledgeController),hazard))return false;
            GameObject contactSource = __args[0] as GameObject;
            if (contactSource != null && BelongsToEnemy(contactSource))
                TraceEnemyContact(FindParent(contactSource.transform, healthManagerType), false);
            // Resolve an actual top contact before the physics damage callback can hurt Mario.
            if (hazard == 1 && ContactBounceOffEnemy(__args[0] as GameObject))
            {
                return false;
            }
            // 1 is spikes and thorns, and 2 is what the game's "Spike Collider" patches report;
            // pits and the rest stay deadly.
            if (hazard != 1 && hazard != 2)
            {
                if (spikeLogs < 3)
                {
                    spikeLogs++;
                    string what = __args[0] == null ? "?" : (__args[0] as Component)?.name ?? __args[0].ToString();
                    Instance?.Log.LogInfo("Mario was hurt by '" + what + "' as hazard type " + hazard + ".");
                }
                return true;
            }
            // Straight after a bounce he is still overlapping whatever he bounced from; those
            // touches are the same pogo, not a new hit to bounce or hurt him again.
            if (Environment.TickCount - lastPogoAt < 450)
            {
                return false;
            }
            // A creature's own hit is never a hazard to bounce from, whatever type it reports.
            if (BelongsToEnemy(__args[0] as GameObject))
            {
                return true;
            }
            if (SpikePogo())
            {
                return false;
            }
            if (spikeLogs < 6)
            {
                spikeLogs++;
                uint move = lastController == null || Instance?.knightFollow == null ? 0u : Instance.knightFollow.Action(lastController);
                float fall = lastController == null || Instance?.knightFollow == null ? 0f : Instance.knightFollow.VerticalSpeed(lastController);
                Instance?.Log.LogInfo("spikes hurt Mario while he was in move " + move + " falling " + fall.ToString("0") + ".");
            }
            return true;
        }

        // Only report enemies that actually contact the player (plus living zero-health
        // receivers). No scene scan or extra physics query; each kind is once per enemy.
        private static void TraceEnemyContact(Component enemy, bool zeroHealth)
        {
            if (!DebugSettings.VerboseLogging) return;
            if (enemy == null || enemyContactReports.Count >= 24) return;
            long key = ((long)enemy.GetInstanceID() << 1) | (zeroHealth ? 1L : 0L);
            if (!enemyContactReports.Add(key)) return;
            try
            {
                string shapes = "";
                foreach (Collider2D shape in enemy.GetComponentsInChildren<Collider2D>(true))
                    shapes += " [" + shape.GetType().Name + " layer=" + shape.gameObject.layer + " enabled=" + shape.enabled
                        + " active=" + shape.gameObject.activeInHierarchy + " trigger=" + shape.isTrigger
                        + " center=" + shape.bounds.center + " size=" + shape.bounds.size + "]";
                string states = "";
                if (fsmType != null)
                    foreach (Component fsm in enemy.GetComponents(fsmType))
                        states += " [" + Read(fsm, "FsmName") + ":" + Read(fsm, "ActiveStateName") + "]";
                object controller = Instance?.knightFollow?.Controller();
                Transform hero = Instance?.knightFollow?.HeroTransform(controller);
                Collider2D body = hero == null ? null : hero.GetComponent<Collider2D>();
                Instance?.Log.LogInfo("Enemy contact report (" + (zeroHealth ? "living zero-health receiver" : "touched player") + "): "
                    + enemy.name + " #" + enemy.GetInstanceID() + " hp=" + hpField?.GetValue(enemy)
                    + " isDead=" + Read(enemy, "isDead") + " specialDeath=" + Read(enemy, "hasSpecialDeath")
                    + " invincible=" + Read(enemy, "IsInvincible") + " evasion=" + Read(enemy, "evasionByHitRemaining")
                    + " enabled=" + (enemy as Behaviour)?.isActiveAndEnabled + " scale=" + enemy.transform.localScale
                    + " marioBounds=" + (body == null ? "missing" : body.bounds.ToString()) + " shapes=" + shapes + " states=" + states);
            }
            catch (Exception error)
            {
                Instance?.Log.LogWarning("Enemy contact report unavailable: " + error.GetBaseException().Message);
            }
        }

        // The base mod reacts to every hero hit by knocking Mario about (its own postfix still runs
        // when the hit itself is skipped), so any hit in the moment after a bounce is not a hurt:
        // it can neither damage him nor start the hurt knockback. The window is time-based because
        // one bounce can produce several touches in a row, and every one of them has to stay quiet.
        private static bool OnKnightHitPrefix()
        {
            return !AcidSafety.SuppressReaction && Time.unscaledTime - pogoedAt > 0.45f;
        }

        private static bool BelongsToEnemy(GameObject source)
        {
            if (source == null)
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
            return healthManagerType != null
                && (FindParent(source.transform, healthManagerType) != null || source.layer == EnemiesLayer);
        }

    }
}
