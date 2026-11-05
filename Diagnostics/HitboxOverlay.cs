using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HollowMario
{
    // Read-only geometry from the colliders and combat queries themselves. No
    // physics objects, hit tests, input locks or changes to damage are introduced.
    internal static class HitboxOverlay
    {
        private sealed class Volume
        {
            internal readonly Vector3[] Points=new Vector3[16];
            internal int Count;
            internal Color Color;
        }
        private static readonly Color BodyColor=new Color(.15f,.65f,1f),HurtColor=new Color(1f,.85f,.15f),
            AttackColor=new Color(1f,.2f,.25f),StompColor=new Color(.2f,1f,.4f);
        private static readonly Volume[] volumes=new Volume[8];
        private static readonly Vector3[] sortPoints=new Vector3[8],boxPoints=new Vector3[4];
        private static Component hero;
        private static BoxCollider2D body,hurt;
        private static Material material;
        private static bool listening,visible;
        private static int count;
        private static float observedAt;
        private static GUIStyle legendStyle;

        internal static void BeginTick()
        {
            if(!DebugSettings.ShowHitboxes)return;
            count=0;observedAt=Time.time;
        }
        internal static void Attack(Bounds reach,Vector3 motion)
        {if(DebugSettings.ShowHitboxes)Capture(reach,motion,AttackColor);}
        internal static void Stomp(Bounds mario,Vector3 start,Vector3 end)
        {
            if(!DebugSettings.ShowHitboxes)return;
            // Current foot region swept forward to the predicted next step.
            // Target-specific top tolerance is not part of this visible volume.
            Bounds foot=StompContact.AnticipationFoot(mario,start);
            foot.center+=end-start;
            Capture(foot,end-start,StompColor);
        }
        private static void Capture(Bounds reach,Vector3 motion,Color color)
        {
            if(count>=volumes.Length)return;
            var volume=volumes[count]??(volumes[count]=new Volume());
            volume.Count=SweepPolygon(reach,motion,volume.Points);volume.Color=color;count++;
        }

        // Convex hull of a box translated along one tick: a rectangle or hexagon,
        // including diagonal dives. The enclosing overlap-query box is not a hitbox.
        internal static int SweepPolygon(Bounds end,Vector3 motion,Vector3[] result)
        {
            for(int i=0;i<4;i++)
            {
                Vector3 point=new Vector3((i&1)==0?end.min.x:end.max.x,(i&2)==0?end.min.y:end.max.y,0f);
                sortPoints[i]=point;sortPoints[i+4]=point-new Vector3(motion.x,motion.y,0f);
            }
            for(int i=1;i<8;i++)
            {
                Vector3 point=sortPoints[i];int j=i-1;
                while(j>=0&&(sortPoints[j].x>point.x||(sortPoints[j].x==point.x&&sortPoints[j].y>point.y)))
                {sortPoints[j+1]=sortPoints[j];j--;}
                sortPoints[j+1]=point;
            }
            int n=0;
            for(int i=0;i<8;i++)
            {
                while(n>=2&&Cross(result[n-2],result[n-1],sortPoints[i])<=0f)n--;
                result[n++]=sortPoints[i];
            }
            int lower=n+1;
            for(int i=6;i>=0;i--)
            {
                while(n>=lower&&Cross(result[n-2],result[n-1],sortPoints[i])<=0f)n--;
                result[n++]=sortPoints[i];
            }
            return n>1?n-1:n;
        }
        private static float Cross(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
        internal static Vector3 BoxCorner(Vector2 size,Vector2 offset,int index)
            =>new Vector3(offset.x+((index==0||index==3)?-size.x:size.x)*.5f,
                offset.y+(index<2?-size.y:size.y)*.5f,0f);

        internal static void Tick(object controller,KnightResetFollow follow)
        {
            visible=false;
            if(!DebugSettings.ShowHitboxes)
            {
                if(listening){Camera.onPostRender-=AfterCamera;listening=false;}
                count=0;hero=null;body=hurt=null;return;
            }
            if(controller==null||follow==null||!CapAccess.Flag(controller,"Wanted"))return;
            bool mario=follow.MarioAlive(controller)||OpeningMario.Presenting||BenchMario.Presenting||ConversationMario.Presenting;
            var current=follow.Hero(controller) as Component;
            if(!mario||current==null||!current.gameObject.activeInHierarchy)return;
            if(hero!=current)
            {
                hero=current;body=hero.GetComponent<BoxCollider2D>();
                hurt=hero.transform.Find("HeroBox")?.GetComponent<BoxCollider2D>();count=0;
            }
            visible=Time.timeScale>0f;
            if(!listening){Camera.onPostRender+=AfterCamera;listening=true;}
        }
        private static bool Live(BoxCollider2D box)=>box!=null&&box.enabled&&box.gameObject.activeInHierarchy;
        private static void AfterCamera(Camera camera)
        {
            if(!visible||!DebugSettings.ShowHitboxes||hero==null||camera!=Camera.main)return;
            if(material==null)
            {
                Shader shader=Shader.Find("Hidden/Internal-Colored");if(shader==null)return;
                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull",(int)CullMode.Off);material.SetInt("_ZWrite",0);
                material.SetInt("_ZTest",(int)CompareFunction.Always);
            }
            if(!material.SetPass(0))return;
            GL.PushMatrix();
            try
            {
                GL.LoadProjectionMatrix(camera.projectionMatrix);GL.modelview=camera.worldToCameraMatrix;
                if(Live(body))DrawBox(body,BodyColor);
                if(Live(hurt))DrawBox(hurt,HurtColor);
                // Keep each 30 Hz observation until the next tick, but never leave
                // an old attack on screen after a handover, freeze or room change.
                if(FollowingGameplay()&&Time.time-observedAt<=.1f)
                    for(int i=0;i<count;i++)Draw(volumes[i].Points,volumes[i].Count,volumes[i].Color,hero.transform.position.z);
            }
            finally{GL.PopMatrix();}
        }
        private static bool FollowingGameplay()=>!OpeningMario.Presenting&&!BenchMario.Presenting&&!ConversationMario.Presenting;
        internal static void Clear(){visible=false;count=0;hero=null;body=hurt=null;}
        private static void DrawBox(BoxCollider2D box,Color color)
        {
            for(int i=0;i<4;i++)boxPoints[i]=box.transform.TransformPoint(BoxCorner(box.size,box.offset,i));
            Draw(boxPoints,4,color,box.transform.position.z);
        }
        private static void Draw(Vector3[] points,int length,Color color,float z)
        {
            if(length<2)return;
            GL.Begin(GL.TRIANGLES);GL.Color(new Color(color.r,color.g,color.b,.16f));
            for(int i=1;i+1<length;i++){Vertex(points[0],z);Vertex(points[i],z);Vertex(points[i+1],z);}
            GL.End();GL.Begin(GL.LINES);GL.Color(new Color(color.r,color.g,color.b,.85f));
            for(int i=0;i<length;i++){Vertex(points[i],z);Vertex(points[(i+1)%length],z);}
            GL.End();
        }
        private static void Vertex(Vector3 point,float z)=>GL.Vertex3(point.x,point.y,z);
        internal static void Legend()
        {
            if(!visible||!DebugSettings.ShowHitboxes)return;
            if(legendStyle==null)legendStyle=new GUIStyle(GUI.skin.box){alignment=TextAnchor.UpperLeft,richText=true,fontSize=14};
            GUI.Box(new Rect(Screen.width-228,16,212,88),
                "<color=#269FFF>Collision body</color>\n<color=#FFD926>Damage hurtbox</color>\n<color=#FF3340>Active move reach</color>\n<color=#33FF66>Stomp anticipation</color>",legendStyle);
        }
        internal static void Dispose()
        {
            if(listening)Camera.onPostRender-=AfterCamera;
            listening=visible=false;count=0;hero=null;body=hurt=null;
            if(material!=null)UnityEngine.Object.Destroy(material);material=null;
        }
    }
}
