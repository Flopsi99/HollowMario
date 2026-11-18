using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HollowMario.ApiRuntime;
using Modding;
using UnityEngine;

namespace HollowMario
{
    public sealed partial class Plugin { internal bool ApiStartupComplete; }

    // One API entry point; no injected chainloader and no live unload toggle.
    public sealed partial class HollowMarioApi : Mod
    {
        private static bool started;
        private GameObject host;
        public HollowMarioApi() : base("Hollow Mario") { }
        public override string GetVersion() => Plugin.PluginVersion;

        public override void Initialize()
        {
            using (var timing = new StartupTiming(Log, Plugin.PluginVersion, Time.realtimeSinceStartup))
            {
            timing.Next("requirements and ROM lookup");
            if (started) throw new InvalidOperationException("Mario's API build is already initialized.");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BepInEx"))
                throw new InvalidOperationException("This build uses the community API. Disable the BepInEx injector and restart Hollow Knight.");
            string package = Path.GetDirectoryName(GetType().Assembly.Location);
            string game = Path.GetDirectoryName(Application.dataPath);
            string rom = Setup.RomResolver.Find(game: game);
            if (rom == null || !File.Exists(Path.Combine(package,"MarioHollow-native","sm64.dll")))
            {
                started = true;
                host = new GameObject("Hollow Mario ROM setup");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<ApiRomSetup>().Begin(package, path => StartAfterSetup(package, game, path), LogError);
                timing.Next("runtime setup handoff");
                timing.Complete();
                return;
            }
            started = true;
            StartWithRom(package, game, rom, timing);
            }
        }

        private void StartAfterSetup(string package, string game, string rom)
        {
            using (var timing = new StartupTiming(Log, Plugin.PluginVersion, Time.realtimeSinceStartup))
                StartWithRom(package, game, rom, timing);
        }

        private void StartWithRom(string package, string game, string rom, StartupTiming timing)
        {
            timing.Next("host assembly loading");
            AppDomain.CurrentDomain.SetData("HollowMario.LoadingRoute", "CommunityApi");
            Environment.SetEnvironmentVariable("MARIO_HK_ROMDIR", Path.GetDirectoryName(rom));
            var bridge = Assembly.LoadFrom(Path.Combine(package, "MarioHollow-native",
                "HollowMario.Host.Integration.dll"
                ));
            Type bridgePlugin = bridge.GetType("MarioHollow.Plugin", true);
            Type engine = bridge.GetType("MarioHollow.Sm64", true);
            timing.Next("API services and argument hooks");
            ApiContext.Initialize(package, game, WriteLog);
            ApiArgumentAdapter.Install();
            timing.Next("Mario engine and ROM initialization");
            engine.GetMethod("Initialize").Invoke(null,new object[]{Path.Combine(package,"MarioHollow-native","sm64.dll"),rom});
            timing.Next("host contracts, audio and controller");
            host = new GameObject("Hollow Mario API host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            try
            {
            host.AddComponent(bridgePlugin);
            if (!(bool)engine.GetProperty("Ready").GetValue(null, null))
            {
                string why = Convert.ToString(engine.GetProperty("Problem").GetValue(null, null));
                throw new InvalidOperationException("Mario's engine could not start: " + why);
            }
            if (host.GetComponent(bridge.GetType("MarioHollow.MarioController", true)) == null)
                throw new InvalidOperationException("Hollow Mario's runtime did not finish starting. Restart the game before retrying.");
            timing.Next("companion gameplay hooks");
            var companion = host.AddComponent<Plugin>();
            if (!companion.ApiStartupComplete)
                throw new InvalidOperationException("Mario's companion did not finish starting. Check the preceding mod log and restart the game before retrying.");
            timing.Next("controls and settings menu");
            ApiModSettings.Initialize(companion.Config);
            Log("Community API host initialized; shared companion " + Plugin.PluginVersion + ". Gameplay has not yet been verified.");
            timing.Complete();
            }
            catch
            {
                host.SetActive(false);
                UnityEngine.Object.Destroy(host);
                new HarmonyLib.Harmony(Plugin.PluginGuid).UnpatchSelf();
                new HarmonyLib.Harmony("gg.hollowmario.independent-contracts").UnpatchSelf();
                throw;
            }
        }

        private void WriteLog(int level, string message)
        {
            if ((level & 3) != 0) LogError(message);
            else if ((level & 4) != 0) LogWarn(message);
            else Log(message);
        }
    }
}
