using System;

namespace HollowMario
{
    // Superdash owns charge, flight AND braking. Never replay input or inject the
    // global room-entry event: that bypasses charging and can relaunch a cancelled dash.
    internal static class KnightDash
    {
        internal static bool OwnsBody(string state)
        {
            switch(state)
            {
                case "Relinquish Control":case "On Ground?":
                case "Ground Charge":case "Ground Charged":case "Wall Charge":case "Wall Charged":
                case "Direction":case "Direction Wall":case "G Right":case "G Left":case "Right":case "Left":
                case "Dash Start":case "Dashing":case "Cancelable":
                case "Enter Super Dash":case "Enter L":case "Enter R":case "Enter Velocity":
                case "Charge Cancel Ground":case "Charge Cancel Wall":case "Air Cancel":case "Hit Wall":
                case "Regain Control":case "Cancel":return true;
                default:return false;
            }
        }

        internal static bool KeepOut(object controller)
        {
            object hero=Plugin.Instance?.knightFollow?.Hero(controller);
            object flags=CapAccess.Read(hero,"cState");
            if(hero==null||CapAccess.Flag(flags,"dead")||CapAccess.Flag(flags,"hazardDeath")
                ||CapAccess.Flag(flags,"hazardRespawning")||CapAccess.Flag(flags,"transitioning"))return false;
            object brain=CapAccess.Read(hero,"superDash");
            return OwnsBody(Convert.ToString(CapAccess.Read(brain,"ActiveStateName")));
        }
    }
}
