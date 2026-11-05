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
            return result;
        }
    }
    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                var options = Options.Parse(args);
                if (!options.Choose && !options.RomOnly && options.Render == null && RunRemembered(options)) return;
                using (var form = new SetupWindow(options))
                {
                    if (options.Render != null) { form.Render(options.Render); return; }
                    Application.Run(form);
                }
            }
            catch (Exception error)
            {
                int render=Array.IndexOf(args,"--render");
                if(render>=0 && render+1<args.Length){File.WriteAllText(args[render+1]+".error.txt",error.ToString());Environment.ExitCode=1;}
                else MessageBox.Show(error.Message, "Hollow Mario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        private static bool RunRemembered(Options options)
        {
            var preferences = Preferences.Load();
            string game = options.Game ?? preferences.GameDirectory ?? FindGame();
            if (string.IsNullOrWhiteSpace(game) || !File.Exists(Path.Combine(game,"hollow_knight.exe"))) return false;
            string rom = RomResolver.Find(options.RomDirectory, options.RomFile, preferences.RomPath, game);
            if (!RomResolver.Valid(rom)) return false;
            RequireGameClosed();
            var installer = new Installer(AppDomain.CurrentDomain.BaseDirectory);
            installer.Verify();
            Installer.CheckCommunity(game);
            GameInstall.EnsureApi(game, _ => {});
            RequireGameClosed();
            installer.PrepareCommunity(game, preparedNative:options.NativeDirectory, requireGameClosed:RequireGameClosed);
            preferences.ConfirmLoadingChoice(game, InstallMode.CommunityApi);
            RomResolver.Remember(rom, preferences);
            if (!options.NoLaunch) Launch(game, rom, options.Melty);
            return true;
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
                var info = new ProcessStartInfo(Path.Combine(game,"hollow_knight.exe")) { WorkingDirectory=game, UseShellExecute=false };
                info.EnvironmentVariables["MARIO_HK_ROMDIR"] = Path.GetDirectoryName(rom);
                info.EnvironmentVariables["SteamAppId"] = "367520";
                info.EnvironmentVariables["SteamGameId"] = "367520";
                using (var process = Process.Start(info)) { process.WaitForExit(); Environment.ExitCode=process.ExitCode; }
                return;
            }
            string steam = SteamPath();
            Process.Start(new ProcessStartInfo(steam, "-applaunch 367520") { UseShellExecute=false, CreateNoWindow=true });
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
