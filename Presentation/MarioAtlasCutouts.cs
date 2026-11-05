using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The base atlas bakes decal alpha into opaque face/clothing colours. Wing tiles
    // need real cutouts instead. Correct only those tiles, only when rows are created.
    internal static class MarioAtlasCutouts
    {
        // Installed libsm64's native texture metadata: 11 slots of64px, wing/wing-tip
        // are slots9/10 (32x64 RGBA16). Other slots are opaque painted decals.
        private const int SourceWidth=704, BandHeight=64, FirstWing=576, WingWidth=128;
        private static FieldInfo source,texture,rows;
        private static Texture2D bound,neutral,shade;
        private static Color32[] shadeSource;
        private static Color32[] original,wingBand;
        private static int copied,version,neutralVersion=-1;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony,Assembly assembly,ManualLogSource logger)
        {
            log=logger;
            try
            {
                Type atlas=assembly.GetType("MarioHollow.BakedAtlas"),sm64=assembly.GetType("MarioHollow.Sm64");
                source=AccessTools.Field(sm64,"Atlas");texture=AccessTools.Field(atlas,"texture");rows=AccessTools.Field(atlas,"rowByColour");
                MethodInfo tick=AccessTools.Method(assembly.GetType("MarioHollow.MarioRenderer"),"TakeTick");
                if(source==null||texture==null||rows==null||tick==null)throw new MissingMemberException("Mario atlas bridge");
                harmony.Patch(tick,postfix:new HarmonyMethod(typeof(MarioAtlasCutouts),nameof(AfterTakeTick)));
                MarioAppearance.ReadyTexture=NeutralTexture;
                MarioAppearance.ShadeTexture=ShadeTexture;
            }
            catch(Exception error){Warn(error);}
        }

        internal static void AfterTakeTick()
        {
            try
            {
                Texture2D target=texture?.GetValue(null) as Texture2D;
                if(target==null||!(rows.GetValue(null) is IDictionary colours))return;
                int count=colours.Count;
                Color32[] raw=source.GetValue(null) as Color32[];
                if(raw==null||raw.Length!=SourceWidth*BandHeight||target.width!=SourceWidth+4||target.height!=1024)return;
                if(target!=bound||!ReferenceEquals(raw,original))
                {
                    bound=target;original=raw;copied=0;neutralVersion=-1;
                    wingBand=new Color32[WingWidth*BandHeight];
                    for(int y=0;y<BandHeight;y++)Array.Copy(raw,y*SourceWidth+FirstWing,wingBand,y*WingWidth,WingWidth);
                }
                count=Math.Min(count,target.height/BandHeight);
                if(copied>=count)return;
                for(int row=copied;row<count;row++)target.SetPixels32(FirstWing,row*BandHeight,WingWidth,BandHeight,wingBand);
                target.Apply(false);copied=count;version++;
            }
            catch(Exception error){Warn(error);}
        }

        private static Texture NeutralTexture(Texture actual)
        {
            try
            {
                if(actual!=bound||bound==null)return Texture2D.whiteTexture;
                if(neutral!=null&&neutralVersion==version)return neutral;
                Color32[] pixels=bound.GetPixels32();
                for(int i=0;i<pixels.Length;i++){byte alpha=pixels[i].a;pixels[i]=new Color32(255,255,255,alpha);}
                if(neutral!=null)UnityEngine.Object.Destroy(neutral);
                neutral=new Texture2D(bound.width,bound.height,TextureFormat.RGBA32,false)
                {name="Mario neutral tint with cutouts",filterMode=bound.filterMode,wrapMode=bound.wrapMode};
                neutral.SetPixels32(pixels);neutral.Apply(false,true);neutralVersion=version;
                return neutral;
            }
            catch(Exception error){Warn(error);return actual;}
        }

        private static Texture ShadeTexture()
        {
            long timing=0;
            try
            {
                if(!(source?.GetValue(null) is Color32[] raw)||raw.Length!=SourceWidth*BandHeight)return null;
                if(shade!=null&&ReferenceEquals(raw,shadeSource))return shade;
                timing=PresentationTiming.Begin();
                byte[] eyes=MarioEyeMask.Build(raw);
                var band=new Color32[(SourceWidth+4)*BandHeight];
                for(int y=0;y<BandHeight;y++)for(int x=0;x<SourceWidth+4;x++)
                {
                    byte eye=x<SourceWidth?eyes[y*SourceWidth+x]:(byte)0;
                    byte alpha=x>=FirstWing&&x<SourceWidth?raw[y*SourceWidth+x].a:(byte)255;
                    band[y*(SourceWidth+4)+x]=new Color32(eye,eye,eye,alpha);
                }
                var pixels=new Color32[(SourceWidth+4)*1024];
                for(int row=0;row<16;row++)Array.Copy(band,0,pixels,row*band.Length,band.Length);
                if(shade!=null)UnityEngine.Object.Destroy(shade);
                shade=new Texture2D(SourceWidth+4,1024,TextureFormat.RGBA32,false)
                {name="Mario shade with ROM-shaped white eyes",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                shade.SetPixels32(pixels);shade.Apply(false,true);shadeSource=raw;
                return shade;
            }
            catch(Exception error){Warn(error);return null;}
            finally{PresentationTiming.End(timing,"shade texture setup");}
        }

        internal static void Dispose()
        {
            MarioAppearance.ReadyTexture=null;
            MarioAppearance.ShadeTexture=null;
            if(neutral!=null)UnityEngine.Object.Destroy(neutral);
            if(shade!=null)UnityEngine.Object.Destroy(shade);
            shade=null;shadeSource=null;
            neutral=bound=null;original=wingBand=null;copied=0;neutralVersion=-1;
        }
        private static void Warn(Exception error){if(failures++<4)log?.LogWarning("Mario texture cutouts: "+error.GetBaseException().Message);}
    }
}
