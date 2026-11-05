using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HollowMario;
using UnityEngine;
using UnitySceneManager=UnityEngine.SceneManagement.SceneManager;

namespace MarioHollow
{
    // Private test harness, active ONLY with the no-save launch switch. It
    // feeds an InControl virtual device, never desktop mouse/keyboard input.
    public sealed class LocalProbe:MonoBehaviour
    {
        private MeltyBenchInput pad;
        private readonly HashSet<string> pressed=new HashSet<string>();
        private readonly Queue<string> steps=new Queue<string>();
        private string folder,request,stateFile;
        private float nextRead,nextStep;
        private bool priorBackground;
        private static readonly string[] controls={"DPadUp","DPadDown","DPadLeft","DPadRight","Action1","Action2","Action3","Action4","Start","Select"};
        private void Start()
        {
            priorBackground=Application.runInBackground;Application.runInBackground=true;
            folder=Path.GetDirectoryName(GetType().Assembly.Location);request=Path.Combine(folder,"local-test-input.txt");stateFile=Path.Combine(folder,"local-test-state.txt");
        }
        private void Update()
        {
            try
            {
                if(pad==null)
                {
                    pad=new MeltyBenchInput(typeof(HeroController).Assembly,()=>null,controls);
                    pad.Pressed=name=>pressed.Contains(name);
                }
                if(Time.unscaledTime>=nextStep)
                {
                    pressed.Clear();pad.Held=false;
                    if(steps.Count>0)
                    {
                        string step=steps.Dequeue();string[] parts=step.Split('|');
                        if(parts.Length!=2||!float.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float seconds)||seconds<0||seconds>3)
                            throw new InvalidDataException("Local input step must have a bounded duration and named controls.");
                        foreach(string name in parts[1].Split(','))
                        {
                            string control=name.Trim();if(control.Length==0)continue;
                            if(control=="Screenshot"){ScreenCapture.CaptureScreenshot(Path.Combine(folder,"local-test-shot.png"));continue;}
                            if(control.StartsWith("LoadSave:",StringComparison.Ordinal))
                            {
                                if(!int.TryParse(control.Substring(9),out int slot)||slot<1||slot>4
                                    ||GameManager.UnsafeInstance.gameState!=GlobalEnums.GameState.MAIN_MENU)
                                    throw new InvalidOperationException("A local test can only request a valid save from the title menu.");
                                GameManager.UnsafeInstance.LoadGameFromUI(slot);continue;
                            }
                            if(Array.IndexOf(controls,control)<0)throw new InvalidDataException("Unknown local input control: "+control);
                            pressed.Add(control);
                        }
                        pad.Held=pressed.Count>0;nextStep=Time.unscaledTime+seconds;
                    }
                }
                if(Time.unscaledTime<nextRead)return;nextRead=Time.unscaledTime+.25f;
                if(File.Exists(request))
                {
                    string[] input=File.ReadAllLines(request);File.Delete(request);
                    if(input.Length>100)throw new InvalidDataException("Local input sequence is too long.");
                    foreach(string line in input)if(!string.IsNullOrWhiteSpace(line))steps.Enqueue(line);
                }
                var controller=MarioController.Instance;var hero=HeroController.SilentInstance;
                File.WriteAllText(stateFile,"scene="+UnitySceneManager.GetActiveScene().name+"\nstate="+GameManager.UnsafeInstance?.gameState
                    +"\nmario="+controller?.MarioId+"\nposition="+(hero!=null?hero.transform.position.ToString("F3"):"none")
                    +"\naction="+(controller!=null?controller.State.action.ToString("X"):"none")+"\ntriangles="+controller?.Renderer?.Triangles
                    +"\ncolours="+BakedAtlas.rowByColour.Count+"\nworld="+WorldCollision.Objects+"\nqueued="+steps.Count
                    +"\nheld="+string.Join(",",pressed)+"\nheroEnabled="+(hero!=null?hero.enabled.ToString():"none")
                    +"\ninput="+Read(hero,"acceptingInput")+"\nlock="+Read(hero,"controlReqlinquished")+"\naudio="+GetComponent<Plugin>()?.AudioDiagnostics);
            }
            catch(Exception error){pressed.Clear();pad?.Dispose();pad=null;UnityEngine.Debug.LogError("Independent local probe: "+error);enabled=false;}
        }
        private static object Read(object value,string name)=>value?.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value);
        private void OnDestroy(){pressed.Clear();pad?.Dispose();Application.runInBackground=priorBackground;}
    }
}
