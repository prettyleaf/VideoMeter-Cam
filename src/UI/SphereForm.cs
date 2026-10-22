using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using VideoMeterCam.Core;
using VideoMeterCam.IO;

namespace VideoMeterCam.UI
{
    /// <summary>
    /// Исследование модели сферических искажений (вместо файла Mathcad «Дисторсия»): изменяя R, наблюдать
    /// искажённое (1.12) и восстановленное (1.13) изображения, зависимость ρ1(ρ) и работу интерполяции (рис. 1.9).
    /// </summary>
    public sealed class SphereForm : Form
    {
        private readonly MainForm _main;
        private readonly ComboBox _source, _method;
        private readonly NumBox _r, _cx, _cy;
        private readonly TrackBar _track;
        private readonly CheckBox _fill;
        private readonly Label _info;
        private readonly ImageViewer _vDist, _vRest;
        private readonly Label _capDist, _capRest;
        private readonly PlotView _plot;
        private readonly Timer _debounce;
        private ImageData _img, _dist, _rest;
        private Bitmap _bDist, _bRest;
        private bool _loading;

        public SphereForm(MainForm main)
        {
            _main = main;
            Theme.ApplyForm(this);
            Text = "Модель сферических искажений (1.12), (1.13)";
            Size = new Size(Theme.S(1280), Theme.S(840));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            // --- настройки
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(330), BackColor = Color.White };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(10)) };
            Func<string, Label> cap = s => new Label { Text = s, AutoSize = true, ForeColor = Theme.Primary, Font = Theme.BoldFont, Margin = new Padding(0, Theme.S(10), 0, Theme.S(2)) };
            int w = Theme.S(300);
            t.Controls.Add(cap("Изображение"));
            _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            t.Controls.Add(_source);
            var formula = new Label
            {
                AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(10), 0, 0),
                Text = "Искажение (1.12):  ρ1 = R·sin(arctg(ρ/R)) = R·ρ / √(R² + ρ²)\nКоррекция (1.13):  ρ = R·tg(arcsin(ρ1/R)) = R·ρ1 / √(R² − ρ1²)\n" +
                       "ρ — расстояние от центра, R — радиус сферы (пикс.). При R ≫ ρ: ρ1 ≈ ρ; при R ≪ ρ: ρ1 ≈ R; при ρ = R: ρ1 = R/√2 ≈ 0,707·R."
            };
            t.Controls.Add(formula);
            t.Controls.Add(cap("Радиус сферы R"));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            _r = new NumBox { Decimals = 1, Min = 1, Max = 1e6, Caption = "R", Width = Theme.S(80) };
            row.Controls.Add(_r);
            row.Controls.Add(new Label { Text = "пикс.", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(Theme.S(4), Theme.S(5), 0, 0) });
            t.Controls.Add(row);
            _track = new TrackBar { Minimum = 0, Maximum = 400, TickFrequency = 50, Width = w, SmallChange = 2, LargeChange = 20 };
            t.Controls.Add(_track);
            t.Controls.Add(new Label { Text = "R от 0,1 до 10 полудиагоналей кадра (логарифмическая шкала)", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.SmallFont });
            t.Controls.Add(cap("Центр искажений"));
            var crow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            _cx = new NumBox { Decimals = 1, Caption = "Центр X", Width = Theme.S(70) };
            _cy = new NumBox { Decimals = 1, Caption = "Центр Y", Width = Theme.S(70) };
            crow.Controls.Add(new Label { Text = "X:", AutoSize = true, Margin = new Padding(0, Theme.S(5), Theme.S(2), 0) });
            crow.Controls.Add(_cx);
            crow.Controls.Add(new Label { Text = "Y:", AutoSize = true, Margin = new Padding(Theme.S(8), Theme.S(5), Theme.S(2), 0) });
            crow.Controls.Add(_cy);
            t.Controls.Add(crow);
            t.Controls.Add(cap("Пересчёт координат"));
            _method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _method.Items.AddRange(new object[] { "Прямой — перенос пикселей (как в Mathcad)", "Обратный — с билинейной интерполяцией" });
            _method.SelectedIndex = 0;
            t.Controls.Add(_method);
            _fill = new CheckBox { Text = "Заполнить пропуски в восстановленном изображении (адаптивная линейная интерполяция, рис. 1.9)", AutoSize = true, MaximumSize = new Size(w, 0), Margin = new Padding(0, Theme.S(8), 0, 0) };
            t.Controls.Add(_fill);
            _info = new Label { AutoSize = true, MaximumSize = new Size(w, 0), ForeColor = Theme.Primary, Font = Theme.BoldFont, Margin = new Padding(0, Theme.S(12), 0, 0) };
            t.Controls.Add(_info);
            var btns = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(w + Theme.S(10), 0), Margin = new Padding(0, Theme.S(12), 0, 0) };
            var bd = Theme.MakeButton("Искажённое → в главное окно");
            bd.Click += (s, e) => ToMain(_dist, "сфера R=" + Ui.G(_r.Value));
            var br = Theme.MakeButton("Восстановленное → в главное окно");
            br.Click += (s, e) => ToMain(_rest, "восстановлено R=" + Ui.G(_r.Value) + (_fill.Checked ? " + интерполяция" : ""));
            btns.Controls.Add(bd);
            btns.Controls.Add(br);
            t.Controls.Add(btns);
            left.Controls.Add(t);
            left.Controls.Add(Theme.MakeHeader("Параметры модели"));

            // --- изображения и график
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = Theme.S(5), BackColor = Theme.Tint, FixedPanel = FixedPanel.Panel2 };
            var pair = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = Theme.S(5), BackColor = Theme.Tint };
            _vDist = new ImageViewer { Dock = DockStyle.Fill, AllowRoi = false, EmptyText = "Выберите изображение" };
            _vRest = new ImageViewer { Dock = DockStyle.Fill, AllowRoi = false, EmptyText = "" };
            _capDist = Theme.MakeHeader("Искажённое изображение (1.12)");
            _capRest = Theme.MakeHeader("Восстановленное изображение (1.13)");
            pair.Panel1.Controls.Add(_vDist);
            pair.Panel1.Controls.Add(_capDist);
            pair.Panel2.Controls.Add(_vRest);
            pair.Panel2.Controls.Add(_capRest);
            _plot = new PlotView { Dock = DockStyle.Fill, ExportName = "Зависимость rho1(rho)" };
            split.Panel1.Controls.Add(pair);
            split.Panel2.Controls.Add(_plot);
            Controls.Add(split);
            Controls.Add(left);
            Load += (s, e) =>
            {
                try
                {
                    split.SplitterDistance = Math.Max(Theme.S(250), split.Height - Theme.S(250));
                    pair.SplitterDistance = pair.Width / 2;
                }
                catch { }
            };

            _debounce = new Timer { Interval = 250 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Recompute(); };
            _r.TextChanged += (s, e) => { if (_loading) return; SyncTrack(); Touch(); };
            _track.Scroll += (s, e) => { if (_loading) return; _loading = true; _r.Value = Math.Round(TrackToR(_track.Value)); _loading = false; Touch(); };
            _cx.TextChanged += (s, e) => { if (!_loading) Touch(); };
            _cy.TextChanged += (s, e) => { if (!_loading) Touch(); };
            _method.SelectedIndexChanged += (s, e) => Touch();
            _fill.CheckedChanged += (s, e) => Touch();
            _source.SelectedIndexChanged += (s, e) => { if (!_loading) SourceChanged(); };
            _main.Workspace.DocsChanged += DocsChanged;
            FormClosed += (s, e) =>
            {
                _main.Workspace.DocsChanged -= DocsChanged;
                _debounce.Dispose();
                if (_bDist != null) _bDist.Dispose();
                if (_bRest != null) _bRest.Dispose();
            };
            FillSources();
        }

        private void DocsChanged(object s, EventArgs e) { if (!IsDisposed) FillSources(); }

        private double Rho { get { return _img == null ? 1 : LensModel.HalfDiagonal(_img.Width, _img.Height); } }

        private double TrackToR(int v) { return Rho * Math.Pow(10, (v - 200) / 200.0); }

        private void SyncTrack()
        {
            double r = _r.Value;
            if (double.IsNaN(r) || r <= 0 || _img == null) return;
            int v = (int)Math.Round(200 + 200 * Math.Log10(r / Rho));
            _loading = true;
            _track.Value = Math.Max(_track.Minimum, Math.Min(_track.Maximum, v));
            _loading = false;
        }

        private void Touch()
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void FillSources()
        {
            string cur = _source.SelectedItem as string;
            _loading = true;
            _source.Items.Clear();
            foreach (var d in _main.Workspace.Docs) _source.Items.Add(d.Name);
            _loading = false;
            if (_source.Items.Count == 0) { _img = null; Recompute(); return; }
            int i = cur != null ? _source.Items.IndexOf(cur) : -1;
            if (i < 0 && _main.Workspace.Active != null) i = _main.Workspace.Docs.IndexOf(_main.Workspace.Active);
            _source.SelectedIndex = Math.Max(0, i);
        }

        private void SourceChanged()
        {
            var d = _main.Workspace.Find(_source.SelectedItem as string);
            if (d == null) return;
            bool first = _img == null;
            bool sizeChanged = first || _img.Width != d.Data.Width || _img.Height != d.Data.Height;
            _img = d.Data;
            if (sizeChanged)
            {
                _loading = true;
                _cx.Value = (_img.Width - 1) / 2.0;
                _cy.Value = (_img.Height - 1) / 2.0;
                if (first || double.IsNaN(_r.Value)) _r.Value = Math.Round(Rho);
                _loading = false;
                SyncTrack();
            }
            Recompute();
        }

        internal void TourSetR(double r) { _r.Value = r; _fill.Checked = true; Recompute(); }

        private void Recompute()
        {
            if (_img == null || double.IsNaN(_r.Value) || _r.Value <= 0 || double.IsNaN(_cx.Value) || double.IsNaN(_cy.Value))
            {
                _vDist.SetImage(null, false);
                _vRest.SetImage(null, false);
                _info.Text = "";
                return;
            }
            double R = _r.Value, cx = _cx.Value, cy = _cy.Value;
            var m = _method.SelectedIndex == 0 ? MapMethod.Forward : MapMethod.Inverse;
            int gaps = 0, gapsAfter = 0;
            Ui.Busy(this, () =>
            {
                _dist = Warp.SphereDistort(_img, R, cx, cy, m);
                _rest = Warp.SphereCorrect(_dist, R, cx, cy, m);
                gaps = Warp.CountGaps(_rest);
                if (_fill.Checked) { _rest = Warp.FillGaps(_rest); gapsAfter = Warp.CountGaps(_rest); }
            });
            if (_dist == null) return;
            _dist.Name = Path.GetFileNameWithoutExtension(_img.Name);
            _rest.Name = _dist.Name;
            bool keep = _bDist != null && _bDist.Width == _dist.Width && _bDist.Height == _dist.Height;
            Bitmap oldD = _bDist, oldR = _bRest;
            _bDist = ImageIO.ToBitmap(_dist);
            _bRest = ImageIO.ToBitmap(_rest);
            _vDist.SetImage(_bDist, keep);
            _vRest.SetImage(_bRest, keep);
            if (oldD != null) oldD.Dispose();
            if (oldR != null) oldR.Dispose();
            double rho = Rho, r1 = Warp.SphereRho1(rho, R);
            _info.Text = "Полудиагональ кадра ρmax = " + Ui.F(rho, 1) + " пикс.\n" +
                         "Угол кадра: ρ1 = " + Ui.F(r1, 1) + " пикс. (сжатие в " + Ui.F(rho / r1, 2) + " раза)\n" +
                         "R / ρmax = " + Ui.F(R / rho, 3) + "\n" +
                         "Незаполненных элементов в восстановленном: " + gaps.ToString("N0") + (_fill.Checked ? " → после интерполяции " + gapsAfter.ToString("N0") : "");
            _capRest.Text = "Восстановленное изображение (1.13)" + (m == MapMethod.Forward ? ", прямой пересчёт" : ", обратный пересчёт") + (_fill.Checked ? " + интерполяция (рис. 1.9)" : "");
            DrawPlot(R, rho);
        }

        private void DrawPlot(double R, double rhoMax)
        {
            var p = new Plot { Title = "Зависимость ρ1(ρ) — рис. 1.8", XLabel = "ρ, пикс.", YLabel = "ρ1, пикс." };
            int n = 200;
            double xmax = Math.Max(rhoMax, 2.5 * R);
            var x = new double[n + 1];
            var y = new double[n + 1];
            for (int i = 0; i <= n; i++) { x[i] = xmax * i / n; y[i] = Warp.SphereRho1(x[i], R); }
            p.Add("ρ1 = R·ρ/√(R²+ρ²), R = " + Ui.F(R, 0), x, y, Theme.Primary).Width = 2.2f;
            var id = p.Add("ρ1 = ρ (без искажений)", new[] { 0.0, xmax }, new[] { 0.0, xmax }, Theme.Muted);
            id.Dash = DashStyle.Dot;
            p.Lines.Add(new RefLine { Value = R, Color = Theme.AccentDark, Label = "ρ1 → R при ρ ≫ R;  при ρ = R: ρ1 = R/√2 = " + Ui.F(R / Math.Sqrt(2), 1) });
            p.Lines.Add(new RefLine { Value = rhoMax, Horizontal = false, Color = Color.FromArgb(0xE0, 0x6A, 0x1E), Label = "угол кадра" });
            p.YMin = 0;
            p.YMax = Math.Max(R * 1.1, Math.Min(xmax, rhoMax * 1.1));
            p.XMin = 0;
            p.XMax = xmax;
            p.LegendCorner = Corner.BottomRight;
            _plot.Plot = p;
        }

        private void ToMain(ImageData img, string suffix)
        {
            if (img == null) return;
            var c = img.Clone();
            c.Name = Path.GetFileNameWithoutExtension(_img.Name) + " — " + suffix;
            c.FilePath = null;
            _main.AddImage(c).Modified = true;
        }
    }
}
