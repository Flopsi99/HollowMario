using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The native metal display list depends on N64-generated reflection coordinates,
    // which the base adapter does not export. Supply them from its existing normals,
    // sampling the original ROM-backed64x32 metal map (native atlas slot0).
    internal static class MarioMetalTexture
    {
        private static FieldInfo source,view,normalField,uvField,meshField;
        private static PropertyInfo triangles;
        private static Color32[] original;
        private static Texture2D metal;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony,Type controller,ManualLogSource logger)
        {
            log=logger;
            try
            {
                Type renderer=controller.Assembly.GetType("MarioHollow.MarioRenderer");
                source=AccessTools.Field(controller.Assembly.GetType("MarioHollow.Sm64"),"Atlas");
                view=AccessTools.Field(controller,"Renderer");normalField=AccessTools.Field(renderer,"normals");
                uvField=AccessTools.Field(renderer,"uvs");meshField=AccessTools.Field(renderer,"mesh");
                triangles=AccessTools.Property(renderer,"Triangles");
                if(source==null||view==null||normalField==null||uvField==null||meshField==null||triangles==null)
                    throw new MissingMemberException("Metal reflection geometry");
                harmony.Patch(AccessTools.Method(renderer,"TakeTick"),postfix:new HarmonyMethod(typeof(MarioMetalTexture),nameof(AfterTakeTick)));
                MarioAppearance.MetalTexture=Texture;
            }
            catch(Exception error){Warn(error);}
        }

        private static Texture Texture()
        {
            if(!(source?.GetValue(null) is Color32[] raw)||raw.Length!=704*64)return null;
            if(metal!=null&&ReferenceEquals(raw,original))return metal;
            if(metal!=null)UnityEngine.Object.Destroy(metal);
            var pixels=new Color32[64*32];
            for(int y=0;y<32;y++)Array.Copy(raw,y*704,pixels,y*64,64);
            metal=new Texture2D(64,32,TextureFormat.RGBA32,false)
                {name="Mario original metal reflection",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            metal.SetPixels32(pixels);metal.Apply(false,true);original=raw;
            return metal;
        }

        private static void AfterTakeTick(object __instance)
        {
            if(!CapSystem.Metallic||MarioAppearance.Shaded)return;
            object controller=Plugin.Instance?.knightFollow?.Controller();
            if(controller==null||!ReferenceEquals(view.GetValue(controller),__instance))return;
            try
            {
                var normals=normalField.GetValue(__instance) as Vector3[];
                var uv=uvField.GetValue(__instance) as Vector2[];
                var mesh=meshField.GetValue(__instance) as Mesh;
                if(normals==null||uv==null||mesh==null)return;
                int count=Math.Min(Convert.ToInt32(triangles.GetValue(__instance,null))*3,Math.Min(normals.Length,uv.Length));
                for(int i=0;i<count;i++)
                {
                    Vector3 n=normals[i];double length=Math.Sqrt(n.x*n.x+n.y*n.y+n.z*n.z);
                    float x=length>1e-6?(float)(n.x/length):0f,y=length>1e-6?(float)(n.y/length):0f;
                    // Texel-centre bounds keep filtering inside the original metal tile.
                    uv[i]=new Vector2((.5f+(x*.5f+.5f)*63f)/64f,(.5f+(y*.5f+.5f)*31f)/32f);
                }
                mesh.uv=uv; // Reuse the base buffers; no per-tick array or texture allocation.
            }
            catch(Exception error){Warn(error);}
        }
        internal static void Refresh(object controller)
        {object renderer=controller==null?null:view?.GetValue(controller);if(renderer!=null)AfterTakeTick(renderer);}

        internal static void Dispose()
        {
            MarioAppearance.MetalTexture=null;
            if(metal!=null)UnityEngine.Object.Destroy(metal);
            metal=null;original=null;
        }
        private static void Warn(Exception error){if(failures++<4)log?.LogWarning("Mario metal reflection: "+error.GetBaseException().Message);}
    }
}
