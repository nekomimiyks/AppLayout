using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AppLayout
{
    [DataContract]
    public class AppEntry
    {
        [DataMember(Name = "name", Order = 1)] public string Name;
        [DataMember(Name = "process", Order = 2)] public string Process;
        [DataMember(Name = "class", Order = 3)] public string Class;
        [DataMember(Name = "appId", Order = 4)] public string AppId;
        [DataMember(Name = "titleMode", Order = 5)] public string TitleMode; // none / exact / contains / regex
        [DataMember(Name = "title", Order = 6)] public string Title;
        [DataMember(Name = "launchId", Order = 7)] public string LaunchId;
        [DataMember(Name = "shortcut", Order = 8)] public string Shortcut;
        [DataMember(Name = "exePath", Order = 9)] public string ExePath;
        [DataMember(Name = "x", Order = 10)] public int X;
        [DataMember(Name = "y", Order = 11)] public int Y;
        [DataMember(Name = "width", Order = 12)] public int Width;
        [DataMember(Name = "height", Order = 13)] public int Height;
        [DataMember(Name = "maximized", Order = 14)] public bool Maximized;
        [DataMember(Name = "id", Order = 0, EmitDefaultValue = false)] public string Id;

        public void EnsureId() { if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N"); }

        /// 「ウィンドウの見分け方」の表示用文字列
        public string TitleRuleText
        {
            get
            {
                switch (TitleModeOrNone)
                {
                    case "exact": return "タイトル一致: " + Title;
                    case "contains": return "タイトルを含む: " + Title;
                    case "regex": return "正規表現: " + Title;
                    default: return "アプリで判定";
                }
            }
        }

        public AppEntry Clone() { return (AppEntry)MemberwiseClone(); }

        public string TitleModeOrNone { get { return string.IsNullOrEmpty(TitleMode) ? "none" : TitleMode; } }

        public bool Matches(WinInfo w)
        {
            if (!string.Equals(ClassKey(w.Class), ClassKey(Class), StringComparison.Ordinal)) return false;
            if (!string.Equals(w.ProcessName, Process ?? "", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(AppId) && w.AppId != AppId) return false;
            string t = Title ?? "";
            switch (TitleModeOrNone)
            {
                case "exact": return w.Title == t;
                case "contains": return w.Title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
                case "regex":
                    try { return Regex.IsMatch(w.Title, t, RegexOptions.None, TimeSpan.FromMilliseconds(100)); } catch { return false; }
                default: return true;
            }
        }

        /// 起動のたびに変わる部分（WinFormsのハッシュ、WPFのGUID、Qtのバージョン）を除いたクラス名
        public static string ClassKey(string c)
        {
            c = c ?? "";
            var m = Regex.Match(c, @"^(WindowsForms\d+\.[A-Za-z]+(?:\.\d+)?)\.app\.");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(c, @"^(HwndWrapper\[[^;]*);;");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(c, @"^Qt\d+(QWindow.*)$");
            if (m.Success) return "Qt" + m.Groups[1].Value;
            return c;
        }

        public void SetPosition(WinInfo w)
        {
            X = w.Bounds.Left; Y = w.Bounds.Top; Width = w.Width; Height = w.Height; Maximized = w.ShowCmd == 3;
        }

        public static AppEntry FromWindow(WinInfo w)
        {
            var e = new AppEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = FriendlyName(w),
                Process = w.ProcessName,
                Class = w.Class,
                AppId = w.AppId ?? "",
                TitleMode = "none",
                Title = "",
                LaunchId = w.AppId ?? "",
                Shortcut = "",
                ExePath = w.ExePath ?? ""
            };
            e.SetPosition(w);
            return e;
        }

        static string FriendlyName(WinInfo w)
        {
            string t = w.Title ?? "";
            // 「ページ名 - アプリ名」形式ならアプリ名側を使う
            int i = t.LastIndexOf(" - ", StringComparison.Ordinal);
            if (i >= 0 && i + 3 < t.Length) t = t.Substring(i + 3);
            if (t.Length == 0 || t.Length > 40) t = w.ProcessName;
            return t;
        }
    }

    [DataContract]
    public class LayoutConfig
    {
        [DataMember(Name = "enabled", Order = 1)] public bool Enabled;
        [DataMember(Name = "launchMissingAtStartup", Order = 2)] public bool LaunchMissingAtStartup;
        [DataMember(Name = "intervalSeconds", Order = 3)] public int IntervalSeconds;
        [DataMember(Name = "savedAt", Order = 4)] public string SavedAt;
        [DataMember(Name = "minimizeToTrayOnClose", Order = 5)] public bool MinimizeToTrayOnClose;
        [DataMember(Name = "apps", Order = 6)] public List<AppEntry> Apps;
        [DataMember(Name = "language", Order = 7, EmitDefaultValue = false)] public string Language;
        [DataMember(Name = "checkForUpdates", Order = 8, EmitDefaultValue = false)] public bool CheckForUpdates;

        public LayoutConfig() { Defaults(); }
        [OnDeserializing] void OnDeserializing(StreamingContext c) { Defaults(); }
        void Defaults() { Enabled = true; LaunchMissingAtStartup = true; MinimizeToTrayOnClose = true; IntervalSeconds = 2; Apps = new List<AppEntry>(); }
    }

    public class ConfigStore
    {
        [DataContract]
        sealed class SettingsBackup
        {
            [DataMember(Name = "format", IsRequired = true)] public string Format;
            [DataMember(Name = "version", IsRequired = true)] public int Version;
            [DataMember(Name = "settings", IsRequired = true)] public LayoutConfig Settings;
        }

        const string BackupFormat = "AppLayout.Settings";
        const int BackupVersion = 1;
        const int MaxBackupBytes = 8 * 1024 * 1024;
        public readonly string Path;
        public LayoutConfig Config;
        public readonly object Sync = new object();
        readonly string importPath;
        byte[] savedContents;
        const int SharingViolation = 32;

        public ConfigStore(string path) : this(path, null) { }
        ConfigStore(string path, string importPath) { Path = path; this.importPath = importPath; }

        public void Load()
        {
            lock (Sync)
            {
                byte[] contents = ReadFile(Path);
                if (contents != null)
                {
                    Config = Decode(contents);
                    savedContents = contents;
                    return;
                }
                using (LockFile())
                {
                    // 別セッションが先に新規作成していたらその内容を使う。
                    contents = ReadFile(Path);
                    if (contents != null)
                    {
                        Config = Decode(contents);
                        savedContents = contents;
                        return;
                    }
                    Config = importPath == null ? new LayoutConfig() : Decode(File.ReadAllBytes(importPath));
                    WriteFile();
                }
            }
        }

        internal static ConfigStore OpenDefault()
        {
            return OpenDefault(AppDomain.CurrentDomain.BaseDirectory,
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppLayout"));
        }

        internal static ConfigStore OpenDefault(string directory, string applicationData)
        {
            string local = System.IO.Path.Combine(directory, "layout.json");
            string fallback = System.IO.Path.Combine(applicationData, "layout.json");
            string imported = System.IO.Path.Combine(applicationData, "portable", PortableKey(local), "layout.json");
            if (File.Exists(imported)) return new ConfigStore(imported);
            bool localExists = File.Exists(local);
            bool directoryWritable = CanWrite(directory);
            if (directoryWritable && (!localExists || CanUpdate(local))) return new ConfigStore(local);
            // 旧版が書き込めないディレクトリから使っていたユーザー設定はそのまま引き継ぐ。
            if (!directoryWritable && File.Exists(fallback)) return new ConfigStore(fallback);
            return localExists ? new ConfigStore(imported, local) : new ConfigStore(fallback);
        }

        static string PortableKey(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant())), 0, 16).Replace("-", "");
        }

        static bool CanUpdate(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) return true;
            }
            catch (IOException ex) { if ((ex.HResult & 0xffff) == SharingViolation) return true; throw; }
            catch (UnauthorizedAccessException) { return false; }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string t = System.IO.Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(t, ""); File.Delete(t); return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Save()
        {
            lock (Sync)
            {
                using (LockFile())
                {
                    VerifyUnchanged();
                    WriteFile();
                }
            }
        }

        internal void ExportSettings(string destination)
        {
            lock (Sync)
            {
                var settings = Decode(savedContents);
                RemoveLaunchTargets(settings);
                settings.CheckForUpdates = false;
                var backup = new SettingsBackup { Format = BackupFormat, Version = BackupVersion, Settings = settings };
                string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(destination),
                    ".applayout-" + System.IO.Path.GetRandomFileName() + ".tmp");
                bool created = false;
                try
                {
                    using (var file = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        created = true;
                        WriteJson(file, backup);
                        if (file.Length > MaxBackupBytes) throw InvalidBackup();
                    }
                    File.Move(temporary, destination);
                }
                finally { if (created) DeleteTemporary(temporary); }
            }
        }

        internal static LayoutConfig ReadBackup(string source)
        {
            SettingsBackup backup;
            try
            {
                using (var file = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length > MaxBackupBytes) throw InvalidBackup();
                    backup = (SettingsBackup)new DataContractJsonSerializer(typeof(SettingsBackup)).ReadObject(file);
                }
            }
            catch (SerializationException) { throw InvalidBackup(); }
            if (backup == null || backup.Format != BackupFormat || backup.Version != BackupVersion ||
                backup.Settings == null || backup.Settings.Apps == null ||
                backup.Settings.Apps.Any(entry => entry == null || entry.Width <= 0 || entry.Height <= 0 ||
                    (long)entry.X + entry.Width > int.MaxValue || (long)entry.Y + entry.Height > int.MaxValue ||
                    string.IsNullOrWhiteSpace(entry.Process)))
                throw InvalidBackup();
            Normalize(backup.Settings);
            RemoveLaunchTargets(backup.Settings);
            backup.Settings.Enabled = false;
            backup.Settings.LaunchMissingAtStartup = false;
            return backup.Settings;
        }

        internal void RestoreBackup(LayoutConfig settings)
        {
            lock (Sync)
            {
                using (LockFile())
                {
                    VerifyUnchanged();
                    var previous = Config;
                    settings.CheckForUpdates = previous.CheckForUpdates;
                    Config = settings;
                    try { WriteFile(Path + ".before-restore"); }
                    catch { Config = previous; throw; }
                }
            }
        }

        static void RemoveLaunchTargets(LayoutConfig settings)
        {
            foreach (var entry in settings.Apps)
                entry.LaunchId = entry.Shortcut = entry.ExePath = "";
        }

        static IOException InvalidBackup()
        {
            return new IOException(L.T("AppLayoutの対応する設定バックアップファイルを選んでください。"));
        }

        void VerifyUnchanged()
        {
            byte[] current = ReadFile(Path);
            if (current != savedContents &&
                (current == null || savedContents == null || !current.SequenceEqual(savedContents)))
                throw new IOException(L.T("設定が別の場所で変更されました。AppLayoutを再起動して最新の設定を読み込んでください。"));
        }

        FileStream LockFile()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            var elapsed = Stopwatch.StartNew();
            for (;;)
            {
                try { return File.Open(Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException ex)
                {
                    if ((ex.HResult & 0xffff) != SharingViolation || elapsed.ElapsedMilliseconds >= 500) throw;
                    Thread.Sleep(20);
                }
            }
        }

        void WriteFile(string backup = null)
        {
            string previousSavedAt = Config.SavedAt;
            string tmp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)),
                ".layout-" + System.IO.Path.GetRandomFileName() + ".tmp");
            bool created = false;
            try
            {
                Config.SavedAt = DateTime.Now.ToString("o");
                using (var fs = File.Open(tmp, FileMode.CreateNew))
                {
                    created = true;
                    WriteJson(fs, Config);
                }
                byte[] written = File.ReadAllBytes(tmp);
                if (File.Exists(Path)) File.Replace(tmp, Path, backup);
                else File.Move(tmp, Path);
                savedContents = written;
            }
            catch
            {
                Config.SavedAt = previousSavedAt;
                throw;
            }
            finally
            {
                if (created) DeleteTemporary(tmp);
            }
        }

        static void WriteJson<T>(Stream stream, T value)
        {
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
                new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
        }

        static void DeleteTemporary(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static byte[] ReadFile(string path)
        {
            try
            {
                using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var bytes = new MemoryStream())
                {
                    file.CopyTo(bytes);
                    return bytes.ToArray();
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        static LayoutConfig Decode(byte[] contents)
        {
            LayoutConfig config;
            using (var ms = new MemoryStream(contents))
            using (var reader = new StreamReader(ms, Encoding.UTF8, true))
            using (var json = new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd())))
                config = (LayoutConfig)new DataContractJsonSerializer(typeof(LayoutConfig)).ReadObject(json);
            if (config == null) throw new SerializationException("Empty layout configuration.");
            Normalize(config);
            return config;
        }

        static void Normalize(LayoutConfig config)
        {
            if (config.Apps == null) config.Apps = new List<AppEntry>();
            config.Apps.RemoveAll(entry => entry == null);
            if (config.IntervalSeconds < 1) config.IntervalSeconds = 2;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in config.Apps)
            {
                entry.EnsureId();
                while (!ids.Add(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            }
        }
    }
}
