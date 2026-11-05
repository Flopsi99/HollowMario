using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // The inventory's ART_UPPER is Oro/hasUpwardSlash; ART_DASH is
    // Sheo/hasDashSlash. Native move names do not describe these save flags.
    internal static class CapDescriptions
    {
        private static ManualLogSource log;
        private static int warnings;
        internal static string Description(string key,string binding)
        {
            string selection="Use ["+binding+"] and press ";
            switch(key)
            {
                case "INV_DESC_ART_CYCLONE":return selection+"left.\nThe signature cap of Nailmaster Mato.\nTake flight and reach new heights.";
                case "INV_DESC_ART_UPPER":return selection+"up.\nThe signature cap of Nailmaster Oro.\nSneak in the shadows, unseen by all.";
                case "INV_DESC_ART_DASH":return selection+"right.\nThe signature cap of Nailmaster Sheo.\nBreak your foes and remain stainless.";
                default:return null;
            }
        }
        internal static string Find(string key,string table,string language,bool selected,string fsm,string owner,string binding)
        {
            if(!selected||language!="EN"||(table!="UI"&&table!="Game Text")||fsm!="Update Text"||owner!="Inv")return null;
            switch(key)
            {
                case "INV_NAME_ART_CYCLONE":return "Wing Cap";
                case "INV_NAME_ART_UPPER":return "Vanish Cap";
                case "INV_NAME_ART_DASH":return "Metal Cap";
                default:return Description(key,binding);
            }
        }
        private static object Read(object value,string member)=>PickupMessages.Member(value,member);
        private static string String(object value,string member)=>Convert.ToString(Read(Read(value,member),"Value"));
        private static string Language()=>Convert.ToString(AccessTools.Method(AccessTools.TypeByName("Language.Language"),"CurrentLanguage",Type.EmptyTypes).Invoke(null,null));
        private static object InventoryFsm(object action,string name)
        {
            object fsm=Read(action,"Fsm");
            return Convert.ToString(Read(fsm,"Name"))==name&&Read(fsm,"GameObject") is GameObject owner&&owner.name=="Inv"?fsm:null;
        }
        internal static void Arm(Harmony harmony,ManualLogSource logger)
        {
            log=logger;
            try
            {
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.GetLanguageString"),"OnEnter"),
                    postfix:new HarmonyMethod(typeof(CapDescriptions),nameof(AfterLanguage)));
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetTextMeshProText"),"OnEnter"),
                    postfix:new HarmonyMethod(typeof(CapDescriptions),nameof(AfterWrite)));
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("HutongGames.PlayMaker.Actions.ActivateGameObject"),"OnEnter"),
                    postfix:new HarmonyMethod(typeof(CapDescriptions),nameof(AfterControls)));
            }
            catch(Exception error){Warn(error);}
        }
        private static void AfterLanguage(object __instance)
        {
            try
            {
                string key=String(__instance,"convName");
                if(key==null||(!key.StartsWith("INV_NAME_ART_",StringComparison.Ordinal)&&!key.StartsWith("INV_DESC_ART_",StringComparison.Ordinal)))return;
                object fsm=InventoryFsm(__instance,"Update Text");if(fsm==null)return;
                string result=Find(key,String(__instance,"sheetName"),Language(),PickupArtwork.Selected(),"Update Text","Inv",CapControls.PromptName);
                if(result!=null)PickupMessages.Set(Read(__instance,"storeValue"),"Value",result);
            }
            catch(Exception error){Warn(error);}
        }
        private static void AfterWrite(object __instance)
        {
            try
            {
                object fsm=InventoryFsm(__instance,"Update Text");if(fsm==null)return;
                GameObject target=Read(__instance,"go") as GameObject;
                if(target==null||(target.name!="Text Desc"&&target.name!="Text Desc Low"))return;
                object variables=Read(fsm,"Variables");
                string key=Convert.ToString(Read(CapAccess.Call(variables,"GetFsmString","Convo Desc"),"Value"));
                if(!PickupArtwork.Selected()||Language()!="EN"||Description(key,"")==null)
                {target.GetComponent<InventoryCapPrompt>()?.Clear();return;}
                Component label=target.GetComponent(AccessTools.TypeByName("TMPro.TextMeshPro"));
                if(label!=null)(target.GetComponent<InventoryCapPrompt>()??target.AddComponent<InventoryCapPrompt>()).Bind(label,key);
            }
            catch(Exception error){Warn(error);}
        }
        private static void AfterControls(object __instance)
        {
            try
            {
                object fsm=InventoryFsm(__instance,"Button Control");
                if(fsm==null||!PickupArtwork.Selected()||Language()!="EN")return;
                string state=Convert.ToString(Read(Read(__instance,"State"),"Name"));
                if(state!="Cyclone Slash"&&state!="Dash Slash"&&state!="Great Slash")return;
                // These three native states normally add Nail Art attack diagrams
                // and use the smaller description region. Our instructions are in
                // the description itself; retain the normal full text region.
                Transform root=((GameObject)Read(fsm,"GameObject")).transform;
                root.Find("Item Control")?.gameObject.SetActive(false);
                root.Find("Text Desc Low")?.gameObject.SetActive(false);
                root.Find("Text Desc")?.gameObject.SetActive(true);
            }
            catch(Exception error){Warn(error);}
        }
        internal static void Warn(Exception error)
        {if(warnings++<3)log?.LogWarning("Mario's cap descriptions: "+error.GetBaseException().Message);}
    }

    // Refresh only on the game's existing device-change event and panel enable;
    // there is no input polling or global text/table replacement.
    internal sealed class InventoryCapPrompt:MonoBehaviour
    {
        private Component label;
        private string key;
        private object handler;
        private Delegate changed;
        internal void Bind(Component target,string descriptionKey)
        {label=target;key=descriptionKey;enabled=true;Subscribe();Refresh();}
        private void Subscribe()
        {
            if(changed!=null||label==null||!isActiveAndEnabled)return;
            handler=PickupMessages.Handler();if(handler==null)return;
            changed=Delegate.CreateDelegate(PickupMessages.Changed.EventHandlerType,this,nameof(Refresh));
            PickupMessages.Changed.AddEventHandler(handler,changed);
        }
        public void Refresh()
        {
            try
            {
                if(label!=null&&PickupArtwork.Selected())
                    PickupMessages.Set(label,"text",CapDescriptions.Description(key,CapControls.PromptName));
            }
            catch(Exception error){CapDescriptions.Warn(error);}
        }
        internal void Clear(){Unsubscribe();label=null;key=null;enabled=false;}
        private void OnEnable(){Subscribe();Refresh();}
        private void Unsubscribe()
        {
            if(changed!=null)PickupMessages.Changed.RemoveEventHandler(handler,changed);
            changed=null;handler=null;
        }
        private void OnDisable()=>Unsubscribe();
        private void OnDestroy()=>Unsubscribe();
    }
}
