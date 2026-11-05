using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Keep the pool's dummy, timing, reward, particles and surface effects.
    // Only Mario's private presentation mesh is cropped at that surface.
    internal static class ShadePoolMario
    {
        internal const string Group="shade-pool";
        internal const float HeadBaseHeight=1.05f;
        private static Type heroType,animatorType;
        private static Transform actor;
        private static MeshRenderer knight;
        private static bool oldHidden,waterReady;
        private static int phase;
        private static float feetOffset,waterY;
        private static readonly List<Vector3> vertices=new List<Vector3>(3072);
        private static readonly List<int> triangles=new List<int>(3072),visible=new List<int>(3072);
        internal static bool Active=>actor!=null&&actor.gameObject.activeInHierarchy;
        internal static int Phase(string fsm,string owner,string state)
        {
            if(fsm!="Get Shadow Dash"||owner!="Dish Plat")return -1;
            switch(state)
            {
                case "Fall":return 1;
                case "Shake 1":case "Shake 2":case "Shaking":case "Emerge 1":case "Emerge 2":case "PlayerData":case "UI Msg":return 2;
                case "Get Up":return 3;
                case "End":case "Got":return 0;
                default:return -1;
            }
        }
        internal static void Arm(Harmony harmony,Type hero,Type animator)
        {
            heroType=hero;animatorType=animator;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(ShadePoolMario),nameof(BeforeState)));
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Get Shadow Dash")return;
            var owner=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(owner==null)return;
            int next=Phase("Get Shadow Dash",owner.name,Convert.ToString(CapAccess.Read(__instance,"Name")));
            if(next<0)return;
            if(next==0){ConversationMario.ReleaseActionHold(Group);Forget();return;}
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;
                var proxy=CapAccess.Read(CapAccess.Call(CapAccess.Read(fsm,"Variables"),"GetFsmGameObject","Knight Dummy"),"Value") as GameObject;
                if(hero==null||proxy==null||!ConversationMario.StartAction(hero,ConversationMario.Idle,0f,false,
                    holdControl:true,group:Group))return;
                if(actor!=proxy.transform)
                {
                    Forget();actor=proxy.transform;knight=proxy.GetComponent<MeshRenderer>();
                    if(knight!=null)oldHidden=knight.forceRenderingOff;
                    var box=hero.GetComponent<BoxCollider2D>();feetOffset=box!=null?box.bounds.min.y-hero.transform.position.y:0f;
                    var surface=owner.GetComponent<Collider2D>();
                    if(surface!=null){waterY=surface.bounds.max.y;waterReady=true;}
                }
                phase=next;
                if(!waterReady&&next==2&&knight!=null){waterY=knight.bounds.min.y;waterReady=true;}
                if(knight!=null)knight.forceRenderingOff=true;
            }
            catch(Exception error)
            {Forget();ConversationMario.ReleaseActionHold(Group);Plugin.Instance?.Log.LogWarning("Mario's shade pool: "+error.GetBaseException().Message);}
        }
        internal static float FeetHeight(int stage,float surface,float normal,float progress)
            =>stage==1?normal:Mathf.Lerp(surface-HeadBaseHeight,normal,stage==3?Mathf.Clamp01(progress):0f);
        private static float RiseProgress()
        {
            object animator=actor.GetComponent(animatorType),clip=CapAccess.Read(animator,"CurrentClip");
            var frames=CapAccess.Read(clip,"frames") as Array;
            return ArrivalMario.Progress(CapAccess.Number(animator,"clipTime"),0f,frames!=null?frames.Length-1f:1f);
        }
        internal static Vector3 Anchor
        {
            get
            {
                Vector3 point=actor.position;
                point.y=waterReady?FeetHeight(phase,waterY,point.y+feetOffset,phase==3?RiseProgress():0f):point.y+feetOffset;
                return point;
            }
        }
        internal static void Cover(MeshRenderer mario,Vector3 nativeFeet)
        {
            if(!Active||!waterReady||mario==null)return;
            if(knight!=null)knight.forceRenderingOff=true;
            ClipBelow(mario,nativeFeet.y+waterY-Anchor.y);
        }
        internal static void ClipBelow(MeshRenderer mario,float cutoff)
        {
            Mesh mesh=mario.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)return;
            mesh.GetVertices(vertices);mesh.GetTriangles(triangles,0);visible.Clear();
            for(int i=0;i+2<triangles.Count;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(Above(vertices[a].y,vertices[b].y,vertices[c].y,cutoff))
                {visible.Add(a);visible.Add(b);visible.Add(c);}
            }
            mesh.SetTriangles(visible,0,false);
        }
        internal static bool Above(float a,float b,float c,float surface)=>a>=surface&&b>=surface&&c>=surface;
        internal static void Forget()
        {
            if(knight!=null)knight.forceRenderingOff=oldHidden;
            actor=null;knight=null;phase=0;feetOffset=waterY=0f;waterReady=false;
            vertices.Clear();triangles.Clear();visible.Clear();
        }
    }
}
