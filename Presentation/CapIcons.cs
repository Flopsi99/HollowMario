using System;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // One set of creator badges for native inventory sprites and the selector's textures.
    // Bindings follow the actual Nail Art/save flag; a badge's color has no gameplay meaning.
    internal static class CapIcons
    {
        private sealed class Entry
        {
            internal readonly CapKind Kind;
            internal readonly string File, Child, OriginalName;
            internal Texture2D Texture;
            internal Sprite Sprite, Original;
            internal SpriteRenderer Renderer;
            internal bool Failed;
            internal Entry(CapKind kind,string file,string child,string original)
            { Kind=kind;File=file;Child=child;OriginalName=original; }
        }
        private static readonly Entry[] entries={
            new Entry(CapKind.Wing,"WingCapBadge.png","Art Cyclone","Inv_0023_inv_whirlwind_skill"),
            new Entry(CapKind.Vanish,"VanishCapBadge.png","Art Uppercut","Inv_0022_inv_uppercut_skill"),
            new Entry(CapKind.Metal,"MetalCapBadge.png","Art Dash","Inv_0021_inv_dash_strike_skill")
        };
        private static readonly Entry noCap=new Entry(CapKind.None,"NoCapBadge.png",null,null);
        private static readonly Entry locked=new Entry(CapKind.None,"LockedCapBadge.png",null,null);
        private static Transform inventory;
        private static ManualLogSource log;
        private static int failures;

        internal static void Arm(Harmony harmony,ManualLogSource logger)
        {
            log=logger;
            try
            {
                var enabled=AccessTools.Method(AccessTools.TypeByName("InvItemDisplay"),"OnEnable");
                if(enabled==null)throw new MissingMethodException("InvItemDisplay.OnEnable");
                harmony.Patch(enabled,postfix:new HarmonyMethod(typeof(CapIcons),nameof(InventoryOpened)));
            }
            catch(Exception error){Warn(error);}
        }
        private static Entry Find(CapKind kind)
        { if(kind==CapKind.None)return noCap;foreach(var entry in entries)if(entry.Kind==kind)return entry;return null; }

        internal static Texture2D Texture(CapKind kind)=>Load(Find(kind));
        internal static Texture2D LockedTexture=>Load(locked);
        private static Texture2D Load(Entry entry)
        {
            if(entry==null||entry.Failed)return null;
            if(entry.Texture!=null)return entry.Texture;
            Texture2D texture=null;
            try
            {
                using(Stream resource=typeof(CapIcons).Assembly.GetManifestResourceStream("HollowMario."+entry.File))
                using(var bytes=new MemoryStream())
                {
                    if(resource==null)throw new FileNotFoundException(entry.File);
                    resource.CopyTo(bytes);
                    texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    texture.name="Mario cap badge: "+entry.File;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
                    if(!ImageConversion.LoadImage(texture,bytes.ToArray(),true))throw new InvalidDataException(entry.File);
                }
                entry.Texture=texture;return texture;
            }
            catch(Exception error)
            {
                if(texture!=null)UnityEngine.Object.Destroy(texture);
                entry.Failed=true;Warn(error);return null;
            }
        }

        private static void InventoryOpened(object __instance)
        {
            try
            {
                Component item=__instance as Component;
                Transform root=item==null?null:item.transform.parent;
                if(root==null||root.name!="Inv_Items")return;
                Entry entry=null;
                foreach(Entry candidate in entries)if(candidate.Child==item.name){entry=candidate;break;}
                if(entry==null)return;
                Type type=__instance.GetType();
                if(Convert.ToString(type.GetField("playerDataBool").GetValue(__instance))!=CapDefinition.For(entry.Kind).Unlock)return;
                Sprite original=type.GetField("activeSprite").GetValue(__instance) as Sprite;
                if(original==null||original.name!=entry.OriginalName)return;
                if(inventory!=root)
                {
                    Restore();inventory=root;
                }
                entry.Renderer=item.GetComponent<SpriteRenderer>();entry.Original=original;
                bool selected=PickupArtwork.Selected();
                if(entry.Renderer==null)return;
                // InvItemDisplay has just chosen its active/inactive sprite. Leave
                // locked items and foreign art alone; run after this per-item write,
                // rather than the backboard which can enable before the icons do.
                if(entry.Renderer.sprite!=entry.Original&&entry.Renderer.sprite!=entry.Sprite)return;
                if(selected&&entry.Sprite==null)
                {
                        Texture2D texture=Texture(entry.Kind);if(texture==null)return;
                        Vector2 pivot=new Vector2(original.pivot.x/original.rect.width,original.pivot.y/original.rect.height);
                        // Preserve native on-screen height even if a later art revision uses a larger canvas.
                        float ppu=original.pixelsPerUnit*texture.height/original.rect.height;
                        entry.Sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,ppu,0,SpriteMeshType.FullRect);
                        entry.Sprite.name="Mario inventory cap: "+entry.Kind;
                }
                entry.Renderer.sprite=selected&&entry.Sprite!=null?entry.Sprite:entry.Original;
            }
            catch(Exception error){Warn(error);}
        }

        private static void Restore()
        {
            foreach(Entry entry in entries)
            {
                if(entry.Renderer!=null&&entry.Original!=null&&entry.Renderer.sprite==entry.Sprite)
                    entry.Renderer.sprite=entry.Original;
                entry.Renderer=null;entry.Original=null;
            }
            inventory=null;
        }
        internal static void Dispose()
        {
            Restore();
            foreach(Entry entry in entries)Release(entry);
            Release(noCap);Release(locked);
        }
        private static void Release(Entry entry)
        {
            if(entry.Sprite!=null)UnityEngine.Object.Destroy(entry.Sprite);
            if(entry.Texture!=null)UnityEngine.Object.Destroy(entry.Texture);
            entry.Sprite=null;entry.Texture=null;entry.Failed=false;
        }
        private static void Warn(Exception error)
        { if(failures++<4)log?.LogWarning("Mario cap badge: "+error.GetBaseException().Message); }
    }
}
