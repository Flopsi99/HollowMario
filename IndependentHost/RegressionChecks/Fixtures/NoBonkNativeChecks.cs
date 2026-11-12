using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using MarioHollow;
using MonoMod.RuntimeDetour;

class NoBonkNativeChecks
{
    const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    const uint Jump=0x03000880u,Dive=0x0188088Au,LongJump=0x03000888u;
    const uint Freefall=0x0100088Cu,Pound=0x008008A9u,BackBonk=0x010208B0u,SoftBonk=0x010208B6u,AirHitWall=0x000008A7u;
    const uint WallKick=0x03000886u;
    static Type gates,badge;
    static Delegate originalSet;
    static readonly List<uint> initialized=new List<uint>();
    static readonly GCHandle[] pins=new GCHandle[4];
    static SM64MarioGeometryBuffers buffers;
    static SM64MarioState state;
    static int id;
    static bool canWallJump,enabled;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void Sound(uint sound,IntPtr position);
    static readonly Sound soundCallback=ObserveSound;
    static Sound originalSound;
    static int bonkSounds;
    static void ObserveSound(uint sound,IntPtr position)
    {
        uint sample=sound&0xF0FF0000u;
        if(sample==0x00450000u||sample==0x00420000u)bonkSounds++;
        originalSound(sound,position);
    }

