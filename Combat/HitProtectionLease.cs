using System;

namespace HollowMario
{
    // One owner for the shared hit/respawn flag. Unity can stop a coroutine
    // without running its finally block, so LateUpdate also expires the lease.
    internal sealed class HitProtectionLease
    {
        internal const float StunPauseSeconds=3f;
        private int generation;
        private bool active;
        private float remaining;
        private Action cleanup;
        internal bool Active=>active;
        internal void Cancel()=>End(generation);
        internal int Begin(float watchdogSeconds,Action onEnd)
        {
            generation++;active=true;
            remaining=watchdogSeconds>0f&&!float.IsInfinity(watchdogSeconds)?watchdogSeconds:StunPauseSeconds+2f;
            cleanup=onEnd;
            return generation;
        }
        internal bool Current(int ticket)=>active&&ticket==generation;
        internal void End(int ticket)
        {
            if(!Current(ticket))return;
            active=false;var finish=cleanup;cleanup=null;finish?.Invoke();
        }
        internal bool Tick(float delta)
        {
            if(!active||delta<=0f||float.IsNaN(delta)||float.IsInfinity(delta))return false;
            remaining-=delta;
            if(remaining>0f)return false;
            End(generation);return true;
        }
        internal static bool PauseForStun(bool stunned,float delta,ref float budget)
        {
            if(!stunned||budget<=0f)return false;
            budget=Math.Max(0f,budget-Math.Max(0f,delta));return true;
        }
    }
}
