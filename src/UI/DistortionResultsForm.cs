using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using VideoMeterCam.Core;
using VideoMeterCam.IO;

namespace VideoMeterCam.UI
{
    /// <summary>Результаты Distortion: рисунки (как в Imatest), сводка коэффициентов и таблица узлов.</summary>
    public sealed class DistortionResultsForm : Form
    {
        private readonly DistortionResult _r;
        private readonly TabControl _tabs;
        private readonly List<KeyValuePair<string, Figure>> _figures = new List<KeyValuePair<string, Figure>>();
        private readonly DataGridView _grid, _summary;

        private static readonly Color MeasuredColor = Color.FromArgb(0xD0, 0x20, 0x20);
        private static readonly Color Ideal = Color.FromArgb(0x20, 0x50, 0xD0);
        private static readonly Color Model3 = Theme.Primary;
        private static readonly Color Model5 = Color.FromArgb(0xE0, 0x6A, 0x1E);

        public DistortionResultsForm(DistortionResult r, ImageData img, ImageData corrected)
        {
            _r = r;
            Theme.ApplyForm(this);
            Text = "Distortion — результаты: " + r.Title;
            Size = new Size(Theme.S(1180), Theme.S(880));
            MinimumSize = new Size(Theme.S(800), Theme.S(560));
            StartPosition = FormStartPosition.CenterScreen;
            Theme.FitToScreen(this);

            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(Theme.S(12), Theme.S(4)) };
            if (r.Settings.PlotDistortion) AddFigure("Рис. 1 — Distortion", BuildFigure(r, img));
            if (r.Settings.PlotRadial) AddFigure("Рис. 2 — Radial plot", BuildRadial(r));
            if (r.Settings.PlotRadiusCorrection) AddFigure("Рис. 3 — Radius correction", BuildRadiusCorrection(r));
            if (corrected != null)
            {
                var tp = new TabPage("Исправленное изображение") { BackColor = Color.White };
                var v = new ImageViewer { Dock = DockStyle.Fill, AllowRoi = false };
                var bmp = ImageIO.ToBitmap(corrected);
                v.SetImage(bmp, false);
                Disposed += (s, e) => bmp.Dispose();
                tp.Controls.Add(v);
                tp.Controls.Add(new Label
                {
                    Dock = DockStyle.Top, Height = Theme.S(26), Padding = new Padding(Theme.S(8), Theme.S(5), 0, 0), BackColor = Theme.TintLight,
                    Text = "Изображение исправлено по модели " + (r.Settings.Order == 5 ? "5-го порядка" : "3-го порядка") + " и открыто в главном окне («" + corrected.Name + "»)."
                });
                _tabs.TabPages.Add(tp);
            }
            _summary = Grid();
            _summary.Columns.Add("k", "Величина");
            _summary.Columns.Add("v", "Значение");
            _summary.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (var kv in SummaryRows(r)) _summary.Rows.Add(kv.Key, kv.Value);
            var ts = new TabPage("Сводка") { BackColor = Color.White };
            ts.Controls.Add(_summary);
            _tabs.TabPages.Add(ts);

            _grid = Grid();
            foreach (string c in Cols) _grid.Columns.Add(c, c);
            FillNodes();
            var tn = new TabPage("Таблица узлов") { BackColor = Color.White };
            tn.Controls.Add(_grid);
            _tabs.TabPages.Add(tn);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(46), Padding = new Padding(Theme.S(6)), BackColor = Theme.SurfaceAlt, WrapContents = false };
            var bSave = Theme.MakeButton("Сохранить рисунок (TIFF)…", true);
            bSave.Click += (s, e) => SaveCurrent();
            var bAll = Theme.MakeButton("Сохранить всё в папку…");
            bAll.Click += (s, e) => SaveAll();
            var bCopy = Theme.MakeButton("Копировать");
            bCopy.Click += (s, e) => CopyCurrent();
            var bCsv = Theme.MakeButton("Таблица CSV…");
            bCsv.Click += (s, e) => Ui.SaveText(this, TableCsv(), PlotView.Safe("Distortion " + r.Title) + ".csv");
            bar.Controls.AddRange(new Control[] { bSave, bAll, bCopy, bCsv });

