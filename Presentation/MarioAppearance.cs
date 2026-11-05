using System;
using System.Reflection;
using UnityEngine;

namespace HollowMario
{
    // One property-block owner. Layered effects cannot restore an obsolete cap/shade snapshot.
    internal static class MarioAppearance
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static FieldInfo view,mesh;
        private static object controller;
        private static MeshRenderer bound;
        private static MaterialPropertyBlock original,work;
        private static Color baseColor;
        private static Texture baseTexture;
        private static bool vanish,shade,ready,metal;
        internal static Func<Texture,Texture> ReadyTexture;
        internal static Func<Texture> MetalTexture;
        internal static Func<Texture> ShadeTexture;
        internal static bool Shaded=>shade;
        internal static Color ShadeTint=>Color.black;
        internal static void Configure(Type type)
        { view=type.GetField("Renderer",Any);mesh=view?.FieldType.GetField("MeshRenderer",Any); }
        internal static void Vanish(object c,bool on) { controller=c;vanish=on;Apply(); }
        internal static void Metal(object c,bool on) { controller=c;metal=on;Apply(); }
        internal static void Shade(object c,bool on)
        { if(c!=null)controller=c;shade=on;Apply();if(!on&&metal)MarioMetalTexture.Refresh(controller); }
        internal static void Ready(object c,bool on) { if(c!=null)controller=c;ready=on;Apply(); }

        private static void Apply()
        {
            bool effect=vanish||shade||ready||metal;
            if(!effect) { Restore();return; }
            object renderer=controller==null?null:view?.GetValue(controller);
            MeshRenderer target=renderer==null?null:mesh?.GetValue(renderer) as MeshRenderer;
            if(target==null) { Restore();return; }
            if(target!=bound)
            {
                Restore();bound=target;
                if(original==null){original=new MaterialPropertyBlock();work=new MaterialPropertyBlock();}
                bound.GetPropertyBlock(original);bound.GetPropertyBlock(work);
                baseColor=original.GetColor("_Color");
                if(baseColor.a<=0f)baseColor=bound.sharedMaterial!=null?bound.sharedMaterial.GetColor("_Color"):Color.white;
                baseTexture=original.GetTexture("_MainTex")??bound.sharedMaterial?.mainTexture;
            }
            Texture dark=shade?ShadeTexture?.Invoke():null;
            Color color=shade?(dark!=null?Color.white:ShadeTint):ready?new Color(.55f,.55f,.55f,1):baseColor;
            color.a=baseColor.a*(vanish?.42f:1f);
            work.SetColor("_Color",color);
            Texture actual=metal?(MetalTexture?.Invoke()??baseTexture):baseTexture;
            work.SetTexture("_MainTex",dark??(ready&&!shade?(ReadyTexture?.Invoke(actual)??Texture2D.whiteTexture):actual));
            bound.SetPropertyBlock(work);
        }

        private static void Restore()
        {
            if(bound!=null)bound.SetPropertyBlock(original);
            bound=null;
        }
        internal static void Reset() { vanish=shade=ready=metal=false;Restore();controller=null; }
    }
}
