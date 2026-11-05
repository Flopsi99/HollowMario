using System;
using System.Reflection;
using UnityEngine;
namespace HollowMario
{
    internal static class NativeDamageTimer
    {
        private static Type type;private static FieldInfo parry,parryDuration;
        internal static void GrantStomp(object hero)
        {
            if(hero==null)return;
            Cache(hero);
            if(parry==null||parryDuration==null)return;
            float duration=Convert.ToSingle(parryDuration.GetValue(hero));
            if(duration<=0f||float.IsNaN(duration)||float.IsInfinity(duration))return;
            // One native parry window, refreshed rather than added. Preserve any
            // longer protection already supplied by another successful move.
            parry.SetValue(hero,Math.Max(Convert.ToSingle(parry.GetValue(hero)),duration));
        }
        internal struct StompWindow
        {
            internal object Hero;
            internal FieldInfo Timer;
            internal float Before,Applied;
        }
        internal static StompWindow BeginStomp(object hero)
        {
            if(hero==null)return default(StompWindow);
            Cache(hero);
            if(parry==null)return default(StompWindow);
            float before=Convert.ToSingle(parry.GetValue(hero));
            GrantStomp(hero);
            return new StompWindow{Hero=hero,Timer=parry,Before=before,Applied=Convert.ToSingle(parry.GetValue(hero))};
        }
        internal static void CancelStomp(StompWindow window)
        {
            // Undo only our own unchanged grant when hit delivery failed.
            if(window.Timer!=null&&Convert.ToSingle(window.Timer.GetValue(window.Hero))==window.Applied)
                window.Timer.SetValue(window.Hero,window.Before);
        }
        internal struct DiveHit
        {
            internal object Target,Hero;
            internal FieldInfo Health;
            internal int Before;
        }

        internal static DiveHit BeginDiveHit(object target,object hero,FieldInfo health,uint action)
        {
            if((action!=0x0188088Au&&action!=0x00880456u)||target==null||hero==null||health==null)return default(DiveHit);
            return new DiveHit{Target=target,Hero=hero,Health=health,Before=Convert.ToInt32(health.GetValue(target))};
        }

        internal static void FinishDiveHit(DiveHit hit)
        {
            if(hit.Health==null||hit.Before<=0||Convert.ToInt32(hit.Health.GetValue(hit.Target))>=hit.Before)return;
            KnightResetFollow follow=Plugin.Instance?.knightFollow;
            object controller=follow?.Controller();
            if(controller==null||!follow.MarioAlive(controller)||!ReferenceEquals(follow.Hero(controller),hit.Hero))return;
            Cache(hit.Hero);
            if(parry==null||parryDuration==null)return;
            float duration=2f*Convert.ToSingle(parryDuration.GetValue(hit.Hero));
            if(duration<=0f||float.IsNaN(duration)||float.IsInfinity(duration))return;
            parry.SetValue(hit.Hero,Math.Max(Convert.ToSingle(parry.GetValue(hit.Hero)),duration));
        }

        private static void Cache(object hero)
        {
            if(type==hero.GetType())return;
            type=hero.GetType();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            parry=type.GetField("parryInvulnTimer",flags);
            parryDuration=type.GetField("INVUL_TIME_PARRY",flags);
        }
        // NailParry grants only the native protection timer. Keep the generic hit/deflection
        // paths intact, but the two pound-start animations cannot grant or refresh immunity.
        internal static bool BeforeParry(object __instance)
        {
            KnightResetFollow follow=Plugin.Instance?.knightFollow;
            object controller=follow?.Controller();
            if(controller==null||!follow.MarioAlive(controller)||!ReferenceEquals(follow.Hero(controller),__instance)
                ||follow.Action(controller)!=0x008008A9u)return true;
            int animation=follow.Animation(controller);
            return animation!=0x3B&&animation!=0x3C;
        }
        // HeroController.Update normally expires native parry/quake protection. Mario
        // disables that behaviour; mirror its timer on existing rendered-frame updates.
        internal static void Tick(object hero,float dt)
        {
            if(hero==null||dt<=0||!(hero is Behaviour behaviour)||behaviour.enabled)return;
            Cache(hero);
            if(parry==null)return;float left=Convert.ToSingle(parry.GetValue(hero));
            if(left>0)parry.SetValue(hero,Math.Max(0f,left-dt));
        }
    }
}
