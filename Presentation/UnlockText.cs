using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace HollowMario
{
    // Only the creator-edited English fields; all other language entries stay native.
    // Unedited game text stays in the player's installed game, not in source reference exports.
    internal static class UnlockText
    {
        internal static readonly Dictionary<string,string> Entries=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            {"UI/INV_DESC_DASH","Cloak threaded with mothwing strands. Allows the wearer to Long Jump on the ground or through the air."},
            {"UI/INV_DESC_WALLJUMP","Claw carved from bone. Allows the wearer to connect with walls and leap off of them."},
            {"UI/GET_SUPERDASH_1","while on the ground to concentrate the force."},
            {"UI/INV_DESC_SHADOWDASH","Cloak formed from the substance of the Abyss. Allows the wearer to Long Jump through enemies and their attacks without taking damage."},
            {"UI/INV_DESC_SPELL_FOCUS","Focus collected SOUL to repair your body and heal damage.\n\nStrike enemies to gather SOUL."},
            {"Lore Tablets/SENSE_TAB_01","Plumber. Though bound, you shall know the state of the world.\n\nHallownest will be whole again."},
        };
        private static MethodInfo currentLanguage;
        internal static void Arm(Harmony harmony,ManualLogSource log)
        {
            try
            {
                Type language=AccessTools.TypeByName("Language.Language");
                currentLanguage=AccessTools.Method(language,"CurrentLanguage",Type.EmptyTypes);
                harmony.Patch(AccessTools.Method(language,"Get",new[]{typeof(string),typeof(string)}),
                    postfix:new HarmonyMethod(typeof(UnlockText),nameof(After)));
            }
            catch(Exception error){log.LogWarning("Mario's unlock wording: "+error.GetBaseException().Message);}
        }
        private static void After(string __0,string __1,ref string __result)
        {
            if(Entries.TryGetValue(__1+"/"+__0,out string text)&&PickupArtwork.Selected()&&
                Convert.ToString(currentLanguage.Invoke(null,null))=="EN")__result=text;
        }
    }
}
