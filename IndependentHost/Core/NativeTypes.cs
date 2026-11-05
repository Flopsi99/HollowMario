using System;
using System.Runtime.InteropServices;

namespace HollowMario.Host
{
    // ABI declarations only, from libsm64's public header at fd118132.
    // No decompiled bridge implementation, engine algorithms or game assets.
    [StructLayout(LayoutKind.Sequential)]
    public struct Surface
    {
        public short Type,Force; public ushort Terrain;
        public int Ax,Ay,Az,Bx,By,Bz,Cx,Cy,Cz;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public float CameraX,CameraZ,StickX,StickY;
        public byte Jump,Attack,Crouch;
        public static Input Sideways(float horizontal,bool jump,bool attack,bool crouch)
        {
            float amount=Math.Max(-1f,Math.Min(1f,horizontal));
            return new Input{CameraX=amount==0?0:-Math.Sign(amount),CameraZ=amount==0?-1:0,
                StickY=Math.Abs(amount),Jump=(byte)(jump?1:0),Attack=(byte)(attack?1:0),Crouch=(byte)(crouch?1:0)};
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public float X,Y,Z,VelocityX,VelocityY,VelocityZ,Facing,ForwardVelocity;
        public short Health; public uint Action; public int Animation; public short Frame;
        public uint Flags,Particles; public short Invincibility;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct GeometryPointers
    {
        public IntPtr Position,Normal,Colour,UV; public ushort Triangles;
    }
}
