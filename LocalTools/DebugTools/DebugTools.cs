using System;
using System.Collections.Generic;
using System.Linq;
using Modding;
using Modding.Menu;
using UnityEngine;

namespace HollowMarioDebug
{
    public sealed class DebugTools : Mod,ICustomMenuMod,ITogglableMod
    {
        private DebugSession session;
        private bool subscribed;
        private MenuScreen screen;
        private static readonly string[] OnOff={"Off","On"};
        private static string CharmLabel(int id)=>Language.Language.Get("CHARM_NAME_"+id,"UI");
        public DebugTools():base("Hollow Mario Debug Tools (LOCAL ONLY)"){}
        public override string GetVersion()=>"0.2.1-local";
        public bool ToggleButtonInsideMenu=>false;
        public MenuScreen GetMenuScreen(MenuScreen parent,ModToggleDelegates? toggleDelegates)
            =>screen=MenuUtils.CreateMenuScreen("Hollow Mario Debug Tools (LOCAL ONLY)",GetMenuData(null),parent);
        private void RefreshMenu()
        {
            if(screen!=null)foreach(var row in screen.GetComponentsInChildren<MenuSetting>(true))row.RefreshValueFromGameSettings();
        }
        public override void Initialize()
        {
            if(subscribed)return;
            subscribed=true;
            ModHooks.BeforeSavegameSaveHook+=BeforeSave;
            ModHooks.AfterSavegameLoadHook+=AfterLoad;
            ModHooks.BeforeSceneLoadHook+=BeforeScene;
        }
        public void Unload()
        {
            Restore();
            ModHooks.BeforeSavegameSaveHook-=BeforeSave;
            ModHooks.AfterSavegameLoadHook-=AfterLoad;
            ModHooks.BeforeSceneLoadHook-=BeforeScene;
            subscribed=false;
        }
        private static bool InSave=>GameManager.instance!=null&&HeroController.instance!=null
            &&(GameManager.instance.gameState==GlobalEnums.GameState.PLAYING||GameManager.instance.gameState==GlobalEnums.GameState.PAUSED);
        private int Read(string key)=>PlayerData.instance==null?0:Convert.ToInt32(typeof(PlayerData).GetField(key).GetValue(PlayerData.instance));
        private void Edit(Action<DebugSession> change)
        {
            if(!InSave){LogWarn("Load a save, then open the debug menu from Pause > Options > Mods.");return;}
            if(session==null||!ReferenceEquals(session.Data,PlayerData.instance))session=new DebugSession(PlayerData.instance);
            change(session);
            session.Refresh(RefreshNative);
            RefreshMenu();
        }
        private static void RefreshNative()
        {
            HeroController.instance.CharmUpdate();
            // CharmUpdate alone leaves the slash FSM's prepared damage and the
            // mask display stale. Use the native equipment refresh for both.
            PlayMakerFSM.BroadcastEvent("CHARM INDICATOR CHECK");
        }
        private void Restore()
        {
            if(session==null)return;
            bool current=ReferenceEquals(session.Data,PlayerData.instance);
            session.Restore();session=null;
            if(current&&InSave)RefreshNative();
            RefreshMenu();
        }
        private void BeforeSave(SaveGameData save)
        {
            if(session!=null&&ReferenceEquals(PlayerData.instance,session.Data)&&save.playerData!=null)save.playerData=session.ForSave(save.playerData);
        }
        private void AfterLoad(SaveGameData _){session=null;}
        private string BeforeScene(string name){if(name=="Menu_Title")Restore();return name;}
        private IMenuMod.MenuEntry Choice(string name,string[] values,Func<int> read,Action<int> write,string description=null)
            =>new IMenuMod.MenuEntry{Name=name,Values=values,Loader=()=>Math.Max(0,Math.Min(values.Length-1,read())),Saver=write,
                Description=description??"Temporary debug override. Original upgrade values are kept when saving."};
        private void Toggle(List<IMenuMod.MenuEntry> rows,string label,string key)
        {
            rows.Add(Choice(label,OnOff,()=>Read(key),i=>Edit(s=>s.Toggle(key,i))));
        }
        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            var rows=new List<IMenuMod.MenuEntry>{Choice("Restore starting upgrades",new[]{"—","Restore now"},()=>0,i=>{if(i==1)Restore();},
                "Load a save and use this menu while paused. Changes are temporary; collected pickups and quest progress are not reset.")};
            Toggle(rows,"Mothwing Cloak / Long Jump","hasDash");
            Toggle(rows,"Mantis Claw / Wall Jump","hasWalljump");
            Toggle(rows,"Monarch Wings / Double Jump","hasDoubleJump");
            Toggle(rows,"Crystal Heart","hasSuperDash");
            Toggle(rows,"Shade Cloak / Shadow Dash","hasShadowDash");
            Toggle(rows,"Isma's Tear","hasAcidArmour");
            Toggle(rows,"Wing Cap / Cyclone Slash","hasCyclone");
            Toggle(rows,"Vanish Cap / Great Slash","hasUpwardSlash");
            Toggle(rows,"Metal Cap / Dash Slash","hasDashSlash");
            foreach(var item in new[]{new[]{"Fireball","fireballLevel","Vengeful Spirit","Shade Soul"},new[]{"Dive","quakeLevel","Desolate Dive","Descending Dark"},new[]{"Scream","screamLevel","Howling Wraiths","Abyss Shriek"}})
            {
                string key=item[1];rows.Add(Choice(item[0],new[]{"None",item[2],item[3]},()=>Read(key),i=>Edit(s=>s.Spell(key,i))));
            }
            rows.Add(Choice("Nail",new[]{"Old Nail","Sharpened Nail","Channelled Nail","Coiled Nail","Pure Nail"},()=>Read("nailSmithUpgrades"),i=>Edit(s=>s.Nail(i))));
            rows.Add(Choice("Dream Nail",new[]{"None","Dream Nail","Awoken Dream Nail"},()=>Read("hasDreamNail")==0?0:Read("dreamNailUpgraded")!=0?2:1,i=>Edit(s=>s.Dream(i))));
            Toggle(rows,"Dreamgate","hasDreamGate");
            foreach(var item in new[]{new[]{"Lumafly Lantern","hasLantern"},new[]{"Tram Pass","hasTramPass"},new[]{"King's Brand","hasKingsBrand"},new[]{"Map","hasMap"},new[]{"Quill","hasQuill"},new[]{"Hunter's Journal","hasJournal"},new[]{"Hunter's Mark","hasHuntersMark"},new[]{"Godtuner","hasGodfinder"}})Toggle(rows,item[0],item[1]);
            rows.Add(Choice("Masks",new[]{"5","6","7","8","9"},()=>Read("maxHealthBase")-5,i=>Edit(s=>{s.Put("maxHealthBase",i+5);s.Put("maxHealth",i+5);})));
            rows.Add(Choice("Soul Vessels",new[]{"0","1","2","3"},()=>Read("MPReserveMax")/33,i=>Edit(s=>s.Put("MPReserveMax",i*33))));
            rows.Add(Choice("Charm Notches",Enumerable.Range(3,9).Select(i=>i.ToString()).ToArray(),()=>Read("charmSlots")-3,i=>Edit(s=>{s.Put("charmSlots",i+3);s.Put("overcharmed",s.Get("charmSlotsFilled")>i+3?1:0);})));
            for(int id=1;id<=40;id++)
            {
                int charm=id;string label=CharmLabel(id);
                rows.Add(Choice("Charm: "+label,new[]{"Not owned","Owned","Equipped"},()=>Read("equippedCharm_"+charm)!=0?2:Read("gotCharm_"+charm),i=>Edit(s=>s.Charm(charm,i))));
            }
            rows.Add(Choice("Grimmchild / Carefree Melody",new[]{"None","Level 1","Level 2","Level 3","Level 4","Carefree Melody"},()=>Read("grimmChildLevel"),i=>Edit(s=>
            {s.Put("grimmChildLevel",i);s.Put("charmCost_40",i==5?3:2);s.Charm(40,i==0?0:s.Charm(40)==2?2:1);})));
            rows.Add(Choice("Kingsoul / Void Heart",new[]{"None","Left half","Right half","Kingsoul","Void Heart"},()=>Read("royalCharmState"),i=>Edit(s=>
                s.Royal(i))));
            Toggle(rows,"Unbreakable Heart","fragileHealth_unbreakable");Toggle(rows,"Unbreakable Greed","fragileGreed_unbreakable");Toggle(rows,"Unbreakable Strength","fragileStrength_unbreakable");
            return rows;
        }
    }
}
