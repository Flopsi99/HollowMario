using System;

namespace HollowMario
{
    // Device-independent gesture state; never changes the player's movement bindings.
    internal sealed class CapSelectorState
    {
        private readonly Action changed;
        private bool stick,armed;
        internal bool Open { get; private set; }
        internal CapSelectorState(Action changed=null){this.changed=changed;}
        private void Set(bool open){if(Open==open)return;Open=open;changed?.Invoke();}
        internal void Press(bool controller)
        {stick=controller;armed=false;Set(controller?!Open:true);}
        internal void Close()=>Set(false);
        internal void Accepted(bool success){if(stick&&success)Close();}
        internal CapKind? Direction(bool keyHeld,float x,float y,bool left,bool up,bool right,bool down)
        {
            if(!Open)return null;
            if(!stick)
            {
                if(!keyHeld){Close();return null;}
                return left?CapKind.Wing:up?CapKind.Vanish:right?CapKind.Metal:down?CapKind.None:(CapKind?)null;
            }
            float magnitude=Math.Max(Math.Abs(x),Math.Abs(y));
            if(magnitude<.25f){armed=true;return null;}
            if(!armed||magnitude<.65f)return null;
            armed=false;
            return Math.Abs(y)>Math.Abs(x)?(y>0?CapKind.Vanish:CapKind.None):(x<0?CapKind.Wing:CapKind.Metal);
        }
    }
}
