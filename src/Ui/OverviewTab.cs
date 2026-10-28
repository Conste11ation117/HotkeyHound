using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    public sealed class OverviewTab : UserControl
    {
        private readonly MainForm _host;
        private readonly Button _scanButton;
        private readonly Button _reportButton;
        private readonly Button _elevateButton;
        private readonly ProgressBar _progress;
        private readonly Label _progressText;
        private readonly Label[] _cardValues;
        private readonly Label[] _cardTitles;
        private readonly ListView _list;

        public OverviewTab(MainForm host)
        {
            _host = host;

            // ------------------------------------------------------------ 顶部：自动换行堆栈
            var top = UiTheme.NewStack();

            var heading = new Label();
            heading.Text = L10n.T("ui.tab.overview");
            heading.Font = UiTheme.TitleFont(14F);
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 2, 0, 4);
            UiTheme.StackAdd(top, heading);

            var buttonRow = UiTheme.NewFlowRow();

            _scanButton = new Button();
            _scanButton.Text = L10n.T("ui.ov.scan");
            _scanButton.Font = UiTheme.TitleFont(12F);
            _scanButton.Size = new Size(160, 46);
            _scanButton.Margin = new Padding(0, 2, 10, 2);
            _scanButton.Click += OnScanClick;
            UiTheme.MarkPrimary(_scanButton);

            _reportButton = new Button();
            _reportButton.Text = L10n.T("ui.ov.report");
            _reportButton.Size = new Size(160, 42);
            _reportButton.Margin = new Padding(0, 4, 10, 4);
            _reportButton.Enabled = false;
            _reportButton.Click += OnReportClick;

            _elevateButton = new Button();
            _elevateButton.Text = L10n.T("ui.ov.elevate");
            // 按钮宽度按实际文字量出来：写死 160 会把「以管理员身份重启」截掉最后两个字。
            _elevateButton.AutoSize = true;
            _elevateButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _elevateButton.Padding = new Padding(10, 0, 10, 0);
            _elevateButton.MinimumSize = new Size(160, 42);
            _elevateButton.Margin = new Padding(0, 4, 10, 4);
            _elevateButton.Visible = !AppInfo.IsAdministrator();
            _elevateButton.Click += delegate (object s, EventArgs e) { _host.RestartAsAdministrator(); };

            buttonRow.Controls.Add(_scanButton);
            buttonRow.Controls.Add(_reportButton);
            buttonRow.Controls.Add(_elevateButton);

            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Fill;
            _progress.Height = 20;
            _progress.Margin = new Padding(0, 4, 0, 2);

            _progressText = UiTheme.StackLabel(L10n.T("ui.ov.notScanned"), UiTheme.Muted);

            UiTheme.StackAdd(top, buttonRow);
            UiTheme.StackAdd(top, _progress);
            UiTheme.StackAdd(top, _progressText);

            // ------------------------------------------------------------ 统计卡片
            var cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Top;
            cards.Height = 82;
            cards.ColumnCount = 4;
            cards.RowCount = 1;
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            string[] titles = new string[]
            {
                L10n.T("ui.ov.card.occupied"), L10n.T("ui.ov.card.free"),
                L10n.T("ui.ov.card.declared"), L10n.T("ui.ov.card.truth")
            };
            _cardValues = new Label[4];
            _cardTitles = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                var box = new CardPanel();
                box.Dock = DockStyle.Fill;
                box.Margin = new Padding(3);

                // 两行布局：数值独占上部、标题贴下。之前用绝对坐标，高 DPI 下数字会压到标题上。
                var stack = new TableLayoutPanel();
                stack.Dock = DockStyle.Fill;
                stack.ColumnCount = 1;
                stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                stack.RowCount = 2;
                stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                stack.Padding = new Padding(12, 6, 12, 8);
                stack.Margin = new Padding(0);
                try { stack.BackColor = Color.Transparent; }
                catch (Exception) { }

                _cardValues[i] = new Label();
                _cardValues[i].Text = "—";
                _cardValues[i].Font = UiTheme.TitleFont(16F);
                _cardValues[i].ForeColor = UiTheme.Accent;
                _cardValues[i].AutoSize = true;
                _cardValues[i].Dock = DockStyle.Fill;
                _cardValues[i].TextAlign = ContentAlignment.BottomLeft;
                _cardValues[i].Margin = new Padding(0);

                _cardTitles[i] = new Label();
                _cardTitles[i].Text = titles[i];
                _cardTitles[i].ForeColor = UiTheme.Muted;
                _cardTitles[i].AutoSize = true;
                _cardTitles[i].Dock = DockStyle.Fill;
                _cardTitles[i].TextAlign = ContentAlignment.TopLeft;
                _cardTitles[i].Margin = new Padding(0);

                stack.Controls.Add(_cardValues[i], 0, 0);
                stack.Controls.Add(_cardTitles[i], 0, 1);
                box.Controls.Add(stack);
                cards.Controls.Add(box, i, 0);
            }

            try
            {
                // 数值 + 标题 + 内边距：按实际字体高度算，避免高 DPI 下两行叠在一起。
                int needed = _cardValues[0].Font.Height + _cardTitles[0].Font.Height + 26;
                if (cards.Height < needed) cards.Height = needed;
            }
            catch (Exception) { }

            // ------------------------------------------------------------ 提示与列表
            // 提示文字用自动换行的堆栈标签：普通的 Dock=Top+AutoSize 标签不会换行，右端会被窗口裁掉。
            var hint = UiTheme.StackLabel(L10n.T("ui.ov.hint"), UiTheme.Muted);
            hint.Padding = new Padding(2, 4, 2, 2);
            var missHint = UiTheme.StackLabel(L10n.T("ui.ov.missHint"), UiTheme.Muted);
            missHint.Padding = new Padding(2, 0, 2, 6);
            var hintPanel = new TableLayoutPanel();
            hintPanel.Dock = DockStyle.Top;
            hintPanel.AutoSize = true;
            hintPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            hintPanel.ColumnCount = 1;
            hintPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hintPanel.RowCount = 2;
            hintPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            hintPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            hintPanel.Controls.Add(hint, 0, 0);
            hintPanel.Controls.Add(missHint, 0, 1);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 140);
            _list.Columns.Add(L10n.T("ui.col.state"), 70);
            _list.Columns.Add(L10n.T("ui.col.owner"), 220);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 80);
            _list.Columns.Add(L10n.T("ui.col.note"), 260);
            // 备注列吃满剩余宽度：最大化时不再截断、右边不再留白。
            UiTheme.FillColumn(_list, 4, 320);
            _list.DoubleClick += OnListDoubleClick;

            Controls.Add(_list);
            Controls.Add(hintPanel);
            Controls.Add(cards);
            Controls.Add(top);

            _host.SnapshotChanged += RefreshFromSnapshot;
        }

        private void OnScanClick(object sender, EventArgs e)
        {
            if (_host.IsScanning) return;
            _scanButton.Enabled = false;
            _reportButton.Enabled = false;
            _progress.Value = 0;
            _progressText.Text = L10n.T("ui.ov.scanning");

            _host.StartScan(
                delegate (int done, int total)
                {
                    if (IsDisposed) return;
                    try
                    {
                        UiPost.Post(this, delegate ()
                        {
                            if (total <= 0) return;
                            _progress.Maximum = total;
                            _progress.Value = Math.Min(done, total);
                            _progressText.Text = L10n.F("ui.ov.progress", done, total);
                        });
                    }
                    catch (Exception) { }
                },
                delegate (ScanSnapshot snapshot, Exception error)
                {
                    _scanButton.Enabled = true;
                    if (error != null)
                    {
                        _progressText.Text = L10n.F("ui.ov.scanFailed", error.Message);
                        MessageBox.Show(this, L10n.F("ui.ov.scanFailedDetail", error),
                                        AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    _progress.Value = _progress.Maximum;
                    _progressText.Text = L10n.F("ui.ov.scanDone", snapshot.ScanMilliseconds);
                    _reportButton.Enabled = true;
                    RefreshFromSnapshot();
                });
        }

        public void RefreshFromSnapshot()
        {
            ScanSnapshot s = _host.Snapshot;
            if (s == null) return;

            _cardValues[0].Text = s.OccupiedCount.ToString();
            _cardValues[1].Text = s.FreeCount.ToString();
            _cardValues[2].Text = s.DeclaredCount.ToString();
            _cardValues[3].Text = s.Truth.Rows.Count.ToString();

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (TruthRow r in s.Truth.Rows)
                {
                    if (r.Kind != TruthKind.MatchesDeclared && r.Kind != TruthKind.RegisteredButUnowned
                        && r.Kind != TruthKind.SystemReserved) continue;
                    var item = new ListViewItem(r.Combo.Display());
                    item.SubItems.Add(L10n.T("rep.state.occupied"));
                    item.SubItems.Add(string.IsNullOrEmpty(r.DeclaredBy)
                        ? (string.IsNullOrEmpty(r.OwnerHint) ? L10n.T("ui.ov.owner.none") : L10n.F("ui.ov.owner.reserved", L10n.Term(r.OwnerHint)))
                        : r.DeclaredBy);
                    item.SubItems.Add(r.Evidence);
                    item.SubItems.Add(r.Note);
                    if (r.Kind == TruthKind.RegisteredButUnowned) item.ForeColor = UiTheme.Error;
                    else if (r.Kind == TruthKind.SystemReserved) item.ForeColor = UiTheme.Info;
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            UiTheme.RefitColumns(_list);
        }

        private void OnListDoubleClick(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) return;
            string declared = _list.SelectedItems[0].SubItems[2].Text;
            MessageBox.Show(this, declared, L10n.T("ui.ov.ownerTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnReportClick(object sender, EventArgs e)
        {
            ScanSnapshot s = _host.Snapshot;
            if (s == null) return;

            string path = Path.Combine(Environment.CurrentDirectory, ReportBuilder.DefaultFileName());
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = L10n.T("ui.ov.saveTitle");
                dialog.Filter = L10n.T("ui.ov.saveFilter");
                dialog.FileName = Path.GetFileName(path);
                dialog.InitialDirectory = Environment.CurrentDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }

            try
            {
                ReportBuilder.Write(s, path);
                _host.Log.Write(L10n.F("cli.scan.reportWritten", path));
                var result = MessageBox.Show(this, L10n.F("ui.ov.reportSaved", path),
                                             AppInfo.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) OpenPath(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("ui.ov.reportWriteFailed", ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(L10n.F("ui.ov.openFailed", path, ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
