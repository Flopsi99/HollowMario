using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Native pooled projectiles keep their own damage, charm combinations,
    // lifetime and effects. Never call Hero.Attack or create an extra nail hit.
    internal static class AttackCharmEffects
    {
        internal sealed class Cycle
        {
            private string previous;
            private int animation,frame;
            private bool sent;
            internal bool Observe(string move,int anim,int current,bool allowed)
            {
                string family=Family(move);
                bool continuous=family=="dive"||family=="pound"||family=="slide_kick";
                if(family!=previous||(!continuous&&(anim!=animation||current<frame)))sent=false;
                previous=family;animation=anim;frame=current;
                if(family==null||!allowed||sent)return false;
                sent=true;return true;
            }
        }
        private static Cycle cycle=new Cycle();
        private static object owner;
        private static MethodInfo spawn;
        private static int failures;
        internal static string Family(string move)
        {
            switch(move)
            {
                case "punch":case "move_punch":case "jump_kick":return move;
                case "slide_kick":case "slide_kick_slide":return "slide_kick";
                case "dive":case "dive_slide":return "dive";
                case "ground_pound":case "ground_pound_land":return "pound";
                default:return null;
            }
        }
        internal static bool BeamMove(string move)=>move=="punch"||move=="move_punch"||move=="jump_kick"||move=="slide_kick";
        internal static bool OrdinaryBeam(int health,int max,bool joni,bool joniBeam)=>joni?joniBeam:health==max;
        internal static bool FuryBeam(int health,int blue,bool fury)=>health==1&&blue<1&&fury;
        internal static void Observe(object controller,string move)
        {
            var follow=Plugin.Instance?.knightFollow;
            if(follow==null||controller==null)return;
            if(!ReferenceEquals(owner,controller)){Forget();owner=controller;}
            object hero=follow.Hero(controller),data=CapAccess.Read(hero,"playerData"),state=CapAccess.Read(hero,"cState");
            int anim=follow.Animation(controller);
            bool allowed=follow.MarioAlive(controller)&&!follow.ControlHeld(controller)
                &&!CapAccess.Flag(state,"dead")&&!CapAccess.Flag(state,"hazardDeath")&&!CapAccess.Flag(state,"transitioning")
                &&CapAccess.Number(data,"health")+CapAccess.Number(data,"healthBlue")>0f
                &&NailCollisionSignals.AllowMoveDamage(follow.Action(controller),anim,11);
            if(!cycle.Observe(move,anim,follow.AnimFrame(controller),allowed))return;
            try
            {
                if(CapAccess.Flag(data,"equippedCharm_38"))
                {
                    object shield=CapAccess.Read(hero,"fsm_orbitShield");
                    if(shield!=null)CapAccess.Call(shield,"SendEvent","SLASH");
                }
                if(!BeamMove(move)||!CapAccess.Flag(data,"equippedCharm_35"))return;
                int health=(int)CapAccess.Number(data,"health"),max=(int)CapAccess.Number(data,"maxHealth"),blue=(int)CapAccess.Number(data,"healthBlue");
                bool right=Mathf.Sin(follow.FaceAngle(controller))>=0f;
                if(OrdinaryBeam(health,max,CapAccess.Flag(data,"equippedCharm_27"),CapAccess.Flag(hero,"joniBeam")))Spawn(hero,data,right,false);
                if(FuryBeam(health,blue,CapAccess.Flag(data,"equippedCharm_6")))Spawn(hero,data,right,true);
            }
            catch(Exception error){if(failures++<3)Plugin.Instance?.Log.LogWarning("Mario's attack charms: "+error.GetBaseException().Message);}
        }
        private static void Spawn(object hero,object data,bool right,bool fury)
        {
            var body=hero as Component;
            var prefab=CapAccess.Read(hero,"grubberFlyBeamPrefab"+(right?"R":"L")+(fury?"_fury":"")) as GameObject;
            if(body==null||prefab==null)return;
            if(spawn==null)spawn=AccessTools.Method(AccessTools.TypeByName("ObjectPoolExtensions"),"Spawn",new[]{typeof(GameObject),typeof(Vector3)});
            if(spawn==null)throw new MissingMethodException("Native beam pool");
            var beam=spawn.Invoke(null,new object[]{prefab,body.transform.position}) as GameObject;
            if(beam==null)return;
            Vector3 scale=beam.transform.localScale;
            scale.y=CapAccess.Flag(data,"equippedCharm_13")?CapAccess.Number(hero,"MANTIS_CHARM_SCALE"):1f;
            beam.transform.localScale=scale;
        }
        internal static void Forget(){owner=null;cycle=new Cycle();}
    }
}