    [DllImport("kernel32",CharSet=CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static void Set(object target,string name,object value)
        =>target.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
    static uint Capture(IntPtr mario,uint action,uint argument)
    {
        initialized.Add(action);
        return Convert.ToUInt32(originalSet.DynamicInvoke(mario,action,argument));
    }
    static void Policy(bool equipped,bool claws,bool active=true)
    {
        var field=gates.GetField("current",Hidden);
        object policy=Activator.CreateInstance(field.FieldType);
        foreach(string name in new[]{"Active","Claws","Dash","NoBonk"})
            field.FieldType.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(policy,
                name=="Active"?active:name=="Claws"?claws:name=="NoBonk"?equipped:true);
        field.SetValue(null,policy);
        canWallJump=active&&equipped&&claws;
        enabled=active&&equipped;
        badge.GetMethod("Forget",Hidden).Invoke(null,null);
    }
    static void Tick(SM64MarioInputs input)
    {
        // This is the same per-tick collision reset performed by BeforeTick.
        badge.GetMethod("BeginTick",Hidden).Invoke(null,new object[]{enabled,canWallJump});
        Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
    }
    static void Collide(uint action,bool equipped,bool claws,bool active=true,bool jumpOnContact=false,bool holdJump=false,float height=500)
    {
        Policy(false,claws,false);
        Native.sm64_set_mario_position(id,action==Pound?170:0,height,0);
        Native.sm64_set_mario_action(id,action);
        Native.sm64_set_mario_faceangle(id,(float)(Math.PI*.5));
        Native.sm64_set_mario_forward_velocity(id,48);
        Native.sm64_set_mario_velocity(id,48,-4,0);
        state=default(SM64MarioState);
        initialized.Clear();
        Policy(equipped,claws,active);
        var input=new SM64MarioInputs{camLookX=-1,stickY=1};
        for(int tick=0;tick<(action==Pound?64:24);tick++)
        {
            input.buttonA=(byte)(holdJump||(jumpOnContact&&state.px>=100)?1:0);
            Tick(input);
            if(initialized.Exists(a=>a==Freefall||a==AirHitWall||a==BackBonk||a==SoftBonk||a==WallKick))return;
        }
        throw new Exception("No real wall collision from "+action.ToString("X"));
    }
    static SM64Surface Triangle(int[] a,int[] b,int[] c,short type=0)
        =>new SM64Surface{type=type,v0x=a[0],v0y=a[1],v0z=a[2],v1x=b[0],v1y=b[1],v1z=b[2],v2x=c[0],v2y=c[1],v2z=c[2]};
    static uint Load(SM64Surface[] faces)
    {
        var pin=GCHandle.Alloc(faces,GCHandleType.Pinned);
        try
        {
            var surface=new SM64SurfaceObject{surfaceCount=(uint)faces.Length,surfaces=pin.AddrOfPinnedObject()};
            return Native.sm64_surface_object_create(ref surface);
        }
        finally{pin.Free();}
    }
    static uint World()
    {
        var faces=new[]{
            Triangle(new[]{-2000,0,-2000},new[]{-2000,0,2000},new[]{2000,0,2000}),
            Triangle(new[]{-2000,0,-2000},new[]{2000,0,2000},new[]{2000,0,-2000}),
            Triangle(new[]{200,0,-1000},new[]{200,0,1000},new[]{200,12000,1000}),
            Triangle(new[]{200,0,-1000},new[]{200,12000,1000},new[]{200,12000,-1000})};
        return Load(faces);
    }
    static void LeaveWall()
    {
        Native.sm64_set_mario_position(id,-500,500,0);
        Native.sm64_set_mario_forward_velocity(id,0);
        Native.sm64_set_mario_velocity(id,0,-4,0);
    }
    static void CheckCeilings()
    {
        uint ceiling=Load(new[]{
            Triangle(new[]{-2000,850,-2000},new[]{2000,850,2000},new[]{-2000,850,2000}),
            Triangle(new[]{-2000,850,-2000},new[]{2000,850,-2000},new[]{2000,850,2000})});
        try
        {
            foreach(bool equipped in new[]{false,true})
            {
                Policy(false,true,false);
                Native.sm64_set_mario_position(id,-500,700,0);
                Native.sm64_set_mario_action(id,Jump);
                Native.sm64_set_mario_faceangle(id,(float)(Math.PI*.5));
                Native.sm64_set_mario_forward_velocity(id,48);
                Native.sm64_set_mario_velocity(id,48,-4,0);
                initialized.Clear();Policy(equipped,true);
                Tick(new SM64MarioInputs{camLookX=-1,stickY=1});
                Check(equipped?state.action==Freefall:state.action==BackBonk||state.action==SoftBonk,"Ceiling-overlap bonk follows badge policy");
                Check(!(bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"Ceiling-only contact grants no wall kick");
            }
            Policy(false,true,false);
            Native.sm64_set_mario_position(id,-500,650,0);
            Native.sm64_set_mario_action(id,Jump);
            Native.sm64_set_mario_forward_velocity(id,0);
            Native.sm64_set_mario_velocity(id,0,50,0);
            initialized.Clear();Policy(true,true);
            for(int i=0;i<3;i++)Tick(default(SM64MarioInputs));
            Check(state.action==Jump,"Ordinary upward head bump retains its normal jump action");
            Check(state.vy<=0,"Head bump stops upward movement");
            Check(!initialized.Contains(BackBonk)&&!initialized.Contains(SoftBonk),"Ordinary head bump does not manufacture stun");
        }
        finally{Native.sm64_surface_object_delete(ceiling);}
    }
    static void CheckWingsPriority(Assembly companion)
    {
        Collide(Jump,true,true);
        var plugin=companion.GetType("HollowMario.Plugin",true);
        var owner=FormatterServices.GetUninitializedObject(plugin);
        typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,new IntPtr(1));
        object logger=Activator.CreateInstance(plugin.GetField("Log",BindingFlags.NonPublic|BindingFlags.Instance).FieldType,new object[]{"bonk-jump-priority"});
        Set(owner,"Log",logger);
        var follow=Activator.CreateInstance(companion.GetType("HollowMario.KnightResetFollow",true),
            BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{logger,typeof(MarioController),200},null);
        Set(owner,"knightFollow",follow);plugin.GetField("Instance",Hidden).SetValue(null,owner);
        var hero=(HeroController)FormatterServices.GetUninitializedObject(typeof(HeroController));
        var data=(PlayerData)FormatterServices.GetUninitializedObject(typeof(PlayerData));
        Set(hero,"playerData",data);Set(hero,"cState",new HeroControllerStates());data.hasDoubleJump=true;
        var actor=(MarioController)FormatterServices.GetUninitializedObject(typeof(MarioController));
        actor.hero=hero;actor.MarioId=id;actor.State=state;MarioController.Instance=actor;
        try
        {
            var wings=companion.GetType("HollowMario.DoubleJump",true);
            wings.GetMethod("Arm",Hidden).Invoke(null,new object[]{typeof(Native)});
            wings.GetMethod("Forget",Hidden).Invoke(null,null);
            var prefix=wings.GetMethod("TickPrefix",Hidden);
            prefix.Invoke(null,new object[]{new object[]{id,default(SM64MarioInputs),state,buffers}});
            var input=new SM64MarioInputs{buttonA=1};
            prefix.Invoke(null,new object[]{new object[]{id,input,state,buffers}});
            Check(!(bool)typeof(HeroController).GetField("doubleJumped",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(hero),"Wings charge is preserved for the bonk wall kick");
            Tick(input);
            Check(state.action==WallKick,"Jump passes through the production Wings prefix into a native wall kick");
        }
        finally{MarioController.Instance=null;plugin.GetField("Instance",Hidden).SetValue(null,null);}
    }
    static void CheckContinuousContact()
    {
        int before=bonkSounds;
        Collide(Jump,true,true,height:8000);
        Check(bonkSounds>before,"Actual impact sound is observed");
        int impactSounds=bonkSounds;initialized.Clear();
        var pushing=new SM64MarioInputs{camLookX=-1,stickY=1};
        for(int i=0;i<90;i++)Tick(pushing);
        Check(bonkSounds==impactSounds,"Holding the wall does not replay bonk sounds");
        Check(!initialized.Contains(Freefall),"One contact does not repeatedly reset the fall action");
        Check(!(bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"Holding the wall cannot renew the kick window");
        pushing.buttonA=1;Tick(pushing);
        Check(state.action!=WallKick,"A late Jump while still touching the same wall cannot wall-kick");

        LeaveWall();Tick(default(SM64MarioInputs));Tick(default(SM64MarioInputs));
        Native.sm64_set_mario_position(id,0,500,0);Native.sm64_set_mario_action(id,Jump);
        Native.sm64_set_mario_faceangle(id,(float)(Math.PI*.5));Native.sm64_set_mario_forward_velocity(id,48);
        Native.sm64_set_mario_velocity(id,48,-4,0);initialized.Clear();pushing.buttonA=0;
        for(int i=0;i<12&&!initialized.Contains(Freefall);i++)Tick(pushing);
        Check(initialized.Contains(Freefall),"A genuinely separate wall impact can recover again");
        Tick(new SM64MarioInputs{buttonA=1});Check(state.action==WallKick,"Separate impacts retain the normal recovery kick");
    }
    static void CheckGroundPoundRecovery()
    {
        foreach(bool claws in new[]{false,true})
        {
            Collide(Pound,true,claws,height:2000);
            Check(state.action==Freefall,"An equipped ground-pound wall bonk returns to normal fall, Claw="+claws);
            Check(state.vy<=0,"Ground-pound recovery adds no upward boost");
            if(claws)
            {
                Tick(new SM64MarioInputs{buttonA=1});
                Check(state.action==WallKick,"The original ground-pound impact retains its normal Claw-gated kick");
            }
            Collide(Pound,false,claws,height:2000);
            Check(state.action==BackBonk,"An unequipped ground pound retains native wall-bonk recovery");
        }
        Collide(Pound,true,false,height:5000);
        Native.sm64_set_mario_action(id,Pound);initialized.Clear();
        for(int i=0;i<64&&!initialized.Contains(BackBonk);i++)Tick(default(SM64MarioInputs));
        Check(initialized.Contains(BackBonk)&&!initialized.Contains(Freefall),"Another pound into the same continuous wall contact cannot repeat the recovery");
    }
    static void CheckPoundAfterContact()
    {
        Collide(Jump,true,true,height:5000);
        Native.sm64_set_mario_action(id,Pound);initialized.Clear();
        for(int i=0;i<64&&!initialized.Contains(Freefall)&&!initialized.Contains(BackBonk);i++)Tick(default(SM64MarioInputs));
        Check(initialized.Contains(Freefall)&&!initialized.Contains(BackBonk),"A ground pound after the initial wall contact must also recover to ordinary fall");
        Check(!(bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"The pound recovery must not renew the already-used wall-kick opportunity");

        PoundAgain();
        Check(initialized.Contains(BackBonk)&&!initialized.Contains(Freefall),"Repeated pounds in this airtime cannot spam the experimental recovery");
        LeaveWall();Tick(default(SM64MarioInputs));Tick(default(SM64MarioInputs));
        PoundAgain();
        Check(initialized.Contains(BackBonk)&&!initialized.Contains(Freefall),"Leaving and returning to the wall does not refill the pound allowance");

        var eligibility=Assembly.LoadFrom(TestEnvironment.CompanionPath).GetType("HollowMario.NativeDamageEligibility",true);
        bool previous=(bool)eligibility.GetMethod("BeginConfirmedHit",Hidden).Invoke(null,null);
        try{Native.sm64_mario_take_damage(id,1,0,300,5000,0);}
        finally{eligibility.GetMethod("EndConfirmedHit",Hidden).Invoke(null,new object[]{previous});}
        PoundAgain();
        Check(initialized.Contains(BackBonk)&&!initialized.Contains(Freefall),"Damage cancels pending recovery without refilling the pound allowance");

        Native.sm64_set_mario_position(id,0,2,0);Native.sm64_set_mario_action(id,Freefall);
        Native.sm64_set_mario_forward_velocity(id,0);Native.sm64_set_mario_velocity(id,0,-4,0);
        Tick(default(SM64MarioInputs));
        Check((state.action&0x800u)==0,"The actor really lands before replenishing recovery");
        PoundAgain();
        Check(initialized.Contains(Freefall)&&!initialized.Contains(BackBonk),"Landing refills the single experimental pound recovery");
    }
    static void PoundAgain()
    {
        Native.sm64_set_mario_position(id,170,5000,0);Native.sm64_set_mario_action(id,Pound);initialized.Clear();
        for(int i=0;i<64&&!initialized.Contains(Freefall)&&!initialized.Contains(BackBonk);i++)Tick(default(SM64MarioInputs));
    }
    static void CheckPoundAtLedge()
    {
        uint ledge=Load(new[]{
            Triangle(new[]{-300,600,-1000},new[]{-300,600,1000},new[]{100,600,1000}),
            Triangle(new[]{-300,600,-1000},new[]{100,600,1000},new[]{100,600,-1000}),
            Triangle(new[]{-300,570,-1000},new[]{100,570,1000},new[]{-300,570,1000}),
            Triangle(new[]{-300,570,-1000},new[]{100,570,-1000},new[]{100,570,1000}),
            Triangle(new[]{-300,570,-1000},new[]{-300,570,1000},new[]{-300,600,1000}),
            Triangle(new[]{-300,570,-1000},new[]{-300,600,1000},new[]{-300,600,-1000})});
        try
        {
            Policy(false,true,false);
            Native.sm64_set_mario_position(id,-500,472,0);Native.sm64_set_mario_action(id,Dive);
            Native.sm64_set_mario_faceangle(id,(float)(Math.PI*.5));Native.sm64_set_mario_forward_velocity(id,48);
            Native.sm64_set_mario_velocity(id,48,0,0);initialized.Clear();Policy(true,true);
            var input=new SM64MarioInputs{camLookX=-1,stickY=1};
            for(int i=0;i<12&&!initialized.Contains(Freefall);i++)Tick(input);
            Check(initialized.Contains(Freefall),"Real shallow ledge contact opens the normal badge recovery; action="+state.action.ToString("X")+", position="+state.px+","+state.py+", requests="+string.Join(",",initialized.ConvertAll(a=>a.ToString("X"))));
            Native.sm64_set_mario_action(id,Pound);initialized.Clear();
            for(int i=0;i<64&&!initialized.Contains(Freefall)&&!initialized.Contains(BackBonk);i++)Tick(default(SM64MarioInputs));
            Check(initialized.Contains(Freefall)&&!initialized.Contains(BackBonk),"Pounding close to the ledge must recover, action="+state.action.ToString("X")+", position="+state.px+","+state.py);
        }
        finally{Native.sm64_surface_object_delete(ledge);}
    }
    static void CheckDamageStates(Assembly companion)
    {
        foreach(uint hurt in new[]{BackBonk,0x010208B1u,0x010208B2u,0x010208B3u,SoftBonk,0x010208B8u,0x010208BDu,0x010208BEu})
        {
            Policy(false,true,false);Native.sm64_set_mario_position(id,170,500,0);
            Native.sm64_set_mario_action(id,Freefall);Native.sm64_set_mario_action(id,hurt);
            Native.sm64_set_mario_faceangle(id,(float)(Math.PI*.5));Native.sm64_set_mario_forward_velocity(id,48);
            Native.sm64_set_mario_velocity(id,48,-4,0);initialized.Clear();Policy(true,true);
            for(int i=0;i<6;i++)Tick(new SM64MarioInputs{camLookX=-1,stickY=1,buttonA=(byte)(i%2)});
            Check((state.action&0x20000u)!=0&&!initialized.Contains(Freefall)&&!initialized.Contains(WallKick),"Wall contact cannot cancel damage/throw/stumble "+hurt.ToString("X"));
            Check(!(bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"Damage movement cannot arm a recovery kick");
        }
        Collide(Jump,true,true);
        Check((bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"A normal impact starts its kick window");
        var eligibility=companion.GetType("HollowMario.NativeDamageEligibility",true);
        bool previous=(bool)eligibility.GetMethod("BeginConfirmedHit",Hidden).Invoke(null,null);
        try{Native.sm64_mario_take_damage(id,1,0,300,500,0);}
        finally{eligibility.GetMethod("EndConfirmedHit",Hidden).Invoke(null,new object[]{previous});}
        Check(!(bool)badge.GetProperty("Pending",Hidden).GetValue(null,null),"An accepted hit immediately cancels the old wall recovery");
        Tick(default(SM64MarioInputs));
        Check((state.action&0x20000u)!=0,"Confirmed enemy knockback remains active after the next tick");
    }
    static int Main(string[] args)
    {
        try
        {
            TestEnvironment.StartNative();
            var companion=Assembly.LoadFrom(TestEnvironment.CompanionPath);
            gates=companion.GetType("HollowMario.NativeMovementGates",true);
            badge=companion.GetType("HollowMario.NoBonk",true);
            gates.GetMethod("Install",Hidden).Invoke(null,new object[]{TestEnvironment.Library,GetModuleHandle(TestEnvironment.Library)});
            var setter=gates.GetField("originalAction",Hidden);
            originalSet=(Delegate)setter.GetValue(null);
            setter.SetValue(null,Delegate.CreateDelegate(setter.FieldType,typeof(NoBonkNativeChecks).GetMethod("Capture",Hidden)));
            var symbols=(Dictionary<string,IntPtr>)companion.GetType("HollowMario.NativeSymbols",true).GetMethod("Functions",Hidden)
                .Invoke(null,new object[]{TestEnvironment.Library,GetModuleHandle(TestEnvironment.Library),new[]{"play_sound"}});
            var soundHook=new NativeDetour(symbols["play_sound"],soundCallback,new NativeDetourConfig{ManualApply=true});
            originalSound=soundHook.GenerateTrampoline<Sound>();soundHook.Apply();
            string[] names={"position","normal","color","uv"};object raw=buffers;
            for(int i=0;i<pins.Length;i++)
            {
                pins[i]=GCHandle.Alloc(new float[i==3?6144:9216],GCHandleType.Pinned);
                typeof(SM64MarioGeometryBuffers).GetField(names[i]).SetValue(raw,pins[i].AddrOfPinnedObject());
            }
            buffers=(SM64MarioGeometryBuffers)raw;
            uint world=World();id=Native.sm64_mario_create(0,2,0);
            Check(id>=0,"Actual floor supports the test actor");
            try
            {
                foreach(uint start in new[]{Jump,Dive,LongJump})
                foreach(bool claws in new[]{false,true})
                {
                    Collide(start,true,claws);
                    Check(state.action==Freefall,"Equipped wall bonk immediately falls from "+start.ToString("X")+", Claw="+claws);
                    Check(state.vy<=0,"Bonk recovery adds no upward launch");
                    Check(!initialized.Exists(a=>a==AirHitWall||a==BackBonk||a==SoftBonk),"Native stun initializer is bypassed");
                    Tick(new SM64MarioInputs{buttonZ=1});
                    Check(state.action==Pound,"Next input can start the normal ground pound");

                    Collide(start,true,claws);
                    Tick(new SM64MarioInputs{buttonB=1});
                    Check(initialized.Contains(Dive),"Next input starts the normal airborne attack, including when it hits the same wall again");

                    Collide(start,false,claws);
                    Check(state.action==AirHitWall||state.action==BackBonk||state.action==SoftBonk,"Unequipped badge retains native bonking");
                }
                Collide(Dive,true,false,false);
                Check(state.action==BackBonk,"Inactive gameplay scope retains native bonking");

                foreach(uint start in new[]{Jump,Dive,LongJump})
                {
                    Collide(start,true,true);
                    Tick(new SM64MarioInputs{buttonA=1});
                    Check(state.action==WallKick&&state.vy>40&&state.vx<0,"Fresh Jump wall-kicks away from "+start.ToString("X"));

                    Collide(start,true,true,jumpOnContact:true);
                    Check(state.action==WallKick,"Jump at collision retains immediate wall kick");

                    Collide(start,true,false);
                    Tick(new SM64MarioInputs{buttonA=1});
                    Check(state.action!=WallKick,"Claw remains required for the wall kick");
                }
                Collide(Jump,true,true,holdJump:true);
                Tick(new SM64MarioInputs{buttonA=1});
                Check(state.action==Freefall,"Holding Jump through contact does not automatically kick");
                Tick(default(SM64MarioInputs));Tick(new SM64MarioInputs{buttonA=1});
                Check(state.action==WallKick,"Release and repress during the recovery window wall-kicks");

                foreach(int delay in new[]{3,9})
                {
                    Collide(Jump,true,true);LeaveWall();
                    for(int i=0;i<delay;i++)Tick(default(SM64MarioInputs));
                    Tick(new SM64MarioInputs{buttonA=1});
                    Check(delay==3?state.action==WallKick:state.action==Freefall,"Wall-kick grace window and expiry are finite");
                }
                Collide(Jump,true,true);LeaveWall();
                Native.sm64_set_mario_action(id,BackBonk);
                Tick(new SM64MarioInputs{buttonA=1});
                Check(state.action==BackBonk,"A subsequent knockback cancels the wall-kick window");

                CheckCeilings();
                CheckWingsPriority(companion);
                CheckContinuousContact();
                CheckGroundPoundRecovery();
                CheckPoundAtLedge();
                CheckPoundAfterContact();
                CheckDamageStates(companion);

                Policy(true,false);
                Native.sm64_set_mario_position(id,0,500,0);
                Native.sm64_set_mario_action(id,Dive);
                initialized.Clear();
                Native.sm64_mario_take_damage(id,1,0,100,500,0);
                Check(initialized.Exists(a=>a==BackBonk||a==0x010208B1u||a==0x010208B2u||a==0x010208B3u),"Real enemy damage still enters native knockback");
                Check(!initialized.Contains(Freefall),"Damage does not become a badge recovery");
            }
            finally
            {
                Policy(false,false,false);
                Native.sm64_mario_delete(id);Native.sm64_surface_object_delete(world);
                gates.GetMethod("Dispose",Hidden).Invoke(null,null);
                soundHook.Dispose();
                foreach(var pin in pins)if(pin.IsAllocated)pin.Free();
            }
            Console.WriteLine("PASS native recovery/kicks, Wings priority and ceilings; 90-tick sustained wall contact without repeated sounds/actions/kick refresh; separate-impact reset; knockback/throws/stumble exclusions and accepted-hit cancellation. No game launched.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
