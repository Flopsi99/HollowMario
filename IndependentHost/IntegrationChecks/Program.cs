using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using HollowMario.ApiRuntime;
using HollowMario.Host;
using MarioHollow;
using UnityEngine;

internal static class Program
{
    private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static void Check(bool good,string why){if(!good)throw new Exception(why);}
    private static void Required(Type type,string fields,string methods)
    {
        foreach(string field in fields.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))Check(type.GetField(field,Any)!=null,type.Name+" field "+field);
        foreach(string method in methods.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))Check(type.GetMethod(method,Any)!=null,type.Name+" method "+method);
    }
    private static MethodInfo Method(Assembly assembly,string type,string name)=>assembly.GetType("HollowMario."+type,true).GetMethod(name,Any);
    private static HarmonyMethod Hook(Assembly assembly,string type,string name)=>new HarmonyMethod(Method(assembly,type,name));
    private static void RenderContract(GeometryFrame geometry,int triangles,byte[] atlas)
    {
        int outward=0,inward=0,eyes=0;
        Vector3 Point(int index)=>MarioMath.World(geometry.Positions[index*3],geometry.Positions[index*3+1],geometry.Positions[index*3+2]);
        for(int t=0;t<triangles;t++)
        {
            int first=t*3;Vector3 a=Point(MeshProjection.Index(first)),b=Point(MeshProjection.Index(first+1)),c=Point(MeshProjection.Index(first+2));
            Vector3 normal=Vector3.zero;
            for(int v=0;v<3;v++){int n=(first+v)*3;normal+=MeshProjection.Normal(geometry.Normals[n],geometry.Normals[n+1],geometry.Normals[n+2]);}
            float agreement=Vector3.Dot(Vector3.Cross(b-a,c-a),normal);if(agreement>.000001f)outward++;if(agreement<-.000001f)inward++;
            float u=(geometry.UVs[first*2]+geometry.UVs[first*2+2]+geometry.UVs[first*2+4])/3;
            float vcoord=(geometry.UVs[first*2+1]+geometry.UVs[first*2+3]+geometry.UVs[first*2+5])/3;
            if(u==1&&vcoord==1)continue;
            Vector2 mapped=MeshProjection.AtlasUV(u,vcoord,3);
            Check(Math.Abs(mapped.x*708-u*704)<.0001&&Math.Abs(mapped.y*1024-(3*64+vcoord*64))<.0001,"ROM texture UVs retain the correct tile and palette row.");
            int x=(int)(u*704),y=(int)(vcoord*64);
            if(x>=320&&x<576&&y>=0&&y<64&&atlas[(y*704+x)*4+3]>0)eyes++;
        }
        Check(outward>100&&outward>inward*5,"Z reflection, winding and normals preserve outward-facing native model surfaces: "+outward+" outward / "+inward+" inward.");
        Check(eyes>0,"Real model geometry reaches nontransparent ROM eye decals through the renderer's UV conversion.");
        var stageFeet=new Vector3(-63,-63,-63);var seat=new Vector3(14,20,.1f);var vertex=stageFeet+new Vector3(.2f,1,.5f);
        Vector3 placed=MeshProjection.Layer(vertex,stageFeet)+seat-stageFeet;
        Check(Math.Abs(placed.z-(seat.z))<.00001f&&Math.Abs(placed.x-(seat.x+.2f))<.00001f,"Every model vertex uses the host sprite plane, including remote-stage bench presentation.");
        Console.WriteLine("PASS real ROM geometry through render conversion: outward winding/normals, eye-decal UVs and foreground bench placement. GPU shader appearance still needs playtesting.");
    }
    private static void Bindings(Assembly companion)
    {
        Required(typeof(MarioController),"Instance,MarioId,State,PrevState,MarkPosition,hero,feetOffset,body,box,collision,Wanted,Renderer,Attacks","Tick,Update,LateUpdate,Activate,Deactivate,Recenter,KnightFree,SetWanted,OnKnightHit,Awake");
        Required(typeof(MarioRenderer),"Buffers,mesh,normals,uvs,MeshRenderer,Object","TakeTick,Render,Dispose");
        Required(typeof(WorldCollision),"tracked,nextScan,Objects","Create,Tick,Rebuild");
        Required(typeof(WorldCollision.Entry),"Id,SeenAt","");
        Check(typeof(WorldCollision.Entry).GetField("SeenAt").FieldType.FullName=="UnityEngine.Vector3","Moving-wall anchor is a world position, not a scan timestamp.");
        Check(typeof(WorldCollision).GetField("Objects").FieldType==typeof(int),"Void guard reads the native object count.");
        Required(typeof(Sm64),"Atlas,romHandle","");Required(typeof(Native),"Sync","");
        Check(Marshal.SizeOf(typeof(SM64MarioState))==60&&Marshal.SizeOf(typeof(SM64MarioInputs))==20&&Marshal.SizeOf(typeof(SM64Surface))==44,"Facade ABI sizes.");
        Check(Sheets.Attacks.Single(r=>r.Id=="jump_kick").Action==0x018008AC&&Sheets.Attacks.Single(r=>r.Id=="slide_kick").Action==0x018008AA,"Attack rows use the public upstream jump/slide kick IDs, not adjacent actions.");
        var harmony=new Harmony("hollowmario.independent.contract-checks");
        Method(companion,"ApiArgumentAdapter","Install").Invoke(null,null);
        var tick=typeof(Native).GetMethod("sm64_mario_tick");
        try
        {
            harmony.Patch(typeof(MarioController).GetMethod("Tick"),prefix:Hook(companion,"Plugin","TickPrefix"),postfix:Hook(companion,"Plugin","TickPostfix"));
            Check(Harmony.GetPatchInfo(typeof(MarioController).GetMethod("Tick")).Prefixes.Count==1,"The ownership-stamped gameplay tick hooks register with their actual prefix/postfix signatures.");
            foreach(string policy in new[]{"WingCap","Swimming","DoubleJump","PoundLongJump"})
            {
                string name=policy=="WingCap"?"BeforeTick":policy=="Swimming"?"WaterInputs":"TickPrefix";
                var wrapped=(MethodInfo)Method(companion,"ApiArgumentAdapter","Wrap").Invoke(null,new object[]{tick,Method(companion,policy,name)});
                harmony.Patch(tick,prefix:new HarmonyMethod(wrapped));
            }
            foreach(string policy in new[]{"MapFallFlourish","MushroomFallGuard","NativeMovementGates","QuickSlash"})
                harmony.Patch(tick,prefix:Hook(companion,policy,"BeforeTick"),
                    finalizer:Hook(companion,policy,policy=="NativeMovementGates"?"AfterTick":policy=="QuickSlash"?"FinishTick":"Finish"));
            BridgeContracts.Install(companion);
            Check(Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName("HeroController"),"Awake")).Prefixes.Any(p=>p.owner=="gg.hollowmario.independent-contracts"),"Visibility baseline is captured before Hero awake/presentation hiding.");
            Check(Harmony.GetPatchInfo(Method(companion,"KnightResetFollow","ShareMarioSpeed")).Prefixes.Any(p=>p.owner=="gg.hollowmario.independent-contracts"),"Native proxy speed cannot be integrated a second time by Unity.");
            Check(Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.GetVelocity2d"),"DoGetVelocity")).Postfixes.Any(p=>p.owner=="gg.hollowmario.independent-contracts"),"Native speed is exposed to the game's actual managed velocity reader without integrating the body twice.");
            foreach(string phase in new[]{"Enter","Stay","Exit"})
                Check(Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName("CameraLockArea"),"OnTrigger"+phase+"2D")).Prefixes.Any(p=>p.owner=="gg.hollowmario.independent-contracts"),"Camera area excludes the support pad while preserving native contact handling on actual "+phase+" callback.");
            foreach(string actionType in new[]{"Trigger2dEvent","Trigger2dEventLayer"})foreach(string phase in new[]{"Enter","Stay","Exit"})
                Check(Harmony.GetPatchInfo(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions."+actionType),"DoTrigger"+phase+"2D")).Prefixes.Any(p=>p.owner=="gg.hollowmario.independent-contracts"),"Support sensor is filtered at actual FSM trigger entry: "+actionType+phase);
            Check(Harmony.GetPatchInfo(tick).Prefixes.Count==8,"All eight movement gates attach to the new native wrapper.");
            harmony.Patch(typeof(MarioController).GetMethod("SetWanted"),postfix:Hook(companion,"MushroomFallGuard","Wanted"));
            Method(companion,"Plugin","BindIndependentAttackGeometry").Invoke(null,new object[]{typeof(MarioAttacks)});
            Check(MarioAttacks.CandidateBounds!=null,"Host binds the shared move geometry without a second charm scale.");
            harmony.Patch(typeof(MarioAttacks).GetMethod("Strike"),
                prefix:Hook(companion,"Plugin","StrikePrefix"),postfix:Hook(companion,"Plugin","StrikePostfix"),transpiler:Hook(companion,"NailHitDirection","BuildHits"));
            harmony.Patch(typeof(MarioAttacks).GetMethod("Strike"),transpiler:Hook(companion,"NailCharmModifiers","BuildHits"));
        }
        finally{BridgeContracts.Dispose();harmony.UnpatchSelf();new Harmony("gg.melty.hollowmario.api-arguments").UnpatchSelf();}
        Console.WriteLine("PASS companion field/lifecycle contracts, eight real movement hooks and actual nail/charm attack patches on new host.");
    }
    private static void NativeRound(string library,string rom,Assembly companion)
    {
        typeof(Native).GetMethod("Start",Any).Invoke(null,new object[]{library,rom});
        try
        {
            var engine=(Engine)typeof(Native).GetField("Engine",Any).GetValue(null);
            LedgeRegression.Run(companion,library);
            engine.SetWorld(new[]{new Surface{Terrain=1,Ax=-8000,Az=-8000,Bx=-8000,Bz=8000,Cx=8000,Cz=8000},new Surface{Terrain=1,Ax=-8000,Az=-8000,Bx=8000,Bz=8000,Cx=8000,Cz=-8000}});
            using(var memory=new GeometryFrame())
            {
                var pins=new[]{GCHandle.Alloc(memory.Positions,GCHandleType.Pinned),GCHandle.Alloc(memory.Normals,GCHandleType.Pinned),GCHandle.Alloc(memory.Colours,GCHandleType.Pinned),GCHandle.Alloc(memory.UVs,GCHandleType.Pinned)};
                try
                {
                    var buffers=new SM64MarioGeometryBuffers{position=pins[0].AddrOfPinnedObject(),normal=pins[1].AddrOfPinnedObject(),color=pins[2].AddrOfPinnedObject(),uv=pins[3].AddrOfPinnedObject()};
                    int id=Native.sm64_mario_create(0,100,0);var input=new SM64MarioInputs{camLookX=-1,stickY=.8f};var state=new SM64MarioState();
                    for(int i=0;i<50;i++)Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
                    Check(state.px>100&&Math.Abs(state.py)<2&&buffers.numTrianglesUsed>0,"New compatibility wrapper drives real native movement, floor collision and geometry.");
                    RenderContract(memory,buffers.numTrianglesUsed,engine.Atlas);
                    Method(companion,"MapFallFlourish","Configure").Invoke(null,new object[]{typeof(Native),new BepInEx.Logging.ManualLogSource("landing-scope-check")});
                    input=default(SM64MarioInputs);
                    Native.sm64_set_mario_action(id,0x0C400201);Native.sm64_set_mario_velocity(id,0,0,0);
                    for(int step=0;step<6;step++){
                        int prior=BridgeContracts.BeginLandingAnimation();
                        try{Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.animID==0x96,"Landing scope selects its crouch get-up animation.");}
                        finally{BridgeContracts.EndLandingAnimation(prior);}
                    }
                    Check(!(bool)companion.GetType("HollowMario.MapFallFlourish",true).GetField("forced",Any).GetValue(null),"Returning to no previous override clears the global forced-animation flag.");
                    int outer=(int)Method(companion,"MapFallFlourish","PushCinematicAnimation").Invoke(null,new object[]{0x27});
                    int nested=BridgeContracts.BeginLandingAnimation();
                    try{Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.animID==0x96,"Nested landing selects crouch get-up pose.");}
                    finally{BridgeContracts.EndLandingAnimation(nested);}
                    try{Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.animID==0x27,"Landing restores an existing parent animation as well as the empty scope.");}
                    finally{Method(companion,"MapFallFlourish","PopCinematicAnimation").Invoke(null,new object[]{outer});}

                    for(int step=0;step<180;step++){Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.animID!=0x96&&state.action!=0x0C000203,"Idle neither keeps the landing pose nor immediately sleeps after the landing scope.");}
                    Native.sm64_set_mario_action(id,0x03000880);Native.sm64_set_mario_velocity(id,0,40,0);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
                    Check(state.animID!=0x96,"Jump animation is free after landing override cleanup.");
                    Native.sm64_mario_delete(id);id=Native.sm64_mario_create(0,100,0);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Check(state.animID!=0x3A,"Reused actor/room handover cannot inherit the landing override.");
                    Method(companion,"MapFallFlourish","Dispose").Invoke(null,null);

                    Native.sm64_set_mario_position(id,0,800,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,0x018008AC);
                    bool kick=false;for(int i=0;i<8;i++){Native.sm64_mario_tick(id,ref input,ref state,ref buffers);kick|=state.action==Sheets.Attacks.Single(r=>r.Id=="jump_kick").Action;}
                    Check(kick,"The real native aerial kick maps to an enabled damaging row.");
                    input=default(SM64MarioInputs);Native.sm64_set_mario_position(id,0,10,0);Native.sm64_set_mario_velocity(id,0,0,0);Native.sm64_set_mario_action(id,0x0C400201);
                    Native.sm64_mario_tick(id,ref input,ref state,ref buffers);Native.sm64_mario_take_damage(id,1,0,100,0,0);Native.sm64_mario_tick(id,ref input,ref state,ref buffers);
                    Check((state.action&0x20000)!=0&&Math.Abs(state.vx)>.1f,"Accepted native damage actually enters protected knockback and produces movement.");
                    Native.sm64_set_mario_action_arg(id,0x03000880,0);Native.sm64_set_mario_forward_velocity(id,10);Native.sm64_set_mario_faceangle(id,.8f);Native.sm64_set_mario_anim_frame(id,0);Native.sm64_set_mario_state(id,state.flags);
                    Native.sm64_mario_delete(id);Check(Native.sm64_mario_create(0,100,0)==id,"Facade actors are safely retained/reused.");Native.sm64_mario_delete(id);
                    var ctor=companion.GetType("HollowMario.OpeningMarioPose",true).GetConstructors(Any).Single();
                    using(var pose=(IDisposable)ctor.Invoke(new object[]{typeof(Native),library,0f,true}))
                    {
                        MethodInfo tick=pose.GetType().GetMethod("Tick",Any);
                        for(int i=0;i<15;i++)buffers=(SM64MarioGeometryBuffers)tick.Invoke(pose,new object[]{buffers,true});
                        Check(buffers.numTrianglesUsed>0,"Our existing private presentation actor works against replacement exports and surface-object stage.");
                    }
                    engine.Audio(new short[8192]);
                    for(int run=0;run<2;run++)
                    {
                        int pounder=Native.sm64_mario_create(0,800,0);Native.sm64_set_mario_action(pounder,0x008008A9);
                        bool descent=false,landed=false;input=default(SM64MarioInputs);
                        for(int i=0;i<90;i++)
                        {
                            Native.sm64_mario_tick(pounder,ref input,ref state,ref buffers);
                            if(state.action==0x0080023C){landed=true;break;}
                            Check(state.action==0x008008A9,"Reused presentation actor must not cancel pound startup into an unrelated fall: "+state.action.ToString("X"));
                            descent|=state.animID==0x3D;
                        }
                        Check(descent&&landed,"Pound startup/descent/landing survives presentation pooling and repeat activation.");Native.sm64_mario_delete(pounder);
                    }
                    float[] PoundShape(bool sideFlip,float angle)
                    {
                        int actor=Native.sm64_mario_create(0,800,0);
                        var controls=default(SM64MarioInputs);var poseState=default(SM64MarioState);
                        Native.sm64_mario_tick(actor,ref controls,ref poseState,ref buffers);Native.sm64_set_mario_position(actor,0,800,0);Native.sm64_set_mario_faceangle(actor,angle);
                        if(sideFlip){Native.sm64_set_mario_action(actor,0x01000887);Native.sm64_mario_tick(actor,ref controls,ref poseState,ref buffers);}
                        else Native.sm64_set_mario_action(actor,0x008008A9);
                        controls.buttonZ=1;Native.sm64_mario_tick(actor,ref controls,ref poseState,ref buffers);
                        Check(poseState.action==0x008008A9,"Side flip enters pound without losing the action.");
                        var vertices=new float[buffers.numTrianglesUsed*9];Marshal.Copy(buffers.position,vertices,0,vertices.Length);
                        for(int i=0;i<vertices.Length;i+=3){vertices[i]-=poseState.px;vertices[i+1]-=poseState.py;vertices[i+2]-=poseState.pz;}
                        Native.sm64_mario_delete(actor);return vertices;
                    }
                    foreach(float direction in new[]{-1.5707963f,1.5707963f})
                    {
                        var reference=PoundShape(false,direction);var fromFlip=PoundShape(true,direction);
                        Check(reference.Length==fromFlip.Length,"Side-flip handover retains the full pound mesh.");
                        for(int i=0;i<reference.Length;i++)Check(Math.Abs(reference[i]-fromFlip[i])<.1f,"Side-flip visual half-turn cannot leak into pound startup, in either direction.");
                    }
                    Console.WriteLine("PASS native aerial-kick row, real damage knockback, repeated pound startup/descent/landing and left/right side-flip-to-pound geometry.");
                    var faces=new[]{new SM64Surface{terrain=1,v0x=-300,v0z=-300,v1x=-300,v1z=300,v2x=300,v2z=300},
                        new SM64Surface{terrain=1,v0x=-300,v0z=-300,v1x=300,v1z=300,v2x=300,v2z=-300}};
                    var floorPin=GCHandle.Alloc(faces,GCHandleType.Pinned);
                    try
                    {
                        var platform=new SM64SurfaceObject{transform=new SM64ObjectTransform{py=200},surfaceCount=2,surfaces=floorPin.AddrOfPinnedObject()};
                        uint platformId=Native.sm64_surface_object_create(ref platform);
                        int rider=Native.sm64_mario_create(0,350,0);input=default(SM64MarioInputs);
                        for(int i=0;i<30;i++)Native.sm64_mario_tick(rider,ref input,ref state,ref buffers);
                        Check(Math.Abs(state.py-200)<2,"Actor lands on replacement-managed dynamic surfaces.");
                        var moved=new SM64ObjectTransform{py=240};Native.sm64_surface_object_move(platformId,ref moved);
                        for(int i=0;i<15;i++)Native.sm64_mario_tick(rider,ref input,ref state,ref buffers);
                        Check(Math.Abs(state.py-240)<2,"Actor remains on a moving platform.");
                        Native.sm64_surface_object_delete(platformId);
                        for(int i=0;i<35;i++)Native.sm64_mario_tick(rider,ref input,ref state,ref buffers);
                        Check(Math.Abs(state.py)<2,"Removing streamed support safely returns the actor to remaining terrain.");Native.sm64_mario_delete(rider);
                    }
                    finally{floorPin.Free();}
                }
                finally{foreach(var pin in pins)pin.Free();}
            }
            Console.WriteLine("PASS real native simulation through replacement facade, actor reuse, existing cinematic actor/stage and audio after release.");
        }
        finally{typeof(Native).GetMethod("Stop",Any).Invoke(null,null);}
    }
    private static int Main(string[] args)
    {
        try
        {
            string package=Path.GetFullPath(args[0]);ApiContext.Initialize(package,args[1],(level,text)=>Console.WriteLine(text));
            Check(string.Equals(Path.GetFullPath(typeof(Native).Assembly.Location),Path.Combine(package,"MarioHollow-native","HollowMario.Host.Integration.dll"),StringComparison.OrdinalIgnoreCase),"Native integration must come from the delivered package, never a project-reference copy next to the check executable.");
            var companion=Assembly.LoadFrom(Path.Combine(package,"HollowMario.dll"));
            Bindings(companion);NativeRound(Path.GetFullPath(args[2]),args[3],companion);
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="MarioHollow"),"Original assembly never loaded.");
            Console.WriteLine("PASS independent integration checks without game window or original bridge assembly.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