            Controls.Add(_tabs);
            Controls.Add(bar);
        }

        internal void TourTab(int i) { if (i < _tabs.TabPages.Count) _tabs.SelectedIndex = i; }

        private static DataGridView Grid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            return g;
        }

        private void AddFigure(string title, Figure f)
        {
            _figures.Add(new KeyValuePair<string, Figure>(title, f));
            var tp = new TabPage(title) { BackColor = Color.White };
            var pv = new PlotView { Dock = DockStyle.Fill, ExportName = "Distortion " + title.Replace("Рис. ", "рис") + " " + _r.Title };
            pv.Figure = f;
            tp.Controls.Add(pv);
            _tabs.TabPages.Add(tp);
        }

        private static string OrderName(DistortionResult r) { return r.Settings.Order == 5 ? "5-й порядок (h1, h2)" : "3-й порядок (k1)"; }

        // ------------------------------------------------------------------ рисунок 1

        /// <summary>Снимок с найденными узлами, измеренными линиями и идеальной (неискажённой) решёткой.</summary>
        private static Bitmap Annotated(DistortionResult r, ImageData img)
        {
            Rectangle crop = r.Roi;
            int pad = (int)Math.Round(r.Detection.CellSize * 0.6);
            crop.Inflate(pad, pad);
            crop = Rectangle.Intersect(crop, new Rectangle(0, 0, img.Width, img.Height));
            ImageData part = ImageOps.Crop(img, crop);
            Bitmap src = ImageIO.ToBitmap(part);
            float scale = Math.Max(1f, Math.Min(3f, 900f / Math.Max(part.Width, part.Height)));
            var b = new Bitmap((int)(part.Width * scale), (int)(part.Height * scale));
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, b.Width, b.Height);
                using (var veil = new SolidBrush(Color.FromArgb(110, 255, 255, 255))) g.FillRectangle(veil, 0, 0, b.Width, b.Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Func<PointD, PointF> map = p => new PointF((float)((p.X - crop.X + 0.5) * scale), (float)((p.Y - crop.Y + 0.5) * scale));
                var sel = r.Selected;
                var det = r.Detection;
                // идеальная решётка (без дисторсии): прямые линии проективной сетки
                using (var pen = new Pen(Ideal, Math.Max(1f, scale * 0.8f)) { DashStyle = DashStyle.Dash })
                {
                    for (int j = 0; j < det.Rows; j++) g.DrawLine(pen, map(sel.H.Map(0, j)), map(sel.H.Map(det.Cols - 1, j)));
                    for (int i = 0; i < det.Cols; i++) g.DrawLine(pen, map(sel.H.Map(i, 0)), map(sel.H.Map(i, det.Rows - 1)));
                }
                // измеренные линии
                using (var pen = new Pen(MeasuredColor, Math.Max(1.2f, scale)))
                    foreach (PointD[] line in DistortionForm.LatticePaths(det))
                    {
                        var pts = new List<PointF>();
                        for (int k = 0; k <= line.Length; k++)
                        {
                            bool ok = k < line.Length && line[k].IsValid;
                            if (ok) pts.Add(map(line[k]));
                            if ((!ok || k == line.Length) && pts.Count > 0) { if (pts.Count > 1) g.DrawLines(pen, pts.ToArray()); pts.Clear(); }
                        }
                    }
                if (r.Settings.PlotPoints)
                    using (var br = new SolidBrush(Color.FromArgb(0, 150, 40)))
                    {
                        float d = Math.Max(3f, scale * 2.5f);
                        foreach (var n in det.Nodes) { PointF q = map(n.P); g.FillEllipse(br, q.X - d / 2, q.Y - d / 2, d, d); }
                    }
                // центр дисторсии
                PointF c = map(new PointD(sel.Model.Cx, sel.Model.Cy));
                using (var pen = new Pen(Color.Black, 1.5f))
                {
                    g.DrawLine(pen, c.X - 10, c.Y, c.X + 10, c.Y);
                    g.DrawLine(pen, c.X, c.Y - 10, c.X, c.Y + 10);
                }
            }
            src.Dispose();
            return b;
        }

        public static Figure BuildFigure(DistortionResult r, ImageData img)
        {
            var fig = new Figure { Title = "Distortion: " + r.Title + " — " + OrderName(r), Size = new Size(1300, 860) };
            fig.Add(0, 0, 0.67f, 1, new Figure.ImagePanel
            {
                Image = Annotated(r, img),
                Caption = "Красные линии — измеренные строки и столбцы таблицы, синие штриховые — та же решётка без дисторсии; зелёные точки — узлы; крест — центр дисторсии"
            });
            var tp = new Figure.TextPanel { Title = "Результаты" };
            foreach (var kv in SummaryRows(r)) tp.Rows.Add(kv);
            fig.Add(0.67f, 0, 0.33f, 1, tp);
            return fig;
        }

        private static List<KeyValuePair<string, string>> SummaryRows(DistortionResult r)
        {
            var rows = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, v) => rows.Add(new KeyValuePair<string, string>(k, v));
            var m3 = r.Fit3.Model;
            var m5 = r.Fit5.Model;
            add("Файл", r.FileName);
            add("Кадр", r.ImageWidth + "×" + r.ImageHeight + " пикс., полудиагональ " + Ui.F(m3.Rn, 1));
            add("Таблица", (r.Settings.Pattern == GridPattern.Checkerboard ? "шахматное поле" : "сетка") + ", узлов " + r.Detection.Nodes.Count +
                           " (" + r.Detection.Cols + "×" + r.Detection.Rows + ")" + (r.Rejected > 0 ? ", отброшено " + r.Rejected : ""));
            add("", null);
            add("k1 (3-й порядок, 1.4)", Ui.F(r.K1, 5));
            add("h1, h2 (5-й порядок, 1.5)", Ui.F(r.H1, 5) + ";  " + Ui.F(r.H2, 5));
            add("Вид дисторсии", r.Selected.Model.K1 > 0.0005 ? "бочкообразная (k1 > 0)" : (r.Selected.Model.K1 < -0.0005 ? "подушкообразная (k1 < 0)" : "практически нет"));
            add("SMIA TV distortion", Ui.F(r.SmiaTv, 3) + " %");
            add("Дисторсия в углу кадра (r = 1)", Ui.F(r.CornerDistortion, 3) + " %");
            if (r.Settings.Decentering)
                add("Центр дисторсии", Ui.F(r.Selected.Model.Cx, 1) + "; " + Ui.F(r.Selected.Model.Cy, 1) +
                    " (сдвиг " + Ui.F(r.Selected.Model.Cx - (r.ImageWidth - 1) / 2.0, 1) + "; " + Ui.F(r.Selected.Model.Cy - (r.ImageHeight - 1) / 2.0, 1) + ")");
            add("СКО: без дисторсии / 3-й / 5-й", Ui.F(r.Fit0.Rms, 3) + " / " + Ui.F(r.Fit3.Rms, 3) + " / " + Ui.F(r.Fit5.Rms, 3) + " пикс.");
            add("", null);
            var g = r.Grid;
            if (g != null)
            {
                add("Коэффициенты по крайним линиям (узлы " + (g.I0 + 1) + "…" + (g.I1 + 1) + " × " + (g.J0 + 1) + "…" + (g.J1 + 1) + "):", null);
                var sc = g.Sides;
                add("K1 верхняя сторона (1.8)", Ui.F(sc.K1, 3) + " % (" + SideCoefficients.BulgeName(sc.Bulge1) + ")");
                add("K2 левая сторона (1.9)", Ui.F(sc.K2, 3) + " % (" + SideCoefficients.BulgeName(sc.Bulge2) + ")");
                add("K3 правая сторона (1.10)", Ui.F(sc.K3, 3) + " % (" + SideCoefficients.BulgeName(sc.Bulge3) + ")");
                add("K4 нижняя сторона (1.11)", Ui.F(sc.K4, 3) + " % (" + SideCoefficients.BulgeName(sc.Bulge4) + ")");
                add("Трапеция: верх/низ (1.2)", Ui.F(g.TrapH, 3) + " %  (L1 = " + Ui.F(sc.AB, 1) + ", L2 = " + Ui.F(sc.CD, 1) + ")");
                add("Трапеция: лево/право (1.2)", Ui.F(g.TrapV, 3) + " %  (L1 = " + Ui.F(sc.AC, 1) + ", L2 = " + Ui.F(sc.BD, 1) + ")");
                add("Kгв — по вертикали (1.3)", Ui.F(g.Kv, 3) + " %  (hmax = " + Ui.F(g.CellHMax, 1) + ", hmin = " + Ui.F(g.CellHMin, 1) + ")");
                add("Kгг — по горизонтали (1.3)", Ui.F(g.Kh, 3) + " %  (bmax = " + Ui.F(g.CellBMax, 1) + ", bmin = " + Ui.F(g.CellBMin, 1) + ")");
            }
            else add("Коэффициенты по крайним линиям", "нет полного прямоугольника узлов");
            add("", null);
            add("r_u, r_d — неискажённый и искажённый радиусы, нормированные на полудиагональ кадра.", null);
            add("SMIA TV: 100·(A − B)/B; отрицательна для «бочки». Дисторсия в углу: 100·(r_d − r_u)/r_u.", null);
            add("Дата анализа", r.Time.ToString("dd.MM.yyyy HH:mm"));
            return rows;
        }

        // ------------------------------------------------------------------ рисунки 2 и 3

        private static void Curves(DistortionResult r, Plot p, Func<LensModel, double, double> f, double rmax)
        {
            int n = 100;
            var x = new double[n + 1];
            var y3 = new double[n + 1];
            var y5 = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double rd = rmax * i / n;
                x[i] = rd;
                y3[i] = f(r.Fit3.Model, rd);
                y5[i] = f(r.Fit5.Model, rd);
            }
            var s3 = p.Add("Модель 3-го порядка (k1 = " + Ui.F(r.K1, 4) + ")", x, y3, Model3);
            s3.Width = r.Settings.Order == 3 ? 2.2f : 1.3f;
            var s5 = p.Add("Модель 5-го порядка (h1 = " + Ui.F(r.H1, 4) + ", h2 = " + Ui.F(r.H2, 4) + ")", x, y5, Model5);
            s5.Width = r.Settings.Order == 5 ? 2.2f : 1.3f;
            s5.Dash = DashStyle.Dash;
        }

        private static void Measured(DistortionResult r, Plot p, Func<double, double, double> f, out double rmax)
        {
            int n = r.Detection.Nodes.Count;
            var x = new double[n];
            var y = new double[n];
            rmax = 0;
            for (int k = 0; k < n; k++)
            {
                double rd = r.NodeRd(k), ru = r.NodeRu(k);
                x[k] = rd;
                y[k] = f(rd, ru);
                rmax = Math.Max(rmax, rd);
            }
            var s = p.Add("Узлы таблицы (измерено)", x, y, Color.FromArgb(150, 150, 150), Marker.Circle);
            s.Width = 0;
            rmax = Math.Max(1, rmax);
        }

        public static Figure BuildRadial(DistortionResult r)
        {
            var fig = new Figure { Title = "Distortion: " + r.Title + " — Radial plot", Size = new Size(1200, 800) };
            var p = new Plot { Title = "Локальная геометрическая дисторсия: 100·(r_d − r_u)/r_u", XLabel = "r_d — искажённый радиус (1 — угол кадра)", YLabel = "Дисторсия, %" };
            double rmax;
            Measured(r, p, (rd, ru) => ru > 1e-3 ? 100 * (rd - ru) / ru : double.NaN, out rmax);
            Curves(r, p, (m, rd) => m.LocalDistortionPercent(rd), rmax);
            p.Lines.Add(new RefLine { Value = 0, Color = Theme.Muted });
            p.XMin = 0; p.XMax = rmax;
            p.Notes.Add("SMIA TV distortion = " + Ui.F(r.SmiaTv, 3) + " %");
            p.Notes.Add("Дисторсия в углу кадра = " + Ui.F(r.CornerDistortion, 3) + " %");
            p.Notes.Add("Отрицательная — бочкообразная, положительная — подушкообразная");
            fig.Add(0, 0, 1, 1, new Figure.PlotPanel { Plot = p });
            return fig;
        }

        public static Figure BuildRadiusCorrection(DistortionResult r)
        {
            var fig = new Figure { Title = "Distortion: " + r.Title + " — Radius correction", Size = new Size(1200, 900) };
            var a = new Plot { Title = "Коррекция радиуса: r_u − r_d (формулы 1.4, 1.5)", XLabel = "r_d — искажённый радиус", YLabel = "r_u − r_d" };
            double rmax;
            Measured(r, a, (rd, ru) => ru - rd, out rmax);
            Curves(r, a, (m, rd) => m.Ru(rd) - rd, rmax);
            a.Lines.Add(new RefLine { Value = 0, Color = Theme.Muted });
            a.XMin = 0; a.XMax = rmax;
            fig.Add(0, 0, 1, 0.55f, new Figure.PlotPanel { Plot = a });

            var b = new Plot { Title = "Неискажённый радиус r_u в зависимости от искажённого r_d", XLabel = "r_d", YLabel = "r_u", ShowLegend = true };
            double dummy;
            Measured(r, b, (rd, ru) => ru, out dummy);
            Curves(r, b, (m, rd) => m.Ru(rd), rmax);
            var diag = b.Add("r_u = r_d (без дисторсии)", new[] { 0.0, rmax }, new[] { 0.0, rmax }, Theme.Muted);
            diag.Dash = DashStyle.Dot;
            b.XMin = 0; b.XMax = rmax;
            fig.Add(0, 0.55f, 1, 0.45f, new Figure.PlotPanel { Plot = b });
            return fig;
        }

        // ------------------------------------------------------------------ таблица узлов

        private static readonly string[] Cols = { "№", "Столбец", "Строка", "x, пикс.", "y, пикс.", "x модели", "y модели", "Отклонение, пикс.", "r_d", "r_u", "Дисторсия, %" };

        private void FillNodes()
        {
            var det = _r.Detection;
            var sel = _r.Selected;
            for (int k = 0; k < det.Nodes.Count; k++)
            {
                var n = det.Nodes[k];
                PointD q = k < sel.Predicted.Length ? sel.Predicted[k] : PointD.NaN;
                double rd = _r.NodeRd(k), ru = _r.NodeRu(k);
                _grid.Rows.Add(k + 1, n.I + 1, n.J + 1, Ui.F(n.P.X, 3), Ui.F(n.P.Y, 3), Ui.F(q.X, 3), Ui.F(q.Y, 3),
                    Ui.F(k < sel.Errors.Length ? sel.Errors[k] : double.NaN, 3), Ui.F(rd, 4), Ui.F(ru, 4), Ui.F(ru > 1e-3 ? 100 * (rd - ru) / ru : double.NaN, 3));
            }
        }

        private string TableCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Distortion;" + Csv.Esc(_r.Title));
            foreach (DataGridViewRow row in _summary.Rows)
            {
                string k = Convert.ToString(row.Cells[0].Value), v = Convert.ToString(row.Cells[1].Value);
                if (k.Length == 0 && v.Length == 0) continue;
                sb.AppendLine(Csv.Esc(k) + ";" + Csv.Esc(v));
            }
            sb.AppendLine();
            sb.AppendLine(string.Join(";", Cols));
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var cells = new string[row.Cells.Count];
                for (int i = 0; i < cells.Length; i++) cells[i] = Csv.Esc(Convert.ToString(row.Cells[i].Value));
                sb.AppendLine(string.Join(";", cells));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ сохранение

        private Figure CurrentFigure()
        {
            int i = _tabs.SelectedIndex;
            if (i >= 0 && i < _figures.Count) return _figures[i].Value;
            return _figures.Count > 0 ? _figures[0].Value : null;
        }

        private void SaveCurrent()
        {
            var f = CurrentFigure();
            if (f == null) { Ui.Info(this, "Рисунков нет (отметьте их в настройках Plot)."); return; }
            int i = Math.Max(0, Math.Min(_figures.Count - 1, _tabs.SelectedIndex));
            using (var dlg = new SaveFileDialog { Filter = ImageIO.BitmapSaveFilter, FileName = PlotView.Safe("Distortion " + _r.Title + " рис" + (i + 1)), Title = "Сохранить рисунок" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                Settings.LastDir = Path.GetDirectoryName(path);
                Ui.Busy(this, () => { using (var b = f.Render()) ImageIO.SaveBitmap(b, path); });
            }
        }

        private void SaveAll()
        {
            using (var fb = new FolderBrowserDialog { Description = "Папка для рисунков (TIFF без сжатия) и таблицы (CSV)" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) fb.SelectedPath = Settings.LastDir;
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                Ui.Busy(this, () =>
                {
                    string baseName = PlotView.Safe("Distortion " + _r.Title);
                    for (int i = 0; i < _figures.Count; i++)
                        using (var b = _figures[i].Value.Render())
                            ImageIO.SaveBitmap(b, Path.Combine(fb.SelectedPath, baseName + " рис" + (i + 1) + ".tif"));
                    File.WriteAllText(Path.Combine(fb.SelectedPath, baseName + ".csv"), TableCsv(), new UTF8Encoding(true));
                    Settings.LastDir = fb.SelectedPath;
                });
                Ui.Info(this, "Сохранено в папку:\n" + fb.SelectedPath);
            }
        }

        private void CopyCurrent()
        {
            int i = _tabs.SelectedIndex;
            var page = _tabs.SelectedTab;
            if (page != null && page.Controls.Count > 0 && page.Controls[0] is DataGridView)
            {
                var g = (DataGridView)page.Controls[0];
                g.SelectAll();
                var o = g.GetClipboardContent();
                if (o != null) Clipboard.SetDataObject(o);
                return;
            }
            var f = i >= 0 && i < _figures.Count ? _figures[i].Value : null;
            if (f == null) return;
            using (var b = f.Render()) Clipboard.SetImage(b);
        }
    }
}
