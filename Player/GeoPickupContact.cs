using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Native Geo only checks OnTriggerEnter, after its 0.25-second delay, and
    // only accepts HeroBox. Retry actual contact; never award money ourselves.
    internal sealed class GeoPickupContact:MonoBehaviour
    {
        private static FieldInfo activated,pickupStart;
        private static MethodInfo collect;
        private static int failures;
        private Component coin;
        internal static void Arm(Harmony harmony)
        {
            Type geo=AccessTools.TypeByName("GeoControl");
            activated=AccessTools.Field(geo,"activated");pickupStart=AccessTools.Field(geo,"pickupStartTime");
            collect=AccessTools.Method(geo,"OnTriggerEnter2D",new[]{typeof(Collider2D)});
            if(activated==null||pickupStart==null||collect==null)throw new MissingMemberException("Native Geo pickup");
            harmony.Patch(AccessTools.Method(geo,"OnEnable"),postfix:new HarmonyMethod(typeof(GeoPickupContact),nameof(AfterEnabled)));
            harmony.Patch(collect,prefix:new HarmonyMethod(typeof(GeoPickupContact),nameof(BeforeCollect)));
        }
        private static void AfterEnabled(object __instance)
        {
            var source=__instance as Component;
            if(source==null||!source.gameObject.activeInHierarchy)return;
            var helper=source.GetComponent<GeoPickupContact>();
            if(helper==null)helper=source.gameObject.AddComponent<GeoPickupContact>();
            helper.coin=source;
        }
        internal static bool Ready(bool alreadyCollected,float now,float eligibleAt)
            =>!alreadyCollected&&now>=eligibleAt;
        private static bool Mario(out Component hero)
        {
            var follow=Plugin.Instance?.knightFollow;
            object controller=follow?.Controller();hero=controller==null?null:follow.Hero(controller) as Component;
            if(hero==null||!follow.MarioAlive(controller)||follow.ControlHeld(controller))return false;
            object state=CapAccess.Read(hero,"cState");
            return !CapAccess.Flag(state,"dead")&&!CapAccess.Flag(state,"hazardDeath")&&!CapAccess.Flag(state,"hazardRespawning");
        }
        private static Collider2D Collector(Collider2D contact)
        {
            if(contact==null||!Mario(out Component hero))return null;
            // The ordinary hurt sensor already has the native collection tag.
            // The untagged interaction probe is routed explicitly below.
            if(contact.CompareTag("HeroBox")&&contact.transform.IsChildOf(hero.transform))return contact;
            if(contact.gameObject!=hero.gameObject&&!Plugin.IsFeetPad(contact))return null;
            var box=hero.transform.Find("HeroBox")?.GetComponent<Collider2D>();
            return box!=null&&box.CompareTag("HeroBox")?box:null;
        }
        private static void BeforeCollect(ref Collider2D __0)
        {
            // Genuine contact remains mandatory; the probe is not tagged Player.
            if(__0==null||(!Plugin.IsFeetPad(__0)&&!__0.CompareTag("Player")))return;
            Collider2D target=Collector(__0);if(target!=null)__0=target;
        }
        // A solid Geo piece can be stopped by the root body just outside the
        // smaller HeroBox. That physical contact must count as touching too.
        private void OnCollisionEnter2D(Collision2D contact){if(contact!=null)OnTriggerStay2D(contact.collider);}
        private void OnCollisionStay2D(Collision2D contact){if(contact!=null)OnTriggerStay2D(contact.collider);}
        private void OnTriggerStay2D(Collider2D contact)
        {
            try
            {
                if(coin==null||!Ready((bool)activated.GetValue(coin),Time.time,(float)pickupStart.GetValue(coin)))return;
                Collider2D target=Collector(contact);
                if(target!=null)collect.Invoke(coin,new object[]{target});
            }
            catch(Exception error){if(failures++<3)Plugin.Instance?.Log.LogWarning("Mario's Geo contact: "+error.GetBaseException().Message);}
        }
    }
}
