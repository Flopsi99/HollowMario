using System;
using System.IO;
using System.Linq;
using System.Net;
using System.IO.Compression;
using System.Collections.Generic;
using Mono.Cecil;
using HollowMario.Setup;

namespace HollowMario.Launcher
{
    internal static class GameInstall
    {
        internal const string SupportedVersion = "1.5.78.11833";
        internal static bool Inspect(string game, string assembly, out string version)
        {
            if (!File.Exists(Path.Combine(game, "hollow_knight.exe"))) throw new InvalidDataException("Choose your Hollow Knight installation.");
            using (var metadata = AssemblyDefinition.ReadAssembly(assembly))
            {
                version = Convert.ToString(metadata.MainModule.GetType("Constants")?.Fields.FirstOrDefault(f=>f.Name=="GAME_VERSION")?.Constant);
                if (version != SupportedVersion) throw new InvalidDataException("This Hollow Mario build supports Hollow Knight " + SupportedVersion + ". Your selected installation uses " + version + ".");
                return metadata.MainModule.GetType("Modding.Mod") != null;
            }
        }
        internal static bool HasApi(string game)
        {
            return Inspect(game, Path.Combine(game, "hollow_knight_Data", "Managed", "Assembly-CSharp.dll"), out _);
        }
        internal static void EnsureApi(string game, Action<string> progress)
        {
            Installer.CheckCommunity(game);
            if (HasApi(game)) { ValidateApiLibraries(game); return; }
            progress("Getting the Hollow Knight community loader…");
            const string url = "https://github.com/hk-modding/api/releases/download/1.5.78.11833-77/moddingapi.v77.windows.zip";
            const string sha = "bc9f0db3d0916b05cd5a2420bb602fb1b239ce3ff6c289fd84bffb682fb8f1d6";
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowMario", "downloads", "api-v77.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(cache));
            if (!File.Exists(cache) || Disk.Hash(cache) != sha)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var client = new WebClient()) client.DownloadFile(url, cache + ".download");
                if (Disk.Hash(cache + ".download") != sha) throw new InvalidDataException("The community loader download could not be verified.");
                if (File.Exists(cache)) File.Delete(cache);
                File.Move(cache + ".download", cache);
            }
            string managed = Path.Combine(game, "hollow_knight_Data", "Managed");
            string original = Path.Combine(managed, "Assembly-CSharp.dll");
            string backup = original + ".v";
            Program.RequireGameClosed();
            if (!File.Exists(backup)) Disk.Copy(original, backup);
            // Downloaded loader files stay on the player's PC. No game assembly
            // or loader-repacked game content is included in our upload.
            var files=new Dictionary<string,byte[]>();
            using (var zip = ZipFile.OpenRead(cache))
                foreach (var entry in zip.Entries.Where(x=>x.Name.Length>0))
                {
                    string relative = entry.FullName.Replace('\\','/');
                    int split = relative.LastIndexOf("/Managed/", StringComparison.OrdinalIgnoreCase);
                    if (split >= 0) relative = relative.Substring(split + 9);
                    using (var input = entry.Open()) using (var output = new MemoryStream())
                    { input.CopyTo(output); files.Add(Disk.Under(managed,relative),output.ToArray()); }
                }
            Program.RequireGameClosed();
            var previous=files.Keys.ToDictionary(path=>path,path=>File.Exists(path)?File.ReadAllBytes(path):null);
            var written=new List<string>();
            try
            {
                foreach(var file in files){Disk.AtomicWrite(file.Key,file.Value);written.Add(file.Key);}
                ValidateApiLibraries(game);
            }
            catch
            {
                foreach(string path in written.AsEnumerable().Reverse())
                { if(previous[path]==null)File.Delete(path);else Disk.AtomicWrite(path,previous[path]); }
                throw;
            }
        }
        private static void ValidateApiLibraries(string game)
        {
            foreach (var expected in new[] { Tuple.Create("MonoMod.Utils.dll",new Version("21.4.29.1")), Tuple.Create("Mono.Cecil.dll",new Version("0.11.3.0")) })
                using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(game,"hollow_knight_Data","Managed",expected.Item1)))
                    if (assembly.Name.Version != expected.Item2) throw new InvalidDataException("This community loader version has not been tested with Hollow Mario. Install community API v77 for Hollow Knight 1.5.78.11833, then try again.");
        }
    }
}
