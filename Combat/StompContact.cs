using UnityEngine;

namespace HollowMario
{
    // One rule for downward enemy contact: a small lateral allowance and a shallow band at the
    // top of the actual hurtbox. Sweep the foot's path so fast falls cannot skip that band.
    internal static class StompContact
    {
        private const float SidePaddingFraction = 0.25f;
        private const float TopFraction = 0.10f;
        private const float MaxTopDepth = 0.35f;
        private const float SurfaceSkin = 0.12f;

        internal static float FootHalfWidth(Bounds mario)=>mario.extents.x+mario.size.x*SidePaddingFraction;

        internal static Bounds SearchBounds(Bounds mario, Vector3 start, Vector3 end)
        {
            float halfWidth = FootHalfWidth(mario);
            float offset = mario.center.x - end.x;
            float minX = Mathf.Min(start.x, end.x) + offset - halfWidth;
            float maxX = Mathf.Max(start.x, end.x) + offset + halfWidth;
            float minY = Mathf.Min(start.y, end.y) - MaxTopDepth;
            float maxY = Mathf.Max(start.y, end.y) + SurfaceSkin;
            return new Bounds(new Vector3((minX+maxX)*0.5f, (minY+maxY)*0.5f, 0f),
                new Vector3(maxX-minX, maxY-minY, 0f));
        }

        // Native speeds are displacement per 30 Hz step, not world units/second.
        internal static Vector3 AnticipationEnd(Vector3 feet, Vector3 nativeVelocity)
        {
            if(nativeVelocity.y>=0f||float.IsNaN(nativeVelocity.y)||float.IsInfinity(nativeVelocity.y)
                ||float.IsNaN(nativeVelocity.x)||float.IsInfinity(nativeVelocity.x))return feet;
            return feet+new Vector3(nativeVelocity.x,nativeVelocity.y,0f)*.0105f;
        }
        internal static Bounds AnticipationFoot(Bounds mario,Vector3 feet)
            =>new Bounds(new Vector3(mario.center.x,feet.y-SurfaceSkin*.5f,0f),
                new Vector3(FootHalfWidth(mario)*2f,SurfaceSkin,0f));
        internal static Bounds SearchStompBounds(Bounds mario,Vector3 start,Vector3 end,Vector3 next)
        {
            Bounds search=SearchBounds(mario,start,end),futureBody=mario;
            futureBody.center+=next-end;
            search.Encapsulate(SearchBounds(futureBody,end,next));
            return search;
        }
        internal static bool TryStomp(Bounds mario,Vector3 start,Vector3 end,Vector3 next,Bounds enemy,out float fraction)
        {
            if(TryContact(mario,start,end,enemy,out fraction))return true;
            // An anticipatory hit may bounce here but must never teleport Mario
            // into a future position. Fractions >1 also sort after actual contacts.
            if(!TrySpan(mario.center.x-end.x,FootHalfWidth(mario),end,next,enemy,SurfaceSkin,out float future))return false;
            fraction=1f+future;
            return true;
        }

        internal static bool LandsOnTop(Bounds mario, Vector3 start, Vector3 end, Bounds enemy)
        {
            float ignored;
            return TryContact(mario, start, end, enemy, out ignored);
        }

        internal static bool TryContact(Bounds mario, Vector3 start, Vector3 end, Bounds enemy, out float fraction)
        {
            return TrySpan(mario.center.x-end.x,FootHalfWidth(mario),start,end,enemy,0f,out fraction);
        }
        private static bool TrySpan(float offset,float halfWidth,Vector3 start,Vector3 end,Bounds enemy,float below,out float fraction)
        {
            fraction=0f;
            if(end.y>=start.y-.0001f||enemy.size.y<=0f)return false;
            float depth=Mathf.Min(enemy.size.y*TopFraction,MaxTopDepth);
            float enter=0f,leave=1f;
            if(!Clip(start.y,end.y,enemy.max.y-depth,enemy.max.y+SurfaceSkin+below,ref enter,ref leave)
                ||!Clip(start.x+offset,end.x+offset,enemy.min.x-halfWidth,enemy.max.x+halfWidth,ref enter,ref leave))return false;
            fraction=enter;
            return true;
        }

        internal static bool PoundStrikeTouchesTop(Bounds mario, Vector3 start, Vector3 end, Bounds enemy,
            float attackBottom, float attackTop)
        {
            // The pound's accepted damage shape reaches below Mario's feet. If that shape hits
            // a creature from above, register its bounce now: waiting for physical foot contact
            // lets a killing blow remove the creature before the stomp check can ever see it.
            if (end.y >= start.y - 0.0001f || enemy.size.y <= 0f) return false;
            float depth = Mathf.Min(enemy.size.y * TopFraction, MaxTopDepth);
            if (end.y < enemy.max.y-depth || attackBottom > enemy.max.y+SurfaceSkin
                || attackTop < enemy.max.y-depth) return false;
            float halfWidth = FootHalfWidth(mario);
            return mario.center.x+halfWidth >= enemy.min.x && mario.center.x-halfWidth <= enemy.max.x;
        }

        private static bool Clip(float start, float end, float min, float max, ref float enter, ref float leave)
        {
            float delta = end-start;
            if (Mathf.Abs(delta) < 0.000001f) return start >= min && start <= max;
            float a = (min-start)/delta, b = (max-start)/delta;
            enter = Mathf.Max(enter, Mathf.Min(a,b));
            leave = Mathf.Min(leave, Mathf.Max(a,b));
            return enter <= leave;
        }
    }
}
