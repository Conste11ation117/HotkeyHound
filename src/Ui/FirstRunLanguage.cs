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
    /// 布局用 AutoSize 行，绝不写死坐标（否则文本一换行就与单选按钮重叠）。
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
            ClientSize = new Size(580, 360);
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BaseFont();

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 标题
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 说明（自动换行）
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // English
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 中文
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));     // 弹性空白
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 按钮
            root.Padding = new Padding(26, 20, 26, 18);

            var heading = new Label();
            heading.Text = "Choose your language / 选择界面语言";
            heading.Font = UiTheme.TitleFont(14F);
            heading.AutoSize = true;
            heading.Dock = DockStyle.Fill;
            heading.Margin = new Padding(0, 0, 0, 10);

            var explain = new Label();
            explain.Text = "HotkeyHound speaks English and Simplified Chinese.\r\n"
                         + "You can change this at any time in Report & About → Language.\r\n"
                         + "HotkeyHound 支持英文与简体中文，之后可随时在「报告与关于」页切换。";
            explain.AutoSize = true;
            explain.Dock = DockStyle.Fill;
            explain.ForeColor = UiTheme.Muted;
            explain.Margin = new Padding(0, 0, 0, 12);

            _en = new RadioButton();
            _en.Text = "English (en-US)  —  default / 默认";
            _en.AutoSize = true;
            _en.Dock = DockStyle.Fill;
            _en.Checked = true;
            _en.Margin = new Padding(0, 4, 0, 4);

            _zh = new RadioButton();
            _zh.Text = "中文（简体） (zh-CN)";
            _zh.AutoSize = true;
            _zh.Dock = DockStyle.Fill;
            _zh.Margin = new Padding(0, 4, 0, 4);

            var buttonRow = new TableLayoutPanel();
            buttonRow.Dock = DockStyle.Fill;
            buttonRow.AutoSize = true;
            buttonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonRow.ColumnCount = 2;
            buttonRow.RowCount = 1;
            buttonRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var ok = new Button();
            ok.Text = "Continue / 继续";
            ok.AutoSize = true;
            ok.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ok.Padding = new Padding(14, 2, 14, 2);
            ok.MinimumSize = new Size(170, 36);
            ok.DialogResult = DialogResult.OK;
            UiTheme.MarkPrimary(ok);
            buttonRow.Controls.Add(new Label(), 0, 0);
            buttonRow.Controls.Add(ok, 1, 0);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(explain, 0, 1);
            root.Controls.Add(_en, 0, 2);
            root.Controls.Add(_zh, 0, 3);
            root.Controls.Add(new Label(), 0, 4);
            root.Controls.Add(buttonRow, 0, 5);
            Controls.Add(root);
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
