using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BepInEx.Configuration
{
    // A deliberately limited INI adapter: bool, float, string and int are all
    // the shared code uses. Unknown entries survive a save. Importing an old
    // injection configuration is read-only; API changes go to this mod's folder.
    public sealed class ConfigFile
    {
        private readonly string path;
        private readonly SortedDictionary<string, SortedDictionary<string, string>> sections =
            new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> bound = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<Action> reload = new List<Action>();
        public bool SaveOnConfigSet { get; set; } = true;
        public string ConfigFilePath => path;

        // Unity Mono's File.Replace fails intermittently with ERROR_UNABLE_TO_REMOVE_REPLACED
        // on the game drive. Rename the completed sibling file instead; Windows
        // leaves the prior configuration in place if this promotion is rejected.
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, uint flags);

        public ConfigFile(string file, bool saveOnInit) : this(file, (string)null)
        { if (saveOnInit) Save(); }

        public ConfigFile(string file, string importIfMissing = null)
        {
            path = Path.GetFullPath(file);
            string read = File.Exists(path) ? path : importIfMissing;
            Read(read);
        }

        private void Read(string read)
        {
            if (string.IsNullOrEmpty(read) || !File.Exists(read)) return;
            string section = "";
            foreach (string raw in File.ReadAllLines(read))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                { section = line.Substring(1, line.Length - 2); continue; }
                int equals = line.IndexOf('=');
                if (equals < 1) continue;
                Section(section)[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = "")
        {
            string id = section + "\n" + key;
            if (bound.TryGetValue(id, out object existing))
                return existing as ConfigEntry<T> ?? throw new InvalidOperationException("Setting type changed: " + key);
            T value = defaultValue;
            if (Section(section).TryGetValue(key, out string raw))
            {
                try { value = (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture); }
                catch (Exception error) when (error is FormatException || error is InvalidCastException || error is OverflowException)
                { /* Same safe default behavior as a malformed injection setting. */ }
            }
            var entry = new ConfigEntry<T>(value, changed =>
            {
                Section(section)[key] = Convert.ToString(changed, CultureInfo.InvariantCulture);
                if (SaveOnConfigSet) Save();
            });
            bound.Add(id, entry);
            reload.Add(() =>
            {
                T loaded = defaultValue;
                if (Section(section).TryGetValue(key, out string text))
                {
                    try { loaded = (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture); }
                    catch (Exception error) when (error is FormatException || error is InvalidCastException || error is OverflowException) { }
                }
                entry.SetWithoutSaving(loaded);
            });
            Section(section)[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (SaveOnConfigSet) Save();
            return entry;
        }

        private SortedDictionary<string, string> Section(string name)
        {
            if (!sections.TryGetValue(name, out var values))
                sections.Add(name, values = new SortedDictionary<string, string>(StringComparer.Ordinal));
            return values;
        }

        public void Save()
        {
            var text = new StringBuilder("# Hollow Mario - community API settings\n");
            foreach (var section in sections)
            {
                text.Append('\n').Append('[').Append(section.Key).Append("]\n");
                foreach (var item in section.Value)
                    text.Append(item.Key).Append(" = ").Append(item.Value).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
            const uint replaceExistingAndWriteThrough = 0x1 | 0x8;
            int error = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (MoveFileEx(temporary, path, replaceExistingAndWriteThrough)) return;
                error = Marshal.GetLastWin32Error();
                bool readOnly = File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
                if (readOnly || (error != 5 && error != 32 && error != 33 && error != 1175 && error != 1176) || attempt == 3) break;
                System.Threading.Thread.Sleep(20 * (attempt + 1));
            }
            File.Delete(temporary);
            throw new Win32Exception(error, "Mario's API settings could not be saved (Windows " + error + ").");
        }

        public void Reload()
        {
            sections.Clear();
            Read(path);
            foreach (var update in reload) update();
        }
    }

    public sealed class ConfigEntry<T>
    {
        private T value;
        private readonly Action<T> changed;
        internal ConfigEntry(T initial, Action<T> onChanged) { value = initial; changed = onChanged; }
        internal void SetWithoutSaving(T loaded) { value = loaded; }
        public T Value
        {
            get => value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
                changed(value);
                this.value = value;
            }
        }
    }
}
