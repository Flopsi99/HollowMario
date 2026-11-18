using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MarioHollow;
using MonoMod.RuntimeDetour;
using UnityEngine;

class VerticalConveyorChecks
{
    const BindingFlags A=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const uint Freefall=0x0100088Cu,WallKick=0x03000886u;
    static Type gates,belts;
    static bool available=true,claws=true;
    static float beltSpeed=3;
    static int id,bonks,expectedSide=1;
    static SM64MarioState state;
    static SM64MarioGeometryBuffers buffers;
    static readonly GCHandle[] pins=new GCHandle[4];
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Sound(uint sound,IntPtr position);
    static readonly Sound soundCallback=ObserveSound;
    static Sound originalSound;
    [DllImport("kernel32",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void ObserveSound(uint sound,IntPtr position)
    {uint sample=sound&0xF0FF0000u;if(sample==0x00450000u||sample==0x00420000u)bonks++;originalSound(sound,position);}
    // The real native wall/probe/movement remain active. Only the Unity scene's
    // conveyor lookup is substituted by an authored up/down belt endpoint.
    static bool Scene(Vector2 point,int side,ref float speed,ref bool __result)
    {Check(side==expectedSide,"Only the wall physically touching Mario is queried");speed=beltSpeed;__result=available;return false;}
    static bool Pending=>(bool)belts.GetProperty("Pending",A).GetValue(null,null);
    static void Policy(bool active,bool hasClaws)
    {
        claws=hasClaws;var field=gates.GetField("current",A);var value=Activator.CreateInstance(field.FieldType);
        foreach(string name in new[]{"Active","Claws","Dash","NoBonk"})field.FieldType.GetField(name,A).SetValue(value,
            name=="Active"?active:name=="Claws"?hasClaws:name=="NoBonk"?false:true);
        field.SetValue(null,value);
    }
    static void Tick(bool jump=false,float horizontal=float.NaN)
    {
        if(float.IsNaN(horizontal))horizontal=expectedSide;
        belts.GetMethod("BeginTick",A).Invoke(null,new object[]{claws,id});
        var input=new SM64MarioInputs{camLookX=horizontal>=0?-1:1,camLookZ=0,stickY=Math.Abs(horizontal),buttonA=(byte)(jump?1:0)};
        Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
    }
    static void Start(bool hasClaws=true,float height=10000,int side=1)
    {
        expectedSide=side;
        belts.GetMethod("Forget",A).Invoke(null,null);Policy(false,hasClaws);
        Native.sm64_set_mario_position(id,side*140,height,0);Native.sm64_set_mario_action(id,Freefall);
        Native.sm64_set_mario_faceangle(id,side*Mathf.PI*.5f);Native.sm64_set_mario_forward_velocity(id,24);
        Native.sm64_set_mario_velocity(id,side*24,-4,0);state=default;Policy(true,hasClaws);bonks=0;
    }
    static SM64Surface Face(int[] a,int[] b,int[] c)=>new SM64Surface{v0x=a[0],v0y=a[1],v0z=a[2],v1x=b[0],v1y=b[1],v1z=b[2],v2x=c[0],v2y=c[1],v2z=c[2]};
    static uint Load(SM64Surface[] faces)
    {
        var pin=GCHandle.Alloc(faces,GCHandleType.Pinned);
        try{var surface=new SM64SurfaceObject{surfaceCount=(uint)faces.Length,surfaces=pin.AddrOfPinnedObject()};return Native.sm64_surface_object_create(ref surface);}
        finally{pin.Free();}
    }
    static int Main(string[] args)
    {
        var h=new Harmony("hollowmario.vertical-conveyors-check");NativeDetour soundHook=null;uint world=uint.MaxValue;
        try
        {
            TestEnvironment.StartNative();var mod=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            gates=mod.GetType("HollowMario.NativeMovementGates",true);belts=mod.GetType("HollowMario.VerticalConveyors",true);
            gates.GetMethod("Configure",A).Invoke(null,new object[]{typeof(Native)});
            gates.GetMethod("Install",A).Invoke(null,new object[]{TestEnvironment.Library,GetModuleHandle(TestEnvironment.Library)});
            h.Patch(belts.GetMethod("FindBelt",A),prefix:new HarmonyMethod(typeof(VerticalConveyorChecks),nameof(Scene)));
            var functions=(Dictionary<string,IntPtr>)mod.GetType("HollowMario.NativeSymbols",true).GetMethod("Functions",A)
                .Invoke(null,new object[]{TestEnvironment.Library,GetModuleHandle(TestEnvironment.Library),new[]{"play_sound"}});
            soundHook=new NativeDetour(functions["play_sound"],soundCallback,new NativeDetourConfig{ManualApply=true});
            originalSound=soundHook.GenerateTrampoline<Sound>();soundHook.Apply();
            object boxed=buffers;string[] names={"position","normal","color","uv"};
            for(int i=0;i<4;i++){pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(boxed,pins[i].AddrOfPinnedObject());}
            buffers=(SM64MarioGeometryBuffers)boxed;
            world=Load(new[]{
                Face(new[]{-2000,0,-2000},new[]{-2000,0,2000},new[]{2000,0,2000}),Face(new[]{-2000,0,-2000},new[]{2000,0,2000},new[]{2000,0,-2000}),
                Face(new[]{200,0,-1000},new[]{200,0,1000},new[]{200,30000,1000}),Face(new[]{200,0,-1000},new[]{200,30000,1000},new[]{200,30000,-1000}),
                Face(new[]{-200,0,1000},new[]{-200,0,-1000},new[]{-200,30000,-1000}),Face(new[]{-200,0,1000},new[]{-200,30000,-1000},new[]{-200,30000,1000})});
            id=Native.sm64_mario_create(0,10000,0);Check(id>=0,"Native actor created on authored floor");
            foreach(int side in new[]{-1,1})foreach(float speed in new[]{3f,-3f,-20f,-10f,-5f,5f,6f,8f,9f,10f})
            {
                beltSpeed=speed;available=true;Start(true,10000,side);Tick();Tick();
                for(int tick=0;tick<90;tick++)
                {
                    float before=state.py;Tick();
                    Check(Math.Abs((state.py-before)*.0105f*30f-speed)<.002f,"Carry matches the Knight's configured world speed every simulation step: "+speed);
                    Check(state.action==Freefall&&Pending,"Contact stays actionable beyond the ordinary wall-kick window");
                }
                Check(bonks==0,"Sustained conveyor contact has no repeated bonk sound");
                Tick(true);Check(state.action==WallKick,"A fresh Jump still wall-kicks after ninety contact ticks, with no No Bonk charm");
                Check(!Pending,"Wall kick releases belt attachment");
                float afterKick=state.py;Tick(true,-side);Check(!Pending&&state.py>afterKick,"Jumping away keeps the native upward launch, even on a downward belt");
            }
            available=true;beltSpeed=6;Start(false);for(int tick=0;tick<8;tick++)Tick();
            Check(!Pending&&state.vy<0,"Neither carry nor persistent wall jumps exists before Wall Jump is unlocked");
            available=false;Start();for(int tick=0;tick<8;tick++)Tick();Check(!Pending&&state.vy<0,"An ordinary wall or inactive/nonvertical belt retains native falling");
            available=true;Start();Tick();Tick();available=false;Tick();Check(!Pending,"Leaving the conveyor ends the wall-kick opportunity immediately");
            available=true;Start();Tick();Tick();Native.sm64_set_mario_forward_velocity(id,-24);Native.sm64_set_mario_velocity(id,-24,0,0);Tick(false,-1);
            Check(!Pending&&state.px<150,"Moving away is never pulled back onto the belt");
            Start();Tick();Tick();Native.sm64_mario_take_damage(id,1,0,-100,10000,0);
            Check(!Pending,"An accepted native hit cancels the contact before knockback");Tick();
            Check((state.action&0x20000u)!=0&&!Pending,"Damage knockback is neither turned into freefall nor cancelled by conveyor carry");
            beltSpeed=-20;Start(true,200);for(int tick=0;tick<40;tick++)Tick();
            Check(state.py>=-.01f&&!Pending,"A descending belt cannot carry Mario through its floor");
            Check((state.action&0x20000u)==0,"The fastest downward belt does not leave Mario in damage knockback at its floor");
            uint roof=Load(new[]{Face(new[]{-2000,5200,-2000},new[]{2000,5200,2000},new[]{-2000,5200,2000}),Face(new[]{-2000,5200,-2000},new[]{2000,5200,-2000},new[]{2000,5200,2000})});
            try{beltSpeed=10;Start(true,5000);for(int tick=0;tick<60;tick++)Tick();Check(state.py<=5040.1f,"Upward carry still respects the native ceiling clearance");}
            finally{Native.sm64_surface_object_delete(roof);}
            belts.GetMethod("Forget",A).Invoke(null,null);Check(!Pending,"Scene/handover cleanup releases the belt");
            Console.WriteLine("PASS native vertical conveyor carry at signed Knight speeds, ninety-tick delayed wall jumps, no bonk sound loop, both departures, Claw gating, normal-wall exclusion, damage, floor and ceiling collision. Only belt scene lookup substituted.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            h.UnpatchSelf();if(gates!=null){Policy(false,false);gates.GetMethod("Dispose",A).Invoke(null,null);}
            soundHook?.Dispose();if(id>=0)Native.sm64_mario_delete(id);if(world!=uint.MaxValue)Native.sm64_surface_object_delete(world);
            foreach(var pin in pins)if(pin.IsAllocated)pin.Free();
        }
    }
}
