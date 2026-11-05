using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Only registered acquisition panel states get the player's illustrations.
    // Temporarily substitute its sprite parameter, then restore it even on exceptions.
    // Keep reward logic, native animation, other pickups and inventory sprites intact.
    internal static class PickupArtwork
    {
        internal sealed class Swap { internal object Parameter; internal UnityEngine.Object Original; }
        private static FieldInfo instance, wanted, spriteField;
        private static PropertyInfo actionFsm, actionState, fsmName, fsmOwner, stateName, value;
        private static readonly Dictionary<PickupReplacement,Texture2D> textures=new Dictionary<PickupReplacement,Texture2D>();
        private static readonly Dictionary<PickupReplacement,Sprite> pictures=new Dictionary<PickupReplacement,Sprite>();
        private static readonly HashSet<PickupReplacement> failedImages=new HashSet<PickupReplacement>();
        private static readonly List<UnlockPicture> specialPictures=new List<UnlockPicture>();
        private static ManualLogSource log;
        private static int warnings;

        internal static void Arm(Harmony harmony, Type controller, ManualLogSource logger)
        {
            log=logger;
            try
            {
                Type action=AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetSpriteRendererSprite");
                instance=AccessTools.Field(controller,"Instance");wanted=AccessTools.Field(controller,"Wanted");
                spriteField=AccessTools.Field(action,"sprite");
                actionFsm=AccessTools.Property(action,"Fsm");actionState=AccessTools.Property(action,"State");
                fsmName=AccessTools.Property(actionFsm.PropertyType,"Name");
                fsmOwner=AccessTools.Property(actionFsm.PropertyType,"GameObject");
                stateName=AccessTools.Property(actionState.PropertyType,"Name");
                value=AccessTools.Property(spriteField.FieldType,"Value");
                if(instance==null||wanted==null||fsmName==null||fsmOwner==null||stateName==null||value==null)
                    throw new MissingMemberException("Pickup picture metadata");
                harmony.Patch(AccessTools.Method(action,"OnEnter"),
                    prefix:new HarmonyMethod(typeof(PickupArtwork),nameof(Before)),
                    finalizer:new HarmonyMethod(typeof(PickupArtwork),nameof(After)));
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("PlayMakerFSM"),"OnEnable"),
                    postfix:new HarmonyMethod(typeof(PickupArtwork),nameof(AfterEnabled)));
            }
            catch(Exception error){Warn(error);}
        }

        internal static Sprite Load(PickupReplacement entry,Sprite original)
        {
            if(pictures.TryGetValue(entry,out Sprite picture))return picture;
            if(string.IsNullOrEmpty(entry.Image)||failedImages.Contains(entry))return null;
            Texture2D texture=null;
            try
            {
            using(Stream resource=typeof(PickupArtwork).Assembly.GetManifestResourceStream("HollowMario."+entry.Image))
            using(var bytes=new MemoryStream())
            {
                if(resource==null)throw new FileNotFoundException("Bundled pickup picture: "+entry.Id);
                resource.CopyTo(bytes);
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                texture.name="Mario pickup: "+entry.Id;texture.wrapMode=TextureWrapMode.Clamp;
                texture.filterMode=FilterMode.Bilinear;
                if(!ImageConversion.LoadImage(texture,bytes.ToArray(),true))throw new InvalidDataException("Pickup PNG");
            }
            Vector2 pivot=new Vector2(original.pivot.x/original.rect.width,original.pivot.y/original.rect.height);
            picture=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),pivot,original.pixelsPerUnit,0,SpriteMeshType.FullRect);
            picture.name="Mario pickup: "+entry.Id;
            pictures.Add(entry,picture);textures.Add(entry,texture);
            return picture;
            }
            catch(Exception error)
            {
                if(texture!=null)UnityEngine.Object.Destroy(texture);
                failedImages.Add(entry);Warn(error);return null;
            }
        }

        internal static bool Selected()
        {
            object mario=instance?.GetValue(null);
            return mario!=null&&wanted.GetValue(mario) is bool selected&&selected;
        }
        // These three illustrations are independent tutorial/story objects, rather
        // than Msg Control acquisitions. Find them once when their scene loads,
        // including inactive prompts; also catch the instantiated charm tutorial.
        internal static void SceneLoaded()
        {
            specialPictures.RemoveAll(picture=>picture==null);
            foreach(SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                if(renderer.gameObject.scene.IsValid())ObserveSpecial(renderer);
        }
        private static void AfterEnabled(Component __instance)
        {
            if(__instance.name=="Image"||__instance.name=="Charms_prompt")
                ObserveSpecial(__instance.GetComponent<SpriteRenderer>());
        }
        private static void ObserveSpecial(SpriteRenderer renderer)
        {
            if(renderer==null||renderer.sprite==null||renderer.GetComponent<UnlockPicture>()!=null)return;
            foreach(PickupReplacement entry in PickupReplacements.Entries)
                if(entry.State==null&&entry.OriginalSprite==renderer.sprite.name)
                {
                    var picture=renderer.gameObject.AddComponent<UnlockPicture>();
                    picture.Bind(renderer,entry);specialPictures.Add(picture);return;
                }
        }
        internal static PickupReplacement Match(object action,out GameObject panel)
        {
            panel=null;
            if(!Selected())return null;
            object fsm=actionFsm.GetValue(action,null),state=actionState.GetValue(action,null);
            if(fsm==null||state==null||!Equals(fsmName.GetValue(fsm,null),"Msg Control"))return null;
            PickupReplacement entry=PickupReplacements.Find(Convert.ToString(stateName.GetValue(state,null)));
            if(entry==null)return null;
            GameObject owner=fsmOwner.GetValue(fsm,null) as GameObject;
            if(owner==null||!owner.name.StartsWith("UI Msg Get Item",StringComparison.Ordinal))return null;
            panel=owner;return entry;
        }
        internal static void ApplyToPanel(GameObject panel,PickupReplacement entry)
        {
            try
            {
                // The normal live prefab already has its icon assigned; the White variant
                // uses a SetSpriteRendererSprite action. Support both presentation paths.
                Transform icon=panel.transform.Find("Icon");
                SpriteRenderer renderer=icon==null?null:icon.GetComponent<SpriteRenderer>();
                if(renderer!=null&&renderer.sprite!=null&&renderer.sprite.name==entry.OriginalSprite)
                {
                    Sprite replacement=Load(entry,renderer.sprite);if(replacement!=null)renderer.sprite=replacement;
                }
            }
            catch(Exception error){Warn(error);}
        }

        private static void Before(object __instance,out Swap __state)
        {
            __state=null;
            try
            {
                PickupReplacement entry=Match(__instance,out GameObject owner);if(entry==null)return;
                object parameter=spriteField.GetValue(__instance);
                Sprite original=value.GetValue(parameter,null) as Sprite;
                if(original==null||original.name!=entry.OriginalSprite)return;
                Sprite replacement=Load(entry,original);if(replacement==null)return;
                __state=new Swap{Parameter=parameter,Original=original};
                value.SetValue(parameter,replacement,null);
            }
            catch(Exception error){Warn(error);}
        }
        private static void After(Swap __state)
        {
            if(__state!=null)value.SetValue(__state.Parameter,__state.Original,null);
        }
        private static void Warn(Exception error)
        {
            if(warnings++<4)log?.LogWarning("Mario's pickup kept its original artwork: "+error.GetBaseException().Message);
        }
        internal static void Dispose()
        {
            foreach(UnlockPicture picture in specialPictures)if(picture!=null)
            {picture.Restore();UnityEngine.Object.Destroy(picture);}
            specialPictures.Clear();
            foreach(Sprite picture in pictures.Values)if(picture!=null)UnityEngine.Object.Destroy(picture);
            foreach(Texture2D texture in textures.Values)if(texture!=null)UnityEngine.Object.Destroy(texture);
            pictures.Clear();textures.Clear();failedImages.Clear();
        }
    }

    internal sealed class UnlockPicture:MonoBehaviour
    {
        private SpriteRenderer target;
        private Sprite original,replacement;
        private PickupReplacement entry;
        internal void Bind(SpriteRenderer renderer,PickupReplacement definition)
        {target=renderer;original=renderer.sprite;entry=definition;LateUpdate();}
        private void LateUpdate()
        {
            if(target==null)return;
            if(PickupArtwork.Selected())
            {
                if(replacement==null)replacement=PickupArtwork.Load(entry,original);
                if(replacement!=null)target.sprite=replacement;
            }
            else Restore();
        }
        internal void Restore(){if(target!=null&&original!=null)target.sprite=original;}
    }
}
