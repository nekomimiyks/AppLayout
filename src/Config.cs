using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

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
                    try { return Regex.IsMatch(w.Title, t); } catch { return false; }
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
        [DataMember(Name = "importedFrom", Order = 5, EmitDefaultValue = false)] public string ImportedFrom;
        [DataMember(Name = "apps", Order = 6)] public List<AppEntry> Apps;

        public LayoutConfig() { Defaults(); }
        [OnDeserializing] void OnDeserializing(StreamingContext c) { Defaults(); }
        void Defaults() { Enabled = true; LaunchMissingAtStartup = true; IntervalSeconds = 2; Apps = new List<AppEntry>(); }
    }

    // 旧版（PowerShell版）の state\fixed-app-layout.json
    [DataContract]
    class OldConfig
    {
        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "apps")] public List<AppEntry> Apps;
    }

    public class ConfigStore
    {
        public readonly string Path;
        public LayoutConfig Config;
        public readonly object Sync = new object();

        public ConfigStore(string path) { Path = path; }

        public string Load()
        {
            if (File.Exists(Path))
            {
                Config = Read<LayoutConfig>(Path);
                if (Config.Apps == null) Config.Apps = new List<AppEntry>();
                if (Config.IntervalSeconds < 1) Config.IntervalSeconds = 2;
                return null;
            }
            Config = new LayoutConfig();
            string imported = TryImport();
            Save();
            return imported;
        }

        string TryImport()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>
            {
                @"C:\Apps\AppLayout\state\fixed-app-layout.json",
                System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, @"..\state\fixed-app-layout.json"))
            };
            foreach (var p in candidates)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    var old = Read<OldConfig>(p);
                    if (old.Apps == null) continue;
                    foreach (var a in old.Apps)
                    {
                        // 旧版スクリプトにあった個別のタイトル条件を引き継ぐ
                        if (string.Equals(a.Process, "LINE", StringComparison.OrdinalIgnoreCase)) { a.TitleMode = "exact"; a.Title = "LINE"; }
                        else if (string.Equals(a.Process, "Discord", StringComparison.OrdinalIgnoreCase)) { a.TitleMode = "regex"; a.Title = "Discord$"; }
                        else { a.TitleMode = "none"; a.Title = ""; }
                        if (a.ExePath == null) a.ExePath = "";
                        Config.Apps.Add(a);
                    }
                    Config.ImportedFrom = p;
                    return p;
                }
                catch { }
            }
            return null;
        }

        public void Save()
        {
            lock (Sync)
            {
                Config.SavedAt = DateTime.Now.ToString("o");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                string tmp = Path + ".tmp";
                using (var fs = File.Create(tmp))
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), true, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(LayoutConfig)).WriteObject(w, Config);
                }
                if (File.Exists(Path)) File.Replace(tmp, Path, null);
                else File.Move(tmp, Path);
            }
        }

        static T Read<T>(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }
    }
}
