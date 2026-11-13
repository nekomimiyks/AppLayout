using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AppLayout
{
    public class EntryStatus
    {
        public string Text;
        public int Kind; // 0=正常 1=注意 2=停止・未検出
    }

    /// 一定間隔で記憶済みウィンドウを保存位置へ戻すバックグラウンド処理
    public class Keeper
    {
        readonly ConfigStore store;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread thread;
        volatile bool stopping;
        readonly bool startupMode;
        readonly DateTime startedAt = DateTime.Now;
        bool launchPassDone;
        public string LogPath;

        public event Action<Dictionary<AppEntry, EntryStatus>> StatusUpdated;

        public Keeper(ConfigStore store, bool startupMode)
        {
            this.store = store;
            this.startupMode = startupMode;
            launchPassDone = !startupMode;
        }

        public void Start()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "Keeper" };
            thread.Start();
        }

        public void Stop() { stopping = true; wake.Set(); }
        public void Kick() { wake.Set(); }

        void Loop()
        {
            while (!stopping)
            {
                int interval = 2;
                try { interval = Tick(); }
                catch (Exception ex) { Log(ex.ToString()); }
                wake.WaitOne(Math.Max(1, interval) * 1000);
            }
        }

        int Tick()
        {
            AppEntry[] apps; bool enabled, launchMissing; int interval;
            lock (store.Sync)
            {
                apps = store.Config.Apps.ToArray();
                enabled = store.Config.Enabled;
                launchMissing = store.Config.LaunchMissingAtStartup;
                interval = store.Config.IntervalSeconds;
            }
            var wins = WindowScanner.ReadAll();
            var result = new Dictionary<AppEntry, EntryStatus>();
            bool doLaunch = !launchPassDone && (DateTime.Now - startedAt).TotalSeconds > 20;

            foreach (var a in apps)
            {
                var matches = new List<WinInfo>();
                foreach (var w in wins) if (!WindowScanner.IsOwn(w) && a.Matches(w)) matches.Add(w);

                EntryStatus st;
                if (matches.Count == 0)
                {
                    st = new EntryStatus { Text = "未起動（ウィンドウなし）", Kind = 2 };
                    if (doLaunch && launchMissing && enabled && TryLaunch(a)) st.Text = "起動しました";
                }
                else if (matches.Count > 1)
                    st = new EntryStatus { Text = "該当が" + matches.Count + "個（条件を絞ってください）", Kind = 1 };
                else if (!enabled)
                    st = new EntryStatus { Text = "一時停止中", Kind = 2 };
                else
                {
                    var w = matches[0];
                    if (w.Minimized) st = new EntryStatus { Text = "最小化中（そのまま）", Kind = 0 };
                    else
                    {
                        int r = WindowScanner.Apply(w.Handle, a.X, a.Y, a.Width, a.Height, a.Maximized);
                        if (r == 1) st = new EntryStatus { Text = "位置を戻しました", Kind = 0 };
                        else if (r == 0) st = new EntryStatus { Text = "配置中", Kind = 0 };
                        else
                        {
                            var mons = WindowScanner.GetMonitors();
                            bool off = WindowScanner.MonitorLabel(a.X, a.Y, a.Width, a.Height, mons)== "なし";
                            st = off ? new EntryStatus { Text = "保存位置のモニタがありません", Kind = 1 }
                                     : new EntryStatus { Text = "配置中", Kind = 0 };
                        }
                    }
                }
                result[a] = st;
            }
            if (doLaunch) launchPassDone = true;

            var h = StatusUpdated;
            if (h != null) h(result);
            return interval;
        }

        bool TryLaunch(AppEntry a)
        {
            // ブラウザ型アプリ（Chromeアプリ等）は本体プロセスが別用途で動いていても起動する
            bool hostedApp = !string.IsNullOrEmpty(a.AppId);
            if (!hostedApp && Process.GetProcessesByName(a.Process ?? "").Length > 0) return false;
            try
            {
                if (!string.IsNullOrEmpty(a.Shortcut) && File.Exists(a.Shortcut))
                    Process.Start(new ProcessStartInfo(a.Shortcut) { UseShellExecute = true });
                else if (!string.IsNullOrEmpty(a.LaunchId))
                    Process.Start("explorer.exe", "shell:AppsFolder\\" + a.LaunchId);
                else if (!string.IsNullOrEmpty(a.ExePath) && File.Exists(a.ExePath))
                    Process.Start(new ProcessStartInfo(a.ExePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(a.ExePath) });
                else return false;
                return true;
            }
            catch (Exception ex) { Log("起動失敗 " + a.Name + ": " + ex.Message); return false; }
        }

        void Log(string msg)
        {
            try { if (LogPath != null) File.AppendAllText(LogPath, DateTime.Now.ToString("o") + " " + msg + Environment.NewLine); } catch { }
        }
    }
}
