using System;
using System.Drawing;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 首次启动的语言选择框（默认选中 English）。
    ///
    /// 为什么必须是独立窗口：界面上的所有文字都在 MainForm 构造时按语言生成，
    /// 所以语言必须在 MainForm 之前定下来。而此刻还没有「当前语言」，本窗口刻意
    /// 双语并列书写，不走 L10n（这是一次性的引导，不是界面文案）。
    /// 用户没选就关掉窗口时按默认英文处理，并照样写入配置——下次启动不再打扰。
    /// </summary>
    public sealed class FirstRunLanguage : Form
    {
        private readonly RadioButton _en;
        private readonly RadioButton _zh;

        private FirstRunLanguage()
        {
            UiTheme.EnsureInitialized();

            Text = "HotkeyHound — Language / 语言";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 300);
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BaseFont();

            var heading = new Label();
            heading.Text = "Choose your language\n选择界面语言";
            heading.Font = UiTheme.TitleFont(14F);
            heading.AutoSize = true;
            heading.Location = new Point(24, 22);

            var explain = new Label();
            explain.Text = "HotkeyHound speaks English and Simplified Chinese.\r\n"
                         + "You can change this at any time in Report & About → Language.\r\n"
                         + "HotkeyHound 支持英文与简体中文，之后可随时在「报告与关于」页切换。";
            explain.AutoSize = true;
            explain.ForeColor = UiTheme.Muted;
            explain.Location = new Point(26, 92);

            _en = new RadioButton();
            _en.Text = "English (en-US)  —  default / 默认";
            _en.AutoSize = true;
            _en.Location = new Point(30, 152);
            _en.Checked = true;

            _zh = new RadioButton();
            _zh.Text = "中文（简体） (zh-CN)";
            _zh.AutoSize = true;
            _zh.Location = new Point(30, 182);

            var ok = new Button();
            ok.Text = "Continue / 继续";
            ok.Size = new Size(170, 36);
            ok.Location = new Point(ClientSize.Width - 170 - 24, ClientSize.Height - 36 - 22);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.DialogResult = DialogResult.OK;
            UiTheme.MarkPrimary(ok);

            Controls.Add(heading);
            Controls.Add(explain);
            Controls.Add(_en);
            Controls.Add(_zh);
            Controls.Add(ok);
            AcceptButton = ok;

            // 直接关窗（X / Esc）按默认英文处理：第一次打开就能用，不会问第二遍。
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    _en.Checked = true;
                }
            };
        }

        /// <summary>弹出选择框并写回配置；返回最终语言。</summary>
        public static string Ask()
        {
            string chosen;
            using (var form = new FirstRunLanguage())
            {
                try { form.ShowDialog(); }
                catch (Exception) { }
                chosen = form._zh.Checked ? L10n.Chinese : L10n.English;
            }
            L10n.SetLanguage(chosen);
            L10n.WriteSetting("firstRun", "1");
            return chosen;
        }
    }
}
