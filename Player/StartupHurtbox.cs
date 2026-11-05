using UnityEngine;

namespace HollowMario
{
    // Change only the native damage trigger, not the physical/support collider used
    // to build Mario's offensive reach. Compact poses retain the bottom;
    // ledge hanging moves this damage box down to the hanging body instead.
    internal static class StartupHurtbox
    {
        private static object owner;
        private static BoxCollider2D box;
        private static Vector2 normalSize,normalOffset,appliedSize,appliedOffset;
        private static bool modified;
        internal const uint LedgeHang=0x0800034Bu;
        internal static void Sync(object controller,KnightResetFollow follow)
        {
            object hero=controller==null?null:follow?.Hero(controller);
            if(hero==null||!follow.MarioAlive(controller)||!(hero is Component body)
                ||(hero is Behaviour actor&&actor.enabled)){Forget();return;}
            if(!ReferenceEquals(owner,hero))
            {
                Forget();owner=hero;
                box=body.transform.Find("HeroBox")?.GetComponent<BoxCollider2D>();
            }
            if(box==null)return;
            uint action=follow.Action(controller);int animation=follow.Animation(controller);
            if(action!=LedgeHang&&!IsCompact(action,animation)){Restore();return;}
            if(!modified){normalSize=box.size;normalOffset=box.offset;}
            float facing=action==LedgeHang?CapAccess.Number(CapAccess.Read(controller,"State"),"faceAngle"):0f;
            Dimensions(action,animation,facing,normalSize,normalOffset,out Vector2 size,out Vector2 offset);
            if(action==LedgeHang)offset=LedgeLocalOffset(normalOffset,offset,box.transform.worldToLocalMatrix);
            if(box.size!=size)box.size=size;
            if(box.offset!=offset)box.offset=offset;
            appliedSize=size;appliedOffset=offset;modified=true;
        }
        internal static bool BlocksLedgeHazard(uint action,int hazard)
            =>action==LedgeHang&&hazard>=2&&hazard<=5;
        // Dimensions expresses the ledge displacement in world space. The Hero
        // and HeroBox can mirror it again; convert only that displacement, while
        // retaining the collider's original local offset.
        internal static Vector2 LedgeLocalOffset(Vector2 baseline,Vector2 displaced,Matrix4x4 worldToLocal)
            =>baseline+(Vector2)worldToLocal.MultiplyVector(displaced-baseline);
        internal static void Dimensions(uint action,int animation,float facing,Vector2 size,Vector2 offset,out Vector2 resultSize,out Vector2 resultOffset)
        {
            resultSize=size;resultOffset=offset;
            if(action==LedgeHang)
            {
                // ROM idle-on-ledge geometry puts his body's centre about 115
                // native units below standing and 30 outside the gripping edge.
                resultOffset+=new Vector2(-Mathf.Sin(facing)*30f,-115f)*.0105f;
            }
            else if(IsCompact(action,animation))
            {
                // Native short/standing body heights: preserve the lower edge.
                resultSize.y*=100f/160f;resultOffset.y+=(resultSize.y-size.y)*.5f;
            }
        }
        internal static bool IsCompact(uint action,int animation)
            =>(action&0x00008000u)!=0u||NailCollisionSignals.IsPoundStartup(action,animation);
        private static void Restore()
        {
            if(modified&&box!=null&&box.size==appliedSize&&box.offset==appliedOffset)
            {box.size=normalSize;box.offset=normalOffset;}
            modified=false;
        }
        internal static void Forget(){Restore();owner=null;box=null;}
    }
}
