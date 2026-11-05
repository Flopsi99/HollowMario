using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Only the player's doubles in these three controllers belong to Mario.
    // The King, Hollow Knight, falling corpses, effects and native input steps
    // remain untouched. No additional gameplay actor or controller is created.
    internal static class BirthplaceMario
    {
        internal const string Group="birthplace";
        internal const int LegsStuck=0x55,Hang=0x33,Climb=0x00,BackwardFall=0x02;
        internal const int BellyProne=20,UnstuckFirst=116,UnstuckLast=150,ClimbAttemptLast=7;
        internal const float LedgeLowering=.4f; // Fraction of Mario's rendered hanging height.
        private static Type heroType,animatorType;
        private static GameObject owner;
        private static Transform actor;
        private static MeshRenderer sourceBody;
        private static readonly List<MeshRenderer> hidden=new List<MeshRenderer>(2);
        private static readonly List<bool> wasHidden=new List<bool>(2);
        private static readonly List<Vector3> vertices=new List<Vector3>(3072);
        private static int phase;
        private static float age,duration=1f,furthest,feetOffset,ledgeDrop;
        private static bool right;
        internal static bool Active=>actor!=null&&owner!=null&&owner.activeInHierarchy;
        internal static bool OwnsSequence=>phase!=0;

        internal static int Phase(string scene,string fsm,string controller,string state)
        {
            if(fsm!="Control")return 0;
            if(scene=="Abyss_15"&&controller=="Dream Enter Abyss")
                return state=="Impact"?10:state=="Wait"||state=="Black"||state=="Change Scene"?11:0;
            if(scene!="Dream_Abyss")return 0;
            if(controller=="Entry Cutscene")
            {
                switch(state)
                {
                    case "Init":case "Start Pause":case "Intro Text":case "Fade Pause":case "Fade Up":
                    case "Corpse 1":case "Corpse 2":case "Corpse 3":case "Input":case "Input 2":case "Input 3":return 20;
                    case "First Shake":case "Shake 2":case "Shake 3":return 21;
                    case "Climb 1":return 22;
                    case "Climb 2":return 23;
                    case "Climb 3":return 24;
                    case "Input 4":return 26;
                    case "Input 5":return 27;
                    case "End":return -1;
                    default:return 0;
                }
            }
            if(controller=="End Cutscene")
            {
                switch(state)
                {
                    case "Init":case "Idle":return 0;
                    case "Clamber1":return 41;
                    case "Fall":case "Black":case "Text 1":case "Charm Pause":case "Charm Get":
                    case "Text 2":case "Text 3":case "Return Pause":case "Return":case "Get Msg":case "Remove Overcharm":return 42;
                    default:return 40;
                }
            }
            return 0;
        }
        internal static void Arm(Harmony harmony,Type hero,Type animator)
        {
            heroType=hero;animatorType=animator;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.FsmState"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(BirthplaceMario),nameof(BeforeState)));
            harmony.Patch(AccessTools.Method(hero,"RegainControl"),
                postfix:new HarmonyMethod(typeof(BirthplaceMario),nameof(AfterControl)));
        }
        private static GameObject Variable(object fsm,string name)
            =>CapAccess.Read(CapAccess.Call(CapAccess.Read(fsm,"Variables"),"GetFsmGameObject",name),"Value") as GameObject;
        private static void Hide(GameObject target,GameObject hero)
        {
            if(target==null||target==hero)return;
            var body=target.GetComponent<MeshRenderer>();
            if(body==null||hidden.Contains(body))return;
            hidden.Add(body);wasHidden.Add(body.forceRenderingOff);body.forceRenderingOff=true;
        }
        private static float Seconds(object state,int next)
        {
            if(CapAccess.Read(state,"Actions") is Array actions)
                foreach(object action in actions)
                    if(action?.GetType().Name=="Wait")
                    {
                        float seconds=CapAccess.Number(CapAccess.Read(action,"time"),"Value");
                        if(seconds>0f)return seconds;
                    }
            return next==10?28f/16f:next==24?25f/12f:.5f;
        }
        private static void BeforeState(object __instance)
        {
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Control")return;
            var root=CapAccess.Read(fsm,"GameObject") as GameObject;
            if(root==null||(root.name!="Dream Enter Abyss"&&root.name!="Entry Cutscene"&&root.name!="End Cutscene"))return;
            string state=Convert.ToString(CapAccess.Read(__instance,"Name"));
            int next=Phase(root.scene.name,"Control",root.name,state);if(next==0)return;
            if(next<0)
            {if(owner==root&&phase!=0){phase=25;ConversationMario.ReleaseActionHold(Group);}return;}
            try
            {
                var hero=CapAccess.Read(heroType,"instance") as Component;if(hero==null)return;
                bool early=root.name=="Entry Cutscene"&&(state=="Init"||state=="Start Pause"||state=="Intro Text"||state=="Fade Pause");
                string name=next==42?"Dummy Over":"Dummy";
                var proxy=early?hero.gameObject:Variable(fsm,name)??root.transform.Find(root.name=="Dream Enter Abyss"?"Knight Dummy":name)?.gameObject;
                if(proxy==null)return;
                bool faceRight=CapAccess.Flag(CapAccess.Read(hero,"cState"),"facingRight");
                ArrivalMario.Sample first=At(next,0f);
                if(!ConversationMario.StartAction(hero,first.Animation,Facing(next,0f,faceRight),false,restart:true,holdControl:true,group:Group))return;
                if(owner!=root)
                {
                    Forget();owner=root;
                    var box=hero.GetComponent<BoxCollider2D>();feetOffset=box!=null?box.bounds.min.y-hero.transform.position.y:0f;
                }
                actor=proxy.transform;sourceBody=proxy.GetComponent<MeshRenderer>();phase=next;right=faceRight;
                age=furthest=0f;duration=Seconds(__instance,next);
                Hide(proxy,hero.gameObject);
                // Fall switches from Dummy to the foreground Dummy Over. Hide
                // both player pictures, without touching the other Knight.
                if(root.name=="End Cutscene")
                {Hide(root.transform.Find("Dummy")?.gameObject,hero.gameObject);Hide(root.transform.Find("Dummy Over")?.gameObject,hero.gameObject);}
            }
            catch(Exception error)
            {Forget();ConversationMario.ReleaseActionHold(Group);Plugin.Instance?.Log.LogWarning("Mario's Birthplace sequence: "+error.GetBaseException().Message);}
        }
        private static void AfterControl(object __instance)=>ConversationMario.ActionControlReturned(Group,__instance);
        internal static ArrivalMario.Sample At(int kind,float progress)
        {
            float p=Mathf.Clamp01(progress);
            if(kind==10)
                return p<.08f?new ArrivalMario.Sample{Animation=ConversationMario.Idle,Frame=0}
                    :new ArrivalMario.Sample{Animation=ArrivalMario.BellyLand,Frame=Mathf.RoundToInt(Mathf.Clamp01((p-.08f)/.52f)*BellyProne)};
            if(kind==11)return new ArrivalMario.Sample{Animation=ArrivalMario.BellyLand,Frame=BellyProne};
            if(kind==20)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=30};
            if(kind==21)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=30+Mathf.RoundToInt(10f*Mathf.Sin(p*Mathf.PI*2f))};
            if(kind==22)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=Mathf.RoundToInt(Mathf.Lerp(70f,90f,p))};
            if(kind==23)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=Mathf.RoundToInt(Mathf.Lerp(90f,115f,p))};
            if(kind==24)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=Mathf.RoundToInt(Mathf.Lerp(UnstuckFirst,UnstuckLast,p))};
            if(kind==26||kind==27)return new ArrivalMario.Sample{Animation=LegsStuck,Frame=kind==26?90:115};
            if(kind==40)return new ArrivalMario.Sample{Animation=Hang,Frame=0};
            if(kind==41)return p>=1f?new ArrivalMario.Sample{Animation=Hang,Frame=0}
                :new ArrivalMario.Sample{Animation=Climb,Frame=Mathf.RoundToInt(ClimbAttemptLast*(1f-Mathf.Abs(p*2f-1f)))};
            if(kind==42)return new ArrivalMario.Sample{Animation=BackwardFall,Frame=0};
            return new ArrivalMario.Sample{Animation=ConversationMario.Idle,Frame=0};
        }
        internal static float Facing(int kind,float progress,bool faceRight)
            =>kind>=40?Mathf.PI*.5f:kind==25?ConversationMario.SideFacing(faceRight):kind>=20&&kind<=27
                ?(kind==24?Mathf.Lerp(0f,ConversationMario.SideFacing(faceRight),Mathf.Clamp01((progress-.75f)/.25f)):0f)
                :ConversationMario.SideFacing(faceRight);
        internal static bool TrySample(out ArrivalMario.Sample sample,out float yaw)
        {
            sample=default(ArrivalMario.Sample);yaw=0f;
            if(!Active){ConversationMario.ReleaseActionHold(Group);return false;}
            foreach(var body in hidden)if(body!=null)body.forceRenderingOff=true;
            age+=Time.deltaTime;float p=age/Mathf.Max(.01f,duration);
            if(phase==10||phase==24)
            {
                object animator=actor.GetComponent(animatorType),clip=CapAccess.Read(animator,"CurrentClip");
                string expected=phase==10?"Knight Collapse":"Climb 3";
                if(Convert.ToString(CapAccess.Read(clip,"name"))==expected&&CapAccess.Read(clip,"frames") is Array frames)
                    p=ArrivalMario.Progress(CapAccess.Number(animator,"clipTime"),0f,frames.Length-1f);
            }
            furthest=Mathf.Max(furthest,p);sample=At(phase,furthest);yaw=Facing(phase,furthest,right);return true;
        }
        internal static Vector3 Anchor=>actor.position+Vector3.up*(feetOffset-(phase==42?ledgeDrop:0f));
        internal static void Cover(MeshRenderer mario,Vector3 nativeFeet,int triangleCount)
        {
            if(!Active||mario==null)return;
            foreach(var body in hidden)if(body!=null)body.forceRenderingOff=true;
            if(sourceBody!=null){mario.sortingLayerID=sourceBody.sortingLayerID;mario.sortingOrder=sourceBody.sortingOrder;}
            if(phase>=20&&phase<=27)ShadePoolMario.ClipBelow(mario,nativeFeet.y);
            if((phase==40||phase==41)&&sourceBody!=null)
            {
                // Follow the native hanging double, but its sprite top includes
                // tall horns above the grip. Lower Mario by a fixed fraction of
                // his first hanging pose; retain that offset through the fall.
                Mesh mesh=mario.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)return;
                mesh.GetVertices(vertices);int count=Mathf.Min(vertices.Count,triangleCount*3);
                float top=float.NegativeInfinity,bottom=float.PositiveInfinity;
                for(int i=0;i<count;i++){top=Mathf.Max(top,vertices[i].y);bottom=Mathf.Min(bottom,vertices[i].y);}
                if(!float.IsNegativeInfinity(top))
                {
                    if(ledgeDrop<=0f)ledgeDrop=(top-bottom)*LedgeLowering;
                    Vector3 point=mario.transform.position;point.y=sourceBody.bounds.max.y-top-ledgeDrop;mario.transform.position=point;
                }
            }
        }
        internal static void Forget()
        {
            for(int i=0;i<hidden.Count;i++)if(hidden[i]!=null)hidden[i].forceRenderingOff=wasHidden[i];
            hidden.Clear();wasHidden.Clear();vertices.Clear();owner=null;actor=null;sourceBody=null;
            phase=0;age=furthest=feetOffset=ledgeDrop=0f;duration=1f;
        }
    }
}
