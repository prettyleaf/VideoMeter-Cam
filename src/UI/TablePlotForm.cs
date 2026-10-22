using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    /// <summary>Построение графика по таблице значений (например, коэффициенты искажений в зависимости от угла поворота таблицы).</summary>
    public sealed class TablePlotForm : Form
    {
        private readonly DataGridView _grid;
        private readonly TextBox _title, _xl, _yl;
        private readonly PlotView _plot;
        private bool _filling;

        public TablePlotForm()
        {
            Theme.ApplyForm(this);
            Text = "График по таблице";
            Size = new Size(Theme.S(1180), Theme.S(720));
            MinimumSize = new Size(Theme.S(820), Theme.S(500));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(470), BackColor = Color.White };
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(Theme.S(8)) };
            Func<string, TextBox> field = cap =>
            {
                fields.Controls.Add(new Label { Text = cap, AutoSize = true, Margin = new Padding(0, Theme.S(6), Theme.S(6), 0) });
                var tb = new TextBox { Width = Theme.S(330) };
                tb.TextChanged += (s, e) => Redraw();
                fields.Controls.Add(tb);
                return tb;
            };
            _title = field("Заголовок:");
            _xl = field("Ось X:");
            _yl = field("Ось Y:");
            _title.Text = "Зависимость коэффициента искажений от угла поворота таблицы";
            _xl.Text = "α, °";
            _yl.Text = "Коэффициент, %";
            var hint = new Label
            {
                Dock = DockStyle.Top, Height = Theme.S(52), Padding = new Padding(Theme.S(8), Theme.S(4), Theme.S(8), 0), ForeColor = Theme.Muted,
                Text = "Первый столбец — значения по оси X, остальные — ряды графика. Двойной щелчок по заголовку столбца — переименовать ряд. " +
                       "Таблицу можно вставить из Excel или из таблицы результатов (Ctrl+V)."
            };
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, GridColor = Theme.TintLight
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            _grid.Columns.Add("x", "X");
            _grid.Columns.Add("s1", "Ряд 1");
            _grid.Columns.Add("s2", "Ряд 2");
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.CellValueChanged += (s, e) => { if (!_filling) Redraw(); };
            _grid.RowsRemoved += (s, e) => { if (!_filling) Redraw(); };
            _grid.ColumnHeaderMouseDoubleClick += (s, e) => RenameColumn(e.ColumnIndex);
            _grid.KeyDown += GridKeyDown;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(76), Padding = new Padding(Theme.S(4)), BackColor = Theme.SurfaceAlt, WrapContents = true };
            Func<string, Action, Button> b = (text, a) =>
            {
                var btn = Theme.MakeButton(text);
                btn.MinimumSize = new Size(Theme.S(70), Theme.S(26));
                btn.Click += (s, e) => a();
                bar.Controls.Add(btn);
                return btn;
            };
            b("+ ряд", AddSeries);
            b("− ряд", RemoveSeries);
            b("Вставить", Paste);
            b("Очистить", () => { _grid.Rows.Clear(); Redraw(); });
            b("Сохранить график…", () => _plot.SaveImageDialog());
            b("Копировать график", () => _plot.CopyImage());
            b("CSV…", () => Ui.SaveText(this, TableText(';'), "График.csv"));
            left.Controls.Add(_grid);
            left.Controls.Add(hint);
            left.Controls.Add(fields);
            left.Controls.Add(bar);
            left.Controls.Add(Theme.MakeHeader("Таблица значений"));

            _plot = new PlotView { Dock = DockStyle.Fill, ExportName = "График", EmptyText = "Введите значения в таблицу слева" };
            Controls.Add(_plot);
            Controls.Add(left);
            Redraw();
        }

        private void AddSeries()
        {
            if (_grid.Columns.Count >= 9) return;
            int n = _grid.Columns.Count;
            int i = _grid.Columns.Add("s" + n, "Ряд " + n);
            _grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            Redraw();
        }

        private void RemoveSeries()
        {
            if (_grid.Columns.Count <= 2) return;
            _grid.Columns.RemoveAt(_grid.Columns.Count - 1);
            Redraw();
        }

        private void RenameColumn(int c)
        {
            if (c < 0) return;
            var dlg = new ParamDialog("Название столбца");
            var t = dlg.AddText("Название:", _grid.Columns[c].HeaderText, 1, 240);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _grid.Columns[c].HeaderText = t.Text.Trim();
            Redraw();
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V) { Paste(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && _grid.SelectedCells.Count > 0)
            {
                _filling = true;
                foreach (DataGridViewCell c in _grid.SelectedCells) c.Value = null;
                _filling = false;
                Redraw();
                e.Handled = true;
            }
        }

        private void Paste()
        {
            string text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;
            var rows = new List<string[]>();
            foreach (string line in text.Replace("\r", "").Split('\n'))
                if (line.Trim().Length > 0) rows.Add(line.Split('\t', ';'));
            if (rows.Count == 0) return;
            int r0 = _grid.CurrentCell != null ? _grid.CurrentCell.RowIndex : 0, c0 = _grid.CurrentCell != null ? _grid.CurrentCell.ColumnIndex : 0;
            _filling = true;
            // первая строка без чисел — заголовки столбцов
            double dummy;
            string[] first = rows[0];
            if (!NumParse.TryParse(first[first.Length > 1 ? 1 : 0], out dummy))
            {
                while (_grid.Columns.Count < Math.Min(9, c0 + first.Length)) AddSeries();
                for (int j = 0; j < first.Length && c0 + j < _grid.Columns.Count; j++)
                    if (first[j].Trim().Length > 0) _grid.Columns[c0 + j].HeaderText = first[j].Trim();
                rows.RemoveAt(0);
            }
            for (int k = 0; k < rows.Count; k++)
            {
                string[] cells = rows[k];
                while (_grid.Columns.Count < Math.Min(9, c0 + cells.Length)) AddSeries();
                int r = r0 + k;
                while (_grid.Rows.Count - 1 <= r) _grid.Rows.Add();
                for (int j = 0; j < cells.Length && c0 + j < _grid.Columns.Count; j++) _grid.Rows[r].Cells[c0 + j].Value = cells[j].Trim();
            }
            _filling = false;
            Redraw();
        }

        internal void TourFill(string[] headers, double[][] rows)
        {
            _filling = true;
            while (_grid.Columns.Count < headers.Length) AddSeries();
            for (int i = 0; i < headers.Length; i++) _grid.Columns[i].HeaderText = headers[i];
            _grid.Rows.Clear();
            foreach (var r in rows)
            {
                int i = _grid.Rows.Add();
                for (int j = 0; j < r.Length && j < _grid.Columns.Count; j++) _grid.Rows[i].Cells[j].Value = Ui.F(r[j], 3);
            }
            _filling = false;
            Redraw();
        }

        private void Redraw()
        {
            if (_plot == null) return;
            var p = new Plot { Title = _title.Text, XLabel = _xl.Text, YLabel = _yl.Text };
            Marker[] markers = { Marker.Circle, Marker.Square, Marker.Diamond, Marker.Triangle };
            int count = 0;
            for (int c = 1; c < _grid.Columns.Count; c++)
            {
                var xs = new List<double>();
                var ys = new List<double>();
                foreach (DataGridViewRow r in _grid.Rows)
                {
                    if (r.IsNewRow) continue;
                    double x, y;
                    if (!NumParse.TryParse(Convert.ToString(r.Cells[0].Value), out x)) continue;
                    if (!NumParse.TryParse(Convert.ToString(r.Cells[c].Value), out y)) continue;
                    xs.Add(x);
                    ys.Add(y);
                }
                if (xs.Count == 0) continue;
                // по возрастанию X
                var idx = new List<int>();
                for (int i = 0; i < xs.Count; i++) idx.Add(i);
                idx.Sort((a, b) => xs[a].CompareTo(xs[b]));
                var X = new double[xs.Count];
                var Y = new double[xs.Count];
                for (int i = 0; i < idx.Count; i++) { X[i] = xs[idx[i]]; Y[i] = ys[idx[i]]; }
                p.Add(_grid.Columns[c].HeaderText, X, Y, Plot.Palette[(c - 1) % Plot.Palette.Length], markers[(c - 1) % markers.Length]).Width = 1.8f;
                count++;
            }
            _plot.Plot = count > 0 ? p : null;
            _plot.Invalidate();
        }

        private string TableText(char sep)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < _grid.Columns.Count; c++) sb.Append(c > 0 ? sep.ToString() : "").Append(Csv.Esc(_grid.Columns[c].HeaderText));
            sb.AppendLine();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.IsNewRow) continue;
                for (int c = 0; c < _grid.Columns.Count; c++) sb.Append(c > 0 ? sep.ToString() : "").Append(Csv.Esc(Convert.ToString(r.Cells[c].Value)));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
