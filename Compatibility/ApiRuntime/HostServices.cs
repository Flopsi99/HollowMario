using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using HollowMario.ApiRuntime;

// These are the small service contracts used by our host and companion
// source. They do NOT start BepInEx, discover plugins, patch game assemblies, or
// install an injector. Only the community API creates our Unity components.
namespace HollowMario.ApiRuntime
{
    public static class ApiContext
    {
        public static string PackageDirectory { get; private set; }
        public static string GameDirectory { get; private set; }
        public static Action<int, string> WriteLog { get; private set; }

        public static void Initialize(string packageDirectory, string gameDirectory, Action<int, string> writeLog)
        {
            if (PackageDirectory != null) throw new InvalidOperationException("Mario's API host is already initialized.");
            PackageDirectory = Path.GetFullPath(packageDirectory);
            GameDirectory = Path.GetFullPath(gameDirectory);
            WriteLog = writeLog ?? throw new ArgumentNullException(nameof(writeLog));
        }
    }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public string GUID { get; }
        public string Name { get; }
        public string Version { get; }
        public BepInPlugin(string guid, string name, string version)
        { GUID = guid; Name = name; Version = version; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInDependency : Attribute
    {
        [Flags] public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
        public BepInDependency(string guid, DependencyFlags flags = DependencyFlags.HardDependency) { }
    }

    public sealed class PluginInfo
    {
        public string Location { get; internal set; }
    }

    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        protected BaseUnityPlugin()
        {
            var identity = GetType().GetCustomAttribute<BepInPlugin>();
            if (identity == null) throw new InvalidOperationException("Missing Mario component identity.");
            if (HollowMario.ApiRuntime.ApiContext.PackageDirectory == null)
                throw new InvalidOperationException("The community API must initialize Mario's services first.");
            Logger = new Logging.ManualLogSource(identity.Name);
            Info = new PluginInfo { Location = GetType().Assembly.Location };
            string file = identity.GUID + ".cfg";
            string import = Path.Combine(ApiContext.GameDirectory, "BepInEx", "config", file);
            if (identity.GUID == "gg.melty.hollowmario")
            {
                const string oldFile = "gg.melty.mariohollow.moving-floor.cfg";
                foreach (string candidate in new[] {
                    Path.Combine(ApiContext.PackageDirectory, "settings", oldFile),
                    Path.Combine(Path.GetDirectoryName(ApiContext.PackageDirectory), "FloorFix", "settings", oldFile),
                    Path.Combine(ApiContext.GameDirectory, "BepInEx", "config", oldFile) })
                    if (File.Exists(candidate)) { import = candidate; break; }
            }
            Config = new Configuration.ConfigFile(
                Path.Combine(HollowMario.ApiRuntime.ApiContext.PackageDirectory, "settings", file),
                import);
        }
        public Logging.ManualLogSource Logger { get; }
        public Configuration.ConfigFile Config { get; }
        public PluginInfo Info { get; }
    }

    public static class Paths
    {
        public static string BepInExRootPath => HollowMario.ApiRuntime.ApiContext.PackageDirectory;
    }
}

namespace BepInEx.Logging
{
    [Flags]
    public enum LogLevel { None = 0, Fatal = 1, Error = 2, Warning = 4, Message = 8, Info = 16, Debug = 32, All = 63 }
    public sealed class ManualLogSource
    {
        public string SourceName { get; }
        public ManualLogSource(string name) { SourceName = name; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Log(LogLevel level, object value)
        { HollowMario.ApiRuntime.ApiContext.WriteLog?.Invoke((int)level, "[" + SourceName + "] " + value); }
        public void LogInfo(object value) => Log(LogLevel.Info, value);
        public void LogDebug(object value) => Log(LogLevel.Debug, value);
        public void LogMessage(object value) => Log(LogLevel.Message, value);
        public void LogWarning(object value) => Log(LogLevel.Warning, value);
        public void LogError(object value) => Log(LogLevel.Error, value);
        public void LogFatal(object value) => Log(LogLevel.Fatal, value);
    }
}
