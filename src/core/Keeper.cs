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
        readonly object placementSync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread thread;
        volatile bool stopping;
        readonly bool startupMode;
        readonly Stopwatch uptime = Stopwatch.StartNew();
        readonly HashSet<string> launchedAtStartup = new HashSet<string>();
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
                if (!stopping) wake.WaitOne(Math.Max(1, Math.Min(interval, int.MaxValue / 1000)) * 1000);
            }
        }

        int Tick()
        {
            List<AppEntry> apps; bool enabled, launchMissing; int interval;
            lock (store.Sync)
            {
                apps = store.Config.Apps;
                enabled = store.Config.Enabled;
                launchMissing = store.Config.LaunchMissingAtStartup;
                interval = store.Config.IntervalSeconds;
            }
            var result = new Dictionary<AppEntry, EntryStatus>();
            var wins = apps.Count == 0 ? new List<WinInfo>() : WindowScanner.ReadAll();
            Dictionary<IntPtr, int> assignments;
            var matchesByEntry = WindowScanner.MatchLayouts(apps, wins, out assignments);
            bool doLaunch = !launchPassDone && uptime.Elapsed.TotalSeconds > 20;

            foreach (var a in apps)
            {
                if (stopping) break;
                var matches = matchesByEntry[a];

                EntryStatus st;
                if (matches.Count == 0)
                {
                    st = new EntryStatus { Text = "未起動（ウィンドウなし）", Kind = 2 };
                    if (doLaunch && launchMissing && enabled)
                    {
                        lock (store.Sync)
                        {
                            if (!stopping && store.Config.Enabled && store.Config.LaunchMissingAtStartup &&
                                store.Config.Apps == apps && !launchedAtStartup.Contains(a.Id) &&
                                !WindowScanner.HasCloakedMatch(wins, a) && TryLaunch(a))
                            {
                                launchedAtStartup.Add(a.Id);
                                st.Text = "起動しました";
                            }
                        }
                    }
                }
                else if (matches.Count > 1 || assignments[matches[0].Handle] > 1)
                    st = new EntryStatus { Text = "該当が複数", Kind = 1 };
                else if (!enabled)
                    st = new EntryStatus { Text = "一時停止中", Kind = 2 };
                else
                {
                    var w = matches[0];
                    if (w.Minimized) st = new EntryStatus { Text = "最小化中（そのまま）", Kind = 0 };
                    else
                    {
                        int r = Apply(w, a, apps, true);
                        if (r == 1) st = new EntryStatus { Text = "位置を戻しました", Kind = 0 };
                        else if (r == 0 || r == WindowScanner.PlacementDeferred) st = new EntryStatus { Text = "配置中", Kind = 0 };
                        else
                        {
                            var mons = WindowScanner.GetMonitors();
                            bool off = WindowScanner.MonitorLabel(a.X, a.Y, a.Width, a.Height, mons)== "なし";
                            st = off ? new EntryStatus { Text = "保存位置のモニタがありません", Kind = 1 }
                                     : new EntryStatus { Text = "配置を変更できませんでした", Kind = 1 };
                        }
                    }
                }
                result[a] = st;
            }
            if (doLaunch)
            {
                lock (store.Sync) launchPassDone = store.Config.Apps == apps;
                if (launchPassDone) launchedAtStartup.Clear();
            }

            var h = StatusUpdated;
            if (!stopping && h != null) h(result);
            return interval;
        }

        internal int Apply(WinInfo previous, AppEntry entry, List<AppEntry> matchedEntries, bool automatic)
        {
            lock (placementSync)
            {
                var window = WindowScanner.ReadCurrent(previous);
                if (window == null || !entry.Matches(window) ||
                    matchedEntries.Exists(other => other != entry && other.Matches(window))) return -1;
                return WindowScanner.Apply(window.Handle, window.Pid, entry.X, entry.Y, entry.Width, entry.Height,
                    entry.Maximized, delegate
                    {
                        lock (store.Sync)
                            return !stopping && store.Config.Apps == matchedEntries && (!automatic || store.Config.Enabled);
                    }, delegate
                    {
                        if (WindowScanner.HasOtherMatch(window, entry)) return false;
                        var latest = WindowScanner.ReadCurrent(window);
                        return latest != null && entry.Matches(latest) &&
                            !matchedEntries.Exists(other => other != entry && other.Matches(latest));
                    });
            }
        }

        bool TryLaunch(AppEntry a)
        {
            // ブラウザ型アプリ（Chromeアプリ等）は本体プロセスが別用途で動いていても起動する
            bool hostedApp = !string.IsNullOrEmpty(a.AppId);
            try
            {
                if (!hostedApp)
                {
                    var processes = Process.GetProcessesByName(a.Process ?? "");
                    foreach (var process in processes) process.Dispose();
                    if (processes.Length > 0) return false;
                }
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
