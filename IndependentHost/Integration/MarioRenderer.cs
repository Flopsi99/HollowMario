using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HollowMario.Host;
using UnityEngine;

namespace MarioHollow
{
    public static class BakedAtlas
    {
        internal static Texture2D texture;
        internal static readonly Dictionary<Color32,int> rowByColour=new Dictionary<Color32,int>();
        internal static int Row(Color32 colour)
        {
            if(rowByColour.TryGetValue(colour,out int row))return row;
            if(texture==null)texture=new Texture2D(708,1024,TextureFormat.RGBA32,false){name="Hollow Mario ROM palette",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            if(rowByColour.Count>=16)throw new InvalidOperationException("Native model uses more than sixteen material colours.");
            row=rowByColour.Count;var pixels=new Color32[708*64];
            for(int y=0;y<64;y++)for(int x=0;x<708;x++)
            {
                if(x>=704){pixels[y*708+x]=colour;continue;}
                Color32 texel=Sm64.Atlas[y*704+x];int alpha=texel.a;
                pixels[y*708+x]=new Color32((byte)((texel.r*alpha+colour.r*(255-alpha))/255),
                    (byte)((texel.g*alpha+colour.g*(255-alpha))/255),(byte)((texel.b*alpha+colour.b*(255-alpha))/255),255);
            }
            texture.SetPixels32(0,row*64,708,64,pixels);texture.Apply(false);rowByColour.Add(colour,row);return row;
        }
        internal static void Dispose(){if(texture!=null)UnityEngine.Object.Destroy(texture);texture=null;rowByColour.Clear();}
    }
    public sealed class MarioRenderer:IDisposable
    {
        private readonly GeometryFrame memory=new GeometryFrame();
        public SM64MarioGeometryBuffers Buffers;
        public readonly GameObject Object;
        public readonly MeshRenderer MeshRenderer;
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly Vector3[] previous=new Vector3[3072],current=new Vector3[3072],rendered=new Vector3[3072],normals=new Vector3[3072];
        private readonly Vector2[] uvs=new Vector2[3072];
        private readonly Color[] colours=new Color[3072];
        private readonly int[] triangleOrder=new int[1024],visibleIndices=new int[3072];
        private readonly float[] triangleDepth=new float[1024];
        public bool DoubleSided {get;set;}
        public int Triangles{get;private set;}
        private bool fresh=true,disposed;
        public MarioRenderer(string name)
        {
            Buffers=new SM64MarioGeometryBuffers{position=memory.Pointers.Position,normal=memory.Pointers.Normal,color=memory.Pointers.Colour,uv=memory.Pointers.UV};
            // Preserve the tint/continuous alpha used by Vanish and hazard
            // fades. Cutout shaders would turn those effects into opaque/off.
            // Cull and depth-sort the 3D surfaces before this sprite shader.
            var shader=Shader.Find("Sprites/Default");
            if(shader==null)throw new InvalidOperationException("Mario render shader missing.");
            material=new Material(shader);mesh=new Mesh{name=name};mesh.MarkDynamic();
            // Fixed buffer capacity: native ticks update attributes; Render is
            // the single owner of interpolated vertices and visible triangles.
            mesh.vertices=current;
            Object=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));Object.layer=LayerMask.NameToLayer("Player");
            Object.GetComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer=Object.GetComponent<MeshRenderer>();MeshRenderer.sharedMaterial=material;
            HostTrace.Write("renderer "+name+": shader="+shader.name+", reflected winding; depth=host sprite plane");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]public unsafe void TakeTick()
        {
            if(disposed)throw new ObjectDisposedException(nameof(MarioRenderer));
            int count=Buffers.numTrianglesUsed*3;if(count>3072)throw new InvalidOperationException("Native geometry exceeds capacity.");
            Array.Copy(current,previous,current.Length);
            float* positions=(float*)Buffers.position,n=(float*)Buffers.normal,c=(float*)Buffers.color,u=(float*)Buffers.uv;
            for(int i=0;i<count;i++)
            {
                int v=i*3,t=i*2;current[i]=MarioMath.World(positions[v],positions[v+1],positions[v+2]);
                normals[i]=MeshProjection.Normal(n[v],n[v+1],n[v+2]);
                var colour=new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c[v])*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(c[v+1])*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(c[v+2])*255),255);
                int row=BakedAtlas.Row(colour);uvs[i]=MeshProjection.AtlasUV(u[t],u[t+1],row);
                float light=.72f+.28f*Mathf.Clamp01(normals[i].y*.8f-normals[i].x*.25f-normals[i].z*.3f);colours[i]=new Color(light,light,light,1f);
            }
            if(fresh)HostTrace.Write("first mesh: triangles="+Buffers.numTrianglesUsed+", palette rows="+BakedAtlas.rowByColour.Count+", firstUV="+(count>0?uvs[0].ToString():"none")+", shader="+material.shader.name);
            if(fresh||count!=Triangles*3){Array.Copy(current,previous,current.Length);fresh=false;}
            Triangles=Buffers.numTrianglesUsed;material.mainTexture=BakedAtlas.texture;
            mesh.normals=normals;mesh.uv=uvs;mesh.colors=colours;
        }
        private readonly float[] depthBeforeProjection=new float[3072];
        [MethodImpl(MethodImplOptions.NoInlining)]public void Render(float alpha,Vector3 feet)
        {
            if(disposed)return;
            var hero=HeroController.SilentInstance;
            var host=hero!=null?hero.GetComponent<MeshRenderer>():null;
            if(host!=null){MeshRenderer.sortingLayerID=host.sortingLayerID;MeshRenderer.sortingOrder=host.sortingOrder;}
            // Geometry is world-space. The script presentation code places and
            // rotates Object around its own feet anchor after this call.
            Object.transform.position=Vector3.zero;
            int count=Triangles*3;
            var bounds=new Bounds(feet,Vector3.zero);
            for(int i=0;i<count;i++)
            {
                rendered[i]=Vector3.Lerp(previous[i],current[i],Mathf.Clamp01(alpha));
                depthBeforeProjection[i]=rendered[i].z;
                rendered[i]=MeshProjection.Layer(rendered[i],feet);
                if(i==0)bounds=new Bounds(rendered[i],Vector3.zero);else bounds.Encapsulate(rendered[i]);
            }
            mesh.vertices=rendered;mesh.bounds=bounds;
            {
                // Back-to-front visible surfaces prevent rear head/body faces
                // overwriting the face, while retaining native decal alpha.
                int visible=0;
                for(int t=0;t<Triangles;t++)
                {
                    int a=t*3;Vector3 edge=rendered[a+2]-rendered[a],other=rendered[a+1]-rendered[a];
                    bool wing=uvs[a].x>=576f/708f&&uvs[a].x<704f/708f;
                    if(!MeshProjection.Visible(edge,other,DoubleSided||wing))continue;
                    triangleOrder[visible]=t;triangleDepth[visible]=-(depthBeforeProjection[a]+depthBeforeProjection[a+1]+depthBeforeProjection[a+2]);visible++;
                }
                Array.Sort(triangleDepth,triangleOrder,0,visible);
                for(int t=0;t<visible;t++){int a=triangleOrder[t]*3;visibleIndices[t*3]=a;visibleIndices[t*3+1]=a+2;visibleIndices[t*3+2]=a+1;}
                mesh.SetTriangles(visibleIndices,0,visible*3,0,false);
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;memory.Dispose();UnityEngine.Object.Destroy(Object);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);
        }
    }
}
