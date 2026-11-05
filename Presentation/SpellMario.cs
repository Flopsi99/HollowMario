using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Native spell FSMs and hazard recovery remain the only gameplay owners.
    internal static class SpellMario
    {
        private static Type heroType,playerType,spriteType;
        private static MethodInfo sound;
        private static Component hazardSprite;
        private static int warnings;
        private static readonly Dictionary<MeshRenderer,bool> hidden=new Dictionary<MeshRenderer,bool>();
        internal const int ScreamHoldFrame=12;
        internal const int CrouchHoldFrame=2;
        internal const int Crawl=0x99;
        internal const uint LavaSound=0x2414A080u;
        internal struct Plan
        {internal int Animation,Hold;internal string Level;internal bool Restart,Side,HoldControl;}
        internal static void Arm(Harmony harmony,Type native)
        {
            heroType=AccessTools.TypeByName("HeroController");playerType=AccessTools.TypeByName("PlayerData");
            spriteType=AccessTools.TypeByName("tk2dBaseSprite");sound=AccessTools.Method(native,"sm64_play_sound_global");
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(SpellMario),nameof(BeforeState)));
            harmony.Patch(AccessTools.Method(heroType,"DieFromHazard"),
                prefix:new HarmonyMethod(typeof(SpellMario),nameof(BeforeHazard)));
            Type pool=AccessTools.TypeByName("ObjectPool");
            foreach(MethodInfo method in pool.GetMethods(CapAccess.Any))
                if(method.Name=="Spawn"&&method.IsStatic&&method.ReturnType==typeof(GameObject)
                    &&method.GetParameters().Length>0&&method.GetParameters()[0].ParameterType==typeof(GameObject))
                    harmony.Patch(method,postfix:new HarmonyMethod(typeof(SpellMario),nameof(AfterHazardEffect)));
        }
        internal static Plan ForState(string state)
        {
            switch(state)
            {
                case "Fireball Antic":case "Fireball 1":case "Fireball 2":case "Fireball Recoil":
                    return new Plan{Animation=0x67,Hold=-1,Level="fireballLevel",Side=true,Restart=state=="Fireball 1"||state=="Fireball 2"};
                case "Start Slug Anim":case "Focus Start":case "Focus":case "Focus Heal":case "Focus S":
                case "Focus Left":case "Focus Right":case "Focus Heal 2":
                    return new Plan{Animation=0x97,Hold=CrouchHoldFrame,Level="focus",HoldControl=true};
                case "Focus Cancel":case "Focus Cancel 2":case "Focus Get Finish":case "Focus Get Finish 2":
                    return new Plan{Animation=0x96,Hold=-1,Level="focus"};
                case "Quake Antic":return new Plan{Animation=0x3C,Hold=-1,Level="quakeLevel"};
                case "Q1 Effect":case "Q2 Effect":case "Quake1 Down":case "Quake2 Down":case "Enter Quake":
                    return new Plan{Animation=0x3D,Hold=-1,Level="quakeLevel"};
                case "Quake1 Land":case "Q2 Land":case "Q2 Pillar":case "Quake Finish":
                    return new Plan{Animation=0x3A,Hold=-1,Level="quakeLevel"};
                case "Scream Antic1":case "Scream Antic2":case "Scream Burst 1":case "Scream Burst 2":case "End Roar":case "End Roar 2":
                    return new Plan{Animation=0xC0,Hold=ScreamHoldFrame,Level="screamLevel"};
                case "Scream End":case "Scream End 2":return new Plan{Animation=0xC0,Hold=-1,Level="screamLevel"};
                default:return default(Plan);
            }
        }
        internal static Plan ForDashState(string state)
        {
            switch(state)
            {
                case "Relinquish Control":case "Ground Charge":case "Ground Charged":case "Wall Charge":case "Wall Charged":
                    return new Plan{Animation=0x97,Hold=CrouchHoldFrame,Level="superdash",Side=true,HoldControl=true};
                case "Dash Start":case "Dashing":case "Cancelable":case "Enter Super Dash":case "Enter Velocity":
                    return new Plan{Animation=0x8C,Hold=-1,Level="superdash",Side=true,HoldControl=true};
                case "Charge Cancel Ground":case "Charge Cancel Wall":
                    return new Plan{Animation=0x96,Hold=-1,Level="superdash",Side=true,HoldControl=true};
                case "Air Cancel":case "Hit Wall":
                    return new Plan{Animation=0x8D,Hold=-1,Level="superdash",Side=true,HoldControl=true};
                default:return default(Plan);
            }
        }
        internal static Plan ForFocusState(string state,bool shapeOfUnn)
        {
            Plan plan=ForState(state);
            if(shapeOfUnn&&plan.Level=="focus"&&plan.HoldControl)
            {plan.Animation=Crawl;plan.Hold=-1;}
            return plan;
        }
        internal static int CrawlHold(float horizontalSpeed)=>Mathf.Abs(horizontalSpeed)<.05f?0:-1;
        internal static float CrawlSpeed(float previousX,float x,float deltaTime)=>deltaTime>0f?(x-previousX)/deltaTime:0f;
        internal static bool ReleasesHold(string state)
            =>state=="Inactive"||state=="Regain Control"||state=="Cancel"||state=="Cancel All"||state=="FSM Cancel";
        internal static Plan ForMapState(string state)
        {
            switch(state)
            {
                case "Walk Left":case "Walk Right":return new Plan{Animation=0x18,Hold=-1,Level="map",HoldControl=true};
                case "Open Map":case "Map Idle":case "Turn L?":case "Turn R?":
                    return new Plan{Animation=0x3F,Hold=-1,Level="map",HoldControl=true};
                case "Close Map":case "Close Doub Check":case "Double Fail":case "Get Inventory":case "Extra Check":
                    return new Plan{Animation=0x3F,Hold=-1,Level="map"};
                default:return default(Plan);
            }
        }
        internal static Plan ForOtherState(string fsm,string state)
        {
            if(fsm=="Roar Lock")
            {
                if(state=="Lock Start"||state=="Flip"||state=="Set Push Direction"||state=="Check On Ground")
                    return new Plan{Animation=0x74,Hold=4,Level="roar",Side=true,HoldControl=true};
                if(state=="Lock Grounded")return new Plan{Animation=0x74,Hold=4,Level="roar",Side=true,HoldControl=true};
                if(state=="Lock Air")return new Plan{Animation=0x02,Hold=4,Level="roar",Side=true,HoldControl=true};
            }
            if(fsm=="Dream Return")
            {
                if(state=="Prostrate"||state=="Prostrate Grimm"||state=="Gate Fade")return new Plan{Animation=0x87,Hold=-1,Level="wake",Side=true,HoldControl=true};
                if(state=="Get Up"||state=="Get Up 2")return new Plan{Animation=0xC9,Hold=-1,Level="wake",Side=true,HoldControl=true};
                if(state=="Land")return new Plan{Animation=0x57,Hold=-1,Level="wake",Side=true,HoldControl=true};
            }
            if(fsm=="Dream Nail")
            {
                switch(state)
                {
                    case "Start":return new Plan{Animation=DreamNailMario.Grab,Hold=-1,Level="dream",Side=true,HoldControl=true};
                    case "Charge":case "Slash Antic":return new Plan{Animation=DreamNailMario.Hold,Hold=0,Level="dream",Side=true,HoldControl=true};
                    case "Slash":return new Plan{Animation=DreamNailMario.Swing,Hold=-1,Level="dream",Side=true,Restart=true,HoldControl=true};
                    case "Charge Cancel":return new Plan{Animation=DreamNailMario.Release,Hold=-1,Level="dream",Side=true};
                    case "Idle Anim":return new Plan{Animation=0xC5,Hold=-1,Level="dream",Side=true};
                    case "Set Charge Start":case "Set Charge":case "Set Antic":
                        return new Plan{Animation=0x97,Hold=2,Level="dream",Side=true,HoldControl=true};
                    case "Warp Charge Start":case "Warp Charge":case "Warp Effect":
                        return new Plan{Animation=ConversationMario.DreamTwirl,Hold=-1,Level="dream",Side=true,HoldControl=true};
                    case "Set":return new Plan{Animation=0x3A,Hold=-1,Level="dream",Side=true,Restart=true,HoldControl=true};
                    case "Set Recover":case "Warp Cancel":return new Plan{Animation=0x96,Hold=-1,Level="dream",Side=true};
                }
            }
            return default(Plan);
        }
        internal static int RoarAnimation(string state,bool grounded)
            =>state=="Lock Air"?0x02:state=="Lock Grounded"?0x74:grounded?0x74:0x02;
        internal static bool WatchesFsm(string name)
            =>name=="Spell Control"||name=="Superdash"||name=="Map Control"||name=="Roar Lock"
                ||name=="Dream Nail"||name=="Dream Return"||name=="Inventory Control"
                ||name=="Stag Control"||name=="Shop Region";
        private static void BeforeState(object __instance)
        {
            try
            {
            object fsm=CapAccess.Read(__instance,"Fsm");
            string name=Convert.ToString(CapAccess.Read(fsm,"Name"));
            if(!WatchesFsm(name))return;
            Component current=CapAccess.Read(heroType,"instance") as Component;
            string state=Convert.ToString(CapAccess.Read(__instance,"Name"));
            if(current==null||DeathMario.Active)return;
            bool right=CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight");
            if(name=="Inventory Control")
            {
                if(state=="Open")ConversationMario.StartAction(current,0x3F,ConversationMario.SideFacing(right),false,holdControl:true,group:"inventory");
                if(state=="Regain Control"||state=="Regain Control 2"||state=="Damage Close"||state=="Closed")ConversationMario.ReleaseActionHold("inventory");
                return;
            }
            if(name=="Stag Control"&&(state=="Take Control"||state=="Open map"))
            {ConversationMario.StartAction(current,ConversationMario.LookUp,ConversationMario.SideFacing(right),false,group:"stag");return;}
            if(name=="Shop Region"&&(state=="Sit"||state=="Sitting"||state=="Rise"))
            {
                object variables=CapAccess.Read(fsm,"Variables");
                var seat=CapAccess.Read(CapAccess.Call(variables,"FindFsmGameObject","Knight Sit"),"Value") as GameObject;
                if(seat==null)return;
                if(ConversationMario.StartAction(current,state=="Rise"?0xC8:state=="Sit"?0x84:0x85,
                    right?Mathf.PI*.5f:-Mathf.PI*.5f,false,group:"ground-sit"))HideBody(seat.GetComponent<MeshRenderer>());
                return;
            }
            if(name!="Spell Control"&&name!="Superdash"&&name!="Map Control"&&name!="Roar Lock"&&name!="Dream Nail"&&name!="Dream Return")return;
            if(!ReferenceEquals(CapAccess.Read(fsm,"GameObject"),current.gameObject))return;
            string group=name=="Superdash"?"superdash":name=="Map Control"?"map":name=="Roar Lock"?"roar":name=="Dream Nail"?"dream":name=="Dream Return"?"wake":"focus";
            if(ReleasesHold(state)||state=="Quick Cancel"||state=="Fsm Cancel"||state=="Cancel Effects"||state=="Air Regain")ConversationMario.ReleaseActionHold(group);
            object player=CapAccess.Read(playerType,"instance");
            Plan plan=name=="Superdash"?ForDashState(state):name=="Map Control"?ForMapState(state):name=="Spell Control"?ForFocusState(state,CapAccess.Flag(player,"equippedCharm_28")):ForOtherState(name,state);
            if(plan.Level==null)return;
            if(plan.Level=="roar")plan.Animation=RoarAnimation(state,CapAccess.Flag(CapAccess.Read(current,"cState"),"onGround"));
            bool dark=plan.Level.EndsWith("Level",StringComparison.Ordinal)&&Convert.ToInt32(CapAccess.Read(player,plan.Level))>=2;
            float yaw=plan.Level=="focus"?ConversationMario.SideFacing(right):plan.Side?(CapAccess.Flag(CapAccess.Read(current,"cState"),"facingRight")?Mathf.PI*.5f:-Mathf.PI*.5f):0f;
            if(plan.Animation==Crawl&&(state=="Focus Left"||state=="Focus Right"))yaw=ConversationMario.SideFacing(state=="Focus Right");
            bool started=ConversationMario.StartAction(current,plan.Animation,yaw,dark,plan.Hold,plan.Restart,false,plan.HoldControl,plan.Level);
            if(started&&name=="Dream Nail")DreamNailMario.Begin(state,current);
            }
            catch(Exception error){Failed(error);}
        }
        private static void BeforeHazard(object __instance,object __0)
        {
            try
            {
            if(Convert.ToInt32(__0)==0)return;
            Component current=__instance as Component;
            object state=CapAccess.Read(current,"cState");
            if(ConversationMario.IsHazardPose||CapAccess.Flag(state,"hazardDeath")||CapAccess.Flag(state,"dead"))return;
            if(ConversationMario.StartAction(current,0x29,0f,false,-1,false,true))
                sound?.Invoke(null,new object[]{LavaSound}); // Once per real native hazard recovery.
            }
            catch(Exception error){Failed(error);}
        }
        private static void AfterHazardEffect(GameObject __0,GameObject __result)
        {
            try
            {
            if(!ConversationMario.IsHazardPose||__0==null||__result==null)return;
            object current=CapAccess.Read(heroType,"instance");
            if(!ReferenceEquals(__0,CapAccess.Read(current,"acidDeathPrefab"))
                &&!ReferenceEquals(__0,CapAccess.Read(current,"spikeDeathPrefab")))return;
            // Both native hazard prefabs put the Knight body on their root.
            // Children include acid splashes and spike-pierce effects; keep
            // their sprite renderers, particles and animation routines visible.
            MeshRenderer mesh=__result.GetComponent<MeshRenderer>();
            if(mesh!=null)
            {
                if(!hidden.ContainsKey(mesh))hidden.Add(mesh,mesh.forceRenderingOff);
                mesh.forceRenderingOff=true;
            }
            hazardSprite=__result.GetComponent(spriteType);
            }
            catch(Exception error){Failed(error);}
        }
        private static void HideBody(MeshRenderer mesh)
        {
            if(mesh==null)return;
            if(!hidden.ContainsKey(mesh))hidden.Add(mesh,mesh.forceRenderingOff);
            mesh.forceRenderingOff=true;
        }
        private static void Failed(Exception error)
        {ConversationMario.Stop();if(warnings++<3)Plugin.Instance?.Log.LogWarning("Mario's spell/hazard visuals unavailable: "+error.GetBaseException().Message);}
        internal static float HazardEffectAlpha=>hazardSprite!=null&&CapAccess.Read(hazardSprite,"color") is Color tint?tint.a:1f;
        internal static void RestoreHazardEffects()
        {
            foreach(var pair in hidden)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
            hidden.Clear();hazardSprite=null;
        }
    }
}
