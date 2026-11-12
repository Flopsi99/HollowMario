using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {
        // Uses the replacement host and the game's real input actions. Never
        // borrows the retired bridge's reporter or writes control/save flags.
        private IEnumerator ReplacementGameplayTest()
        {
            if(!MeltyTest.Active)yield break;
            // TestPersistenceGuard is armed before this coroutine starts. Load
            // an existing slot through the game's own path; never create a save.
            Type gameType=AccessTools.TypeByName("GameManager");
            float menuDeadline=Time.unscaledTime+30f;
            object game=null;
            while(Time.unscaledTime<menuDeadline)
            {
                game=TestSingleton(gameType,"UnsafeInstance");
                if(Convert.ToString(Read(game,"gameState"))=="MAIN_MENU")break;
                if(Convert.ToString(Read(game,"gameState"))=="PLAYING")break;
                yield return null;
            }
            if(Convert.ToString(Read(game,"gameState"))=="MAIN_MENU")
            {
                int slot=0;
                for(int i=1;i<=4;i++)if(File.Exists(Path.Combine(Application.persistentDataPath,"user"+i+".dat"))){slot=i;break;}
                if(slot==0){MeltyTest.Report("gameplay-scene",false,"No existing save is available for the protected test");yield break;}
                yield return new WaitForSecondsRealtime(1f);
                AccessTools.Method(gameType,"LoadGameFromUI",new[]{typeof(int)}).Invoke(game,new object[]{slot});
            }
            float sceneDeadline=Time.unscaledTime+45f;
            while(Time.unscaledTime<sceneDeadline&&Convert.ToString(Read(game,"gameState"))!="PLAYING")yield return null;
            bool playing=Convert.ToString(Read(game,"gameState"))=="PLAYING";
            MeltyTest.Report("gameplay-scene",playing,"The save didn't reach a playable room");
            if(!playing)yield break;
            float deadline=Time.unscaledTime+85f;
            object controller=null;
            while(Time.unscaledTime<deadline)
            {
                controller=controllerType?.GetField("Instance")?.GetValue(null);
                if(meltyBenchExited&&controller!=null&&Convert.ToInt32(Read(controller,"MarioId"))>=0)break;
                yield return null;
            }
            bool active=meltyBenchExited&&controller!=null&&Convert.ToInt32(Read(controller,"MarioId"))>=0;
            MeltyTest.Report("mario-active",active,"Mario never became playable after normal bench exit");
            if(!active)yield break;
            yield return new WaitForSecondsRealtime(.5f);
            var renderer=Read(controller,"Renderer");
            MeltyTest.Report("mario-rendered",renderer!=null&&Convert.ToInt32(Read(renderer,"Triangles"))>0,"Mario has no rendered triangles");
            object state=Read(controller,"State");float before=Convert.ToSingle(Read(state,"px"));
            var pad=new MeltyBenchInput(AccessTools.TypeByName("HeroController").Assembly,()=>null,new[]{"DPadRight","Action1"});
            try
            {
                pad.Pressed=name=>name=="DPadRight";pad.Held=true;
                yield return new WaitForSecondsRealtime(1.5f);
                pad.Held=false;pad.Pressed=name=>false;
                state=Read(controller,"State");float after=Convert.ToSingle(Read(state,"px"));
                MeltyTest.Report("mario-moves",Mathf.Abs(after-before)>30f,"Mario didn't move with the game's Right action");
                controllerType.GetMethod("Toggle").Invoke(controller,null);
                yield return null;
                MeltyTest.Report("knight-switch",Convert.ToInt32(Read(controller,"MarioId"))<0,"Toggle didn't restore the Knight");
                controllerType.GetMethod("Toggle").Invoke(controller,null);
                yield return new WaitForSecondsRealtime(1f);
                MeltyTest.Report("mario-returns",Convert.ToInt32(Read(controller,"MarioId"))>=0,"Mario didn't return after toggling back");
            }
            finally{pad.Dispose();}
        }
    }
}
