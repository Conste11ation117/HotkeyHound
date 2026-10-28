using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 帮助窗口：用 WebBrowser 渲染内嵌的帮助 HTML（docs/help.*.html 作为资源嵌进 exe）。
    /// 带「中文 / English」切换与「在浏览器中打开」（导出到临时目录后 ShellExecute）。
    /// 帮助窗口本身不安装任何钩子。
    /// </summary>
    public sealed class HelpForm : Form
    {
        private readonly WebBrowser _browser;
        private readonly Button _zhButton;
        private readonly Button _enButton;
        private readonly Button _openButton;
        private readonly Button _closeButton;
        private string _lang;

        public HelpForm(string initialLang)
        {
            _lang = L10n.Normalize(initialLang) ?? L10n.Current;

            Text = L10n.T("help.title") + " — " + AppInfo.Title;
            ClientSize = ClampToScreen(new Size(900, 640));
            MinimumSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            try { UiTheme.ApplyFont(this); }
            catch (Exception) { }

            // 流式按钮行：任何语言、任何 DPI 下都不会因为写死坐标而互相压住。
            var bar = UiTheme.NewFlowRow();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.Padding = new Padding(10, 8, 10, 8);
            bar.BackColor = UiTheme.SurfaceAlt;
            bar.Tag = "sidebar";

            _zhButton = MakeButton("help.zh", false);
            _zhButton.Click += delegate (object s, EventArgs e) { SwitchTo(L10n.Chinese); };
            _enButton = MakeButton("help.en", false);
            _enButton.Click += delegate (object s, EventArgs e) { SwitchTo(L10n.English); };
            _openButton = MakeButton("help.openBrowser", true);
            _openButton.Click += delegate (object s, EventArgs e) { OpenInBrowser(); };
            _closeButton = MakeButton("help.close", false);
            _closeButton.Click += delegate (object s, EventArgs e) { Close(); };

            bar.Controls.Add(_zhButton);
            bar.Controls.Add(_enButton);
            bar.Controls.Add(_openButton);
            bar.Controls.Add(_closeButton);

            _browser = new WebBrowser();
            _browser.Dock = DockStyle.Fill;
            _browser.ScriptErrorsSuppressed = true;
            _browser.IsWebBrowserContextMenuEnabled = false;
            _browser.AllowWebBrowserDrop = false;

            Controls.Add(_browser);
            Controls.Add(bar);
            UiTheme.ApplyTree(this);

            LoadHtml();
        }

        private static Button MakeButton(string key, bool wide)
        {
            var b = new Button();
            b.Text = L10n.T(key);
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(wide ? 142 : 82, 30);
            b.Padding = new Padding(12, 0, 12, 0);
            b.Margin = new Padding(0, 0, 8, 0);
            return b;
        }

        private void LoadHtml()
        {
            string html = HelpResources.Read(_lang);
            if (html == null)
            {
                html = "<html><body><p>" + Html.Escape(L10n.T("help.missing")) + "</p></body></html>";
            }
            try
            {
                // 用 DocumentText 渲染内嵌 HTML；内容不含外链。
                _browser.DocumentText = html;
            }
            catch (Exception) { }
        }

        private void SwitchTo(string lang)
        {
            _lang = lang;
            LoadHtml();
        }

        private void OpenInBrowser()
        {
            try
            {
                string html = HelpResources.Read(_lang);
                if (html == null) html = "<html><body><p>" + Html.Escape(L10n.T("help.missing")) + "</p></body></html>";
                string dir = TempWorkspace.Acquire("HotkeyHound-Help");
                string path = Path.Combine(dir, HelpResources.FileName(_lang));
                File.WriteAllText(path, html, new System.Text.UTF8Encoding(true));
                OverviewTab.OpenPath(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("help.exportFailed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static Size ClampToScreen(Size desired)
        {
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int w = Math.Min(desired.Width, Math.Max(320, wa.Width - 40));
                int h = Math.Min(desired.Height, Math.Max(240, wa.Height - 40));
                return new Size(w, h);
            }
            catch (Exception) { return desired; }
        }
    }
}
