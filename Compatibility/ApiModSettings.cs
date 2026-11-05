using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using InControl;
using Modding;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using UnityEngine.UI;

namespace HollowMario
{
    public sealed partial class HollowMarioApi : ICustomMenuMod
    {
        public bool ToggleButtonInsideMenu => false;
        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
            => ApiModSettings.Build(modListMenu);
    }

    // Controls use the community API's native menu and binding widgets.
    internal static class ApiModSettings
    {
        private sealed class Bindings : PlayerActionSet
        {
            internal readonly PlayerAction CapKey, CapButton, Room, Health, Bench, Hitboxes;
            internal Bindings()
            {
                CapKey=CreatePlayerAction("Cap selection key");
                CapButton=CreatePlayerAction("Cap selection button");
                Room=CreatePlayerAction("Room reset");
                Health=CreatePlayerAction("Enemy health display");
                Bench=CreatePlayerAction("Return to bench");
                Hitboxes=CreatePlayerAction("Hitbox display");
            }
        }
        private static Bindings bindings;
        private static readonly Dictionary<PlayerAction,ConfigEntry<string>> saved=new Dictionary<PlayerAction,ConfigEntry<string>>();
        private static MenuScreen screen;

        internal static void Initialize(ConfigFile config)
        {
            if(bindings!=null)return;
            bindings=new Bindings();
            Bind(config,bindings.Room,"RoomResetKey","F5");
            Bind(config,bindings.Health,"HealthDisplayKey","F8");
            Bind(config,bindings.Bench,"BenchReturnKey","F9");
            Bind(config,bindings.Hitboxes,"HitboxDisplayKey","F7");
            RefreshCaps();
            var harmony=new Harmony(Plugin.PluginGuid);
            foreach(Type row in new[]{typeof(MappableKey),typeof(MappableControllerButton)})
            {
                Patch(harmony,row,"SetupBindingListenOptions",nameof(ListenOptions),false);
                Patch(harmony,row,"OnBindingAdded",nameof(Added),false);
                Patch(harmony,row,"OnBindingFound",nameof(Found),true);
                Patch(harmony,row,"AbortRebind",nameof(Abort),true);
            }
            harmony.Patch(AccessTools.Method(typeof(MappableControllerButton),"ShowCurrentBinding"),
                prefix:new HarmonyMethod(typeof(ApiModSettings),nameof(ShowButton)));
        }
        private static void Patch(Harmony h,Type row,string method,string hook,bool prefix)
        {
            var patch=new HarmonyMethod(typeof(ApiModSettings),hook);
            h.Patch(AccessTools.Method(row,method),prefix:prefix?patch:null,postfix:prefix?null:patch);
        }
        private static void Bind(ConfigFile config,PlayerAction action,string name,string fallback)
        {
            saved[action]=config.Bind("Debug",name,fallback,"Keyboard/mouse shortcut. Change in Options > Mods > Hollow Mario.");
            Restore(action);
        }
        private static BindingSource Parse(string text)
        {
            if(text.StartsWith("Mouse:",StringComparison.Ordinal))
                return new MouseBindingSource((Mouse)Enum.Parse(typeof(Mouse),text.Substring(6)));
            return new KeyBindingSource((Key)Enum.Parse(typeof(Key),text));
        }
        private static void Restore(PlayerAction action)
        {
            action.ClearBindings();
            if(saved.TryGetValue(action,out var entry))
            {
                try{action.AddBinding(Parse(entry.Value));}
                catch(ArgumentException){action.AddBinding(Parse(action==bindings.Room?"F5":action==bindings.Health?"F8":action==bindings.Hitboxes?"F7":"F9"));}
            }
            else
            {
                var actual=CapControls.Action as PlayerAction;
                if(actual!=null)
                    foreach(var b in actual.UnfilteredBindings)
                        if((action==bindings.CapButton)==(b is DeviceBindingSource))action.AddBinding(CopyBinding(b));
            }
        }
        private static BindingSource CopyBinding(BindingSource binding)
        {
            // InControl rejects a BindingSource already owned by another action.
            // The menu mirrors the gameplay action; each needs its own source object.
            if(binding is KeyBindingSource key)return new KeyBindingSource(key.Control);
            if(binding is MouseBindingSource mouse)return new MouseBindingSource(mouse.Control);
            if(binding is DeviceBindingSource button)return new DeviceBindingSource(button.Control);
            throw new ArgumentException("Unsupported cap binding: "+binding.GetType().Name);
        }
        private static void RefreshCaps(){Restore(bindings.CapKey);Restore(bindings.CapButton);}
        internal static bool Pressed(KeyCode key)
        {
            if(bindings==null||Time.timeScale<=0f||bindings.IsListeningForBinding
                ||(screen!=null&&screen.gameObject.activeInHierarchy)
                ||GameManager.instance==null||GameManager.instance.isPaused||GameManager.instance.gameState!=GlobalEnums.GameState.PLAYING)return false;
            PlayerAction action=key==KeyCode.F5?bindings.Room:key==KeyCode.F8?bindings.Health:key==KeyCode.F7?bindings.Hitboxes:key==KeyCode.F9?bindings.Bench:null;
            return action!=null&&action.WasPressed;
        }
        private static PlayerAction Ours(object row)
        {
            var action=CapAccess.Read(row,"playerAction") as PlayerAction;
            return action!=null&&ReferenceEquals(action.Owner,bindings)?action:null;
        }
        private static void ListenOptions(object __instance)
        {
            var action=Ours(__instance);if(action==null)return;
            // Never let a rebind clear another action, including another mod's bindings.
            action.ListenOptions=bindings.ListenOptions;
            action.ListenOptions.UnsetDuplicateBindingsOnSet=false;
            if(action==bindings.CapButton&&CapAccess.Read(__instance,"unmappableButtons") is System.Collections.IList blocked)
                for(int i=blocked.Count-1;i>=0;i--)
                    if(Convert.ToString(CapAccess.Read(blocked[i],"Control"))=="RightStickButton")blocked.RemoveAt(i);
        }
        private static bool Found(object __instance,BindingSource __1,ref bool __result)
        {
            var action=Ours(__instance);if(action==null)return true;
            bool multiple=__1 is KeyBindingSource key&&key.Control.IncludeCount!=1;
            bool duplicate=bindings.Actions.Any(a=>a!=action&&a.HasBinding(__1));
            if(action==bindings.CapKey||action==bindings.CapButton)duplicate|=CapControls.Conflict(__1);
            if(!multiple&&!duplicate)return true;
            var label=(CapAccess.Read(__instance,"keymapText")??CapAccess.Read(__instance,"buttonmapText")) as Text;
            if(label!=null)label.text=multiple?"Choose one key":"Already used";
            __result=false;return false;
        }
        private static void Added(object __instance,BindingSource __1)
        {
            var action=Ours(__instance);if(action==null)return;
            Store(action,__1);
            action.StopListeningForBinding();
            Restore(action);
            CapAccess.Call(__instance,__instance is MappableKey?"GetBinding":"GetBindingPublic");
            CapAccess.Call(__instance,"ShowCurrentBinding");
            action.ListenOptions=null;
        }
        private static void Store(PlayerAction action,BindingSource binding)
        {
            if(saved.TryGetValue(action,out var entry))
                entry.Value=binding is MouseBindingSource mouse?"Mouse:"+mouse.Control:((KeyBindingSource)binding).Control.GetInclude(0).ToString();
            else CapControls.SaveBinding(binding);
        }
        private static void Abort(object __instance)
        {
            var action=Ours(__instance);if(action==null)return;
            action.StopListeningForBinding();Restore(action);action.ListenOptions=null;
            CapAccess.Call(__instance,__instance is MappableKey?"GetBinding":"GetBindingPublic");
        }
        private static bool ShowButton(MappableControllerButton __instance)
        {
            if(Ours(__instance)!=bindings?.CapButton)return true;
            // The native skin table has no right-stick-click picture.
            var label=CapAccess.Read(__instance,"buttonmapText") as Text;
            var image=CapAccess.Read(__instance,"buttonmapSprite") as Image;
            if(label==null||image==null)return true;
            label.text=CapControls.ControllerName;
            image.sprite=CapAccess.Read(CapAccess.Read(__instance,"uibs"),"blankKey") as Sprite;
            if(CapAccess.Read(__instance,"listeningThrobber") is Component throbber)throbber.gameObject.SetActive(false);
            __instance.interactable=true;return false;
        }
        internal static MenuScreen Build(MenuScreen parent)
        {
            MenuButton back;
            var builder=MenuUtils.CreateMenuBuilderWithBackButton("Hollow Mario",parent,out back);
            if(bindings!=null)
            {
                builder.AddContent(RegularGridLayout.CreateVerticalLayout(90f),c=>
                {
                    if(MarioVolume.Available)
                    {
                        MenuOptionHorizontal volume;
                        c.AddHorizontalOption("MarioVolume",new HorizontalOptionConfig{
                            Label="Mario volume",
                            Options=Enumerable.Range(0,101).Select(i=>i+"%").ToArray(),
                            ApplySetting=(_,i)=>MarioVolume.SetPercent(i),
                            RefreshSetting=(s,_)=>s.optionList.SetOptionTo(MarioVolume.Percent),
                            CancelAction=_=>UIManager.instance.UIGoToDynamicMenu(parent)
                        },out volume);
                        volume.menuSetting.RefreshValueFromGameSettings();
                    }
                    AddKey(c,bindings.CapKey,"Cap selection key",parent);
                    MappableControllerButton button;
                    c.AddButtonBind("Cap selection controller",bindings.CapButton,new ButtonBindConfig{
                        Label="Cap selection button",CancelAction=_=>UIManager.instance.UIGoToDynamicMenu(parent)
                    },out button);
                    AddKey(c,bindings.Room,"Room reset",parent);
                    AddKey(c,bindings.Health,"Enemy health display",parent);
                    AddKey(c,bindings.Hitboxes,"Hitbox display",parent);
                    AddKey(c,bindings.Bench,"Return to bench",parent);
                });
                builder.MenuObject.AddComponent<ApiMenuRefresh>();
            }
            else
            {
                builder.AddContent(RegularGridLayout.CreateVerticalLayout(105f),c=>c.AddMenuButton("RomRequired",new MenuButtonConfig{
                    Label="Finish Mario ROM setup first",SubmitAction=_=>UIManager.instance.UIGoToDynamicMenu(parent),
                    CancelAction=_=>UIManager.instance.UIGoToDynamicMenu(parent)
                }));
            }
            screen=builder.Build();return screen;
        }
        private static void AddKey(ContentArea c,PlayerAction action,string label,MenuScreen parent)
        {
            c.AddKeybind(action.Name,action,new KeybindConfig{Label=label,CancelAction=_=>UIManager.instance.UIGoToDynamicMenu(parent)});
        }
        internal static void RefreshMenu(GameObject menu)
        {
            RefreshCaps();
            foreach(var row in menu.GetComponentsInChildren<MappableKey>(true)){row.GetBinding();row.ShowCurrentBinding();}
            foreach(var row in menu.GetComponentsInChildren<MappableControllerButton>(true)){row.GetBindingPublic();row.ShowCurrentBinding();}
        }
    }
    internal sealed class ApiMenuRefresh : MonoBehaviour
    {
        private void OnEnable()=>ApiModSettings.RefreshMenu(gameObject);
    }
}
