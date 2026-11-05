using System;
using UnityEngine;

namespace HollowMario
{
    // Project actual liquid shapes down Mario's vertical column, not their
    // rectangular bounds across a room. Terrain anywhere before the selected
    // surface stops the extension, including terrain inside a shallow trigger.
    // Nothing changes the game's colliders or the Knight's water interactions.
    internal static class WaterColumns
    {
        private static RaycastHit2D[] hits=new RaycastHit2D[64];
        private static readonly ContactFilter2D filter=new ContactFilter2D{useTriggers=true};
        private const int MaximumHits=4096;
        private const float BoundaryTolerance=.001f;

        internal static bool Find(Vector2 point,object hero,out float surface,out bool acid,out Collider2D pool)
        {
            surface=float.NaN;acid=false;pool=null;
            int count;
            while((count=Physics2D.Raycast(point,Vector2.up,filter,hits,float.PositiveInfinity))==hits.Length)
            {
                // An incomplete column cannot prove that no floor separates it
                // from a pool. Fail closed rather than invent water below one.
                if(hits.Length>=MaximumHits)return false;
                Array.Resize(ref hits,Math.Min(MaximumHits,hits.Length*2));
            }
            int terrain=LayerMask.GetMask("Terrain","Soft Terrain");
            if(terrain==0)return false; // Cannot prove the column is unobstructed without terrain layers.
            float nearestPool=float.PositiveInfinity,barrier=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                Collider2D shape=hits[i].collider;
                if(shape==null||!shape.enabled||!shape.gameObject.activeInHierarchy)continue;
                float distance=hits[i].distance;
                if(!(distance>=0f))continue;
                string tag=shape.gameObject.tag;
                bool isAcid=tag=="Acid";
                if(shape.isTrigger&&(isAcid||tag=="Water Surface"))
                {
                    // Unsafe acid cannot be bypassed by finding a safe pool
                    // farther above. Tear/Metal permissions remain authoritative.
                    if(isAcid&&!AcidSafety.CanEnterAcid(hero)){barrier=Mathf.Min(barrier,distance);continue;}
                    float top=shape.bounds.max.y;
                    if(point.y>top||distance>=nearestPool)continue;
                    nearestPool=distance;surface=top;acid=isAcid;pool=shape;
                }
                else if(!shape.isTrigger&&!shape.usedByComposite&&(terrain&(1<<shape.gameObject.layer))!=0
                    &&!HeroIgnores.ShouldIgnore(shape))barrier=Mathf.Min(barrier,distance);
            }
            if(pool!=null&&barrier>surface-point.y+BoundaryTolerance)return true;
            surface=float.NaN;acid=false;pool=null;return false;
        }
    }
}
