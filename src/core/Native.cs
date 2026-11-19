using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AppLayout
{
    public static class Native
    {
        public delegate bool EnumProc(IntPtr h, IntPtr p);
        public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr p);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT min, max; public RECT normal; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public uint pid; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr ptr; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint n);
            [PreserveSig] int GetAt(uint n, out PropertyKey k);
            [PreserveSig] int GetValue(ref PropertyKey k, out PropVariant v);
        }
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr w, ref Guid iid, out IPropertyStore store);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant v);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc c, IntPtr p);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr w);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr w, StringBuilder b, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr w, StringBuilder b, int n);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w, out uint p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr w, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr w, ref WINDOWPLACEMENT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr w, ref WINDOWPLACEMENT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr w);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr w);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr p);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int k);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr w, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr w, int index);
        [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr w, int attr, out int value, int size);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public static string AppIdFor(IntPtr w)
        {
            IPropertyStore s = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(w, ref iid, out s) != 0) return "";
                var k = new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                PropVariant v;
                if (s.GetValue(ref k, out v) != 0) return "";
                string t = v.vt == 31 ? Marshal.PtrToStringUni(v.ptr) : "";
                PropVariantClear(ref v);
                return t ?? "";
            }
            catch { return ""; }
            finally { if (s != null) Marshal.ReleaseComObject(s); }
        }

        public static string ExePathFor(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024); int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { CloseHandle(h); }
        }

        public static void MakeDpiAware()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }
    }

    public class WinInfo
    {
        public IntPtr Handle;
        public uint Pid;
        public string Title = "", Class = "", AppId = "", ProcessName = "", ExePath = "";
        public Native.RECT Bounds;
        public int ShowCmd;
        public bool Cloaked;
        public int Width { get { return Bounds.Right - Bounds.Left; } }
        public int Height { get { return Bounds.Bottom - Bounds.Top; } }
        public bool Minimized { get { return ShowCmd == 2 || Native.IsIconic(Handle); } }
        public bool Maximized { get { return ShowCmd == 3 || Native.IsZoomed(Handle); } }
    }

    public class MonitorDesc
    {
        public IntPtr Handle; public int Number; public Native.RECT Bounds; public bool Primary; public string Device;
        /// Windowsの「設定 > ディスプレイ」に表示される番号（\.\DISPLAYn の n）
        public string Label { get { return Number > 0 ? Number.ToString() : "?"; } }
    }

    public static class WindowScanner
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        /// 表示中でタイトルのあるトップレベルウィンドウ（旧版と同じ条件）
        public static List<WinInfo> ReadAll()
        {
            var list = new List<WinInfo>();
            var cache = new Dictionary<uint, string[]>();
            var title = new StringBuilder(1024);
            Native.EnumWindows(delegate (IntPtr h, IntPtr p)
            {
                if (!Native.IsWindowVisible(h) || Native.GetWindowText(h, title, title.Capacity) == 0) return true;
                var w = Describe(h, cache, title.ToString());
                if (w != null) list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// 「一覧から追加」に出す候補（隠れたウィンドウや自分自身は除外）
        public static List<WinInfo> ReadPickable()
        {
            var res = new List<WinInfo>();
            foreach (var w in ReadAll())
                if (IsPickable(w)) res.Add(w);
            return res;
        }

        public static bool IsPickable(WinInfo w)
        {
            if (w.Pid == OwnPid) return false;
            if (w.Cloaked || IsCloaked(w.Handle)) return false;
            int ex = Native.GetWindowLong(w.Handle, -20);
            if ((ex & 0x80) != 0) return false; // WS_EX_TOOLWINDOW
            if (!w.Minimized && (w.Width <= 0 || w.Height <= 0)) return false;
            if (w.Class == "Progman" || w.Class == "WorkerW" || w.Class == "Shell_TrayWnd") return false;
            return true;
        }

        public static bool IsOwn(WinInfo w) { return w.Pid == OwnPid; }

        internal static bool HasCloakedMatch(List<WinInfo> windows, AppEntry entry)
        {
            return windows.Exists(window => window.Cloaked && !IsOwn(window) && entry.Matches(window));
        }

        static bool IsCloaked(IntPtr window)
        {
            const int cloakedAttribute = 14;
            int cloaked;
            return Native.DwmGetWindowAttribute(window, cloakedAttribute, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        internal static Dictionary<AppEntry, List<WinInfo>> MatchLayouts(IEnumerable<AppEntry> entries,
            IEnumerable<WinInfo> windows, out Dictionary<IntPtr, int> assignments)
        {
            var byProcess = new Dictionary<string, List<WinInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var window in windows)
            {
                if (IsOwn(window) || window.Cloaked) continue;
                string process = window.ProcessName ?? "";
                List<WinInfo> candidates;
                if (!byProcess.TryGetValue(process, out candidates))
                    byProcess.Add(process, candidates = new List<WinInfo>());
                candidates.Add(window);
            }

            assignments = new Dictionary<IntPtr, int>();
            var matches = new Dictionary<AppEntry, List<WinInfo>>();
            foreach (var entry in entries)
            {
                var matched = new List<WinInfo>();
                matches.Add(entry, matched);
                List<WinInfo> candidates;
                if (!byProcess.TryGetValue(entry.Process ?? "", out candidates)) continue;
                foreach (var window in candidates)
                {
                    if (!entry.Matches(window)) continue;
                    matched.Add(window);
                    int count;
                    assignments.TryGetValue(window.Handle, out count);
                    assignments[window.Handle] = count + 1;
                }
            }
            return matches;
        }

        public static WinInfo Describe(IntPtr h, Dictionary<uint, string[]> cache)
        {
            return Describe(h, cache, null);
        }

        internal static WinInfo ReadCurrent(WinInfo previous)
        {
            if (previous == null) return null;
            var window = Describe(previous.Handle, null);
            if (window == null || window.Cloaked || window.Minimized || !Native.IsWindowVisible(window.Handle) ||
                window.Pid != previous.Pid || !string.Equals(window.ProcessName, previous.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                AppEntry.ClassKey(window.Class) != AppEntry.ClassKey(previous.Class) ||
                !string.Equals(window.AppId, previous.AppId, StringComparison.Ordinal)) return null;
            return window;
        }

        internal static bool HasOtherMatch(WinInfo current, AppEntry entry)
        {
            var pids = new HashSet<uint> { current.Pid };
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
                using (process) pids.Add((uint)process.Id);
            var cache = new Dictionary<uint, string[]>();
            var title = new StringBuilder(1024);
            bool found = false;
            Native.EnumWindows(delegate(IntPtr handle, IntPtr ignored)
            {
                uint pid;
                if (handle == current.Handle || !Native.IsWindowVisible(handle) ||
                    Native.GetWindowThreadProcessId(handle, out pid) == 0 || pid == OwnPid || !pids.Contains(pid) ||
                    Native.GetWindowText(handle, title, title.Capacity) == 0) return true;
                var candidate = Describe(handle, cache, title.ToString());
                found = candidate != null && !candidate.Cloaked && entry.Matches(candidate);
                return !found;
            }, IntPtr.Zero);
            return found;
        }
        static WinInfo Describe(IntPtr h, Dictionary<uint, string[]> cache, string title)
        {
            if (!Native.IsWindow(h)) return null;
            var w = new WinInfo { Handle = h };
            w.Cloaked = IsCloaked(h);
            if (title == null)
            {
                var buffer = new StringBuilder(1024);
                Native.GetWindowText(h, buffer, buffer.Capacity);
                title = buffer.ToString();
            }
            w.Title = title;
            var c = new StringBuilder(256); Native.GetClassName(h, c, c.Capacity); w.Class = c.ToString();
            uint pid;
            Native.RECT r;
            if (Native.GetWindowThreadProcessId(h, out pid) == 0 || !Native.GetWindowRect(h, out r)) return null;
            w.Pid = pid; w.Bounds = r;
            var pl = new Native.WINDOWPLACEMENT(); pl.length = Marshal.SizeOf(pl); Native.GetWindowPlacement(h, ref pl); w.ShowCmd = pl.showCmd;
            w.AppId = Native.AppIdFor(h);
            string[] proc;
            if (cache == null || !cache.TryGetValue(pid, out proc))
            {
                string exe = Native.ExePathFor(pid), name = "";
                if (exe.Length > 0) name = Path.GetFileNameWithoutExtension(exe);
                else { try { using (var process = Process.GetProcessById((int)pid)) name = process.ProcessName; } catch { } }
                proc = new[] { name, exe };
                if (cache != null) cache[pid] = proc;
            }
            w.ProcessName = proc[0]; w.ExePath = proc[1];
            return w;
        }

        public static List<MonitorDesc> GetMonitors()
        {
            var list = new List<MonitorDesc>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hm, IntPtr hdc, ref Native.RECT rr, IntPtr p)
            {
                var mi = new Native.MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(mi);
                if (Native.GetMonitorInfo(hm, ref mi))
                {
                    var d = new MonitorDesc { Handle = hm, Bounds = mi.rcMonitor, Primary = (mi.dwFlags & 1) != 0, Device = mi.szDevice ?? "" };
                    var m = System.Text.RegularExpressions.Regex.Match(d.Device, @"(\d+)$");
                    if (m.Success) d.Number = int.Parse(m.Groups[1].Value);
                    list.Add(d);
                }
                return true;
            }, IntPtr.Zero);
            list.Sort(delegate (MonitorDesc a, MonitorDesc b) { return a.Number.CompareTo(b.Number); });
            return list;
        }

        public static string MonitorLabel(int x, int y, int width, int height, List<MonitorDesc> mons)
        {
            if (width <= 0 || height <= 0 || (long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
                return "なし";
            var r = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
            IntPtr h = Native.MonitorFromRect(ref r, 0);
            if (h == IntPtr.Zero) return "なし";
            foreach (var m in mons) if (m.Handle == h) return m.Label;
            return "?";
        }

        internal const int PlacementDeferred = -2;

        /// 戻り値: 0=配置済み、1=配置を確認、-1=配置できない、-2=マウス操作中。
        public static int Apply(IntPtr w, int x, int y, int width, int height, bool maximized)
        {
            uint pid;
            Native.GetWindowThreadProcessId(w, out pid);
            return Apply(w, pid, x, y, width, height, maximized, delegate { return true; }, delegate { return true; });
        }

        internal static int Apply(IntPtr w, uint expectedPid, int x, int y, int width, int height, bool maximized,
            Func<bool> canContinue, Func<bool> uniqueMatch)
        {
            if (w == IntPtr.Zero || width <= 0 || height <= 0 ||
                (long)x + width > int.MaxValue || (long)y + height > int.MaxValue) return -1;
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(w, out pid);
            if (thread == 0 || pid != expectedPid) return -1;
            Func<bool> stillTarget = delegate
            {
                uint currentPid;
                return canContinue() && Native.GetWindowThreadProcessId(w, out currentPid) == thread &&
                    currentPid == expectedPid && Native.IsWindowVisible(w) && !IsCloaked(w) && !Native.IsIconic(w);
            };
            if (!stillTarget()) return -1;
            var r = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
            IntPtr targetMonitor = Native.MonitorFromRect(ref r, 0);
            if (targetMonitor == IntPtr.Zero) return -1;
            Native.RECT now;
            if (!Native.GetWindowRect(w, out now)) return -1;
            bool zoomed = Native.IsZoomed(w);
            // 最大化時の枠はWindowsが決める。モニタが合っていれば再最大化しない。
            if (zoomed && maximized && Native.MonitorFromRect(ref now, 0) == targetMonitor) return 0;
            if (!zoomed && !maximized && SameBounds(now, r)) return 0;
            if (Native.GetAsyncKeyState(1) < 0) return PlacementDeferred;
            if (!uniqueMatch() || !stillTarget()) return -1;
            if (Native.GetAsyncKeyState(1) < 0) return PlacementDeferred;
            if (!Native.GetWindowRect(w, out now)) return -1;
            zoomed = Native.IsZoomed(w);
            if (zoomed && maximized && Native.MonitorFromRect(ref now, 0) == targetMonitor) return 0;
            if (!zoomed && !maximized && SameBounds(now, r)) return 0;
            if (zoomed && maximized)
            {
                // 最大化済みの HWND は SetWindowPlacement の通常位置を変えても移動しない。
                // 最大化を解除せず、移動先の作業領域に枠を合わせて一度だけ移動する。
                if (!MoveMaximized(w, ref now, targetMonitor)) return -1;
            }
            else if (zoomed || maximized)
            {
                // 通常位置と表示状態を一つの要求にまとめ、応答の遅い相手でも途中の状態を残さない。
                if (!SetDisplayPlacement(w, r, targetMonitor, ref now, maximized)) return -1;
            }
            else
            {
                if (!MoveWindow(w, r)) return -1;
            }
            return WaitForPlacement(w, stillTarget, delegate(Native.RECT bounds)
            {
                return Native.IsZoomed(w) == maximized &&
                    (maximized ? Native.MonitorFromRect(ref bounds, 0) == targetMonitor : SameBounds(bounds, r));
            }) ? 1 : -1;
        }

        static bool MoveWindow(IntPtr window, Native.RECT bounds)
        {
            const uint positionFlags = 0x0004 | 0x0010 | 0x4000; // Z順・フォーカスを維持し、他プロセスへは非同期。
            return Native.SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, positionFlags);
        }

        static bool MoveMaximized(IntPtr window, ref Native.RECT current, IntPtr monitor)
        {
            IntPtr oldMonitor = Native.MonitorFromRect(ref current, 0);
            var previous = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
            var next = previous;
            if (!Native.GetMonitorInfo(oldMonitor, ref previous) || !Native.GetMonitorInfo(monitor, ref next)) return false;
            double ratio = 1;
            try
            {
                uint from, to, ignored;
                if (Native.GetDpiForMonitor(oldMonitor, 0, out from, out ignored) == 0 && from != 0 &&
                    Native.GetDpiForMonitor(monitor, 0, out to, out ignored) == 0 && to != 0) ratio = (double)to / from;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            var bounds = next.rcWork;
            bounds.Left += (int)Math.Round((current.Left - previous.rcWork.Left) * ratio);
            bounds.Top += (int)Math.Round((current.Top - previous.rcWork.Top) * ratio);
            bounds.Right += (int)Math.Round((current.Right - previous.rcWork.Right) * ratio);
            bounds.Bottom += (int)Math.Round((current.Bottom - previous.rcWork.Bottom) * ratio);
            return MoveWindow(window, bounds);
        }

        static bool SetDisplayPlacement(IntPtr window, Native.RECT requested, IntPtr monitor, ref Native.RECT current, bool maximized)
        {
            const int asyncPlacement = 4, showMaximized = 3, showNormalWithoutActivation = 4;
            var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT)) };
            if (!Native.GetWindowPlacement(window, ref placement)) return false;
            if (!maximized || Native.MonitorFromRect(ref current, 0) != monitor)
            {
                const int toolWindow = 0x80;
                if ((Native.GetWindowLong(window, -20) & toolWindow) == 0)
                {
                    var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
                    if (!Native.GetMonitorInfo(monitor, ref info)) return false;
                    int leftInset = info.rcWork.Left - info.rcMonitor.Left, topInset = info.rcWork.Top - info.rcMonitor.Top;
                    requested.Left -= leftInset; requested.Right -= leftInset;
                    requested.Top -= topInset; requested.Bottom -= topInset;
                }
                placement.normal = requested;
                placement.max = new Native.POINT { X = -1, Y = -1 };
            }
            placement.flags = asyncPlacement;
            placement.showCmd = maximized ? showMaximized : showNormalWithoutActivation;
            return Native.SetWindowPlacement(window, ref placement);
        }

        static bool SameBounds(Native.RECT left, Native.RECT right)
        {
            return Math.Abs((long)left.Left - right.Left) <= 2 && Math.Abs((long)left.Top - right.Top) <= 2 &&
                   Math.Abs((long)left.Right - right.Right) <= 2 && Math.Abs((long)left.Bottom - right.Bottom) <= 2;
        }

        static bool WaitForPlacement(IntPtr window, Func<bool> stillTarget, Predicate<Native.RECT> matches)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                Native.RECT bounds;
                if (!stillTarget() || !Native.GetWindowRect(window, out bounds)) return false;
                if (matches(bounds)) return true;
                Thread.Sleep(10);
            } while (elapsed.ElapsedMilliseconds < 120);
            return false;
        }
    }
}
