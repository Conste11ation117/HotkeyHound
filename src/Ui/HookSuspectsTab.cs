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
        private readonly string _allLevels;

        public HookSuspectsTab(MainForm host)
        {
            _host = host;
            _allLevels = L10n.T("ui.hs.filter.all");

            var top = UiTheme.NewStack();

            var title = new Label();
            title.Text = L10n.T("ui.hs.title");
            title.Font = UiTheme.TitleFont(14F);
            title.AutoSize = true;
            title.Margin = new Padding(0, 2, 0, 4);

            var hint = UiTheme.StackLabel(L10n.T("ui.hs.hint") + "\r\n" + L10n.T("ui.hs.levels"), UiTheme.Muted);

            var filterRow = UiTheme.NewFlowRow();

            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Width = 220;
            _filter.Margin = new Padding(0, 4, 12, 4);
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
            _counter.AutoSize = true;
            _counter.Anchor = AnchorStyles.None;
            _counter.Margin = new Padding(0, 8, 16, 0);
            _counter.ForeColor = UiTheme.Muted;

            var openButton = new Button();
            openButton.Text = L10n.T("ui.hs.openConfig");
            openButton.Size = new Size(180, 30);
            openButton.Margin = new Padding(0, 3, 0, 3);
            openButton.Click += delegate (object s, EventArgs e) { OpenSelectedConfig(); };

            filterRow.Controls.Add(_filter);
            filterRow.Controls.Add(_counter);
            filterRow.Controls.Add(openButton);

            UiTheme.StackAdd(top, title);
            UiTheme.StackAdd(top, hint);
            UiTheme.StackAdd(top, filterRow);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 110);
            _list.Columns.Add(L10n.T("ui.hs.col.software"), 110);
            _list.Columns.Add(L10n.T("ui.hs.col.module"), 120);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 90);
            _list.Columns.Add(L10n.T("ui.hs.col.process"), 80);
            _list.Columns.Add(L10n.T("ui.hs.col.config"), 80);
            _list.Columns.Add(L10n.T("ui.hs.col.source"), 160);
            _list.Columns.Add(L10n.T("ui.col.note"), 220);
            // 来源路径列吃满剩余宽度：长路径不再截断，最大化不再留白。
            UiTheme.FillColumn(_list, 7, 260);
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
                    item.SubItems.Add(string.IsNullOrEmpty(r.SourcePath) ? L10n.T("common.none") : r.SourcePath);
                    item.SubItems.Add(r.Note);
                    item.Tag = r;
                    switch (r.Evidence)
                    {
                        case HookEvidence.Confirmed: item.ForeColor = UiTheme.Success; break;
                        case HookEvidence.HighlySuspected: item.ForeColor = UiTheme.Error; break;
                        case HookEvidence.KnownDefault: item.ForeColor = UiTheme.Warning; break;
                        default: item.ForeColor = UiTheme.Muted; break;
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
            UiTheme.RefitColumns(_list);
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
