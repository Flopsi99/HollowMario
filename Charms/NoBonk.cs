using System;
using System.Runtime.InteropServices;

namespace HollowMario
{
    // The air-step result identifies a real wall collision, including bonks
    // which share their action with enemy knockback. Keep the native collision
    // response and replace only its stun with ordinary airborne control.
    internal static class NoBonk
    {
        internal const string NameKey="CHARM_NAME_14",Name="No Bonk Badge";
        private const uint AirHitWall=0x000008A7u,BackBonk=0x010208B0u,SoftBonk=0x010208B6u,Freefall=0x0100088Cu,GroundPound=0x008008A9u;
        private const int HitWall=2;
        private const int WallOffset=0x60,ActionOffset=0xC,PreviousOffset=0x10,WallTimerOffset=0x2A;
        private const int WindowSteps=9;
        [ThreadStatic] private static IntPtr wallOwner;
        [ThreadStatic] private static uint wallAction;
        [ThreadStatic] private static IntPtr contactOwner;
        [ThreadStatic] private static IntPtr poundOwner;
        [ThreadStatic] private static bool contactUsed,sampled,touching;
        [ThreadStatic] private static IntPtr kickOwner;
        [ThreadStatic] private static int remaining;
        internal static bool Pending=>remaining>0;
        internal struct Saved {internal uint Action,Previous;internal byte Timer;}

        internal static void BeginTick(bool enabled,bool canWallJump)
        {
            wallOwner=IntPtr.Zero;
            if(!enabled){Forget();return;}
            if(sampled&&!touching){contactOwner=IntPtr.Zero;contactUsed=false;}
            sampled=touching=false;
            if(!canWallJump)ClearKick();
            else if(remaining>0&&--remaining==0)ClearKick();
        }

        private static bool Controllable(uint action)
            =>(action&0x800u)!=0&&(action&(0x20000u|0x1000u))==0;

        internal static void AirStep(IntPtr mario,int result,uint action)
        {
            if(contactOwner!=mario){contactOwner=mario;contactUsed=false;}
            sampled=true;
            touching|=result==HitWall||Marshal.ReadIntPtr(mario,WallOffset)!=IntPtr.Zero;
            wallOwner=result==HitWall&&Controllable(action)?mario:IntPtr.Zero;
            wallAction=action;
        }

        internal static uint RemapAction(IntPtr mario,uint before,uint action,bool canWallJump)
        {
            bool pound=before==GroundPound;
            bool bonked=wallOwner==mario&&wallAction==before&&(pound?poundOwner!=mario:!contactUsed)&&Controllable(before)
                &&(action==AirHitWall||action==BackBonk||action==SoftBonk);
            wallOwner=IntPtr.Zero;
            if(bonked)
            {
                bool firstContact=!contactUsed;
                contactUsed=true;
                // A prior wall recovery must not consume the pound experiment.
                // Its separate allowance lasts until this actor lands.
                if(pound)poundOwner=mario;
                // Stop the blocked horizontal motion once. Otherwise normal
                // falling carries its impact speed straight into another bonk.
                Marshal.WriteInt32(mario,0x54,0);
                Marshal.WriteInt32(mario,0x48,0);
                Marshal.WriteInt32(mario,0x50,0);
                // Ceiling edges can also report HitWall. They lose their stun,
                // but only an actual side wall supplies a wall-kick opportunity.
                kickOwner=firstContact&&canWallJump&&Marshal.ReadIntPtr(mario,WallOffset)!=IntPtr.Zero?mario:IntPtr.Zero;
                remaining=kickOwner==IntPtr.Zero?0:WindowSteps;
                return Freefall;
            }
            ActionInitialized(mario,action);
            return action;
        }
        internal static bool PrepareKick(IntPtr mario,out Saved saved)
        {
            saved=default(Saved);
            if(!Pending||kickOwner!=mario||(Marshal.ReadInt16(mario,2)&2)==0
                ||!Controllable(unchecked((uint)Marshal.ReadInt32(mario,ActionOffset))))return false;
            saved=new Saved{Action=unchecked((uint)Marshal.ReadInt32(mario,ActionOffset)),
                Previous=unchecked((uint)Marshal.ReadInt32(mario,PreviousOffset)),Timer=Marshal.ReadByte(mario,WallTimerOffset)};
            Marshal.WriteInt32(mario,PreviousOffset,unchecked((int)AirHitWall));
            Marshal.WriteByte(mario,WallTimerOffset,1);
            return true;
        }
        internal static void RestoreKick(IntPtr mario,Saved saved)
        {
            Marshal.WriteByte(mario,WallTimerOffset,saved.Timer);
            if(unchecked((uint)Marshal.ReadInt32(mario,ActionOffset))==saved.Action)
                Marshal.WriteInt32(mario,PreviousOffset,unchecked((int)saved.Previous));
        }
        internal static void ActionInitialized(IntPtr mario,uint action)
        {
            if(kickOwner==mario&&action!=Freefall)ClearKick();
            if(!Controllable(action))wallOwner=IntPtr.Zero;
            if(contactOwner==mario&&(action&0x800u)==0){contactOwner=IntPtr.Zero;contactUsed=false;}
            if(poundOwner==mario&&(action&0x800u)==0)poundOwner=IntPtr.Zero;
        }
        private static void ClearKick(){kickOwner=IntPtr.Zero;remaining=0;}
        internal static void CancelRecovery()
        {wallOwner=contactOwner=IntPtr.Zero;wallAction=0;contactUsed=sampled=touching=false;ClearKick();}
        internal static void Forget(){CancelRecovery();poundOwner=IntPtr.Zero;}
    }
}
