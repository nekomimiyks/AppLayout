using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AppLayout
{
    static class Program
    {
        // exe 名ごとに分ける（UI 案の exe を同時に試せるように）
        static readonly string InstanceId = Path.GetFileNameWithoutExtension(Application.ExecutablePath).ToLowerInvariant();
        public static readonly string ShowEventName = "Local\\AppLayoutGUI-Show-" + InstanceId;

        [STAThread]
        static void Main(string[] args)
        {
            Native.MakeDpiAware();
            bool startup = false;
            foreach (var a in args) if (a.Equals("/startup", StringComparison.OrdinalIgnoreCase)) startup = true;

            bool created;
            using (var mutex = new Mutex(true, "Local\\AppLayoutGUI-Single-" + InstanceId, out created))
            {
                if (!created)
                {
                    // 既に起動中なら、そちらのウィンドウを前面に出す
                    try { Native.AllowSetForegroundWindow(-1); EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return;
                }
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
    }
}
