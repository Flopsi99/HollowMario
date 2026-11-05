using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using MarioHollow;

// Retains OUR earlier LedgeChecks collision scenarios, with initialization
// through the newly written host. No old mashup assembly or body is an input.
internal static class LedgeRegression
{
    [StructLayout(LayoutKind.Sequential)]private struct Point{public float X,Y,Z;public Point(float x,float y,float z){X=x;Y=y;Z=z;}}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate uint AirStep(IntPtr mario,ref Point intended,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate float FindFloor(float x,float y,float z,out IntPtr floor);
    [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string path);
    private const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    private static Type gates;
    private static AirStep air;
    private static FindFloor floor;
    private static void Check(bool good,string why){if(!good)throw new Exception(why);}
    private static void Quad(List<SM64Surface> faces,int[] a,int[] b,int[] c,int[] d,short type)
    {
        faces.Add(Triangle(a,b,c,type));faces.Add(Triangle(a,c,d,type));
    }
    private static SM64Surface Triangle(int[] a,int[] b,int[] c,short type)=>new SM64Surface{type=type,terrain=1,v0x=a[0],v0y=a[1],v0z=a[2],v1x=b[0],v1y=b[1],v1z=b[2],v2x=c[0],v2y=c[1],v2z=c[2]};
    private static List<SM64Surface> Box(int left,int right,int bottom,int top,short type)
    {
        var faces=new List<SM64Surface>();int z=400;
        Quad(faces,new[]{left,top,-z},new[]{left,top,z},new[]{right,top,z},new[]{right,top,-z},type);
        Quad(faces,new[]{left,bottom,z},new[]{left,bottom,-z},new[]{right,bottom,-z},new[]{right,bottom,z},type);
        Quad(faces,new[]{left,bottom,-z},new[]{left,bottom,z},new[]{left,top,z},new[]{left,top,-z},type);
        Quad(faces,new[]{right,bottom,z},new[]{right,bottom,-z},new[]{right,top,-z},new[]{right,top,z},type);
        Quad(faces,new[]{left,bottom,z},new[]{right,bottom,z},new[]{right,top,z},new[]{left,top,z},type);
        Quad(faces,new[]{right,bottom,-z},new[]{left,bottom,-z},new[]{left,top,-z},new[]{right,top,-z},type);return faces;
    }
    private static uint Load(List<SM64Surface> faces)
    {
        var pin=GCHandle.Alloc(faces.ToArray(),GCHandleType.Pinned);
        try{var value=new SM64SurfaceObject{surfaceCount=(uint)faces.Count,surfaces=pin.AddrOfPinnedObject()};return Native.sm64_surface_object_create(ref value);}
        finally{pin.Free();}
    }
    private static unsafe uint Step(bool enabled,bool mirror,float speed,uint flags,out Point result)
    {
        FieldInfo field=gates.GetField("current",Hidden);object policy=Activator.CreateInstance(field.FieldType);
        field.FieldType.GetField("Active",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(policy,enabled);field.SetValue(null,policy);
        IntPtr memory=Marshal.AllocHGlobal(1024);
        try
        {
            byte* m=(byte*)memory;for(int i=0;i<1024;i++)m[i]=0;
            var intended=new Point(mirror?40:-40,0,0);*(Point*)(m+0x3C)=intended;*(float*)(m+0x48)=mirror?-20:20;*(float*)(m+0x4C)=speed;
            IntPtr support;float height=floor(intended.X,0,0,out support);*(IntPtr*)(m+0x70)=support;*(float*)(m+0x7C)=height;
            uint answer=air(memory,ref intended,flags);result=*(Point*)(m+0x3C);return answer;
        }
        finally{Marshal.FreeHGlobal(memory);}
    }
    private static void Trial(string label,int bottom,int top,bool mirror,float speed,uint flags,bool blocked,short type,bool extra)
    {
        var faces=Box(mirror?-600:0,mirror?0:600,bottom,top,type);
        if(blocked)faces.AddRange(Box(mirror?-600:0,mirror?0:600,top+120,top+300,21));
        uint id=Load(faces);
        try
        {
            Point before,after;uint baseline=Step(false,mirror,speed,flags,out before),actual=Step(true,mirror,speed,flags,out after);
            if(extra){Check(baseline!=3&&actual==3,label+": must gain a grab.");Check(Math.Abs(after.Y-top)<.01f,label+": must use the real top.");}
            else Check(baseline==actual&&Math.Abs(before.X-after.X)<.001f&&Math.Abs(before.Y-after.Y)<.001f,label+": native result/placement must be preserved.");
        }
        finally{Native.sm64_surface_object_delete(id);}
    }
    internal static void Run(Assembly companion,string library)
    {
        gates=companion.GetType("HollowMario.NativeMovementGates",true);Type ledges=companion.GetType("HollowMario.ThinPlatformLedges",true);
        IntPtr module=GetModuleHandle(library);
        var symbols=(Dictionary<string,IntPtr>)companion.GetType("HollowMario.NativeSymbols",true).GetMethod("Functions",Hidden)
            .Invoke(null,new object[]{library,module,new[]{"perform_air_quarter_step","find_floor"}});
        air=(AirStep)Marshal.GetDelegateForFunctionPointer(symbols["perform_air_quarter_step"],typeof(AirStep));
        floor=(FindFloor)Marshal.GetDelegateForFunctionPointer(symbols["find_floor"],typeof(FindFloor));
        ledges.GetMethod("Install",Hidden).Invoke(null,new object[]{library,module});
        uint ground=Load(Box(-1000,1000,-400,-300,21));
        try
        {
            Trial("Thin shelf right",120,140,false,-5,1,false,21,true);Trial("Thin shelf left",120,140,true,-5,1,false,21,true);
            Trial("Very thin shelf",135,140,false,-5,1,false,21,true);Trial("Shallow shelf",60,140,false,-5,1,false,21,true);
            Trial("Thick native ledge",-300,140,false,-5,1,false,21,false);Trial("Rising jump",120,140,false,5,1,false,21,false);
            Trial("Ineligible move",120,140,false,-5,0,false,21,false);Trial("Blocked climb",120,140,false,-5,1,true,21,false);
            Trial("Too high",180,200,false,-5,1,false,21,false);Trial("Too low",50,70,false,-5,1,false,21,false);
            Trial("Burning surface",120,140,false,-5,1,false,1,false);Trial("Full wall",-300,400,false,-5,1,false,21,false);
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ledge-regression.cfg");
            try
            {
                File.WriteAllText(path,"[Movement]\nThinPlatformLedgeGrabs = false\n");
                ledges.GetMethod("Configure",Hidden).Invoke(null,new object[]{new ConfigFile(path),null});
                Trial("Disabled fallback",120,140,false,-5,1,false,21,false);
            }
            finally{if(File.Exists(path))File.Delete(path);}
        }
        finally{Native.sm64_surface_object_delete(ground);ledges.GetMethod("Dispose",Hidden).Invoke(null,null);}
        Console.WriteLine("PASS 13 existing thin-platform native collision scenarios through independent host: shelf sizes/directions, native ledges, rise/move gates, clearance, reach, burning walls and disabled fallback.");
    }
}
