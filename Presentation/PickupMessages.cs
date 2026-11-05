using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    // Scope localized replacements to this acquisition state, never the shared language
    // table. Native item grants, save flags, timing and dismissal remain authoritative.
    internal static class PickupMessages
    {
        internal sealed class Context {internal PickupReplacement Entry;internal GameObject Panel;internal string Key;}
        private const BindingFlags Any=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static FieldInfo key,stored,target,handlerInstance;
        private static Type handlerType,textType;
        private static PropertyInfo text;
        private static ManualLogSource log;
        private static int warnings;
        internal static EventInfo Changed;

        internal static void Arm(Harmony harmony,ManualLogSource logger)
        {
            log=logger;
            try
            {
                Type language=AccessTools.TypeByName("HutongGames.PlayMaker.Actions.GetLanguageString");
                Type write=AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetTextMeshProText");
                key=AccessTools.Field(language,"convName");stored=AccessTools.Field(language,"storeValue");
                target=AccessTools.Field(write,"go");handlerType=AccessTools.TypeByName("InputHandler");
                handlerInstance=AccessTools.Field(handlerType,"Instance");
                textType=AccessTools.TypeByName("TMPro.TextMeshPro");text=AccessTools.Property(textType,"text");
                Changed=handlerType.GetEvent("RefreshActiveControllerEvent",Any);
                if(key==null||stored==null||target==null||text==null||Changed==null||handlerInstance==null)
                    throw new MissingMemberException("Pickup message metadata");
                Type button=AccessTools.TypeByName("HeroActionButton");
                foreach(PickupReplacement entry in PickupReplacements.Entries)
                {
                    foreach(string action in entry.HeadlineButtons)Enum.Parse(button,PromptAction(action));
                    foreach(string action in entry.Buttons)Enum.Parse(button,PromptAction(action));
                }
                harmony.Patch(AccessTools.Method(language,"OnEnter"),
                    prefix:new HarmonyMethod(typeof(PickupMessages),nameof(BeforeLanguage)),
                    postfix:new HarmonyMethod(typeof(PickupMessages),nameof(AfterLanguage)));
                harmony.Patch(AccessTools.Method(write,"OnEnter"),
                    prefix:new HarmonyMethod(typeof(PickupMessages),nameof(BeforeWrite)),
                    postfix:new HarmonyMethod(typeof(PickupMessages),nameof(AfterWrite)));
            }
            catch(Exception error){Warn(error);}
        }
        private static object Read(object obj,string name)
        {
            if(obj==null)return null;Type type=obj.GetType();
            return type.GetProperty(name,Any)?.GetValue(obj,null)??type.GetField(name,Any)?.GetValue(obj);
        }
        internal static void Set(object obj,string name,object value)
        {obj.GetType().GetProperty(name,Any).SetValue(obj,value,null);}
        private static void BeforeLanguage(object __instance,out Context __state)
        {
            __state=null;
            try
            {
                PickupReplacement entry=PickupArtwork.Match(__instance,out GameObject panel);
                if(entry!=null)__state=new Context{Entry=entry,Panel=panel,Key=Convert.ToString(Read(key.GetValue(__instance),"Value"))};
            }
            catch(Exception error){Warn(error);}
        }
        private static void AfterLanguage(object __instance,Context __state)
        {
            try
            {
                string replacement=__state?.Entry.Text(__state.Key);
                if(replacement!=null)Set(stored.GetValue(__instance),"Value",replacement);
            }
            catch(Exception error){Warn(error);}
        }
        private static void BeforeWrite(object __instance,out Context __state)
        {
            __state=null;
            try
            {
                PickupReplacement entry=PickupArtwork.Match(__instance,out GameObject panel);
                if(entry!=null)__state=new Context{Entry=entry,Panel=panel};
            }
            catch(Exception error){Warn(error);}
        }
        private static void AfterWrite(object __instance,Context __state)
        {
            if(__state==null)return;
            try
            {
                GameObject go=target.GetValue(__instance) as GameObject;if(go==null)return;
                PickupArtwork.ApplyToPanel(__state.Panel,__state.Entry);
                if(__state.Entry.Instruction==null)return;
                bool first=go.name=="Msg 1";
                if(!first&&go.name!="Msg 2")return;
                Transform button=__state.Panel.transform.Find("Button");
                if(button!=null)button.gameObject.SetActive(false);
                string[] actions=first?__state.Entry.HeadlineButtons:__state.Entry.Buttons;
                if(actions.Length>0)
                {
                    var prompt=go.GetComponent<PickupPrompt>()??go.AddComponent<PickupPrompt>();
                    prompt.Bind(go.GetComponent(textType),button,__state.Entry,
                        first?__state.Entry.Headline:__state.Entry.Instruction,actions);
                }
                // Native actions write Msg 1 then Msg 2. Measure only once both contain
                // the replacement, retaining the native title/art/fade hierarchy.
                if(!first)PickupPrompt.ArrangeParagraphs(__state.Panel,textType);
            }
            catch(Exception error){Warn(error);}
        }
        internal static object Handler()=>handlerInstance.GetValue(null);
        internal static string PromptAction(string action)=>action=="CAP_SELECTION"?CapControls.MenuActionId.ToString():action;
        internal static object Member(object obj,string name)=>Read(obj,name);
        internal static void Warn(Exception error)
        {if(warnings++<3)log?.LogWarning("Mario's pickup message: "+error.GetBaseException().Message);}
    }
}
