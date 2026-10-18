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
        private readonly Label _hint;

        public OverviewTab(MainForm host)
        {
            _host = host;

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 96;

            _scanButton = new Button();
            _scanButton.Text = L10n.T("ui.ov.scan");
            _scanButton.Font = UiTheme.TitleFont(12F);
            _scanButton.Size = new Size(150, 46);
            _scanButton.Location = new Point(0, 4);
            _scanButton.Click += OnScanClick;
            UiTheme.MarkPrimary(_scanButton);

            _reportButton = new Button();
            _reportButton.Text = L10n.T("ui.ov.report");
            _reportButton.Size = new Size(150, 42);
            _reportButton.Location = new Point(160, 6);
            _reportButton.Enabled = false;
            _reportButton.Click += OnReportClick;

            _elevateButton = new Button();
            _elevateButton.Text = L10n.T("ui.ov.elevate");
            _elevateButton.Size = new Size(150, 42);
            _elevateButton.Location = new Point(320, 6);
            _elevateButton.Visible = !AppInfo.IsAdministrator();
            _elevateButton.Click += delegate (object s, EventArgs e) { _host.RestartAsAdministrator(); };

            _progress = new ProgressBar();
            _progress.Location = new Point(480, 6);
            _progress.Size = new Size(280, 20);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _progressText = new Label();
            _progressText.Location = new Point(480, 32);
            _progressText.Size = new Size(300, 18);
            _progressText.Text = L10n.T("ui.ov.notScanned");
            _progressText.ForeColor = UiTheme.Muted;
            _progressText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            top.Controls.Add(_scanButton);
            top.Controls.Add(_reportButton);
            top.Controls.Add(_elevateButton);
            top.Controls.Add(_progress);
            top.Controls.Add(_progressText);

            var cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Top;
            cards.Height = 74;
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
                var box = new Panel();
                box.Dock = DockStyle.Fill;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.Margin = new Padding(3);

                _cardValues[i] = new Label();
                _cardValues[i].Text = "—";
                _cardValues[i].Font = UiTheme.TitleFont(16F);
                _cardValues[i].Location = new Point(10, 3);
                _cardValues[i].AutoSize = true;

                _cardTitles[i] = new Label();
                _cardTitles[i].Text = titles[i];
                _cardTitles[i].ForeColor = UiTheme.Muted;
                _cardTitles[i].Location = new Point(12, 43);
                _cardTitles[i].AutoSize = true;

                box.Controls.Add(_cardValues[i]);
                box.Controls.Add(_cardTitles[i]);
                cards.Controls.Add(box, i, 0);
            }

            _hint = new Label();
            _hint.Dock = DockStyle.Top;
            _hint.Height = 34;
            _hint.AutoSize = false;
            _hint.ForeColor = UiTheme.Muted;
            _hint.Text = L10n.T("ui.ov.hint");

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 190);
            _list.Columns.Add(L10n.T("ui.col.state"), 90);
            _list.Columns.Add(L10n.T("ui.col.owner"), 330);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 80);
            _list.Columns.Add(L10n.T("ui.col.note"), 360);
            _list.DoubleClick += OnListDoubleClick;

            Controls.Add(_list);
            Controls.Add(_hint);
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
                        BeginInvoke(new Action(delegate ()
                        {
                            if (total <= 0) return;
                            _progress.Maximum = total;
                            _progress.Value = Math.Min(done, total);
                            _progressText.Text = L10n.F("ui.ov.progress", done, total);
                        }));
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
                    if (r.Kind != TruthKind.MatchesDeclared && r.Kind != TruthKind.RegisteredButUnowned) continue;
                    var item = new ListViewItem(r.Combo.Display());
                    item.SubItems.Add(L10n.T("rep.state.occupied"));
                    item.SubItems.Add(string.IsNullOrEmpty(r.DeclaredBy)
                        ? (string.IsNullOrEmpty(r.OwnerHint) ? L10n.T("ui.ov.owner.none") : L10n.F("ui.ov.owner.reserved", L10n.Term(r.OwnerHint)))
                        : r.DeclaredBy);
                    item.SubItems.Add(r.Evidence);
                    item.SubItems.Add(r.Note);
                    if (r.Kind == TruthKind.RegisteredButUnowned) item.ForeColor = UiTheme.Error;
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
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
