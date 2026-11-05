using System;
using System.Reflection;
using System.Collections.Generic;

namespace HollowMario
{
    // The base mod calls OnKnightHit for every TakeDamage attempt, including early returns
    // and skipped originals. React only to real loss or Metal's confirmed absorbed hit.
    internal static class DamageReaction
    {
        internal static event Action<object> Confirmed;
        internal struct Frame
        {
            internal object Hero, Data;
            internal FieldInfo Red, Blue;
            internal int StartRed, StartBlue, CompareRed, CompareBlue;
            internal bool Valid, Delivered, Maskless, Presentation;
            internal string Blockers;
            internal string BlockerKey;
            internal object ContactSource;
            internal int Damage,Hazard;
        }

        [ThreadStatic] private static Frame current;
        private static Type heroType, dataType,bossType;
        private static FieldInfo dataField, redField, blueField, flagsField;
        private static readonly HashSet<string> reported=new HashSet<string>();
        private static readonly Dictionary<Type,Dictionary<string,FieldInfo>> diagnosticFields=new Dictionary<Type,Dictionary<string,FieldInfo>>();
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static object ContactSource=>current.ContactSource;
        internal static bool PresentationHit=>current.Presentation;
        internal static int ContactDamage=>Math.Max(1,current.Damage);
        internal static int ContactHazard=>current.Hazard;
        internal static void CaptureDamageInputs(object __0,int __2,int __3)
        {
            if(!current.Valid)return;
            current.ContactSource=__0;current.Damage=__2;current.Hazard=__3;
        }

        internal static void BeforeDamage(object __instance, out Frame __state)
        {
            __state = current;
            current = default(Frame);
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            object controller = follow?.Controller();
            if (controller == null || !ReferenceEquals(follow.Hero(controller), __instance)) return;
            bool presentation=false;
            presentation=CapAccess.Flag(controller,"Wanted")&&ConversationMario.CanInterruptForDamage(__instance);
            if(!follow.MarioAlive(controller)&&!presentation)return;
            if (heroType != __instance.GetType())
            {
                heroType = __instance.GetType();
                dataField = heroType.GetField("playerData", Fields);
                flagsField = heroType.GetField("cState", Fields);
                bossType=HarmonyLib.AccessTools.TypeByName("BossSceneController");
            }
            object data = dataField?.GetValue(__instance);
            if (data == null) return;
            if (dataType != data.GetType())
            {
                dataType = data.GetType();
                redField = dataType.GetField("health", Fields);
                blueField = dataType.GetField("healthBlue", Fields);
            }
            if (redField == null || blueField == null) return;
            int red = (int)redField.GetValue(data), blue = (int)blueField.GetValue(data);
            current = new Frame { Hero=__instance, Data=data, Red=redField, Blue=blueField,
                StartRed=red, CompareRed=red, StartBlue=blue, CompareBlue=blue, Valid=true, Presentation=presentation };
            object flags=flagsField?.GetValue(__instance);
            current.Blockers="invulnerable="+Read(flags,"invulnerable")+", recoil="+Read(flags,"recoiling")
                +", shade="+Read(flags,"shadowDashing")+", superdash="+Read(flags,"superDashing")+", parry="+Read(__instance,"parryInvulnTimer")
                +", mode="+Read(__instance,"damageMode")+", invincible="+Read(data,"isInvincible")+", invinciTest="+Read(data,"invinciTest");
            // Numeric parry countdowns used to consume all diagnostic slots within
            // one brief window, hiding any genuinely stuck protection later on.
            current.BlockerKey=System.Text.RegularExpressions.Regex.Replace(current.Blockers,@"parry=[^,]*(?:,[0-9]+)?",
                "parry="+(CapAccess.Number(__instance,"parryInvulnTimer")>0f?"active":"none"));
            string recovery=", hazardRespawning="+Read(flags,"hazardRespawning")+", recoilFrozen="+Read(flags,"recoilFrozen")
                +", transition="+Read(__instance,"transitionState")+", takeNoDamage="+Read(__instance,"takeNoDamage")
                +", bossTransition="+CapAccess.Flag(bossType,"IsTransitioning");
            current.BlockerKey+=recovery;
            current.Blockers+=recovery+", marioAction="+follow.Action(controller).ToString("X")+", scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        private static string Read(object value,string name)
        {
            if(value==null)return "?";
            Type type=value.GetType();
            if(!diagnosticFields.TryGetValue(type,out Dictionary<string,FieldInfo> fields))
            {fields=new Dictionary<string,FieldInfo>();diagnosticFields[type]=fields;}
            if(!fields.TryGetValue(name,out FieldInfo field)){field=type.GetField(name,Fields);fields[name]=field;}
            return Convert.ToString(field?.GetValue(value));
        }

        internal static bool BeforeMarioHit(object __instance)
        {
            if (!current.Valid || current.Delivered) return false;
            KnightResetFollow follow = Plugin.Instance?.knightFollow;
            if (follow == null || !follow.MarioAlive(__instance)
                || !ReferenceEquals(follow.Hero(__instance), current.Hero)) return false;
            // Check the pools independently: consuming blue health is damage even if the
            // game's charm/max-health normalization raises the red health in the same call.
            bool lost = current.Maskless || (int)current.Red.GetValue(current.Data) < current.CompareRed
                || (int)current.Blue.GetValue(current.Data) < current.CompareBlue;
            if (lost)
            {
                current.Delivered=true;
                Confirmed?.Invoke(current.Hero);
            }
            return lost;
        }

        // Metal lets the native hit/recoil happen, but intercepts the actual health spend.
        // A rejected hit never reaches that spend and cannot request a reaction here.
        internal static void ConfirmMasklessHit(object data)
        { if(current.Valid&&ReferenceEquals(current.Data,data))current.Maskless=true; }

        internal static void FinishDamage(Frame __state)
        {
            // There is no other bridge's TakeDamage postfix to call OnKnightHit.
            // Deliver exactly once, after the host actually spent health (or an
            // explicitly accepted Metal hit), never for rejected contacts.
            if(current.Valid&&!current.Delivered&&(current.Maskless||(int)current.Red.GetValue(current.Data)<current.CompareRed
                ||(int)current.Blue.GetValue(current.Data)<current.CompareBlue))
            {
                current.Delivered=true;
                Plugin.Instance?.Log.LogInfo("Independent accepted damage: red="+current.StartRed+"->"+current.Red.GetValue(current.Data)+", blue="+current.StartBlue+"->"+current.Blue.GetValue(current.Data)+", hazard="+current.Hazard+"; "+current.Blockers);
                try{Confirmed?.Invoke(current.Hero);}catch(Exception error){Plugin.Instance?.Log.LogWarning("Independent damage reaction failed: "+error);}
            }
            if(current.Valid&&!current.Delivered&&(int)current.Red.GetValue(current.Data)==current.StartRed
                &&(int)current.Blue.GetValue(current.Data)==current.StartBlue&&reported.Count<64&&reported.Add(current.BlockerKey))
                Plugin.Instance?.Log.LogInfo("Damage attempt caused no mask loss: "+current.Blockers+".");
            if (__state.Valid && current.Valid && ReferenceEquals(__state.Data, current.Data))
            {
                // A nested hit already owns its own reaction. Exclude its entire health
                // change from the outer comparison rather than reacting to it a second time.
                __state.CompareRed += (int)current.Red.GetValue(current.Data) - current.StartRed;
                __state.CompareBlue += (int)current.Blue.GetValue(current.Data) - current.StartBlue;
            }
            current = __state;
        }
    }
}
