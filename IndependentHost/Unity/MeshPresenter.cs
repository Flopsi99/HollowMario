using System;
using UnityEngine;

namespace HollowMario.Host.Unity
{
    // Fresh presentation implementation. Geometry/texture bytes only arrive
    // from the running native engine and the player's ROM, never embedded files.
    public sealed class MeshPresenter:IDisposable
    {
        public const float WorldScale=.0105f;
        public readonly GameObject Object;
        public readonly MeshRenderer Renderer;
        private readonly Mesh mesh;
        private readonly Texture2D atlas;
        private readonly Material material;
        private readonly Vector3[] before=new Vector3[3072],after=new Vector3[3072],draw=new Vector3[3072];
        private readonly Vector2[] uv=new Vector2[3072];
        private readonly Color[] colours=new Color[3072];
        private readonly int[] indices=new int[3072];
        private int count;
        private bool initial=true,disposed;
        public MeshPresenter(byte[] romAtlas,string name="Hollow Mario independent mesh")
        {
            if(romAtlas==null||romAtlas.Length!=704*64*4)throw new ArgumentException("Unexpected ROM atlas dimensions.");
            // White lower half supports untextured coloured triangles. The
            // upper half contains the untouched runtime-extracted texture atlas.
            byte[] pixels=new byte[704*128*4];for(int i=0;i<704*64*4;i++)pixels[i]=255;
            Buffer.BlockCopy(romAtlas,0,pixels,704*64*4,romAtlas.Length);
            atlas=new Texture2D(704,128,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            atlas.LoadRawTextureData(pixels);atlas.Apply(false,true);
            Shader shader=Shader.Find("Sprites/Default");if(shader==null)throw new InvalidOperationException("Game sprite shader unavailable.");
            material=new Material(shader){mainTexture=atlas};
            mesh=new Mesh{name=name};mesh.MarkDynamic();
            Object=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            Object.GetComponent<MeshFilter>().sharedMesh=mesh;Renderer=Object.GetComponent<MeshRenderer>();Renderer.sharedMaterial=material;
            int player=LayerMask.NameToLayer("Player");if(player>=0)Object.layer=player;
            for(int i=0;i<indices.Length;i++)indices[i]=i;
        }
        public void Capture(GeometryFrame frame,Vector3 roomOrigin)
        {
            if(disposed)throw new ObjectDisposedException(nameof(MeshPresenter));
            int next=frame.Triangles*3;if(next>after.Length)throw new ArgumentException("Too many native vertices.");
            Array.Copy(after,before,after.Length);
            for(int i=0;i<next;i++)
            {
                int v=i*3,t=i*2;
                // Flatten depth slightly, keeping the Mario model legible in
                // the game's 2D lane without moving the actual simulation.
                after[i]=roomOrigin+new Vector3(frame.Positions[v]*WorldScale,frame.Positions[v+1]*WorldScale,-frame.Positions[v+2]*WorldScale*.1f);
                bool textured=frame.UVs[t]!=1f||frame.UVs[t+1]!=1f;
                uv[i]=textured?new Vector2(frame.UVs[t],.5f+frame.UVs[t+1]*.5f):new Vector2(.5f,.25f);
                float light=.7f+.3f*Mathf.Clamp01(frame.Normals[v+1]*.8f-frame.Normals[v]*.25f-frame.Normals[v+2]*.3f);
                colours[i]=new Color(frame.Colours[v]*light,frame.Colours[v+1]*light,frame.Colours[v+2]*light,1f);
            }
            if(initial||next!=count){Array.Copy(after,before,after.Length);initial=false;}count=next;
        }
        public void Draw(float alpha)
        {
            if(disposed)return;
            for(int i=0;i<count;i++)draw[i]=Vector3.Lerp(before[i],after[i],Mathf.Clamp01(alpha));
            mesh.Clear(false);mesh.vertices=draw;mesh.colors=colours;mesh.uv=uv;
            mesh.SetTriangles(indices,0,count,0,true);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            UnityEngine.Object.Destroy(Object);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(atlas);
        }
    }
}
