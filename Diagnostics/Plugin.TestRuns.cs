using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin
    {
        private string meltyStage="loading";
        private bool meltyDownAcknowledged,meltyBenchExited;

        private IEnumerator PrepareBenchForMeltyTest()
        {
            if(!MeltyTest.Active)yield break;
            Type heroType=AccessTools.TypeByName("HeroController"),gameType=AccessTools.TypeByName("GameManager"),inputType=AccessTools.TypeByName("InputHandler");
            float deadline=Time.unscaledTime+60f;
            object hero=null;
            bool sceneReady=false;
            while(Time.unscaledTime<deadline)
            {
                hero=TestSingleton(heroType,"SilentInstance");
                object game=TestSingleton(gameType,"UnsafeInstance");
                if(hero is Component body&&body!=null&&Convert.ToString(Read(game,"gameState"))=="PLAYING"&&!Equals(Read(Read(hero,"cState"),"transitioning"),true)){sceneReady=true;break;}
                yield return null;
            }
            if(!sceneReady){MeltyTest.Report("mario-on-m",false,"test setup: the playable scene never loaded");meltyStage="failed-scene";yield break;}
            yield return new WaitForSecondsRealtime(1f); // let native bench entry settle
            // Room arrivals and scripted wake-ups can retain control after the
            // scene becomes PLAYING. Wait for their native completion, or for a
            // real bench to accept Down; never treat that transition as a failure.
            while(Time.unscaledTime<deadline
                &&!(Equals(Read(hero,"acceptingInput"),true)&&!Equals(Read(hero,"controlReqlinquished"),true))
                &&!Equals(Read(Read(hero,"playerData"),"atBench"),true))
            {
                meltyStage="waiting-control";
                yield return null;
                hero=TestSingleton(heroType,"SilentInstance");
            }
            if(Equals(Read(hero,"acceptingInput"),true)&&!Equals(Read(hero,"controlReqlinquished"),true))
            {meltyBenchExited=true;meltyStage="gameplay";yield break;}
            if(!Equals(Read(Read(hero,"playerData"),"atBench"),true))
            {MeltyTest.Report("mario-on-m",false,"test setup: control is held outside a bench; no input or unlock forced");meltyStage="failed-control";yield break;}
            meltyStage="bench-down";
            MeltyBenchInput pad=null;
            try{pad=new MeltyBenchInput(heroType.Assembly,()=>Read(Read(TestSingleton(inputType,"Instance"),"inputActions"),"down"));}
            catch(Exception error){MeltyTest.Report("mario-on-m",false,"test pad: "+error.GetBaseException().Message);meltyStage="failed-input";}
            if(pad==null)yield break;
            try
            {
                // A real press/release, bounded retries, then verify the game's
                // own action saw Down AND its ordinary bench exit gave control.
                for(int attempt=0;attempt<2&&!meltyBenchExited;attempt++)
                {
                    pad.Held=true;
                    yield return new WaitForSecondsRealtime(0.35f);
                    pad.Held=false;
                    float until=Time.unscaledTime+3f;
                    while(Time.unscaledTime<until)
                    {
                        meltyDownAcknowledged=pad.Acknowledged;
                        if(Equals(Read(hero,"acceptingInput"),true)&&!Equals(Read(hero,"controlReqlinquished"),true))
                        {meltyBenchExited=true;break;}
                        yield return null;
                    }
                }
                if(!pad.Sent||!meltyDownAcknowledged||!meltyBenchExited)
                {
                    meltyStage="failed-bench";
                    MeltyTest.Report("mario-on-m",false,"test bench exit: sent="+pad.Sent+", game saw Down="+meltyDownAcknowledged+", control returned="+meltyBenchExited);
                    Log.LogWarning("Melty test stopped at its bench-exit assertion; no control flags were forced.");
                }
                else{meltyStage="gameplay";Log.LogInfo("Melty test: the game's Down action acknowledged the test pad and normal bench exit returned control.");}
                pad.Held=false;
                yield return null;
            }
            finally{pad.Dispose();}
        }

        private IEnumerator ResetFollowTest()
        {
            Type testType = controllerType == null ? null : controllerType.Assembly.GetType("MarioHollow.MeltyTest", false);
            FieldInfo doneField = testType == null ? null : testType.GetField("ScriptDone", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            float until = Time.unscaledTime + 240f;
            while (doneField != null && Time.unscaledTime < until && !(bool)doneField.GetValue(null))
            {
                yield return null;
            }
            if (doneField == null)
            {
                MeltyTest.Report(MovingSafetyFloorSheet.ResetCheck, false, "the base test controller was not found");
                yield break;
            }
            yield return new WaitForSeconds(2f);
            if (knightFollow == null || !knightFollow.Armed)
            {
                MeltyTest.Report(MovingSafetyFloorSheet.ResetCheck, false, "reset follow not armed: " + (knightFollow == null ? "missing" : knightFollow.Problem));
                yield break;
            }
            object controller = knightFollow.Controller();
            Transform knight = knightFollow.HeroTransform(controller);
            Vector3? before = knightFollow.Feet(controller);
            if (controller == null || knight == null || before == null)
            {
                MeltyTest.Report(MovingSafetyFloorSheet.ResetCheck, false, "Mario wasn't active to reset");
                yield break;
            }
            float startX = before.Value.x;
            knight.position = knight.position + new Vector3(4f, 0f, 0f);
            yield return new WaitForSeconds(0.5f);
            Vector3? after = knightFollow.Feet(controller);
            if (after == null)
            {
                MeltyTest.Report(MovingSafetyFloorSheet.ResetCheck, false, "Mario's position couldn't be read after the reset");
                yield break;
            }
            bool followed = knightFollow.ResetsFollowed > 0 && Mathf.Abs(after.Value.x - (startX + 4f)) < 1.5f;
            string why = "start x " + startX.ToString("0.0") + ", after x " + after.Value.x.ToString("0.0") + ", resets " + knightFollow.ResetsFollowed;
            MeltyTest.Report(MovingSafetyFloorSheet.ResetCheck, followed, followed ? null : why);
            Log.LogInfo("melty test: " + (followed ? "pass " : "fail ") + MovingSafetyFloorSheet.ResetCheck + ": " + why);
        }

        private IEnumerator WatchMeltyState()
        {
            string testFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Melty","test",MovingSafetyFloorSheet.TestSlug+".json");
            if(!File.Exists(testFile))yield break;
            Match field=Regex.Match(File.ReadAllText(testFile),"\"state\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if(!field.Success)yield break;
            string stateFile=Path.Combine(Path.GetDirectoryName(testFile),Path.GetFileName(Regex.Unescape(field.Groups[1].Value)));
            // Melty keeps the game behind the creator's windows. Permit only
            // test runs to keep ticking without focus; normal play is untouched.
            bool previousBackground=Application.runInBackground;
            Application.runInBackground=true;
            try
            {
                while(File.Exists(testFile))
                {
                    Type heroType=AccessTools.TypeByName("HeroController"),gameType=AccessTools.TypeByName("GameManager");
                    object hero=TestSingleton(heroType,"SilentInstance"),game=TestSingleton(gameType,"UnsafeInstance");
                    Component body=hero as Component;
                    Vector3 at=body==null?Vector3.zero:body.transform.position;
                    Func<object,string,string> flag=(owner,name)=>Equals(Read(owner,name),true)?"true":"false";
                    string health=Convert.ToString(Read(Read(hero,"playerData"),"health"),System.Globalization.CultureInfo.InvariantCulture);
                    string json="{\"stage\":\""+meltyStage+"\",\"downAcknowledged\":"+(meltyDownAcknowledged?"true":"false")+",\"benchExited\":"+(meltyBenchExited?"true":"false")+",\"frame\":"+Time.frameCount+",\"gameState\":\""+Convert.ToString(Read(game,"gameState"))+"\",\"focused\":"+(Application.isFocused?"true":"false")+",\"scale\":"+Time.timeScale.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"hero\":"+(body!=null?"true":"false")+",\"health\":"+(string.IsNullOrEmpty(health)?"-1":health)+",\"acceptingInput\":"+flag(hero,"acceptingInput")+",\"controlRelinquished\":"+flag(hero,"controlReqlinquished")+",\"transitioning\":"+flag(Read(hero,"cState"),"transitioning")+",\"x\":"+at.x.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"y\":"+at.y.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}";
                    try{File.WriteAllText(stateFile,json);}catch(IOException){}catch(UnauthorizedAccessException){}
                    yield return new WaitForSecondsRealtime(1f);
                }
            }
            finally{Application.runInBackground=previousBackground;}
        }

        private static object TestSingleton(Type type,string name)
        {
            if(type==null)return null;
            const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            PropertyInfo property=type.GetProperty(name,flags);
            return property!=null?property.GetValue(null,null):type.GetField(name,flags)?.GetValue(null);
        }

        private IEnumerator StartDraftTestCompatibility()
        {
            // The original test controller uses its original slug. A remix test uses this draft's slug,
            // so create its existing test routine with the new check-file path once the original plugin awakens.
            yield return null;
            string checksPath = FindChecksPath();
            if (checksPath == null || controllerType == null)
            {
                yield break;
            }
            Type testType = controllerType.Assembly.GetType("MarioHollow.MeltyTest", false);
            if (testType == null)
            {
                Log.LogWarning("Moving safety floor test bridge could not find the original test controller.");
                yield break;
            }
            FieldInfo current = testType.GetField("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (current == null || current.GetValue(null) != null)
            {
                yield break;
            }
            try
            {
                ConstructorInfo constructor = testType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                MethodInfo run = testType.GetMethod("Run", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (constructor == null || run == null)
                {
                    Log.LogWarning("Moving safety floor test bridge could not start the original test routine.");
                    yield break;
                }
                object test = constructor.Invoke(new object[] { checksPath });
                current.SetValue(null, test);
                StartCoroutine((IEnumerator)run.Invoke(test, null));
                Log.LogInfo("melty test: remix compatibility routine started");
            }
            catch (Exception exception)
            {
                Log.LogWarning("Moving safety floor test bridge failed: " + exception.Message);
            }
        }

        internal static string FindChecksPath()
        {
            try
            {
                string testFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Melty",
                    "test",
                    MovingSafetyFloorSheet.TestSlug + ".json");
                if (!File.Exists(testFile))
                {
                    return null;
                }
                Match match = Regex.Match(File.ReadAllText(testFile), "\\\"checks\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"");
                return match.Success ? Path.Combine(Path.GetDirectoryName(testFile), Path.GetFileName(Regex.Unescape(match.Groups[1].Value))) : null;
            }
            catch (Exception exception)
            {
                if (Instance != null)
                {
                    Instance.Log.LogWarning("Moving safety floor could not read Melty's test file: " + exception.Message);
                }
                return null;
            }
        }    
    }
}
