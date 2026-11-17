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
            // 単一起動の判定より先にイベントを確保し、初回起動中の表示要求も保持する。
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            using (var mutex = new Mutex(true, InstanceMutexName, out created))
            {
                if (!created)
                {
                    // 既に起動中なら、そちらのウィンドウを前面に出す
                    Native.AllowSetForegroundWindow(-1);
                    showEvent.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var store = new ConfigStore(ConfigStore.DefaultPath());
                try { store.Load(); }
                catch (Exception ex)
                {
                    L.SetLanguage("auto");
                    MessageBox.Show(L.T("設定ファイルを読み込めませんでした。\n") + store.Path + "\n\n" + ex.Message, "AppLayout", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                L.SetLanguage(store.Config.Language);
                var controller = new AppController(store, startup);
                using (var form = UiFactory.CreateMainForm(controller))
                    Shell.Run(form, controller, startup);
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
