using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using HotkeyHound.Core;

namespace HotkeyHound.Ui
{
    public sealed class TruthTab : UserControl
    {
        private readonly MainForm _host;
        private readonly ComboBox _filter;
        private readonly ListView _list;
        private readonly Label _counter;
        private readonly string _allButFree;
        private readonly Dictionary<string, string> _sourceByCombo = new Dictionary<string, string>(StringComparer.Ordinal);

        public TruthTab(MainForm host)
        {
            _host = host;
            _allButFree = L10n.T("ui.truth.filter.all");

            var top = UiTheme.NewStack();

            var heading = new Label();
            heading.Text = L10n.T("ui.tab.truth");
            heading.Font = UiTheme.TitleFont(14F);
            heading.AutoSize = true;
            heading.Margin = new Padding(0, 2, 0, 4);
            UiTheme.StackAdd(top, heading);

            var filterRow = UiTheme.NewFlowRow();

            var label = new Label();
            label.Text = L10n.T("ui.truth.category");
            label.AutoSize = true;
            label.Anchor = AnchorStyles.None;
            label.Margin = new Padding(0, 8, 4, 0);

            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Width = 280;
            _filter.Margin = new Padding(0, 4, 12, 4);
            _filter.Items.Add(_allButFree);
            foreach (TruthKind k in new TruthKind[]
            {
                TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                TruthKind.RegisteredButUnowned, TruthKind.SystemReserved, TruthKind.FreeAndUndeclared
            })
            {
                _filter.Items.Add(TruthTable.KindName(k));
            }
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += delegate (object s, EventArgs e) { Rebuild(); };

            _counter = new Label();
            _counter.AutoSize = true;
            _counter.Anchor = AnchorStyles.None;
            _counter.Margin = new Padding(0, 8, 0, 0);
            _counter.ForeColor = UiTheme.Muted;

            filterRow.Controls.Add(label);
            filterRow.Controls.Add(_filter);
            filterRow.Controls.Add(_counter);

            var explain = UiTheme.StackLabel(L10n.T("ui.truth.explain"), UiTheme.Muted);

            UiTheme.StackAdd(top, filterRow);
            UiTheme.StackAdd(top, explain);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 140);
            _list.Columns.Add(L10n.T("ui.col.type"), 150);
            _list.Columns.Add(L10n.T("ui.col.declaredBy"), 220);
            _list.Columns.Add(L10n.T("ui.col.ownerHint"), 110);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 85);
            _list.Columns.Add(L10n.T("ui.col.note"), 220);
            // 备注列吃满剩余宽度（2.0.0：修「最大化时备注显示不完整」）。
            UiTheme.FillColumn(_list, 5, 300);
            _list.DoubleClick += OnDoubleClick;

            Controls.Add(_list);
            Controls.Add(top);

            _host.SnapshotChanged += Rebuild;
        }

        public void Rebuild()
        {
            ScanSnapshot s = _host.Snapshot;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                if (s == null)
                {
                    _counter.Text = L10n.T("ui.truth.notScanned");
                    return;
                }

                _sourceByCombo.Clear();
                foreach (DeclaredHotkey d in s.Config.Declared)
                {
                    if (d == null || d.Combo == null) continue;
                    string key = d.Combo.Normalize();
                    if (!_sourceByCombo.ContainsKey(key) && !string.IsNullOrEmpty(d.SourcePath))
                        _sourceByCombo[key] = d.SourcePath;
                }

                string selected = _filter.SelectedItem as string;
                TruthKind? only = null;
                foreach (TruthKind k in new TruthKind[]
                {
                    TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                    TruthKind.RegisteredButUnowned, TruthKind.SystemReserved, TruthKind.FreeAndUndeclared
                })
                {
                    if (TruthTable.KindName(k) == selected) { only = k; break; }
                }

                bool freeSampled = only == TruthKind.FreeAndUndeclared;
                int shown = 0;
                int total = 0;

                foreach (TruthRow r in s.Truth.Rows)
                {
                    if (only.HasValue && r.Kind != only.Value) continue;
                    if (!only.HasValue && r.Kind == TruthKind.FreeAndUndeclared) continue;
                    total++;
                }

                foreach (TruthRow r in s.Truth.Rows)
                {
                    if (only.HasValue && r.Kind != only.Value) continue;
                    if (!only.HasValue && r.Kind == TruthKind.FreeAndUndeclared) continue;
                    if (freeSampled && shown >= 300) break;

                    var item = new ListViewItem(r.Combo.Display());
                    item.SubItems.Add(TruthTable.KindName(r.Kind));
                    item.SubItems.Add(string.IsNullOrEmpty(r.DeclaredBy) ? L10n.T("common.none") : r.DeclaredBy);
                    item.SubItems.Add(string.IsNullOrEmpty(r.OwnerHint) ? L10n.T("common.none") : L10n.Term(r.OwnerHint));
                    item.SubItems.Add(r.Evidence);
                    item.SubItems.Add(r.Note);
                    item.Tag = r;
                    switch (r.Kind)
                    {
                        case TruthKind.MatchesDeclared: item.ForeColor = UiTheme.Success; break;
                        case TruthKind.RegisteredButUnowned: item.ForeColor = UiTheme.Error; break;
                        case TruthKind.SystemReserved: item.ForeColor = UiTheme.Info; break;
                        case TruthKind.DeclaredButNotRegistered: item.ForeColor = UiTheme.Warning; break;
                        default: item.ForeColor = UiTheme.Muted; break;
                    }
                    _list.Items.Add(item);
                    shown++;
                }

                _counter.Text = freeSampled && total > shown
                    ? L10n.F("ui.truth.sample", shown, total)
                    : L10n.F("ui.truth.count", shown);
            }
            finally
            {
                _list.EndUpdate();
            }
            UiTheme.RefitColumns(_list);
        }

        private void OnDoubleClick(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) return;
            var row = _list.SelectedItems[0].Tag as TruthRow;
            if (row == null) return;

            string path;
            if (!_sourceByCombo.TryGetValue(row.Combo.Normalize(), out path))
            {
                MessageBox.Show(this, L10n.F("ui.truth.noSource", row.Note),
                                L10n.T("ui.truth.dlgTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            OverviewTab.OpenPath(path);
        }
    }
}
