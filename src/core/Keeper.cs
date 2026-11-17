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
        readonly Stopwatch uptime = Stopwatch.StartNew();
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
            AppEntry[] apps; bool enabled, launchMissing; int interval;
            lock (store.Sync)
            {
                apps = store.Config.Apps.ToArray();
                enabled = store.Config.Enabled;
                launchMissing = store.Config.LaunchMissingAtStartup;
                interval = store.Config.IntervalSeconds;
            }
            var result = new Dictionary<AppEntry, EntryStatus>();
            var wins = apps.Length == 0 ? new List<WinInfo>() : WindowScanner.ReadAll();
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
                                store.Config.Apps.Contains(a) && TryLaunch(a)) st.Text = "起動しました";
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
                        int r;
                        lock (store.Sync)
                        {
                            // 一覧取得の間に解除・編集・一時停止された登録は操作しない。
                            if (stopping || !store.Config.Enabled || !store.Config.Apps.Contains(a)) continue;
                            uint pid;
                            Native.GetWindowThreadProcessId(w.Handle, out pid);
                            r = pid == w.Pid ? WindowScanner.Apply(w.Handle, a.X, a.Y, a.Width, a.Height, a.Maximized) : -1;
                        }
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
            if (!stopping && h != null) h(result);
            return interval;
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
