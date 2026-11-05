using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Native C# bullets share the shield-impact API, rather than the FSM listeners
    // used by orbs. Called only after the existing move/projectile contact gates.
    internal static class NativeProjectileImpact
    {
        private static bool bound;
        private static Type bulletType;
        private static FieldInfo active;
        private static MethodInfo impact;
        private static int failures;
        internal static bool TryImpact(GameObject projectile,Transform origin)
        {
            if(projectile==null||origin==null||!projectile.activeInHierarchy)return false;
            try
            {
                if(!bound)
                {
                    bound=true;bulletType=AccessTools.TypeByName("EnemyBullet");
                    active=AccessTools.Field(bulletType,"active");
                    impact=AccessTools.Method(bulletType,"OrbitShieldHit",new[]{typeof(Transform)});
                    if(active?.FieldType!=typeof(bool)||impact==null||impact.ReturnType!=typeof(void))
                        throw new MissingMemberException("Native bullet impact API");
                }
                if(bulletType==null||active==null||impact==null)return false;
                Component bullet=projectile.GetComponent(bulletType);
                if(!(bullet is Behaviour behaviour)||!behaviour.isActiveAndEnabled||!(bool)active.GetValue(bullet))return false;
                // Its own active flag, impact animation/sound, collider cleanup and
                // recycling remain native. No destruction, damage or immunity writes.
                impact.Invoke(bullet,new object[]{origin});
                return !(bool)active.GetValue(bullet);
            }
            catch(Exception error)
            {
                if(failures++<3)Plugin.Instance?.Log.LogWarning("Mario's native projectile impact: "+error.GetBaseException().Message);
                return false;
            }
        }
    }
}
