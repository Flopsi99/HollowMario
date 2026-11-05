namespace HollowMario
{
    // Count simulation steps rather than rendered frames or duplicated Sync calls. These
    // are short reservations of the jump input for the native takeoff/wall-kick actions.
    internal sealed class AirJumpPriority
    {
        private const uint AirHitWall = 0x000008A7u;
        private const uint WallKick = 0x03000886u;
        private int tick, takeoffUntil, wallUntil;
        private uint previous;
        private bool observed;

        internal void Forget() { tick = takeoffUntil = wallUntil = 0; observed = false; previous = 0; }

        internal void Sample(uint action)
        {
            tick++;
            bool changed = !observed || previous != action;
            if ((action & 0x800u) == 0) takeoffUntil = wallUntil = 0;
            else if (changed)
            {
                if (action == WallKick) takeoffUntil = tick + 4;
                else if (observed && (IsTakeoff(action) || (previous & 0x800u) == 0)) takeoffUntil = tick + 3;
                // Native AIR_HIT_WALL accepts A for two steps, then keeps wallKickTimer for
                // five more in soft bonk/knockback. Do not replace any of those with Wings.
                if (action == AirHitWall) wallUntil = tick + 7;
            }
            previous = action;
            observed = true;
        }

        internal bool Reserved => previous == AirHitWall || tick < takeoffUntil || tick < wallUntil;

        private static bool IsTakeoff(uint action) => action == 0x03000880u || action == 0x03000881u
            || action == 0x01000882u || action == 0x01000883u || action == 0x03000885u
            || action == 0x01000887u || action == 0x03000888u || action == 0x01000889u
            || action == 0x030008A0u;
    }
}
