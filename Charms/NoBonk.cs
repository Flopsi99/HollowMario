using System;
using System.Runtime.InteropServices;

namespace HollowMario
{
    // Only a native wall collision opens this short recovery opportunity.
    // Enemy/hazard knockback and presentation actors never arm it.
    internal static class NoBonk
    {
        internal const string NameKey="CHARM_NAME_14",Name="No Bonk Badge";
        internal const uint Dive=0x0188088Au,AirHitWall=0x000008A7u,BackBonk=0x010208B0u,SoftBonk=0x010208B6u;
        internal const int WindowSteps=9;
        private const int ActionOffset=0xC,PreviousOffset=0x10,WallTimerOffset=0x2A;
        [ThreadStatic] private static IntPtr owner;
        [ThreadStatic] private static int remaining;
        internal static bool Pending=>remaining>0;
        internal struct Saved {internal uint Action,Previous;internal byte Timer;}

        internal static bool IsBonk(uint action)=>action==BackBonk||action==SoftBonk;
        internal static bool Starts(uint before,uint after)
            =>after==AirHitWall||(before==Dive&&after==BackBonk)||(before==AirHitWall&&IsBonk(after));
        internal static void Step(bool enabled)
        {if(!enabled)Forget();else if(remaining>0)remaining--;}
        internal static void ActionInitialized(IntPtr mario,uint before,uint after,bool enabled)
        {
            if(enabled&&Starts(before,after)){owner=mario;remaining=WindowSteps;}
            else if(owner==mario)Forget();
        }
        internal static bool Prepare(IntPtr mario,out Saved saved)
        {
            saved=default(Saved);
            if(!Pending||owner!=mario||(Marshal.ReadInt16(mario,2)&2)==0)return false;
            uint action=unchecked((uint)Marshal.ReadInt32(mario,ActionOffset));if(!IsBonk(action))return false;
            saved=new Saved{Action=action,Previous=unchecked((uint)Marshal.ReadInt32(mario,PreviousOffset)),Timer=Marshal.ReadByte(mario,WallTimerOffset)};
            // check_wall_kick already owns facing, launch velocity, animation and
            // sound. Temporarily supply its ordinary wall-bonk eligibility only.
            Marshal.WriteInt32(mario,PreviousOffset,unchecked((int)AirHitWall));
            Marshal.WriteByte(mario,WallTimerOffset,1);return true;
        }
        internal static void Restore(IntPtr mario,Saved saved)
        {
            Marshal.WriteByte(mario,WallTimerOffset,saved.Timer);
            if(unchecked((uint)Marshal.ReadInt32(mario,ActionOffset))==saved.Action)
                Marshal.WriteInt32(mario,PreviousOffset,unchecked((int)saved.Previous));
        }
        internal static void Forget(){owner=IntPtr.Zero;remaining=0;}
    }
}
