using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using HollowMario.Host.Unity;
using UnityEngine;

namespace MarioHollow
{
    public static class Sm64
    {
        public static bool Ready{get;private set;}
        public static string Problem{get;private set;}
        public static Color32[] Atlas;
        internal static GCHandle romHandle;
        internal static string Rom;
        public static void Initialize(string library,string rom)
        {
            try
            {
                Native.Start(library,rom);romHandle=Native.Engine.RomHandle;Rom=rom;
                byte[] bytes=Native.Engine.Atlas;Atlas=new Color32[704*64];
                for(int i=0;i<Atlas.Length;i++)Atlas[i]=new Color32(bytes[i*4],bytes[i*4+1],bytes[i*4+2],bytes[i*4+3]);
                Ready=true;Problem=null;
            }
            catch(Exception error){Problem=error.GetBaseException().Message;Ready=false;Native.Stop();throw;}
        }
        internal static void Stop(){Ready=false;Native.Stop();romHandle=default(GCHandle);Atlas=null;BakedAtlas.Dispose();}
    }
    public static class MarioAudio { public static float Volume=.8f; }
    [BepInPlugin("gg.hollowmario.independenthost","Hollow Mario independent host","0.1.0")]
    public sealed class Plugin:BaseUnityPlugin
    {
        public static ConfigEntry<float> Volume;
        private AudioOutput audio;
        private float nextDiagnostics;
        internal string AudioDiagnostics=>audio?.Diagnostics??"stopped";
        private void Awake()
        {
            HostTrace.Sink=message=>Logger.LogInfo(message);
            HostTrace.Enabled=Config.Bind("Diagnostics","DetailedTrace",true,"Detailed independent-host playtest logging: ownership, actions, damage, renderer and periodic audio counters.").Value;
            if(!Sm64.Ready)
            {
                string library=Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location),"sm64.dll");
                string rom=HollowMario.Setup.RomResolver.Find(game:Path.GetDirectoryName(Application.dataPath));
                if(rom==null)throw new InvalidOperationException("The independent host needs the player's supported ROM.");
                Sm64.Initialize(library,rom);
            }
            Assembly companion=FindCompanion();
            BridgeContracts.Install(companion);
            Volume=Config.Bind("Audio","MarioVolume",.8f,"Mario sound volume, from zero to one.");MarioAudio.Volume=Volume.Value;
            audio=new AudioOutput(Native.Engine);gameObject.AddComponent<MarioController>();
            int opaque=0;foreach(var pixel in Sm64.Atlas)if(pixel.a!=0)opaque++;
            HostTrace.Write("startup ROM atlas="+Sm64.Atlas.Length+" pixels, nontransparent="+opaque+"; audio "+audio.Diagnostics);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--hollow-mario-no-save")>=0)gameObject.AddComponent<LocalProbe>();
            Logger.LogInfo("Independent host active; original MarioHollow assembly not required.");
        }
        internal static Assembly FindCompanion()
        {
            Assembly companion=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetType("HollowMario.KnightResetFollow",false)!=null);
            // BepInEx creates the dependency host before the companion plugin.
            // Load our packaged assembly for its contracts without running it;
            // the chainloader still owns the companion's component lifecycle.
            if(companion==null)
            {
                string parent=Path.GetDirectoryName(Path.GetDirectoryName(typeof(Plugin).Assembly.Location));
                companion=Assembly.LoadFrom(Path.Combine(parent,"HollowMario.dll"));
            }
            if(companion.GetName().Name!="HollowMario"||companion.GetType("HollowMario.KnightResetFollow",false)==null)
                throw new InvalidOperationException("Hollow Mario companion contracts are unavailable.");
            return companion;
        }
        private void Update()
        {
            if(audio!=null)audio.Volume=MarioAudio.Volume;
            if(Time.unscaledTime>=nextDiagnostics){nextDiagnostics=Time.unscaledTime+5f;HostTrace.Write("audio "+AudioDiagnostics+"; player "+(MarioController.Instance?.Diagnostics??"absent"));}
        }
        private void OnGUI(){ } // Retained hook point; no debugging overlay.
        private void OnDestroy(){BridgeContracts.Dispose();HostTrace.Write("shutdown audio "+AudioDiagnostics);audio?.Dispose();audio=null;GetComponent<MarioController>()?.Shutdown();Sm64.Stop();HostTrace.Sink=null;}
    }
}
