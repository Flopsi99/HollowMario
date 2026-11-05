using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HollowMario
{
    // Per-item data lives here. Art/text/layout/binding code is shared by all entries.
    // Button tokens become actual native button displays, never literal key names.
    internal sealed class PickupReplacement
    {
        internal const string Slot="<mspace=2.2><color=#00000000>|</color></mspace>";
        internal readonly string Id,State,HeadlineKey,Headline,InstructionKey,Template,Instruction,Image,OriginalSprite,TitleKey,Title,Prefix;
        internal readonly string[] HeadlineButtons,Buttons;
        internal readonly bool Enabled;
        internal PickupReplacement(string id,string state,string headlineKey,string headline,
            string instructionKey,string instruction,string image=null,string originalSprite=null,bool enabled=true,
            string titleKey=null,string title=null,string prefix=null)
        {
            Id=id;State=state;HeadlineKey=headlineKey;InstructionKey=instructionKey;
            Template=instruction;Image=image;OriginalSprite=originalSprite;Enabled=enabled;
            TitleKey=titleKey;Title=title;Prefix=prefix;
            Headline=Parse(headline,out HeadlineButtons);Instruction=Parse(instruction,out Buttons);
        }
        private static string Parse(string value,out string[] actions)
        {
            var buttons=new List<string>();
            string result=value==null?null:Regex.Replace(value,@"\{([A-Z_]+)\}",match=>
                {buttons.Add(match.Groups[1].Value);return Slot;});
            actions=buttons.ToArray();return result;
        }
        internal string Text(string key)
        {
            if(key==HeadlineKey)return Headline;
            if(key==InstructionKey)return Instruction;
            if(key==TitleKey)return Title;
            if(Prefix!=null&&key!=null&&key.StartsWith("GET_ITEM_INTRO",StringComparison.Ordinal))return Prefix;
            if(Instruction!=null&&key!=null&&key.StartsWith("BUTTON_DESC_",StringComparison.Ordinal))return "";
            return null;
        }
    }
    internal static class PickupReplacements
    {
        internal static readonly PickupReplacement[] Entries={
            new PickupReplacement("mothwing-cloak","Set Dash","GET_DASH_1","Mario regains his Long Jump.",
                "GET_DASH_2","On the ground, press {DASH} and {JUMP}.\nWhile airborne, press {DASH} and {ATTACK}.\nAllows Mario to turn around quickly when used in the air.",
                "MarioMothwing.png","Dash_Prompt"),
            new PickupReplacement("mantis-claw","Set Walljump","GET_WALLJUMP_1","Mario regains his Wall Jump.",
                "GET_WALLJUMP_2","While airborne, press {JUMP}\nas you meet a wall\nto kick off it.",
                "MarioMantisClaw.png","Wall_Jump_Prompt"),
            new PickupReplacement("wing-cap","Set NA Cyclone","GET_CYCLONE_1",
                "Equip the cap, then perform a Triple Jump to take flight.",
                "GET_CYCLONE_2","Hold UP to gain height, or DOWN to dive toward the ground.\n\nUse {CAP_SELECTION} and press left.",
                "MarioWingCap.png","_0001_charge_cyclone",titleKey:"INV_NAME_ART_CYCLONE",title:"Wing Cap",prefix:"Mastered the"),
            new PickupReplacement("vanish-cap","Set NA D Slash","GET_DSLASH_1",
                "Equip the cap to pass unharmed through enemies, and maybe something else.",
                "GET_DSLASH_2","While vanished, your attacks will pass harmlessly through enemies as well.\n\nUse {CAP_SELECTION} and press up.",
                "MarioVanishCap.png","charge_dash_slash",titleKey:"INV_NAME_ART_UPPER",title:"Vanish Cap",prefix:"Mastered the"),
            new PickupReplacement("metal-cap","Set NA G Slash","GET_GSLASH_1",
                "Equip the cap to strike with greater force, sink through water and acid, and shrug off hazards.",
                "GET_GSLASH_2","Though beware of its high SOUL drain.\n\nUse {CAP_SELECTION} and press right.",
                "MarioMetalCap.png","_0002_charge_slash",titleKey:"INV_NAME_ART_DASH",title:"Metal Cap",prefix:"Mastered the"),
            new PickupReplacement("monarch-wings","Set D Jump","GET_DOUBLEJUMP_1",
                "Press {JUMP} again while in the air to leap a second time.",
                "GET_DOUBLEJUMP_2","Hold the button to jump higher, or steer against your momentum to turn around.",
                "MarioDoubleJump.png","_0009_emperors_wings",titleKey:"INV_NAME_DOUBLEJUMP",title:"Monarch Wings",prefix:"Consumed the"),
            new PickupReplacement("shade-cloak","Set Shadow Dash","GET_SHADOWDASH_1",
                "Perform a Long Jump to be cloaked in shadow.",
                "GET_SHADOWDASH_2","Slip through enemies and their attacks without taking damage.",
                "MarioShadeDash.png","_0000_shadow-dash",titleKey:"INV_NAME_SHADOWDASH",title:"Shade Cloak",prefix:"Absorbed the"),
            new PickupReplacement("crystal-heart","Set Super Dash",null,null,null,null,"03-crystal-heart.png","superdash_prompt"),
            new PickupReplacement("ismas-tear","Pure Seed",null,null,null,null,"05-ismas-tear.png","_0004_acid_armour"),
            new PickupReplacement("focus",null,null,null,null,null,"10-focus.png","Focus_prompt_temp"),
            new PickupReplacement("vengeful-spirit","Set Fireball",null,null,null,null,"11-vengeful-spirit.png","Fireball_prompt"),
            new PickupReplacement("shade-soul","Set Fireball 2",null,null,null,null,"12-shade-soul.png","_0010_shadow_fireball"),
            new PickupReplacement("desolate-dive","SetQuake",null,null,null,null,"13-desolate-dive.png","_0003_quake_spell"),
            new PickupReplacement("descending-dark","Set Quake 2",null,null,null,null,"14-descending-dark.png","_0005_shadow_quake"),
            new PickupReplacement("howling-wraiths","SetScream",null,null,null,null,"15-howling-wraiths.png","_0006_scream_spell"),
            new PickupReplacement("abyss-shriek","SetScream 2",null,null,null,null,"16-abyss-shriek.png","_0007_shadow_scream"),
            new PickupReplacement("dreamgate","Set Dreamgate",null,null,null,null,"19-dreamgate.png","_0008_dream_gate"),
            new PickupReplacement("worldsense",null,null,null,null,null,"27-worldsense.png","world_sense_prompt"),
            new PickupReplacement("charm-tutorial",null,null,null,null,null,"29-charm-tutorial.png","Charms_prompt")
        };
        internal static PickupReplacement Find(string state)
        {
            foreach(PickupReplacement entry in Entries)if(entry.Enabled&&entry.State==state)return entry;
            return null;
        }
    }
}
