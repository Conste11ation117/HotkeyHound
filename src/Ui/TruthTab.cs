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
        private readonly Label _explain;
        private readonly Label _counter;
        private readonly string _allButFree;
        private readonly Dictionary<string, string> _sourceByCombo = new Dictionary<string, string>(StringComparer.Ordinal);

        public TruthTab(MainForm host)
        {
            _host = host;
            _allButFree = L10n.T("ui.truth.filter.all");

            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 70;

            var label = new Label();
            label.Text = L10n.T("ui.truth.category");
            label.Location = new Point(0, 10);
            label.AutoSize = true;

            _filter = new ComboBox();
            _filter.DropDownStyle = ComboBoxStyle.DropDownList;
            _filter.Location = new Point(60, 6);
            _filter.Width = 280;
            _filter.Items.Add(_allButFree);
            foreach (TruthKind k in new TruthKind[]
            {
                TruthKind.MatchesDeclared, TruthKind.DeclaredButNotRegistered,
                TruthKind.RegisteredButUnowned, TruthKind.FreeAndUndeclared
            })
            {
                _filter.Items.Add(TruthTable.KindName(k));
            }
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += delegate (object s, EventArgs e) { Rebuild(); };

            _counter = new Label();
            _counter.Location = new Point(352, 10);
            _counter.AutoSize = true;
            _counter.ForeColor = Color.Gray;

            _explain = new Label();
            _explain.Location = new Point(2, 36);
            _explain.Size = new Size(1000, 30);
            _explain.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _explain.ForeColor = Color.DimGray;
            _explain.Text = L10n.T("ui.truth.explain");

            top.Controls.Add(label);
            top.Controls.Add(_filter);
            top.Controls.Add(_counter);
            top.Controls.Add(_explain);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add(L10n.T("ui.col.combo"), 180);
            _list.Columns.Add(L10n.T("ui.col.type"), 150);
            _list.Columns.Add(L10n.T("ui.col.declaredBy"), 300);
            _list.Columns.Add(L10n.T("ui.col.ownerHint"), 160);
            _list.Columns.Add(L10n.T("ui.col.evidence"), 80);
            _list.Columns.Add(L10n.T("ui.col.note"), 340);
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
                    TruthKind.RegisteredButUnowned, TruthKind.FreeAndUndeclared
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
                        case TruthKind.MatchesDeclared: item.ForeColor = Color.FromArgb(20, 110, 40); break;
                        case TruthKind.RegisteredButUnowned: item.ForeColor = Color.FromArgb(150, 40, 30); break;
                        case TruthKind.DeclaredButNotRegistered: item.ForeColor = Color.FromArgb(160, 100, 10); break;
                        default: item.ForeColor = Color.Gray; break;
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
