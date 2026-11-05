using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    internal static class CapControls
    {
        internal const int MenuActionId=19064;
        private static Type handlerType,keyType,keySource,mouseType,mouseSource,buttonType,buttonSource;
        private static object owner,action;
        private static ConfigEntry<string> keyboard,controller;
        private static ConfigFile settings;
        private static ManualLogSource log;
        private static readonly CapSelectorState selector=new CapSelectorState(()=>Changed?.Invoke());
        private static int failures;
        private static Action<uint> deniedSound;
        // n64decomp/sm64 sounds.h SOUND_MENU_CAMERA_BUZZ + SOUND_STATUS_WAITING.
        private const uint UnavailableSound=0x700E0081u;
        internal static bool Open=>selector.Open;
        internal static string KeyboardName=>BindingLabel(false);
        internal static string ControllerName=>BindingLabel(true);
        internal static string PromptName
        {
            get
            {
                Ensure(Handler);
                bool device=Convert.ToString(CapAccess.Read(Handler,"lastActiveController")).Contains("Device");
                return System.Text.RegularExpressions.Regex.Replace(BindingLabel(device),"([a-z])([A-Z])","$1 $2");
            }
        }
        internal static event Action Changed;
        internal static object Handler=>CapAccess.Read(handlerType,"Instance");
        internal static object Action=>Ensure(Handler);

        internal static void Arm(Harmony harmony,ConfigFile config,ManualLogSource logger)
        {
            log=logger;
            settings=config;
            keyboard=config.Bind("Caps","SelectionKey","Auto","Cap-selection keyboard modifier. Rebind in Keyboard settings.");
            controller=config.Bind("Caps","SelectionButton","RightStickButton","Cap-selection controller button. Rebind in controller settings.");
            handlerType=AccessTools.TypeByName("InputHandler");
            MethodInfo sound=AccessTools.Method(AccessTools.TypeByName("MarioHollow.Native"),"sm64_play_sound_global",new[]{typeof(uint)});
            if(sound!=null)deniedSound=(Action<uint>)Delegate.CreateDelegate(typeof(Action<uint>),sound);
            keyType=AccessTools.TypeByName("InControl.Key");keySource=AccessTools.TypeByName("InControl.KeyBindingSource");
            mouseType=AccessTools.TypeByName("InControl.Mouse");mouseSource=AccessTools.TypeByName("InControl.MouseBindingSource");
            buttonType=AccessTools.TypeByName("InControl.InputControlType");buttonSource=AccessTools.TypeByName("InControl.DeviceBindingSource");
            MethodInfo setup=AccessTools.Method(handlerType,"SetupNonMappableBindings");
            if(setup!=null)harmony.Patch(setup,postfix:new HarmonyMethod(typeof(CapControls),nameof(InputReady)));
            // Controller setup resets EVERY action's bindings, including ours. Restore
            // only after native keyboard/controller mappings have finished rebuilding.
            foreach(string name in new[]{"MapKeyboardLayoutFromGameSettings","MapControllerButtons"})
            {
                MethodInfo mapped=AccessTools.Method(handlerType,name);
                if(mapped!=null)harmony.Patch(mapped,postfix:new HarmonyMethod(typeof(CapControls),nameof(BindingsMapped)));
            }
            MethodInfo map=AccessTools.Method(handlerType,"ActionButtonToPlayerAction");
            if(map!=null)harmony.Patch(map,prefix:new HarmonyMethod(typeof(CapControls),nameof(ResolveAction)));
            foreach(string name in new[]{"ResetDefaultKeyBindings","ResetDefaultControllerButtonBindings"})
            {
                MethodInfo reset=AccessTools.Method(handlerType,name);
                if(reset!=null)harmony.Patch(reset,postfix:new HarmonyMethod(typeof(CapControls),name=="ResetDefaultKeyBindings"?nameof(ResetKeyboard):nameof(ResetController)));
            }
            Ensure(Handler);
        }

        private static void InputReady(object __instance)=>Ensure(__instance);
        private static void BindingsMapped(object __instance){Ensure(__instance);RestoreBindings();}
        private static bool ResolveAction(object __instance,object __0,ref object __result)
        { if(Convert.ToInt32(__0)!=MenuActionId)return true;__result=Ensure(__instance);return false; }
        private static void ResetKeyboard(){keyboard.Value="Auto";RestoreBindings();}
        private static void ResetController(){controller.Value="RightStickButton";RestoreBindings();}

        internal static object Ensure(object handler)
        {
            object actions=CapAccess.Read(handler,"inputActions");
            if(actions==null)return null;
            if(ReferenceEquals(owner,actions)&&action!=null)return action;
            owner=actions;action=null;Close();
            try
            {
                if(CapAccess.Read(actions,"Actions") is IEnumerable existing)
                    foreach(object item in existing)if(Convert.ToString(CapAccess.Read(item,"Name"))=="Cap selection"){action=item;break;}
                if(action==null)action=AccessTools.Method(actions.GetType(),"CreatePlayerAction",new[]{typeof(string)}).Invoke(actions,new object[]{"Cap selection"});
                RestoreBindings();return action;
            }
            catch(Exception error){Warn(error);return null;}
        }

        internal static void RestoreBindings()
        {
            if(action==null)return;
            string before=keyboard.Value;
            bool defaultConflict=(before=="LeftShift"||before=="RightShift"||before=="LeftAlt"||before=="RightAlt")&&Conflict(Binding("Key",before));
            if(before=="Auto"||defaultConflict)
            {
                keyboard.Value="None";
                foreach(string key in new[]{"LeftShift","RightShift","LeftAlt","RightAlt"})
                { object candidate=Binding("Key",key);if(!Conflict(candidate)){keyboard.Value=key;break;} }
            }
            CapAccess.Call(action,"ClearBindings");
            AddSaved(keyboard.Value.StartsWith("Mouse:")?"Mouse":"Key",keyboard.Value.Replace("Mouse:",""));
            AddSaved("Button",controller.Value);
            if(before!=keyboard.Value)settings?.Save();
        }
        private static object Binding(string kind,string value)
        {
            Type e=kind=="Key"?keyType:kind=="Mouse"?mouseType:buttonType;
            Type s=kind=="Key"?keySource:kind=="Mouse"?mouseSource:buttonSource;
            object control=Enum.Parse(e,value);
            if(kind=="Key") { Array keys=Array.CreateInstance(e,1);keys.SetValue(control,0);return Activator.CreateInstance(s,new object[]{keys}); }
            return Activator.CreateInstance(s,new[]{control});
        }
        private static void AddSaved(string kind,string value)
        {
            if(value=="None"||string.IsNullOrEmpty(value))return;
            try { object binding=Binding(kind,value);if(!Conflict(binding))CapAccess.Call(action,"AddBinding",binding); }
            catch(Exception error){Warn(error);}
        }
        internal static bool Conflict(object binding)
        {
            if(binding==null)return true;
            if(CapAccess.Read(owner,"Actions") is IEnumerable all)
                foreach(object other in all)
                    if(!ReferenceEquals(other,action)&&(CapAccess.Read(other,"UnfilteredBindings")??CapAccess.Read(other,"Bindings")) is IEnumerable bindings)
                        foreach(object b in bindings)if(b.Equals(binding))return true;
            return false;
        }
        internal static void SaveBinding(object binding)
        {
            string type=binding.GetType().Name;
            if(type=="KeyBindingSource")keyboard.Value=Convert.ToString(CapAccess.Call(CapAccess.Read(binding,"Control"),"GetInclude",0));
            else if(type=="MouseBindingSource")keyboard.Value="Mouse:"+CapAccess.Read(binding,"Control");
            else if(type=="DeviceBindingSource")controller.Value=Convert.ToString(CapAccess.Read(binding,"Control"));
            RestoreBindings();
            settings?.Save();
        }
        private static string BindingLabel(bool device)
        {
            if(CapAccess.Read(action,"UnfilteredBindings") is IEnumerable bindings)
                foreach(object binding in bindings)
                {
                    string type=binding.GetType().Name;
                    if(device&&type=="DeviceBindingSource")
                    {string name=Convert.ToString(CapAccess.Read(binding,"Control"));return name=="RightStickButton"?"R3":name;}
                    if(!device&&type=="KeyBindingSource")return Convert.ToString(CapAccess.Call(CapAccess.Read(binding,"Control"),"GetInclude",0));
                    if(!device&&type=="MouseBindingSource")return "Mouse "+CapAccess.Read(binding,"Control");
                }
            return "Unmapped";
        }

        internal static void Tick(object current,KnightResetFollow follow)
        {
            try
            {
                object a=Ensure(Handler);
                if(a==null||current==null||!follow.MarioAlive(current)||follow.Busy(current)||Time.timeScale<=0f
                    ||!CapAccess.Flag(Handler,"acceptingInput")||CapAccess.Flag(a,"IsListeningForBinding")) { Close();return; }
                object actions=CapAccess.Read(Handler,"inputActions");
                bool device=Convert.ToString(CapAccess.Read(a,"LastInputType")).Contains("Device");
                if(CapAccess.Flag(a,"WasPressed"))
                {
                    if(!Open&&!CapSystem.CanOpenSelector)
                    {deniedSound?.Invoke(UnavailableSound);return;}
                    selector.Press(device);
                }
                if(!Open)return;
                if(!CapSystem.CanOpenSelector){Close();return;}
                object stick=CapAccess.Read(actions,"rightStick");
                CapKind? choice=selector.Direction(CapAccess.Flag(a,"IsPressed"),CapAccess.Number(stick,"X"),CapAccess.Number(stick,"Y"),
                    Pressed(actions,"left"),Pressed(actions,"up"),Pressed(actions,"right"),Pressed(actions,"down"));
                if(choice.HasValue)selector.Accepted(Choose(choice.Value));
            }
            catch(Exception error){Close();Warn(error);}
        }
        private static bool Choose(CapKind cap)
        {
            bool result=CapSystem.Select(cap,out string message);
            if(!result)CapHud.Message(message);
            return result;
        }
        private static bool Pressed(object actions,string name)=>CapAccess.Flag(CapAccess.Read(actions,name),"WasPressed");
        internal static void Close()=>selector.Close();
        internal static void Dispose(){Close();action=owner=null;}
        private static void Warn(Exception error){if(failures++<5)log?.LogWarning("Cap controls: "+error.GetBaseException().Message);}
    }
}
