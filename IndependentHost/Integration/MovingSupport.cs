using System;
namespace MarioHollow
{
    // Upstream carries horizontal platform displacement. Its floor step handles
    // ascent, but a descending lift can leave Mario behind. Follow only the
    // floor actually supporting him, never a parent or a nearby platform edge.
    public static class MovingSupport
    {
        public struct Contact {public IntPtr Surface;public float Height;}
        public static Contact Capture(SM64MarioState state)
        {
            uint group=state.action&0x1C0u;
            if(group!=0u&&group!=0x40u)return default(Contact);
            float height=Native.sm64_surface_find_floor(state.px,state.py,state.pz,out IntPtr surface);
            return surface!=IntPtr.Zero&&Math.Abs(state.py-height)<4f
                ?new Contact{Surface=surface,Height=height}:default(Contact);
        }
        public static float VerticalDelta(Contact contact,SM64MarioState state)
        {
            if(contact.Surface==IntPtr.Zero)return 0f;
            float height=Native.sm64_surface_find_floor(state.px,state.py,state.pz,out IntPtr surface);
            float delta=height-contact.Height;
            // Replaced/destroyed support and teleports cannot carry the player.
            return surface==contact.Surface&&Math.Abs(delta)<=78f?delta:0f;
        }
        public static float Follow(Contact contact,int id,ref SM64MarioState state)
        {
            float delta=VerticalDelta(contact,state);
            if(delta!=0f){state.py+=delta;Native.sm64_set_mario_position(id,state.px,state.py,state.pz);}
            return delta;
        }
        public static void InheritTakeoff(Contact contact,float lift,int id,ref SM64MarioState state)
        {
            // Follow already applies this tick's displacement. Transfer the
            // supporting lift's upward speed once when a jump actually starts;
            // airborne ticks cannot capture support and cannot add it again.
            if(contact.Surface==IntPtr.Zero||lift<=0f||state.vy<=0f)return;
            switch(state.action)
            {
                case 0x03000880u:case 0x03000881u:case 0x01000882u:
                case 0x01000883u:case 0x03000885u:case 0x01000887u:
                case 0x03000888u:case 0x018008ACu:case 0x03000894u:
                    state.vy+=lift;
                    Native.sm64_set_mario_velocity(id,state.vx,state.vy,state.vz);
                    break;
            }
        }
    }
}
