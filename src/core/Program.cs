using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AppLayout
{
    static class Program
    {
        // ファイル名・UI案・配置フォルダが変わっても同じセッションでは1つだけ起動する。
        public const string InstanceMutexName = "Local\\AppLayoutGUI-Single";
        public const string ShowEventName = "Local\\AppLayoutGUI-Show";

        [STAThread]
        static void Main(string[] args)
        {
            Native.MakeDpiAware();
            bool startup = false;
            foreach (var a in args) if (a.Equals("/startup", StringComparison.OrdinalIgnoreCase)) startup = true;

            bool created;
            using (var mutex = new Mutex(true, InstanceMutexName, out created))
            {
                if (!created)
                {
                    // 既に起動中なら、そちらのウィンドウを前面に出す
                    Native.AllowSetForegroundWindow(-1);
                    // 初回プロセスが通知イベントを作るまでの短い起動競合にも対応。
                    for (int i = 0; i < 20; i++) {
                        try { using (var show = EventWaitHandle.OpenExisting(ShowEventName)) show.Set(); break; }
                        catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
                    }
                    return;
                }
                using (var earlyShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName)) {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var store = new ConfigStore(ConfigStore.DefaultPath());
                try { store.Load(); }
                catch (Exception ex)
                {
                    MessageBox.Show("設定ファイルを読み込めませんでした。\n" + store.Path + "\n\n" + ex.Message, "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var controller = new AppController(store, startup);
                Shell.Run(UiFactory.CreateMainForm(controller), controller, startup);
                }
            }
        }
    }
}

namespace AppLayout
{
    public static class Ui
    {
        static float k = -1;
        public static float K
        {
            get
            {
                if (k < 0) { using (var g = System.Drawing.Graphics.FromHwnd(System.IntPtr.Zero)) k = g.DpiX / 96f; }
                return k;
            }
        }
        public static int S(int v) { return (int)System.Math.Round(v * K); }
        public static System.Drawing.Size Sz(int w, int h) { return new System.Drawing.Size(S(w), S(h)); }
        internal static void SetDpi(int dpi) { if (dpi >= 48 && dpi <= 768) k = dpi / 96f; }
    }
}
