using System;
using System.Runtime.InteropServices;

namespace HollowMario.Host
{
    public sealed class GeometryFrame:IDisposable
    {
        public const int MaxTriangles=1024;
        public readonly float[] Positions=new float[MaxTriangles*9],Normals=new float[MaxTriangles*9],Colours=new float[MaxTriangles*9],UVs=new float[MaxTriangles*6];
        internal GeometryPointers Pointers;
        private readonly GCHandle[] pins=new GCHandle[4];
        private bool disposed;
        public int Triangles=>Pointers.Triangles;
        public GeometryFrame()
        {
            try
            {
                Array[] arrays={Positions,Normals,Colours,UVs};
                for(int i=0;i<4;i++)pins[i]=GCHandle.Alloc(arrays[i],GCHandleType.Pinned);
                Pointers=new GeometryPointers{Position=pins[0].AddrOfPinnedObject(),Normal=pins[1].AddrOfPinnedObject(),Colour=pins[2].AddrOfPinnedObject(),UV=pins[3].AddrOfPinnedObject()};
            }
            catch{Dispose();throw;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var pin in pins)if(pin.IsAllocated)pin.Free();
        }
    }
}
