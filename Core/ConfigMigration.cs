using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace HollowMario
{
    // Compatibility names are retained only at this boundary. Never overwrite
    // a player's already-populated Hollow Mario settings during an update.
    internal static class ConfigMigration
    {
        internal const string LegacyGuid = "gg.melty.mariohollow.moving-floor";
        internal static void ImportLegacy(ConfigFile config)
        {
            string current = config.ConfigFilePath;
            if (File.Exists(current) && File.ReadLines(current).Any(line =>
                !line.TrimStart().StartsWith("#") && !line.TrimStart().StartsWith(";") && line.Contains("="))) return;
            string previous = Path.Combine(Path.GetDirectoryName(current), LegacyGuid + ".cfg");
            if (!File.Exists(previous)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(current));
            File.Copy(previous, current, true);
            config.Reload();
        }
    }
}
