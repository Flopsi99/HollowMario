using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using HollowMario.Setup;
using UnityEngine;

namespace HollowMario
{
    // An API-only first-ROM fallback. No Mario engine/controller is created
    // until the helper validates the player's file. No WinForms runs in Unity.
    public sealed class ApiRomSetup : MonoBehaviour
    {
        private string package;
        private Action<string> ready, error;
        private bool waiting;
        internal void Begin(string directory, Action<string> onReady, Action<string> onError)
        { package=directory;ready=onReady;error=onError;StartCoroutine(SelectRom()); }
        private IEnumerator SelectRom()
        {
            waiting=true;
            string helper=Path.Combine(package,"Setup","HollowMario-Setup.exe");
            string response=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HollowMario","requests",Guid.NewGuid().ToString("N")+".json");
            Process process=null;
            try
            {
                if(!File.Exists(helper))throw new FileNotFoundException("Hollow Mario's ROM chooser is missing. Install the complete mod folder.");
                process=Process.Start(new ProcessStartInfo(helper,"--rom-only --native-dir \""+Path.Combine(package,"MarioHollow-native")+"\" --result-file \""+response+"\"") { UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(helper) });
            }
            catch(Exception exception){error?.Invoke(exception.Message);waiting=false;yield break;}
            while(!process.HasExited)yield return null;
            process.Dispose();waiting=false;
            string rom=RomResolver.Find();
            if(File.Exists(response))File.Delete(response);
            if(rom==null||!File.Exists(Path.Combine(package,"MarioHollow-native","sm64.dll")))yield break;
            try{ready(rom);Destroy(gameObject);}
            catch(Exception exception){error?.Invoke(exception.ToString());}
        }
        private void OnGUI()
        {
            if(waiting)return;
            GUILayout.BeginArea(new Rect(24,24,460,100),GUI.skin.box);
            GUILayout.Label("Hollow Mario needs your Super Mario 64 US ROM.");
            if(GUILayout.Button("Choose ROM…"))StartCoroutine(SelectRom());
            GUILayout.EndArea();
        }
    }
}
