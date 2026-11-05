using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HollowMario.Setup
{
    public static class RomResolver
    {
        public const int RomBytes = 8388608;
        public const string UsSha1 = "9bef1128717f958171a4afac3ed78ee2bb4e86ce";
        public static byte[] Normalize(byte[] bytes)
        {
            if (bytes.Length < 4) return bytes;
            if (bytes[0] == 0x37 && bytes[1] == 0x80)
                for (int i=0; i+1<bytes.Length; i+=2) { byte b=bytes[i]; bytes[i]=bytes[i+1]; bytes[i+1]=b; }
            else if (bytes[0] == 0x40 && bytes[1] == 0x12)
                for (int i=0; i+3<bytes.Length; i+=4) { byte a=bytes[i], b=bytes[i+1]; bytes[i]=bytes[i+3]; bytes[i+1]=bytes[i+2]; bytes[i+2]=b; bytes[i+3]=a; }
            return bytes;
        }
        public static bool Valid(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length != RomBytes) return false;
                using (var sha = SHA1.Create())
                    return BitConverter.ToString(sha.ComputeHash(Normalize(File.ReadAllBytes(path)))).Replace("-", "").Equals(UsSha1, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { return false; }
        }
        public static string Find(string suppliedDirectory = null, string suppliedFile = null, string cached = null, string game = null)
        {
            var candidates = new List<string>();
            AddDirectory(candidates, suppliedDirectory);
            candidates.Add(suppliedFile);
            AddDirectory(candidates, Environment.GetEnvironmentVariable("MARIO_HK_ROMDIR"));
            candidates.Add(cached ?? Preferences.Load().RomPath);
            if (!string.IsNullOrEmpty(game))
                foreach (string relative in new[] { "BepInEx/config/gg.melty.mariohollow.cfg", "hollow_knight_Data/Managed/Mods/HollowMario/settings/gg.melty.mariohollow.cfg", "hollow_knight_Data/Managed/Mods/FloorFix/settings/gg.melty.mariohollow.cfg" })
                {
                    string config=Path.Combine(game,relative.Replace('/',Path.DirectorySeparatorChar));
                    if(File.Exists(config))
                        foreach(string line in File.ReadAllLines(config))
                            if(Regex.IsMatch(line,@"^\s*RomPath\s*="))candidates.Add(line.Substring(line.IndexOf('=')+1).Trim().Trim('"'));
                }
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarioHollow", "melty.properties");
            if (File.Exists(legacy))
                foreach (string line in File.ReadAllLines(legacy))
                    if (line.StartsWith("romdir=", StringComparison.Ordinal)) AddDirectory(candidates, line.Substring(7).Trim());
            string own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Melty", "mods", "own-copies.json");
            if (File.Exists(own))
            {
                string list = Regex.Match(File.ReadAllText(own), "\"super-mario-64\"\\s*:\\s*\\[([^\\]]*)\\]").Groups[1].Value;
                foreach (Match match in Regex.Matches(list, "\"((?:\\\\.|[^\"\\\\])*)\""))
                { string path = Regex.Unescape(match.Groups[1].Value); if (Directory.Exists(path)) AddDirectory(candidates, path); else candidates.Add(path); }
            }
            return candidates.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Valid);
        }
        public static string FindSupplied(string suppliedDirectory,string suppliedFile=null)
        {
            var candidates=new List<string>{suppliedFile};
            AddDirectory(candidates,suppliedDirectory);
            return candidates.Where(x=>!string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Valid);
        }
        private static void AddDirectory(List<string> result, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            try { result.AddRange(Directory.GetFiles(folder).Where(x => new FileInfo(x).Length == RomBytes)); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        }
        public static void Remember(string path, Preferences preferences, string preferencesFile = null, string legacyProperties = null)
        {
            if (!Valid(path)) throw new InvalidDataException("Select an unmodified Super Mario 64 US ROM. This file is not the supported version.");
            preferences.RomPath = Path.GetFullPath(path);
            preferences.Save(preferencesFile);
            // The API host also consumes this remembered ROM handoff.
            string file = legacyProperties ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MarioHollow", "melty.properties");
            var lines = File.Exists(file) ? File.ReadAllLines(file).Where(x => !x.StartsWith("romdir=", StringComparison.Ordinal)).ToList() : new List<string>();
            lines.Add("romdir=" + Path.GetDirectoryName(preferences.RomPath));
            Disk.AtomicWrite(file, new UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine));
        }
    }
}
