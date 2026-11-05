using UnityEngine;
namespace MarioHollow
{
    public static class HostInput
    {
        // Read the game's current actions each tick so rebindings and device
        // swaps apply, without reflective member searches on the hot path.
        public static SM64MarioInputs Read(HeroActions actions,bool blocked)
        {
            if(blocked||actions==null)return default(SM64MarioInputs);
            return Build(actions.moveVector.Vector.x,actions.jump.IsPressed,
                actions.attack.IsPressed,actions.dash.IsPressed);
        }
        public static SM64MarioInputs Build(float horizontal,bool jump,bool attack,bool dash)
        {
            float x=float.IsNaN(horizontal)||float.IsInfinity(horizontal)?0f:Mathf.Clamp(horizontal,-1f,1f);
            return new SM64MarioInputs{camLookX=x==0?0:-Mathf.Sign(x),camLookZ=x==0?-1:0,stickY=Mathf.Abs(x),
                buttonA=(byte)(jump?1:0),buttonB=(byte)(attack?1:0),buttonZ=(byte)(dash?1:0)};
        }
    }
}
