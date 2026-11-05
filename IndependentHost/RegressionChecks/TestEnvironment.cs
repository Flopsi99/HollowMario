using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HollowMario.ApiRuntime;

// Test-only bootstrap. The product never references this assembly.
public static class TestEnvironment
{
    public static string Root, Package, Managed, Library, Rom;
    public static readonly System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
    public static string CompanionPath { get { return Path.Combine(Package, "HollowMario.dll"); } }
    public static string HostPath { get { return Path.Combine(Package, "MarioHollow-native", "HollowMario.Host.Integration.dll"); } }
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Type native;
    public static void Initialize(string root, string package, string game, string library, string rom, bool initializeApi = true)
    {
        Root = root; Package = package; Managed = Path.Combine(game, "hollow_knight_Data", "Managed"); Library = library; Rom = rom;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
            if (new AssemblyName(request.Name).Name == "MarioHollow") throw new InvalidOperationException("Retired dependency requested by a regression test.");
            foreach (string folder in new[] { Package, Path.GetDirectoryName(HostPath), Managed }) {
                string path = Path.Combine(folder, new AssemblyName(request.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        if (initializeApi) ApiContext.Initialize(Package, game, (level, message) => { Messages.Add(message); Console.WriteLine(message); });
    }
    public static void StartNative()
    {
        native = Assembly.LoadFrom(HostPath).GetType("MarioHollow.Native", true);
        native.GetMethod("Start", Any).Invoke(null, new object[] { Library, Rom });
        // Install the real companion lifecycle entry, including its sound
        // cleanup. Never substitute the former pooling callbacks in a fixture.
        var plugin = Assembly.LoadFrom(CompanionPath).GetType("HollowMario.Plugin", true);
        var owner = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(plugin);
        var fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        plugin.GetField("harmony", fields).SetValue(owner, new HarmonyLib.Harmony("hollowmario.regression.lifecycle"));
        plugin.GetField("Log", fields).SetValue(owner, new BepInEx.Logging.ManualLogSource("Regression lifecycle"));
        plugin.GetMethod("KeepMarioLoaded", fields).Invoke(owner, new object[] { native });
    }
    public static int ParkedActors {
        get { return ((System.Collections.ICollection)native.GetField("parked", Any).GetValue(null)).Count; }
    }
    public static object PullAudio(object[] args)
    {
        // Use the independent engine's verified two-block PCM contract, rather
        // than invoking a facade with a different marshaling signature.
        object engine = native.GetField("Engine", Any).GetValue(null);
        return engine.GetType().GetMethod("Audio").Invoke(engine, new object[] { (short[])args[2], (uint)args[1], (uint)args[0] });
    }
    public static void VerifyLoadedInputs()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        if (loaded.Any(a => a.GetName().Name == "MarioHollow")) throw new InvalidOperationException("Retired dependency loaded.");
        foreach (string name in new[] { "HollowMario", "HollowMario.Host.Integration", "HollowMario.Host.Core", "HollowMario.Host.Unity", "HollowMario.ApiRuntime", "0Harmony" }) {
            foreach (var assembly in loaded.Where(a => a.GetName().Name == name))
                if (!Path.GetFullPath(assembly.Location).StartsWith(Path.GetFullPath(Package) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Test loaded a stale/non-package input: " + assembly.Location);
        }
    }
}

// Invoke the ACTUAL packaged internal presentation actor. Do not source-link a
// second copy: a test must fail when the delivered actor has the wrong behavior.
public sealed class OpeningMarioPose : IDisposable
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly object actor;
    readonly Type type;
    readonly int id; // Existing identity assertions inspect the real actor ID.
    public OpeningMarioPose(Type native, string library, float face, bool resting = false)
    {
        type = Assembly.LoadFrom(TestEnvironment.CompanionPath).GetType("HollowMario.OpeningMarioPose", true);
        actor = type.GetConstructors(Any).Single().Invoke(new object[] { native, library, face, resting });
        id = (int)type.GetField("id", Any).GetValue(actor);
    }
    object Call(string name, params object[] args) { return type.GetMethod(name, Any).Invoke(actor, args); }
    public object State { get { return type.GetProperty("State", Any).GetValue(actor, null); } }
    public object Tick(object buffers, bool grounded) { return Call("Tick", buffers, grounded); }
    public void Wake() { Call("Wake"); }
    public void SetAnimationFrame(short frame) { Call("SetAnimationFrame", frame); }
    public bool SetCapVisible(bool visible) { return (bool)Call("SetCapVisible", visible); }
    public void LimitAnimationFrame(int animation, int frame) { Call("LimitAnimationFrame", animation, frame); }
    public void TalkFacing(float angle, bool asleep = false) { Call("TalkFacing", angle, asleep); }
    public void Dispose() { ((IDisposable)actor).Dispose(); }
}
