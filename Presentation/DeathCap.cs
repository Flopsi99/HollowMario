using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Reuse the native corpse-head launch, replacing only its picture. The cap
    // mesh/texture come from the running ROM-backed renderer, never bundled art.
    internal static class DeathCap
    {
        internal sealed class Geometry
        {
            internal float[] Position,Normal,Color,Uv;
            internal int Count;
            internal Vector3 Center;
        }
        private struct Triangle:IEquatable<Triangle>
        {
            private Vector3 a,b,c;
            internal Triangle(float[] p,int at)
            {a=new Vector3(p[at],p[at+1],p[at+2]);b=new Vector3(p[at+3],p[at+4],p[at+5]);c=new Vector3(p[at+6],p[at+7],p[at+8]);}
            public bool Equals(Triangle other)=>a.Equals(other.a)&&b.Equals(other.b)&&c.Equals(other.c);
            public override bool Equals(object other)=>other is Triangle triangle&&Equals(triangle);
            public override int GetHashCode()=>a.GetHashCode()^(b.GetHashCode()*397)^(c.GetHashCode()*7919);
        }
        private static Type rendererType,mathType;
        private static FieldInfo buffers;
        private static MethodInfo takeTick,render,dispose;
        private static GameObject head;
        private static DeathCapView view;
        private static bool pending,placed,lost;
        private static Vector3 capCenter;
        internal static bool Pending=>pending&&head!=null;
        internal static void Arm(Harmony harmony,Type controller,Type native)
        {
            rendererType=controller.Assembly.GetType("MarioHollow.MarioRenderer",true);
            mathType=controller.Assembly.GetType("MarioHollow.MarioMath",true);
            buffers=AccessTools.Field(rendererType,"Buffers");takeTick=AccessTools.Method(rendererType,"TakeTick");
            render=AccessTools.Method(rendererType,"Render");dispose=AccessTools.Method(rendererType,"Dispose");
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.CreateObject"),"OnEnter"),
                prefix:new HarmonyMethod(typeof(DeathCap),nameof(BeforeCreate)),postfix:new HarmonyMethod(typeof(DeathCap),nameof(AfterCreate)));
        }
        internal static bool IsHeadSpawn(string fsm,string state,string prefab,bool ownDeath)
            =>ownDeath&&fsm=="Hero Death Anim"&&(state=="Head Left"||state=="Head Right")&&prefab=="Corpse Head";
        private static void BeforeCreate(object __instance,out bool __state)
        {
            __state=false;if(!DeathMario.Active)return;
            object fsm=CapAccess.Read(__instance,"Fsm");
            if(Convert.ToString(CapAccess.Read(fsm,"Name"))!="Hero Death Anim")return;
            var prefab=CapAccess.Read(CapAccess.Read(__instance,"gameObject"),"Value") as GameObject;
            __state=IsHeadSpawn("Hero Death Anim",Convert.ToString(CapAccess.Read(CapAccess.Read(__instance,"State"),"Name")),
                prefab!=null?prefab.name:null,DeathMario.OwnsEffect(CapAccess.Read(fsm,"GameObject") as GameObject));
        }
        private static void AfterCreate(object __instance,bool __state)
        {
            if(!__state)return;
            var spawned=CapAccess.Read(CapAccess.Read(__instance,"storeObject"),"Value") as GameObject;
            if(spawned==null)return;
            // Clone only: leave the prefab, launch actions, collider and other
            // death effects (including the Shade) under the game's control.
            foreach(Renderer native in spawned.GetComponentsInChildren<Renderer>(true))native.forceRenderingOff=true;
            if(lost||pending)return;
            head=spawned;pending=true;
        }
        internal static Geometry Read(object value)
        {
            int count=Convert.ToInt32(CapAccess.Read(value,"numTrianglesUsed"));
            if(count<1||count>1024)throw new InvalidOperationException("Cap geometry is not ready");
            var result=new Geometry{Count=count,Position=new float[count*9],Normal=new float[count*9],Color=new float[count*9],Uv=new float[count*6]};
            Marshal.Copy((IntPtr)CapAccess.Read(value,"position"),result.Position,0,result.Position.Length);
            Marshal.Copy((IntPtr)CapAccess.Read(value,"normal"),result.Normal,0,result.Normal.Length);
            Marshal.Copy((IntPtr)CapAccess.Read(value,"color"),result.Color,0,result.Color.Length);
            Marshal.Copy((IntPtr)CapAccess.Read(value,"uv"),result.Uv,0,result.Uv.Length);return result;
        }
        internal static Geometry Extract(Geometry wearing,Geometry bare)
        {
            var unchanged=new HashSet<Triangle>();
            for(int i=0;i<bare.Count;i++)unchanged.Add(new Triangle(bare.Position,i*9));
            var indices=new List<int>();
            for(int i=0;i<wearing.Count;i++)
            {
                int at=i*9;
                // Cap cloth (including the textured M emblem) is red in the
                // native output. Exclude changed skin, hair and white gloves.
                if(wearing.Color[at]>.9f&&wearing.Color[at+1]<.08f&&wearing.Color[at+2]<.08f
                    &&!unchanged.Contains(new Triangle(wearing.Position,at)))indices.Add(i);
            }
            if(indices.Count<4)throw new InvalidOperationException("Mario's cap could not be separated from his model");
            int count=indices.Count;
            var cap=new Geometry{Count=count,Position=new float[count*9],Normal=new float[count*9],Color=new float[count*9],Uv=new float[count*6]};
            Vector3 min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=-min;
            for(int i=0;i<count;i++)
            {
                int source=indices[i];Array.Copy(wearing.Position,source*9,cap.Position,i*9,9);
                Array.Copy(wearing.Normal,source*9,cap.Normal,i*9,9);Array.Copy(wearing.Color,source*9,cap.Color,i*9,9);
                Array.Copy(wearing.Uv,source*6,cap.Uv,i*6,6);
                for(int v=0;v<3;v++)
                {
                    int at=i*9+v*3;Vector3 p=new Vector3(cap.Position[at],cap.Position[at+1],cap.Position[at+2]);
                    min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
            }
            cap.Center=(min+max)*.5f;
            for(int i=0;i<count*3;i++){cap.Position[i*3]-=cap.Center.x;cap.Position[i*3+1]-=cap.Center.y;cap.Position[i*3+2]-=cap.Center.z;}
            return cap;
        }
        internal static void Detach(OpeningMarioPose pose,object source,int animation)
        {
            if(!Pending)return;
            pending=false;
            object picture=null;
            try
            {
                Geometry wearing=Read(buffers.GetValue(source));
                int frame=Convert.ToInt32(CapAccess.Read(pose.State,"animFrame"));
                if(!pose.SetCapVisible(false))throw new InvalidOperationException("Mario's cap state is unavailable");
                int previous=MapFallFlourish.PushCinematicAnimation(animation);bool quiet=BenchRestAudio.Begin();
                try{pose.SetAnimationFrame((short)(frame-1));buffers.SetValue(source,pose.Tick(buffers.GetValue(source),true));}
                finally{BenchRestAudio.End(quiet);MapFallFlourish.PopCinematicAnimation(previous);}
                Geometry cap=Extract(wearing,Read(buffers.GetValue(source)));takeTick.Invoke(source,null);
                picture=Activator.CreateInstance(rendererType,new object[]{"Mario's flying cap"});
                // A detached cloth shell exposes its reverse side. Mario's
                // normal closed body still uses back-face rejection.
                rendererType.GetProperty("DoubleSided").SetValue(picture,true,null);
                object geometry=buffers.GetValue(picture);
                Marshal.Copy(cap.Position,0,(IntPtr)CapAccess.Read(geometry,"position"),cap.Position.Length);
                Marshal.Copy(cap.Normal,0,(IntPtr)CapAccess.Read(geometry,"normal"),cap.Normal.Length);
                Marshal.Copy(cap.Color,0,(IntPtr)CapAccess.Read(geometry,"color"),cap.Color.Length);
                Marshal.Copy(cap.Uv,0,(IntPtr)CapAccess.Read(geometry,"uv"),cap.Uv.Length);
                FieldInfo count=geometry.GetType().GetField("numTrianglesUsed");count.SetValue(geometry,Convert.ChangeType(cap.Count,count.FieldType));
                buffers.SetValue(picture,geometry);takeTick.Invoke(picture,null);takeTick.Invoke(picture,null);
                Vector2 origin=(Vector2)CapAccess.Read(mathType,"Origin");float lane=CapAccess.Number(mathType,"LaneZ");
                Vector3 anchor=new Vector3(origin.x,origin.y,lane);render.Invoke(picture,new object[]{1f,anchor});
                var go=CapAccess.Read(picture,"Object") as GameObject;
                go.transform.SetParent(head.transform,false);go.transform.localPosition=-anchor;
                view=go.AddComponent<DeathCapView>();view.Bind(picture,dispose);
                capCenter=new Vector3(origin.x+cap.Center.x*.0105f,origin.y+cap.Center.y*.0105f,lane-cap.Center.z*.0105f);
                lost=true;placed=false;
            }
            catch(Exception error)
            {
                if(picture!=null)dispose.Invoke(picture,null);
                pose.SetCapVisible(true);
                Plugin.Instance?.Log.LogWarning("The death mask was hidden; Mario kept his cap: "+error.GetBaseException().Message);
            }
        }
        internal static void Place(GameObject mario,Vector3 feet)
        {
            if(!lost||placed||head==null||mario==null)return;
            Vector3 center=mario.transform.TransformPoint(feet+Quaternion.Euler(-8f,0f,0f)*(capCenter-feet));
            head.transform.position=center;
            var body=head.GetComponent<Rigidbody2D>();if(body!=null)body.position=center;
            var native=head.GetComponent<Renderer>();var cap=view!=null?view.GetComponent<MeshRenderer>():null;
            if(native!=null&&cap!=null){cap.sortingLayerID=native.sortingLayerID;cap.sortingOrder=native.sortingOrder;}
            placed=true;
        }
        internal static void Stop()
        {
            if(view!=null){var mesh=view.GetComponent<MeshRenderer>();if(mesh!=null)mesh.enabled=false;UnityEngine.Object.Destroy(view.gameObject);}
            view=null;head=null;pending=placed=lost=false;
        }
    }
    internal sealed class DeathCapView:MonoBehaviour
    {
        private object renderer;
        private MethodInfo dispose;
        internal void Bind(object value,MethodInfo cleanup){renderer=value;dispose=cleanup;}
        private void OnDestroy(){if(renderer!=null){object value=renderer;renderer=null;dispose.Invoke(value,null);}}
    }
}
