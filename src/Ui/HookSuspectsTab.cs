using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    /// <summary>
    /// 第 6 页：钩子嫌疑。数据全部来自「读配置 + 看进程 + 对照扫描」，本页加载不安装任何钩子。
    /// </summary>
    public sealed class HookSuspectsTab : UserControl
    {
        private readonly MainForm _host;
        private readonly ComboBox _filter;
        private readonly ListView _list;
        private readonly Label _counter;
        private readonly Button _openButton;
        private readonly string _allLevels;

        public HookSuspectsTab(MainForm host)
        {
            _host = host;
            _allLevels = L10n.T("ui.hs.filter.all");

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 132;

            var title = new Label();
            title.Text = L10n.T("ui.hs.title");
            title.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            title.Location = new Point(0, 4);
            title.AutoSize = true;

            var hint = new Label();
            hint.Location = new Point(2, 30);
            hint.Size = new Size(1010, 56);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            hint.ForeColor = Color.DimGray;
            hint.Text = L10n.T("ui.hs.hint") + "\r\n" + L10n.T("ui.hs.levels");

            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Location = new Point(0, 96);
            _filter.Width = 200;
            _filter.Items.Add(_allLevels);
            foreach (HookEvidence ev in new HookEvidence[]
            {
                HookEvidence.Confirmed, HookEvidence.HighlySuspected,
                HookEvidence.KnownDefault, HookEvidence.ReferenceOnly
            })
            {
                _filter.Items.Add(HookCatalog.EvidenceName(ev));
            }
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += delegate (object s, EventArgs e) { Rebuild(); };

            _counter = new Label();
            _counter.Location = new Point(212, 100);
            _counter.AutoSize = true;
            _counter.ForeColor = Color.Gray;

            _openButton = new Button();
            _openButton.Text = L10n.T("ui.hs.openConfig");
            _openButton.Location = new Point(360, 92);
            _openButton.Size = new Size(170, 28);
            _openButton.Click += delegate (object s, EventArgs e) { OpenSelectedConfig(); };

            top.Controls.Add(title);
            top.Controls.Add(hint);
            top.Controls.Add(_filter);
            top.Controls.Add(_counter);
            top.Controls.Add(_openButton);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 160);
            _list.Columns.Add(L10n.T("ui.hs.col.software"), 140);
            _list.Columns.Add(L10n.T("ui.hs.col.module"), 130);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 90);
            _list.Columns.Add(L10n.T("ui.hs.col.process"), 86);
            _list.Columns.Add(L10n.T("ui.hs.col.config"), 86);
            _list.Columns.Add(L10n.T("ui.col.note"), 380);
            _list.Columns.Add(L10n.T("ui.hs.col.source"), 300);
            _list.DoubleClick += delegate (object s, EventArgs e) { OpenSelectedConfig(); };

            Controls.Add(_list);
            Controls.Add(top);

            _host.SnapshotChanged += Rebuild;
        }

        public void Rebuild()
        {
            ScanSnapshot snapshot = _host.Snapshot;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (snapshot == null)
                {
                    _counter.Text = L10n.T("ui.hs.noScan");
                    return;
                }
                if (snapshot.HookSuspects == null || snapshot.HookSuspects.Count == 0)
                {
                    _counter.Text = L10n.T("ui.hs.empty");
                    return;
                }

                HookEvidence? only = null;
                string selected = _filter.SelectedItem as string;
                foreach (HookEvidence ev in new HookEvidence[]
                {
                    HookEvidence.Confirmed, HookEvidence.HighlySuspected,
                    HookEvidence.KnownDefault, HookEvidence.ReferenceOnly
                })
                {
                    if (HookCatalog.EvidenceName(ev) == selected) { only = ev; break; }
                }

                int shown = 0;
                foreach (HookSuspect r in snapshot.HookSuspects)
                {
                    if (only.HasValue && r.Evidence != only.Value) continue;
                    var item = new ListViewItem(r.Combo == null ? L10n.T("ui.hs.unknownCombo") : r.Combo.Display());
                    item.SubItems.Add(r.Software);
                    item.SubItems.Add(r.Module);
                    item.SubItems.Add(HookCatalog.EvidenceName(r.Evidence));
                    item.SubItems.Add(r.ProcessRunning ? L10n.T("ui.hs.processYes") : L10n.T("ui.hs.processNo"));
                    item.SubItems.Add(HookCatalog.ConfigStateName(r.ConfigState));
                    item.SubItems.Add(r.Note);
                    item.SubItems.Add(string.IsNullOrEmpty(r.SourcePath) ? L10n.T("common.none") : r.SourcePath);
                    item.Tag = r;
                    switch (r.Evidence)
                    {
                        case HookEvidence.Confirmed: item.ForeColor = Color.FromArgb(20, 110, 40); break;
                        case HookEvidence.HighlySuspected: item.ForeColor = Color.FromArgb(150, 40, 30); break;
                        case HookEvidence.KnownDefault: item.ForeColor = Color.FromArgb(160, 100, 10); break;
                        default: item.ForeColor = Color.Gray; break;
                    }
                    _list.Items.Add(item);
                    shown++;
                }
                _counter.Text = L10n.F("ui.hs.count", shown);
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void OpenSelectedConfig()
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, L10n.T("ui.hs.pickRow"), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var row = _list.SelectedItems[0].Tag as HookSuspect;
            if (row == null || string.IsNullOrEmpty(row.SourcePath))
            {
                MessageBox.Show(this, L10n.F("ui.hs.noSource", row == null ? "" : row.Note), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string path = row.SourcePath;
            if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, L10n.F("ui.hs.noSource", path), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string target = path;
            try
            {
                if (File.Exists(path))
                {
                    target = Path.GetDirectoryName(path);
                }
            }
            catch (Exception) { }

            try
            {
                if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                {
                    throw new DirectoryNotFoundException(target);
                }
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L10n.F("ui.hs.openFailed", target, ex.Message), AppInfo.Title,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
