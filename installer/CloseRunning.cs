using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

static class CloseRunning
{
    [StructLayout(LayoutKind.Sequential)]
    struct RestartProcess
    {
        public uint Id;
        public FILETIME Started;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RmRegisterResources(uint session, uint fileCount, IntPtr files,
        uint processCount, [In] RestartProcess[] processes, uint serviceCount, IntPtr services);

    [DllImport("rstrtmgr.dll", ExactSpelling = true)]
    static extern int RmShutdown(uint session, uint flags, IntPtr callback);

    [DllImport("rstrtmgr.dll", ExactSpelling = true)]
    static extern int RmEndSession(uint session);

    delegate bool WindowCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(WindowCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr window, StringBuilder title, int capacity);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter,
        IntPtr reason, uint flags, uint timeout, out IntPtr result);

    static bool CloseHiddenWindow(Process process)
    {
        if (process.MainWindowHandle != IntPtr.Zero) return false;

        const uint queryEndSession = 0x0011;
        const uint endSession = 0x0016;
        const uint abortIfHung = 0x0002;
        var closeForUpdate = new IntPtr(1); // ENDSESSION_CLOSEAPP
        uint id = (uint)process.Id;
        IntPtr target = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr state)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != id || IsWindowVisible(window)) return true;
            var title = new StringBuilder(32);
            GetWindowText(window, title, title.Capacity);
            if (!string.Equals(title.ToString(), "AppLayout", StringComparison.Ordinal)) return true;
            target = window;
            return false;
        }, IntPtr.Zero);

        IntPtr consent;
        if (target == IntPtr.Zero ||
            SendMessageTimeout(target, queryEndSession, IntPtr.Zero, closeForUpdate, abortIfHung, 1500, out consent) == IntPtr.Zero ||
            consent == IntPtr.Zero)
            return false;

        IntPtr ignored;
        SendMessageTimeout(target, endSession, new IntPtr(1), closeForUpdate, abortIfHung, 2000, out ignored);
        return process.WaitForExit(2500);
    }

    static int Main(string[] args)
    {
        if (args.Length != 1) return 1;
        string executable = Path.GetFullPath(args[0]);
        int currentSession = Process.GetCurrentProcess().SessionId;
        var targets = new List<RestartProcess>();
        foreach (var process in Process.GetProcessesByName("AppLayout"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != currentSession ||
                        !string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Older versions keep a hidden WinForms window that Restart Manager does not close.
                    if (CloseHiddenWindow(process)) continue;
                    long started = process.StartTime.ToFileTimeUtc();
                    targets.Add(new RestartProcess { Id = (uint)process.Id,
                        Started = new FILETIME { dwLowDateTime = (int)started, dwHighDateTime = (int)(started >> 32) } });
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }

        if (targets.Count == 0) return 0;
        uint session;
        int result = RmStartSession(out session, 0, new StringBuilder(33));
        if (result != 0) return result;
        try
        {
            result = RmRegisterResources(session, 0, IntPtr.Zero, (uint)targets.Count, targets.ToArray(), 0, IntPtr.Zero);
            return result == 0 ? RmShutdown(session, 0, IntPtr.Zero) : result;
        }
        finally { RmEndSession(session); }
    }
}
