using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AppLayout
{
    internal sealed class AboutDialog : DpiAwareForm
    {
        internal const string GitHubUrl = "https://github.com/nekomimiyks";
        internal const string StoreUrl = "https://apps.microsoft.com/search/publisher?name=nekomimiyks";
        internal const string StoreAppUrl = "ms-windows-store://publisher/?name=nekomimiyks";

        public AboutDialog()
        {
            Text = L.T("アプリについて");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = ShowIcon = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AppTheme.Apply(this);

            var content = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(Ui.S(20)), Margin = Padding.Empty };
            var name = new Label { AutoSize = true, Text = "AppLayout", Margin = new Padding(0, 0, 0, Ui.S(6)) };
            var version = new Label { Name = "AboutVersion", AutoSize = true, Text = L.F("バージョン: {0}", RunningVersion()),
                ForeColor = AppTheme.Muted, Margin = new Padding(0, 0, 0, Ui.S(16)) };
            var github = PublisherLink(L.T("GitHubの公開アプリ"), GitHubUrl);
            var store = PublisherLink(L.T("Microsoft Storeの公開アプリ"), StoreUrl);
            var close = AppTheme.Button(L.T("閉じる"), false);
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, Ui.S(8), 0, 0);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            content.Controls.Add(name);
            content.Controls.Add(version);
            content.Controls.Add(github);
            content.Controls.Add(store);
            content.Controls.Add(close);
            Controls.Add(content);
            ActiveControl = close;
        }

        static string RunningVersion()
        {
#if STORE_BUILD
            try
            {
                var version = Windows.ApplicationModel.Package.Current.Id.Version;
                return new Version(version.Major, version.Minor, version.Build, version.Revision).ToString();
            }
            catch (InvalidOperationException) { } // パッケージ外で起動した開発ビルド。
            catch (COMException) { }
#else
            var version = ReleaseCheck.InstalledVersion;
            if (version != null) return version.ToString();
#endif
            return L.T("開発ビルド");
        }

        LinkLabel PublisherLink(string caption, string url)
        {
            var link = AppTheme.Link(caption, AppTheme.Accent);
            link.Margin = new Padding(0, 0, 0, Ui.S(8));
            link.Links[0].LinkData = url;
            link.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                try
                {
                    string target = (string)e.Link.LinkData;
                    if (target == StoreUrl) OpenStore(OpenUrl);
                    else OpenUrl(target);
                }
                catch (Win32Exception)
                {
                    MessageBox.Show(this, L.T("ブラウザーを開けませんでした。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            return link;
        }

        static void OpenStore(Action<string> open)
        {
            try { open(StoreAppUrl); }
            catch (Win32Exception) { open(StoreUrl); }
        }

        static void OpenUrl(string url)
        {
            using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
        }
    }
}
