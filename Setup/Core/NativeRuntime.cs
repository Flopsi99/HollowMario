using System;
using System.Diagnostics;
using System.IO;

namespace HollowMario.Setup
{
    public static class NativeRuntime
    {
        public static void Ensure(string helperFolder,string destination,Action<string> progress=null)
        {
            if(File.Exists(Path.Combine(destination,"sm64.dll")))return;
            string script=Path.Combine(helperFolder,"Prepare-MarioRuntime.ps1");
            if(!File.Exists(script))throw new FileNotFoundException("Local Mario preparation is missing. Extract the complete download.");
            progress?.Invoke("Preparing Mario on this PC. First setup downloads build tools and may take a few minutes…");
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -Destination \""+Path.GetFullPath(destination)+"\"")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start))
            {
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(900000))throw new IOException("Mario preparation is still running. Wait for it to finish before retrying setup.");
                if(process.ExitCode!=0||!File.Exists(Path.Combine(destination,"sm64.dll")))
                    throw new IOException("Mario could not be prepared locally. "+error.Result);
                GC.KeepAlive(output.Result);
            }
        }
    }
}
