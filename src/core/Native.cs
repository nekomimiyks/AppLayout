using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr w, int cmd);
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
            int cloaked;
            if (Native.DwmGetWindowAttribute(w.Handle, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            int ex = Native.GetWindowLong(w.Handle, -20);
            if ((ex & 0x80) != 0) return false; // WS_EX_TOOLWINDOW
            if (!w.Minimized && (w.Width <= 0 || w.Height <= 0)) return false;
            if (w.Class == "Progman" || w.Class == "WorkerW" || w.Class == "Shell_TrayWnd") return false;
            return true;
        }

        public static bool IsOwn(WinInfo w) { return w.Pid == OwnPid; }

        internal static Dictionary<AppEntry, List<WinInfo>> MatchLayouts(IEnumerable<AppEntry> entries,
            IEnumerable<WinInfo> windows, out Dictionary<IntPtr, int> assignments)
        {
            var byProcess = new Dictionary<string, List<WinInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var window in windows)
            {
                if (IsOwn(window)) continue;
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
        static WinInfo Describe(IntPtr h, Dictionary<uint, string[]> cache, string title)
        {
            if (!Native.IsWindow(h)) return null;
            var w = new WinInfo { Handle = h };
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

        /// 旧版 WindowLayoutNative.Apply と同じ挙動。戻り値: 0=位置そのまま 1=戻した -1=スキップ
        public static int Apply(IntPtr w, int x, int y, int width, int height, bool maximized)
        {
            if (w == IntPtr.Zero || width <= 0 || height <= 0 ||
                (long)x + width > int.MaxValue || (long)y + height > int.MaxValue ||
                Native.IsIconic(w) || Native.GetAsyncKeyState(1) < 0) return -1;
            var r = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
            IntPtr targetMonitor = Native.MonitorFromRect(ref r, 0);
            if (targetMonitor == IntPtr.Zero) return -1;
            Native.RECT now;
            if (!Native.GetWindowRect(w, out now)) return -1;
            bool zoomed = Native.IsZoomed(w);
            // 最大化時の枠はWindowsが決める。モニタが合っていれば再最大化しない。
            if (zoomed && maximized && Native.MonitorFromRect(ref now, 0) == targetMonitor) return 0;
            bool same = Math.Abs((long)now.Left - r.Left) <= 2 && Math.Abs((long)now.Top - r.Top) <= 2 &&
                        Math.Abs((long)now.Right - r.Right) <= 2 && Math.Abs((long)now.Bottom - r.Bottom) <= 2;
            if (same && !zoomed)
                return maximized ? (Native.ShowWindowAsync(w, 3) ? 1 : -1) : 0;
            if (zoomed && !Native.ShowWindowAsync(w, 9)) return -1;
            if (!Native.SetWindowPos(w, IntPtr.Zero, x, y, width, height, 0x0014 | 0x4000)) return -1;
            if (maximized && !Native.ShowWindowAsync(w, 3)) return -1;
            return 1;
        }
    }
}
