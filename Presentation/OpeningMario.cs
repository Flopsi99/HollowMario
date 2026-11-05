using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Only the actual new-game tutorial sequence arms this replacement. Saved
    // games, later room entries and ordinary heavy landings retain their owners.
    internal static class OpeningMario
    {
        private const float LandingPauseScale=.5f;
        private static Type controllerType,nativeType,readyType,rendererType,mathType,heroType;
        private static ManualLogSource log;
        private static OpeningMarioPose pose;
        private static object renderer;
        private static Component hero;
        private static MeshRenderer knight,model;
        private static GameObject modelObject;
        private static BoxCollider2D box;
        private static FieldInfo buffers;
        private static MethodInfo takeTick,render,dispose;
        private static bool pending,landed,showingLanding,oldHidden,returned,conversation,returning;
        private static volatile bool presenting;
        private static float conversationStartFace,conversationFace;
        internal static bool Presenting=>presenting;
        internal static bool OwnsSequence=>pending||presenting;
        internal static Component Hero=>hero;
        internal static bool ReadyForDialogue=>presenting&&returned&&landed&&pose!=null
            &&Convert.ToUInt32(CapAccess.Read(pose.State,"action"))==0x0C400201u;
        private static float accumulator;
        private static int warnings;
        internal static void Arm(Harmony harmony,Type controller,Type native,ManualLogSource logger)
        {
            controllerType=controller;nativeType=native;log=logger;
            readyType=controller.Assembly.GetType("MarioHollow.Sm64",true);
            rendererType=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            mathType=controller.Assembly.GetType("MarioHollow.MarioMath",true);
            heroType=AccessTools.TypeByName("HeroController");
            buffers=AccessTools.Field(rendererType,"Buffers");
            takeTick=AccessTools.Method(rendererType,"TakeTick");render=AccessTools.Method(rendererType,"Render");
            dispose=AccessTools.Method(rendererType,"Dispose");
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("GameManager"),"OnWillActivateFirstLevel"),
                postfix:new HarmonyMethod(typeof(OpeningMario),nameof(NewGame)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(OpeningMario),nameof(BeforeState)),
                postfix:new HarmonyMethod(typeof(OpeningMario),nameof(AfterState)));
            harmony.Patch(AccessTools.Method(controller,"Update"),prefix:new HarmonyMethod(typeof(OpeningMario),nameof(BeforeUpdate)));
            harmony.Patch(render,postfix:new HarmonyMethod(typeof(OpeningMario),nameof(AfterPlayableRender)));
            harmony.Patch(AccessTools.Method(controller.Assembly.GetType("MarioHollow.Plugin",true),"OnGUI"),
                prefix:new HarmonyMethod(typeof(OpeningMario),nameof(EngineOverlay)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.Wait"),"OnEnter"),
                postfix:new HarmonyMethod(typeof(OpeningMario),nameof(AfterOpeningWait)));
        }
        private static void NewGame()
        {
            LakituDialogue.Cancel();Stop();
            object controller=CapAccess.Read(controllerType,"Instance");
            if(controller!=null)
            {
                // A persistent gameplay avatar/character choice can survive a
                // return to the menu. Every NEW game begins with a fresh opening.
                if(Convert.ToInt32(CapAccess.Read(controller,"MarioId"))>=0)
                    AccessTools.Method(controllerType,"Deactivate").Invoke(controller,null);
                CapAccess.Write(controller,"Wanted",true);
            }
            pending=true;
        }
        internal static bool HoldForFirstRoom(string scene,bool armed)
            =>scene=="Opening_Sequence"||(armed&&scene!="Tutorial_01"&&scene!="Menu_Title"&&scene!="Quit_To_Menu");
        internal static bool IsOpeningState(string scene,string owner,string fsm)
            =>scene=="Tutorial_01"&&owner=="Initial Fall Impact"&&fsm=="Control";
        private static bool Match(object state)
        {
            if(!pending||UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Tutorial_01")return false;
            object fsm=CapAccess.Read(state,"Fsm");
            return CapAccess.Read(fsm,"GameObject") is GameObject owner
                &&IsOpeningState("Tutorial_01",owner.name,Convert.ToString(CapAccess.Read(fsm,"Name")));
        }
        private static void BeforeState(object __instance)
        {
            if(Match(__instance)&&Convert.ToString(CapAccess.Read(__instance,"Name"))=="Land")landed=true;
        }
        private static void AfterOpeningWait(object __instance)
        {
            object state=CapAccess.Read(__instance,"State");
            string name=Convert.ToString(CapAccess.Read(state,"Name"));
            if((name!="Land"&&name!="Flock")||!Match(state))return;
            ShortenLandingPause(__instance);
        }
        internal static void ShortenLandingPause(object wait)
        {
            float duration=CapAccess.Number(CapAccess.Read(wait,"time"),"Value");
            if(duration<=0||float.IsNaN(duration)||float.IsInfinity(duration))return;
            // Start halfway through these two opening-only waits. Support both
            // native clock modes without changing shared duration data or the
            // landing/get-up animations. OnEnter resets both clocks every run.
            float skipped=duration*(1-LandingPauseScale);
            CapAccess.Write(wait,"timer",skipped);
            CapAccess.Write(wait,"startTime",CapAccess.Number(wait,"startTime")-skipped);
        }
        private static void AfterState(object __instance)
        {
            if(Match(__instance)&&Convert.ToString(CapAccess.Read(__instance,"Name"))=="Return Control")
            {returned=true;LakituDialogue.Queue();}
        }
        private static bool EngineOverlay()=>!CapAccess.Flag(readyType,"Ready")||DebugSettings.ShowEngineOverlay;
        internal static void BeginConversation()
        {conversation=true;conversationStartFace=conversationFace=CapAccess.Number(pose.State,"faceAngle");}
        internal static void TurnForConversation(float progress)
        {
            // Native yaw zero faces the camera; -45 degrees looks left toward it.
            conversationFace=Mathf.LerpAngle(conversationStartFace*Mathf.Rad2Deg,-45f,Mathf.SmoothStep(0,1,progress))*Mathf.Deg2Rad;
        }
        internal static void ReleaseToGameplay()
        {
            if(!presenting){Stop();return;}
            // Release the private actor for reuse, but keep its last drawn mesh.
            // Activation returns before gameplay's first Render, sometimes in a
            // different Update. Neither that gap nor a retry may reveal the Knight.
            try{pose?.Dispose();}
            finally{pose=null;pending=false;conversation=false;returning=true;}
        }
        private static void AfterPlayableRender(object __instance)
        {
            if(!returning||!ReferenceEquals(__instance,CapAccess.Read(CapAccess.Read(controllerType,"Instance"),"Renderer")))return;
            oldHidden=true; // The playable controller now owns hiding the Knight.
            Stop();
        }
        private static bool BeforeUpdate(object __instance)
        {
            string scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if(returning)
            {
                if(scene!="Tutorial_01"||!CapAccess.Flag(__instance,"Wanted")||hero==null
                    ||CapAccess.Flag(CapAccess.Read(hero,"cState"),"dead"))Stop();
                else if(knight!=null)knight.forceRenderingOff=true;
                return true;
            }
            // On later new games, the parked actor can be created even without
            // ground. Never let it activate behind the opening movie, including
            // before OnWillActivateFirstLevel arms the tutorial replacement.
            if(HoldForFirstRoom(scene,pending)&&pose==null)return false;
            if(!pending)return !LakituDialogue.Active;
            if(scene!="Tutorial_01")
            {if(pose!=null)Stop();return true;}
            if(!CapAccess.Flag(__instance,"Wanted")){Stop();return true;}
            if(!CapAccess.Flag(readyType,"Ready"))return true;
            try
            {
                if(hero==null)hero=CapAccess.Read(heroType,"instance") as Component;
                if(hero==null)return true;
                if(CapAccess.Flag(CapAccess.Read(hero,"cState"),"dead")){Stop();return true;}
                if(renderer==null)
                {
                    // Do not interfere if another integration already owns Mario.
                    if(Convert.ToInt32(CapAccess.Read(__instance,"MarioId"))>=0){Stop();return true;}
                    knight=hero.GetComponent<MeshRenderer>();box=hero.GetComponent<BoxCollider2D>();
                    if(knight==null||box==null){Stop();return true;}
                    oldHidden=knight.forceRenderingOff;
                    bool right=CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight");
                    string library=Path.Combine(Path.GetDirectoryName(nativeType.Assembly.Location),"sm64.dll");
                    pose=new OpeningMarioPose(nativeType,library,right?Mathf.PI*.5f:-Mathf.PI*.5f);
                    presenting=true;
                    renderer=Activator.CreateInstance(rendererType,new object[]{"Hollow Mario opening"});
                    model=CapAccess.Read(renderer,"MeshRenderer") as MeshRenderer;
                    modelObject=CapAccess.Read(renderer,"Object") as GameObject;
                    accumulator=1f/30f;
                }
                accumulator+=Time.deltaTime;
                for(int count=0;accumulator>=1f/30f&&count<4;count++)
                {
                    accumulator-=1f/30f;
                    if(conversation)pose.TalkFacing(conversationFace);
                    int previous=conversation?MapFallFlourish.PushCinematicAnimation(0x1E):-1; // Original look-up animation.
                    try{buffers.SetValue(renderer,pose.Tick(buffers.GetValue(renderer),landed));}
                    finally{if(conversation)MapFallFlourish.PopCinematicAnimation(previous);}
                    takeTick.Invoke(renderer,null);
                    // The animation actor changes staging height at impact.
                    // Do not interpolate that invisible staging jump on screen.
                    if(landed&&!showingLanding){takeTick.Invoke(renderer,null);showingLanding=true;}
                }
                accumulator=Mathf.Min(accumulator,1f/30f);
                Vector2 origin=(Vector2)CapAccess.Read(mathType,"Origin");
                object state=pose.State;
                Vector3 nativeFeet=new Vector3(origin.x+CapAccess.Number(state,"px")*.0105f,
                    origin.y+CapAccess.Number(state,"py")*.0105f,
                    CapAccess.Number(mathType,"LaneZ")-CapAccess.Number(state,"pz")*.0105f);
                render.Invoke(renderer,new object[]{Mathf.Clamp01(accumulator*30f),nativeFeet});
                modelObject.transform.position=new Vector3(hero.transform.position.x,box.bounds.min.y,hero.transform.position.z)-nativeFeet;
                model.sortingLayerID=knight.sortingLayerID;model.sortingOrder=knight.sortingOrder;
                model.enabled=knight.enabled&&!oldHidden;knight.forceRenderingOff=true;
                return false;
            }
            catch(Exception error)
            {
                LakituDialogue.Cancel();Stop();if(warnings++<3)log?.LogWarning("Mario's opening animation unavailable: "+error.GetBaseException().Message);
                return true;
            }
        }
        internal static void SceneLoaded(string name)
        {
            // Auxiliary additive scenes must not cancel an armed first entry.
            if(name=="Menu_Title"||name=="Quit_To_Menu")Stop();
        }
        internal static void Stop()
        {
            presenting=false;pending=false;landed=false;showingLanding=false;returned=false;conversation=false;returning=false;
            try{pose?.Dispose();}
            finally
            {
                pose=null;
                try{if(model!=null)model.enabled=false;if(renderer!=null)dispose.Invoke(renderer,null);}
                finally
                {
                    renderer=null;if(knight!=null)knight.forceRenderingOff=oldHidden;
                    hero=null;knight=model=null;box=null;modelObject=null;accumulator=0f;
                }
            }
        }
    }
}
