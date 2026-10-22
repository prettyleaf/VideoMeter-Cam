using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    /// <summary>
    /// Расчёт коэффициентов искажений по координатам точек (вместо файлов Mathcad): формулы (1.6)–(1.11),
    /// таблица 1.1 для исходного и выпрямленного изображений, а также калькуляторы формул (1.1)–(1.3).
    /// </summary>
    public sealed class CoefficientsForm : Form
    {
        private sealed class Row
        {
            public string Key, Caption;
            public int Number;
        }

        private readonly MainForm _main;
        private GridTemplate _t;
        private readonly List<Row> _rows = new List<Row>();
        private readonly DataGridView _input, _calc;
        private readonly Label[] _source = new Label[2];
        private bool _filling;

        public CoefficientsForm(MainForm main)
        {
            _main = main;
            _t = Settings.Template.Clone();
            Theme.ApplyForm(this);
            Text = "Коэффициенты координатных искажений";
            Size = new Size(Theme.S(1260), Theme.S(780));
            MinimumSize = new Size(Theme.S(860), Theme.S(560));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(Theme.S(12), Theme.S(4)) };

            // --- вкладка 1: K1…K4 по точкам
            var page1 = new TabPage("K1–K4 по точкам (табл. 1.1)") { BackColor = Color.White };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = Theme.S(5), BackColor = Theme.Tint };
            _input = MakeGrid(false);
            foreach (string c in new[] { "Точка", "№", "X исх.", "Y исх.", "X выпр.", "Y выпр." }) _input.Columns.Add(c, c);
            _input.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            _input.Columns[0].ReadOnly = _input.Columns[1].ReadOnly = true;
            _input.Columns[0].DefaultCellStyle.BackColor = _input.Columns[1].DefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _input.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            _input.CellValueChanged += (s, e) => { if (!_filling) Recalc(); };
            _input.KeyDown += InputKeyDown;
            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var intro = new Label
            {
                Dock = DockStyle.Top, Height = Theme.S(92), Padding = new Padding(Theme.S(10), Theme.S(6), Theme.S(10), 0), BackColor = Theme.TintLight,
                Text = "Введите координаты точек (рис. 1.6, 1.7) или передайте их из окна калибровки (кнопка «K1–K4»). A, B, C, D — углы сетки, " +
                       "E и H — середины верхней и нижней сторон, F и G — середины левой и правой сторон: если число строк чётное, " +
                       "это середины отрезков между двумя точками (1.7). Ячейки можно вставлять из буфера обмена (Ctrl+V)."
            };
            var srcPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(10), Theme.S(4), 0, Theme.S(4)) };
            for (int k = 0; k < 2; k++)
            {
                _source[k] = new Label { AutoSize = true, ForeColor = Theme.Muted };
                srcPanel.Controls.Add(_source[k]);
            }
            var bar1 = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(42), Padding = new Padding(Theme.S(4)), BackColor = Theme.SurfaceAlt, WrapContents = false };
            var clr0 = Theme.MakeButton("Очистить «исходное»");
            clr0.Click += (s, e) => ClearColumn(0);
            var clr1 = Theme.MakeButton("Очистить «выпрямленное»");
            clr1.Click += (s, e) => ClearColumn(1);
            var tpl = Theme.MakeButton("Сетка…");
            tpl.Click += (s, e) => TemplateDialog();
            bar1.Controls.AddRange(new Control[] { clr0, clr1, tpl });
            leftPanel.Controls.Add(_input);
            leftPanel.Controls.Add(srcPanel);
            leftPanel.Controls.Add(intro);
            leftPanel.Controls.Add(bar1);

            _calc = MakeGrid(true);
            foreach (string c in new[] { "Величина", "Формула", "Исходное изображение", "Выпрямленное изображение" }) _calc.Columns.Add(c, c);
            _calc.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var bar2 = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(42), Padding = new Padding(Theme.S(4)), BackColor = Theme.SurfaceAlt, WrapContents = false };
            var copy = Theme.MakeButton("Копировать таблицу 1.1", true);
            copy.Click += (s, e) => Clipboard.SetText(Table11('\t'));
            var copyAll = Theme.MakeButton("Копировать расчёт");
            copyAll.Click += (s, e) => { _calc.SelectAll(); var o = _calc.GetClipboardContent(); if (o != null) Clipboard.SetDataObject(o); _calc.ClearSelection(); };
            var csv = Theme.MakeButton("CSV…");
            csv.Click += (s, e) => Ui.SaveText(this, AllCsv(), "Коэффициенты искажений.csv");
            bar2.Controls.AddRange(new Control[] { copy, copyAll, csv });
            rightPanel.Controls.Add(_calc);
            rightPanel.Controls.Add(Theme.MakeHeader("Расчёт по формулам (1.6)–(1.11) и таблица 1.1"));
            rightPanel.Controls.Add(bar2);
            leftPanel.Controls.Add(Theme.MakeHeader("Координаты точек"));
            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightPanel);
            page1.Controls.Add(split);
            Load += (s, e) => { try { split.SplitterDistance = (int)(split.Width * 0.5); } catch { } };
            tabs.TabPages.Add(page1);

            // --- вкладка 2: формулы (1.1)–(1.3)
            var page2 = new TabPage("Формулы (1.1)–(1.3)") { BackColor = Color.White, AutoScroll = true };
            page2.Controls.Add(BuildFormulas());
            tabs.TabPages.Add(page2);

            Controls.Add(tabs);
            BuildRows();
        }

        private static DataGridView MakeGrid(bool readOnly)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = readOnly,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                GridColor = Theme.TintLight
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            g.DefaultCellStyle.SelectionBackColor = Theme.TintLight;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            return g;
        }

        // ------------------------------------------------------------------ точки

        private void BuildRows()
        {
            var keep = new Dictionary<int, string[]>();
            foreach (DataGridViewRow r in _input.Rows)
            {
                var row = r.Tag as Row;
                if (row == null) continue;
                keep[row.Number] = new[] { Cell(r, 2), Cell(r, 3), Cell(r, 4), Cell(r, 5) };
            }
            _rows.Clear();
            Action<string, string, int> add = (k, c, n) => _rows.Add(new Row { Key = k, Caption = c, Number = n });
            add("A", "A — верхний левый", _t.PointA);
            add("B", "B — верхний правый", _t.PointB);
            add("C", "C — нижний левый", _t.PointC);
            add("D", "D — нижний правый", _t.PointD);
            AddMid(add, "E", "E — середина верха", _t.PointE);
            AddMid(add, "H", "H — середина низа", _t.PointH);
            AddMid(add, "F", "F — середина слева", _t.PointF);
            AddMid(add, "G", "G — середина справа", _t.PointG);
            _filling = true;
            _input.Rows.Clear();
            foreach (var r in _rows)
            {
                string[] v;
                keep.TryGetValue(r.Number, out v);
                int i = _input.Rows.Add(r.Caption, r.Number, v != null ? v[0] : "", v != null ? v[1] : "", v != null ? v[2] : "", v != null ? v[3] : "");
                _input.Rows[i].Tag = r;
            }
            _filling = false;
            Recalc();
        }

        private static void AddMid(Action<string, string, int> add, string key, string caption, int[] nums)
        {
            if (nums.Length == 1) add(key, caption, nums[0]);
            else
            {
                add(key + "′", caption + " (1)", nums[0]);
                add(key + "″", caption + " (2)", nums[1]);
            }
        }

        private static string Cell(DataGridViewRow r, int c) { return Convert.ToString(r.Cells[c].Value); }

        /// <summary>Заполняет столбец (0 — исходное, 1 — выпрямленное изображение) координатами точек из окна калибровки.</summary>
        public void SetColumn(int column, GridTemplate t, IDictionary<int, PointD> pts, string imageName)
        {
            if (t.Cols != _t.Cols || t.Rows != _t.Rows) { _t = t.Clone(); BuildRows(); }
            _filling = true;
            foreach (DataGridViewRow r in _input.Rows)
            {
                var row = (Row)r.Tag;
                PointD p;
                bool ok = pts.TryGetValue(row.Number, out p);
                r.Cells[2 + 2 * column].Value = ok ? Ui.F(p.X, 3) : "";
                r.Cells[3 + 2 * column].Value = ok ? Ui.F(p.Y, 3) : "";
            }
            _filling = false;
            _source[column].Text = (column == 0 ? "Исходное: " : "Выпрямленное: ") + imageName;
            Recalc();
        }

        private void ClearColumn(int column)
        {
            _filling = true;
            foreach (DataGridViewRow r in _input.Rows) { r.Cells[2 + 2 * column].Value = ""; r.Cells[3 + 2 * column].Value = ""; }
            _filling = false;
            _source[column].Text = "";
            Recalc();
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (!(e.Control && e.KeyCode == Keys.V)) return;
            string text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text) || _input.CurrentCell == null) return;
            int r0 = _input.CurrentCell.RowIndex, c0 = Math.Max(2, _input.CurrentCell.ColumnIndex);
            string[] lines = text.Replace("\r", "").Split('\n');
            _filling = true;
            for (int i = 0; i < lines.Length && r0 + i < _input.Rows.Count; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] cells = lines[i].Split('\t', ';');
                for (int j = 0; j < cells.Length && c0 + j < _input.Columns.Count; j++) _input.Rows[r0 + i].Cells[c0 + j].Value = cells[j].Trim();
            }
            _filling = false;
            Recalc();
            e.Handled = true;
        }

        private PointD Get(string key, int column, out bool ok)
        {
            ok = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Key != key) continue;
                double x, y;
                if (NumParse.TryParse(Cell(_input.Rows[i], 2 + 2 * column), out x) && NumParse.TryParse(Cell(_input.Rows[i], 3 + 2 * column), out y))
                {
                    ok = true;
                    return new PointD(x, y);
                }
                return PointD.NaN;
            }
            if (key.Length != 1) return PointD.NaN;
            // середина двух точек (1.7)
            bool a, b;
            PointD p1 = Get(key + "′", column, out a), p2 = Get(key + "″", column, out b);
            ok = a && b;
            return ok ? Coef.Mid(p1, p2) : PointD.NaN;
        }

        private SideCoefficients Compute(int column)
        {
            bool ok = true, o;
            var p = new PointD[8];
            string[] keys = { "A", "B", "C", "D", "E", "F", "G", "H" };
            for (int i = 0; i < 8; i++) { p[i] = Get(keys[i], column, out o); ok &= o; }
            return ok ? SideCoefficients.Compute(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]) : null;
        }

        private void Recalc()
        {
            var r0 = Compute(0);
            var r1 = Compute(1);
            _calc.Rows.Clear();
            Action<string, string, Func<SideCoefficients, string>, bool> add = (name, formula, f, bold) =>
            {
                int i = _calc.Rows.Add(name, formula, r0 != null ? f(r0) : "—", r1 != null ? f(r1) : "—");
                if (bold) _calc.Rows[i].DefaultCellStyle.Font = Theme.BoldFont;
            };
            Func<PointD, string> pt = q => Ui.F(q.X, 2) + "; " + Ui.F(q.Y, 2);
            add("|AB|", "(1.6)", s => Ui.F(s.AB, 3), false);
            add("|AC|", "(1.6)", s => Ui.F(s.AC, 3), false);
            add("|BD|", "(1.6)", s => Ui.F(s.BD, 3), false);
            add("|CD|", "(1.6)", s => Ui.F(s.CD, 3), false);
            add("E (верх)", "", s => pt(s.E), false);
            add("E₁ — середина AB", "(1.7)", s => pt(s.E1), false);
            add("F (лево)", "", s => pt(s.F), false);
            add("F₁ — середина AC", "(1.7)", s => pt(s.F1), false);
            add("G (право)", "", s => pt(s.G), false);
            add("G₁ — середина BD", "(1.7)", s => pt(s.G1), false);
            add("H (низ)", "", s => pt(s.H), false);
            add("H₁ — середина CD", "(1.7)", s => pt(s.H1), false);
            add("Δh₁ = |EE₁|", "", s => Ui.F(s.Dh1, 3), false);
            add("Δh₂ = |F₁F|", "", s => Ui.F(s.Dh2, 3), false);
            add("Δh₃ = |G₁G|", "", s => Ui.F(s.Dh3, 3), false);
            add("Δh₄ = |H₁H|", "", s => Ui.F(s.Dh4, 3), false);
            add("", "", s => "", false);
            add("Таблица 1.1", "", s => "", true);
            add("K₁ = Δh₁ / |AB| · 100%", "(1.8)", s => Ui.F(s.K1, 3) + " % (" + SideCoefficients.BulgeName(s.Bulge1) + ")", true);
            add("K₂ = Δh₂ / |AC| · 100%", "(1.9)", s => Ui.F(s.K2, 3) + " % (" + SideCoefficients.BulgeName(s.Bulge2) + ")", true);
            add("K₃ = Δh₃ / |BD| · 100%", "(1.10)", s => Ui.F(s.K3, 3) + " % (" + SideCoefficients.BulgeName(s.Bulge3) + ")", true);
            add("K₄ = Δh₄ / |CD| · 100%", "(1.11)", s => Ui.F(s.K4, 3) + " % (" + SideCoefficients.BulgeName(s.Bulge4) + ")", true);
            add("", "", s => "", false);
            add("Трапеция верх/низ (L1 = |AB|, L2 = |CD|)", "(1.2)", s => Ui.F(s.TrapezoidHorizontal, 3) + " %", false);
            add("Трапеция лево/право (L1 = |AC|, L2 = |BD|)", "(1.2)", s => Ui.F(s.TrapezoidVertical, 3) + " %", false);
        }

        private string Table11(char sep)
        {
            var r0 = Compute(0);
            var r1 = Compute(1);
            var sb = new StringBuilder();
            sb.AppendLine(sep + "Исходное изображение" + sep + "Выпрямленное изображение");
            Func<SideCoefficients, int, string> v = (s, k) => s == null ? "" : Ui.F(k == 1 ? s.K1 : k == 2 ? s.K2 : k == 3 ? s.K3 : s.K4, 3);
            for (int k = 1; k <= 4; k++) sb.AppendLine("K" + k + sep + v(r0, k) + sep + v(r1, k));
            return sb.ToString();
        }

        private string AllCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Точка;№;X исх.;Y исх.;X выпр.;Y выпр.");
            foreach (DataGridViewRow r in _input.Rows)
            {
                var cells = new string[6];
                for (int i = 0; i < 6; i++) cells[i] = Csv.Esc(Cell(r, i));
                sb.AppendLine(string.Join(";", cells));
            }
            sb.AppendLine();
            sb.AppendLine("Величина;Формула;Исходное;Выпрямленное");
            foreach (DataGridViewRow r in _calc.Rows)
            {
                var cells = new string[4];
                for (int i = 0; i < 4; i++) cells[i] = Csv.Esc(Cell(r, i));
                sb.AppendLine(string.Join(";", cells));
            }
            return sb.ToString();
        }

        private void TemplateDialog()
        {
            var dlg = new ParamDialog("Сетка точек", "Число точек сетки определяет номера угловых и средних точек (для таблицы рис. 1.5 — 13 × 10).");
            var cols = dlg.AddNumber("Точек по горизонтали:", _t.Cols, 0, 3, 60);
            var rows = dlg.AddNumber("Точек по вертикали:", _t.Rows, 0, 3, 60);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _t = new GridTemplate { Name = _t.Name, Cols = cols.IntValue, Rows = rows.IntValue, Step = _t.Step };
            BuildRows();
        }

        // ------------------------------------------------------------------ формулы (1.1)–(1.3)

        private Control BuildFormulas()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(14)) };
            t.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(Theme.S(900), 0), ForeColor = Theme.Muted,
                Text = "Длины в пикселях измеряются на изображении инструментом «Точка» главного окна (выделите в списке точек две точки — расстояние, " +
                       "три точки — отклонение третьей от прямой через первые две) или берутся из таблицы узлов модуля Distortion."
            });
            t.Controls.Add(Calculator("(1.1) Подушкообразные и бочкообразные искажения: Kг = Δh / b · 100%", "Δh (прогиб линии)", "b (длина линии)", (a, b) => Coef.Geometric(a, b), "Kг"));
            t.Controls.Add(Calculator("(1.1) По вертикальной стороне: Kг = Δb / h · 100%", "Δb (прогиб линии)", "h (длина линии)", (a, b) => Coef.Geometric(a, b), "Kг"));
            t.Controls.Add(Calculator("(1.2) Трапецеидальные искажения: Kтг = 2·(L2 − L1)/(L1 + L2) · 100%", "L1", "L2", (a, b) => Coef.Trapezoid(a, b), "Kтг"));
            t.Controls.Add(Calculator("(1.3) Нелинейность по вертикали: Kгв = 2·(hmax − hmin)/(hmax + hmin) · 100%", "hmax", "hmin", (a, b) => Coef.Nonlinearity(a, b), "Kгв"));
            t.Controls.Add(Calculator("(1.3) Нелинейность по горизонтали: Kгг = 2·(bmax − bmin)/(bmax + bmin) · 100%", "bmax", "bmin", (a, b) => Coef.Nonlinearity(a, b), "Kгг"));
            return t;
        }

        private static Control Calculator(string title, string la, string lb, Func<double, double, double> f, string res)
        {
            var box = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, Margin = new Padding(0, Theme.S(12), 0, 0) };
            var cap = new Label { Text = title, AutoSize = true, Font = Theme.BoldFont, ForeColor = Theme.Primary, Margin = new Padding(0, 0, 0, Theme.S(4)) };
            box.Controls.Add(cap);
            box.SetColumnSpan(cap, 6);
            var a = new NumBox { Decimals = 4, Caption = la, Width = Theme.S(90), Text = "" };
            var b = new NumBox { Decimals = 4, Caption = lb, Width = Theme.S(90), Text = "" };
            var o = new Label { AutoSize = true, Font = Theme.HeaderFont, ForeColor = Theme.Text, Margin = new Padding(Theme.S(16), Theme.S(5), 0, 0) };
            box.Controls.Add(new Label { Text = la + " =", AutoSize = true, Margin = new Padding(0, Theme.S(6), Theme.S(4), 0) });
            box.Controls.Add(a);
            box.Controls.Add(new Label { Text = lb + " =", AutoSize = true, Margin = new Padding(Theme.S(14), Theme.S(6), Theme.S(4), 0) });
            box.Controls.Add(b);
            box.Controls.Add(o);
            Action upd = () =>
            {
                double x = a.Value, y = b.Value;
                o.Text = double.IsNaN(x) || double.IsNaN(y) ? res + " = —" : res + " = " + Ui.F(f(x, y), 3) + " %";
            };
            a.TextChanged += (s, e) => upd();
            b.TextChanged += (s, e) => upd();
            upd();
            return box;
        }
    }
}
