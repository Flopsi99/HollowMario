using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HollowMario.Setup
{
    public enum InstallMode { LegacyStandalone = 0, CommunityApi = 1 }
    [DataContract] public sealed class LaunchResult
    {
        [DataMember] public bool Success;
        [DataMember] public string Stage;
        [DataMember] public string Message;
    }
    [DataContract] public sealed class Preferences
    {
        [DataMember] public string GameDirectory;
        [DataMember] public string RomPath;
        [DataMember] public InstallMode LastMode;
        [DataMember] public string ChosenGameDirectory;
        [DataMember] public bool LoadingChoiceConfirmed;
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowMario", "settings.json");
        public static Preferences Load(string path = null)
        {
            try { return JsonFile.Read<Preferences>(path ?? DefaultPath); }
            catch (Exception error) when (error is IOException || error is SerializationException || error is UnauthorizedAccessException)
            { return new Preferences(); }
        }
        public void Save(string path = null) => JsonFile.Write(path ?? DefaultPath, this);

        public bool HasLoadingChoiceFor(string game)
        {
            if (!LoadingChoiceConfirmed || string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(ChosenGameDirectory)) return false;
            try { return string.Equals(Path.GetFullPath(game).TrimEnd('\\','/'), Path.GetFullPath(ChosenGameDirectory).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException) { return false; }
        }
        public void ConfirmLoadingChoice(string game, InstallMode mode, string path = null)
        {
            GameDirectory = Path.GetFullPath(game);
            ChosenGameDirectory = GameDirectory;
            LastMode = mode;
            LoadingChoiceConfirmed = true;
            Save(path);
        }
    }
    [DataContract] public sealed class PackageManifest
    {
        [DataMember] public int SchemaVersion = 1;
        [DataMember] public string Product = "Hollow Mario";
        [DataMember] public string Version;
        [DataMember] public string SupportedGameVersion = "1.5.78.11833";
        [DataMember] public string CommunityApi = "77";
        [DataMember] public bool BuildMarioLocally;
        [DataMember] public PackageFile[] Files;
    }
    [DataContract] public sealed class PackageFile
    {
        [DataMember] public string Path;
        [DataMember] public string Sha256;
    }
    public static class JsonFile
    {
        public static T Read<T>(string path)
        {
            // PowerShell-authored manifests may include a UTF-8 BOM; the JSON
            // serializer itself does not consume it on .NET Framework.
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        public static void Write<T>(string path, T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                Disk.AtomicWrite(path, stream.ToArray());
            }
        }
    }
}
