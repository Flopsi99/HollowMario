using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // NailSlash's downward contact recognizes an explicit BounceShroom marker on the
    // touched object. Use that same target and native effects route, not mushroom names,
    // decorative children, environmental sensors or the little solid support underneath.
    internal static class MushroomBounce
    {
        internal const uint SpringAction=0x01000882u; // Native triple jump: no release-to-cut height.
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static Type marker,nonBouncer,heroType;
        private static FieldInfo ordinary,boosted,gravity;
        private static MethodInfo locate,send,bounceLarge;
        private static bool armed;
        private static void Arm()
        {
            if(armed)return;armed=true;
            marker=AccessTools.TypeByName("BounceShroom");nonBouncer=AccessTools.TypeByName("NonBouncer");
            Type fsm=AccessTools.TypeByName("PlayMakerFSM"),utility=AccessTools.TypeByName("FSMUtility");
            locate=utility==null?null:AccessTools.Method(utility,"LocateFSM",new[]{typeof(GameObject),typeof(string)});
            send=fsm==null?null:AccessTools.Method(fsm,"SendEvent",new[]{typeof(string)});
            bounceLarge=marker==null?null:AccessTools.Method(marker,"BounceLarge",new[]{typeof(bool)});
        }
        internal static bool BelongsTo(Collider2D hit)
        {Arm();return hit!=null&&marker!=null&&hit.GetComponentInParent(marker)!=null;}
        internal static bool TryTarget(Collider2D hit,out Component mushroom)
        {
            Arm();mushroom=null;
            if(hit==null||!hit.enabled||!hit.gameObject.activeInHierarchy||marker==null)return false;
            int layer=hit.gameObject.layer;if(layer!=11&&layer!=19&&layer!=16)return false;
            Component veto=nonBouncer==null?null:hit.GetComponent(nonBouncer);
            if(veto!=null&&nonBouncer.GetField("active",Any)?.GetValue(veto) is bool noBounce&&noBounce)return false;
            mushroom=hit.GetComponent(marker);
            // Native BounceShroom.active can be false when it is used as a marker for an
            // FSM-controlled shroom; NailSlash still treats that object as a valid target.
            return mushroom!=null;
        }
        internal static void React(Component mushroom)
        {
            Arm();object fsm=locate?.Invoke(null,new object[]{mushroom.gameObject,"Bounce Shroom"});
            if(fsm!=null&&send!=null)send.Invoke(fsm,new object[]{"BOUNCE UPWARD"});
            else bounceLarge?.Invoke(mushroom,new object[]{true});
        }
        internal static float UpSpeed(object hero,float marioBounce)
        {
            if(hero==null)return marioBounce;
            if(heroType!=hero.GetType())
            {
                heroType=hero.GetType();ordinary=heroType.GetField("BOUNCE_VELOCITY",Any);
                boosted=heroType.GetField("SHROOM_BOUNCE_VELOCITY",Any);gravity=heroType.GetField("DEFAULT_GRAVITY",Any);
            }
            if(ordinary==null||boosted==null||gravity==null)return marioBounce;
            return AddLift(marioBounce,Convert.ToSingle(ordinary.GetValue(hero)),Convert.ToSingle(boosted.GetValue(hero)),
                Math.Abs(Physics2D.gravity.y*Convert.ToSingle(gravity.GetValue(hero))));
        }
        internal static float AddLift(float marioBounce,float knightBounce,float shroomBounce,float knightGravity)
        {
            if(knightGravity<=0||float.IsNaN(knightGravity)||float.IsInfinity(knightGravity)
                ||float.IsNaN(knightBounce)||float.IsInfinity(knightBounce)||float.IsNaN(shroomBounce)||float.IsInfinity(shroomBounce))return marioBounce;
            // Add the Knight's extra mushroom HEIGHT to Mario's existing bounce height.
            // Multiplying velocities would exaggerate the boost under the different gravity.
            double extraHeight=Math.Max(0,((double)shroomBounce*shroomBounce-(double)knightBounce*knightBounce)/(2*knightGravity));
            return (float)Math.Sqrt(marioBounce*marioBounce+2*4*extraHeight/.0105);
        }
    }
}
