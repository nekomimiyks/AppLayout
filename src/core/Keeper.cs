using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

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
        readonly DisplayDetection displays;
        readonly object placementSync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread thread;
        volatile bool stopping;
        readonly bool startupMode;
        readonly Stopwatch uptime = Stopwatch.StartNew();
        int displaySignal;
        bool tracksDisplays;
        internal bool DisplayReady { get { return !tracksDisplays || displays.IsReady(CurrentSignal); } }
        int CurrentSignal { get { return Interlocked.CompareExchange(ref displaySignal, 0, 0); } }
        readonly HashSet<string> launchedAtStartup = new HashSet<string>();
        bool launchPassDone;
        public string LogPath;

        public event Action<Dictionary<AppEntry, EntryStatus>> StatusUpdated;

        public Keeper(ConfigStore store, bool startupMode)
        {
            this.store = store;
            displays = new DisplayDetection(store);
            this.startupMode = startupMode;
            launchPassDone = !startupMode;
        }

        public void Start()
        {
            lock (store.Sync) tracksDisplays = true;
            SystemEvents.DisplaySettingsChanging += DisplayChanged;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            thread = new Thread(Loop) { IsBackground = true, Name = "Keeper" };
            thread.Start();
        }

        public void Stop()
        {
            stopping = true;
            SystemEvents.DisplaySettingsChanging -= DisplayChanged;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            wake.Set();
        }
        public void Kick() { wake.Set(); }

        void DisplayChanged(object sender, EventArgs e)
        {
            Interlocked.Increment(ref displaySignal);
            Kick();
        }

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
            int signal = CurrentSignal;
            var monitors = WindowScanner.GetMonitors();
            var key = DisplayTopology.Key(monitors);
            List<AppEntry> apps; bool enabled, launchMissing; int interval;
            var result = new Dictionary<AppEntry, EntryStatus>();
            lock (store.Sync)
            {
                if (tracksDisplays && (signal != CurrentSignal || !displays.Observe(key, signal, uptime.ElapsedMilliseconds)))
                {
                    Publish(result);
                    return 1;
                }
                apps = store.Apps;
                enabled = store.Config.Enabled;
                launchMissing = store.Config.LaunchMissingAtStartup;
                interval = store.Config.IntervalSeconds;
            }
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
                                DisplayReady && store.Apps == apps && !launchedAtStartup.Contains(a.Id) &&
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
                            bool off = WindowScanner.MonitorLabel(a.X, a.Y, a.Width, a.Height, monitors)== "なし";
                            st = off ? new EntryStatus { Text = "保存位置のモニタがありません", Kind = 1 }
                                     : new EntryStatus { Text = "配置を変更できませんでした", Kind = 1 };
                        }
                    }
                }
                result[a] = st;
            }
            if (doLaunch)
            {
                lock (store.Sync) launchPassDone = DisplayReady && store.Apps == apps;
                if (launchPassDone) launchedAtStartup.Clear();
            }

            Publish(result);
            return interval;
        }

        void Publish(Dictionary<AppEntry, EntryStatus> result)
        {
            var h = StatusUpdated;
            if (!stopping && h != null) h(result);
        }

        internal int Apply(WinInfo previous, AppEntry entry, List<AppEntry> matchedEntries, bool automatic)
        {
            lock (placementSync)
            {
                DisplayLayout display;
                lock (store.Sync) display = store.ActiveDisplay;
                var window = WindowScanner.ReadCurrent(previous);
                if (window == null || !entry.Matches(window) ||
                    matchedEntries.Exists(other => other != entry && other.Matches(window))) return -1;
                return WindowScanner.Apply(window.Handle, window.Pid, entry.X, entry.Y, entry.Width, entry.Height,
                    entry.Maximized, delegate
                    {
                        lock (store.Sync)
                            return !stopping && DisplayReady && store.Apps == matchedEntries && (!automatic || store.Config.Enabled);
                    }, delegate
                    {
                        if (display != null && DisplayTopology.Key(WindowScanner.GetMonitors()) != display.Key) return false;
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
