using System;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // A pit hazard or automatic scene exit is a valid end to a fall too.
    // Only the rescue paths ask this; no extra physics query runs normally.
    internal static class FallDestination
    {
        private static Type damageType,transitionType;
        private static int terrainMask,failures;
        private static readonly RaycastHit2D[] hits=new RaycastHit2D[64];
        private static object lastHero;
        private static Vector3 lastFeet;
        private static float nextProbe;
        private static bool cached;

        internal static bool NativeOwns(object hero)
        {
            object state=CapAccess.Read(hero,"cState");
            return CapAccess.Flag(state,"dead")||CapAccess.Flag(state,"transitioning")
                ||CapAccess.Flag(state,"hazardDeath")||CapAccess.Flag(state,"hazardRespawning");
        }
        internal static bool IsHazard(int kind)=>kind>=2&&kind<=5;
        internal static bool IsExit(bool door,string scene,string gate)
            =>!door&&!string.IsNullOrEmpty(scene)&&!string.IsNullOrEmpty(gate);
        internal static bool HasBelow(Vector3 feet,object hero)
        {
            if(ReferenceEquals(lastHero,hero)&&Time.time<nextProbe
                &&Mathf.Abs(feet.x-lastFeet.x)<.25f&&Mathf.Abs(feet.y-lastFeet.y)<4f)return cached;
            lastHero=hero;lastFeet=feet;nextProbe=Time.time+.25f;
            // Be conservative if a query fails or overflows: do not snatch the
            // player away from a possible native endpoint. F5 remains available.
            cached=true;
            try
            {
                if(damageType==null)damageType=AccessTools.TypeByName("DamageHero");
                if(transitionType==null)transitionType=AccessTools.TypeByName("TransitionPoint");
                if(terrainMask==0)terrainMask=LayerMask.GetMask("Terrain","Soft Terrain");
                var current=hero as Component;
                var body=current!=null?current.GetComponent<BoxCollider2D>():null;
                float width=body!=null?Mathf.Max(.1f,body.bounds.size.x):.7f;
                var filter=new ContactFilter2D{useTriggers=true};
                int count=Physics2D.BoxCast(new Vector2(feet.x,feet.y+.05f),new Vector2(width,.1f),0f,
                    Vector2.down,filter,hits,float.PositiveInfinity);
                if(count==hits.Length)return true;
                for(int i=0;i<count;i++)
                {
                    Collider2D shape=hits[i].collider;
                    if(shape==null||!shape.enabled||!shape.gameObject.activeInHierarchy
                        ||(current!=null&&shape.transform.IsChildOf(current.transform)))continue;
                    if(!shape.isTrigger&&(terrainMask&(1<<shape.gameObject.layer))!=0&&hits[i].normal.y>.1f)return true;
                    if(damageType!=null)
                    {
                        var hazard=shape.GetComponentInParent(damageType) as Behaviour;
                        if(hazard!=null&&hazard.isActiveAndEnabled
                            &&IsHazard(Convert.ToInt32(CapAccess.Read(hazard,"hazardType"))))return true;
                    }
                    if(transitionType!=null)
                    {
                        var exit=shape.GetComponentInParent(transitionType) as Behaviour;
                        if(exit!=null&&exit.isActiveAndEnabled&&IsExit(CapAccess.Flag(exit,"isADoor"),
                            Convert.ToString(CapAccess.Read(exit,"targetScene")),Convert.ToString(CapAccess.Read(exit,"entryPoint"))))return true;
                    }
                }
                return cached=false;
            }
            catch(Exception error)
            {if(failures++<3)Plugin.Instance?.Log.LogWarning("Fall destination check: "+error.GetBaseException().Message);return true;}
        }
        internal static void Forget(){lastHero=null;nextProbe=0f;cached=false;}
    }
}
