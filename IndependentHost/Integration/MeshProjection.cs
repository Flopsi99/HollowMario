using UnityEngine;

namespace MarioHollow
{
    // Geometry-only part of the renderer. Runs without Unity scene objects so
    // real ROM geometry can exercise the rendering conversion offline.
    public static class MeshProjection
    {
        public static int Index(int output)=>output%3==1?output+1:output%3==2?output-1:output;
        public static Vector3 Normal(float x,float y,float z)=>new Vector3(x,y,-z);
        public static Vector2 AtlasUV(float u,float v,int row)
            =>u!=1f||v!=1f?new Vector2(u*704f/708f,(row*64+v*64)/1024f):new Vector2(706f/708f,(row*64+32f)/1024f);
        public static Vector3 Layer(Vector3 vertex,Vector3 feet)
            =>new Vector3(vertex.x,vertex.y,feet.z);
        public static bool Visible(Vector3 edge,Vector3 other,bool doubleSided)
            =>doubleSided||edge.x*other.y-edge.y*other.x<0f;
    }
}
