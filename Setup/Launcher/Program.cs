using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using HollowMario.Setup;

namespace HollowMario.Launcher
{
    internal sealed class Options
    {
        internal string Game, RomDirectory, RomFile, Result, Render, NativeDirectory;
        internal bool RomOnly, NoLaunch, Melty, Choose;
        internal static Options Parse(string[] args)
        {
            var result = new Options();
            for (int i=0; i<args.Length; i++)
            {
                string value() { if (++i>=args.Length) throw new ArgumentException("A setup option is incomplete."); return args[i]; }
                switch (args[i])
                {
                    case "--game-dir": result.Game=value(); break;
                    case "--rom-dir": result.RomDirectory=value(); break;
                    case "--rom-file": result.RomFile=value(); break;
                    case "--rom-only": result.RomOnly=true; break;
                    case "--native-dir": result.NativeDirectory=value(); break;
                    case "--result-file": result.Result=value(); break;
                    case "--render": result.Render=value(); break;
                    case "--no-launch": result.NoLaunch=true; break;
                    case "--melty": result.Melty=true; break;
                    case "--choose": result.Choose=true; break;
                    default: throw new ArgumentException("Unrecognized Hollow Mario setup option.");
                }
            }
            if(result.Melty&&(result.Choose||result.RomOnly||result.Render!=null))
                throw new ArgumentException("Melty's background launch cannot open interactive setup.");
            return result;
        }
    }
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            Options options=null;
            bool melty=Array.IndexOf(args,"--melty")>=0;
            try
            {
                options = Options.Parse(args);
                // Opening the GitHub download always opens setup. Only Melty's
                // explicit flag authorises an unattended installation/launch.
                if(options.Melty){RunAutomatic(options);return;}
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new SetupWindow(options))
                {
                    if (options.Render != null) { form.Render(options.Render); return; }
                    Application.Run(form);
                }
            }
            catch (Exception error)
            {
                if(melty)
                {
                    Environment.ExitCode=1;
                    if(options?.Result!=null)
                        try{JsonFile.Write(options.Result,new LaunchResult{Success=false,Stage="setup",Message=error.Message});}catch(Exception){ }
                    Console.Error.WriteLine("Hollow Mario could not start: "+error.Message);
                    return;
                }
                int render=Array.IndexOf(args,"--render");
                if(render>=0 && render+1<args.Length){File.WriteAllText(args[render+1]+".error.txt",error.ToString());Environment.ExitCode=1;}
                else MessageBox.Show(error.Message, "Hollow Mario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private static void RunAutomatic(Options options)
        {
            if(string.IsNullOrWhiteSpace(options.Game)||!File.Exists(Path.Combine(options.Game,"hollow_knight.exe")))
                throw new InvalidDataException("Melty did not supply an installed Hollow Knight folder. Check the required game in Melty, then try Play again.");
            string game=Path.GetFullPath(options.Game);
            // A remembered local developer ROM must not hide a broken Melty
            // own-copy handoff. Never browse, prompt or replace Melty's game.
            string rom=RomResolver.FindSupplied(options.RomDirectory,options.RomFile);
            if(rom==null)throw new InvalidDataException("Melty has not supplied a supported Super Mario 64 US ROM. Add your own copy in Melty, then try Play again.");
            if(string.IsNullOrWhiteSpace(options.NativeDirectory)||!File.Exists(Path.Combine(options.NativeDirectory,"sm64.dll")))
                throw new InvalidDataException("Melty has not finished preparing Mario's runtime. Finish or repair this release's installation in Melty, then try Play again.");
            RequireGameClosed();
            var installer = new Installer(AppDomain.CurrentDomain.BaseDirectory);
            installer.Verify();
            Installer.CheckCommunity(game);
            GameInstall.EnsureApi(game, _ => {});
            RequireGameClosed();
            installer.PrepareCommunity(game, preparedNative:options.NativeDirectory, requireGameClosed:RequireGameClosed);
            if(options.Result!=null)JsonFile.Write(options.Result,new LaunchResult{Success=true,Stage="ready",Message="Hollow Mario is installed and ready."});
            // Melty supplies these paths every time, so leave the manual
            // installer's remembered choices untouched. The child gets its ROM
            // folder and Melty's test environment directly at launch.
            if (!options.NoLaunch) Launch(game, rom, true);
        }
        internal static void RequireGameClosed()
        {
            if (Process.GetProcessesByName("hollow_knight").Length != 0)
                throw new InvalidOperationException("Close Hollow Knight before changing the setup. Your running game will be left alone.");
        }
        internal static void Launch(string game, string rom, bool melty)
        {
            RequireGameClosed();
            if (melty)
            {
                // Keep Melty's test/launch environment in the actual game process.
                var info = MeltyStartInfo(game,rom);
                using (var process = Process.Start(info)) { process.WaitForExit(); Environment.ExitCode=process.ExitCode; }
                return;
            }
            string steam = SteamPath();
            Process.Start(new ProcessStartInfo(steam, "-applaunch 367520") { UseShellExecute=false, CreateNoWindow=true });
        }
        internal static ProcessStartInfo MeltyStartInfo(string game,string rom)
        {
            var info=new ProcessStartInfo(Path.Combine(game,"hollow_knight.exe")){WorkingDirectory=game,UseShellExecute=false};
            info.EnvironmentVariables["MARIO_HK_ROMDIR"]=Path.GetDirectoryName(rom);
            info.EnvironmentVariables["SteamAppId"]="367520";
            info.EnvironmentVariables["SteamGameId"]="367520";
            return info;
        }
        internal static string SteamPath()
        {
            string folder = Convert.ToString(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
            string path = folder == null ? "" : Path.Combine(folder,"steam.exe");
            if (!File.Exists(path)) path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steam.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("Open Steam once, then try Hollow Mario again.");
            return path;
        }
        internal static string FindGame()
        {
            try
            {
                string steam=Path.GetDirectoryName(SteamPath());
                var libraries = new System.Collections.Generic.List<string> { steam };
                string vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
                if(File.Exists(vdf))
                    foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s+\"([^\"]+)\""))
                        libraries.Add(match.Groups[1].Value.Replace("\\\\","\\"));
                return libraries.Select(x=>Path.Combine(x,"steamapps","common","Hollow Knight")).FirstOrDefault(x=>File.Exists(Path.Combine(x,"hollow_knight.exe")));
            }
            catch { return null; }
        }
    }
}
