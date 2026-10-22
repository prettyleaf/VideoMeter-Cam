using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SigmaLabCam.Core;
using SigmaLabCam.IO;

namespace SigmaLabCam.UI
{
    /// <summary>Модуль Distortion: выбор изображения и рабочей зоны, настройки, поиск узлов, запуск анализа.</summary>
    public sealed class DistortionForm : Form
    {
        private static Rectangle _lastRoi;
        private static Size _lastSize;
        private static DistortionSettings _lastSettings;

        private readonly MainForm _main;
        private readonly ImageViewer _viewer;
        private ImageData _img;
        private Bitmap _bmp;
        private readonly Label _status, _imgLabel;
        private readonly Timer _debounce;
        private GridDetection _det;
        private bool _detDirty = true;

        private TextBox _title;
        private ComboBox _pattern, _polarity, _order, _corrected;
        private CheckBox _decenter, _pFig, _pPoints, _pRadial, _pRadius;
        private NumBox _sigma;
        private bool _loading;
        private bool _titleAuto = true, _settingTitle;

        public DistortionForm(MainForm main)
        {
            _main = main;
            Theme.ApplyForm(this);
            Text = "Distortion — измерение координатных искажений по тестовой таблице";
            Size = new Size(Theme.S(1240), Theme.S(800));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);

            var left = BuildSettings();
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            _viewer = new ImageViewer { Dock = DockStyle.Fill, EmptyText = "Откройте снимок тестовой таблицы «шахматное поле» или «сетка»\n(кнопки внизу) и выделите мышью рабочую зону" };
            _viewer.RoiChanged += (s, e) => Redetect();
            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = Theme.S(46),
                Padding = new Padding(Theme.S(10), Theme.S(4), Theme.S(10), 0),
                BackColor = Theme.TintLight,
                ForeColor = Theme.Text,
                Text = "1) Откройте снимок таблицы.  2) Выделите мышью рабочую зону — область таблицы с узлами (без выделения — весь кадр).  " +
                       "3) Выберите тип таблицы (Pattern) и проверьте, что найденные узлы (зелёные кресты) лежат на углах клеток или пересечениях линий.  4) Нажмите «Анализ»."
            };
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(46), BackColor = Theme.SurfaceAlt, Padding = new Padding(Theme.S(6)) };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, WrapContents = false, Margin = Padding.Empty };
            var bOpen = Theme.MakeButton("Открыть файл…");
            bOpen.Image = Icons.Get("open");
            bOpen.TextImageRelation = TextImageRelation.ImageBeforeText;
            bOpen.Click += (s, e) => OpenFile();
            var bActive = Theme.MakeButton("Взять активное изображение");
            bActive.Click += (s, e) => TakeActive(true);
            var bAll = Theme.MakeButton("Весь кадр");
            bAll.Click += (s, e) => { if (_img != null) _viewer.Roi = Rectangle.Empty; Redetect(); };
            var bRun = Theme.MakeButton("Анализ  (Yes, Continue)", true);
            bRun.Click += (s, e) => Run();
            _imgLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(Theme.S(8), Theme.S(9), 0, 0) };
            bottom.Controls.AddRange(new Control[] { bOpen, bActive, bAll, _imgLabel });
            bRun.Dock = DockStyle.Right;
            bRun.AutoSize = false;
            bRun.Width = Theme.S(210);
            bottomPanel.Controls.Add(bottom);
            bottomPanel.Controls.Add(bRun);
            _status = new Label { Dock = DockStyle.Bottom, Height = Theme.S(24), Padding = new Padding(Theme.S(10), Theme.S(4), 0, 0), ForeColor = Theme.Primary, Font = Theme.BoldFont, BackColor = Color.White };

            center.Controls.Add(_viewer);
            center.Controls.Add(hint);
            center.Controls.Add(_status);
            center.Controls.Add(bottomPanel);
            Controls.Add(center);
            Controls.Add(left);
            AcceptButton = bRun;

            _debounce = new Timer { Interval = 350 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); Detect(); };
            ApplySettings(_lastSettings ?? new DistortionSettings());
            TakeActive(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _debounce.Dispose(); if (_bmp != null) _bmp.Dispose(); }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ настройки

        private Control BuildSettings()
        {
            var outer = new Panel { Dock = DockStyle.Left, Width = Theme.S(380), BackColor = Color.White };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(10)), Dock = DockStyle.Top };
            Func<string, Label> cap = s => new Label { Text = s, AutoSize = true, ForeColor = Theme.Primary, Font = Theme.BoldFont, Margin = new Padding(0, Theme.S(10), 0, Theme.S(2)) };
            Func<string, Label> sub = s => new Label { Text = s, AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(Theme.S(340), 0), Margin = new Padding(0, Theme.S(3), 0, Theme.S(1)) };
            int w = Theme.S(340);

            t.Controls.Add(cap("Title"));
            t.Controls.Add(sub("Название (по умолчанию — имя файла)"));
            _title = new TextBox { Width = w };
            _title.TextChanged += (s, e) => { if (!_settingTitle) _titleAuto = false; };
            t.Controls.Add(_title);

            t.Controls.Add(cap("Pattern — тип тестовой таблицы"));
            _pattern = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _pattern.Items.AddRange(new object[] { "Squares (checkerboard) — шахматное поле", "Grid — сетка линий (или точек)" });
            _pattern.SelectedIndexChanged += (s, e) => { _polarity.Enabled = _pattern.SelectedIndex == 1; Changed(); };
            t.Controls.Add(_pattern);
            t.Controls.Add(sub("Линии сетки:"));
            _polarity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _polarity.Items.AddRange(new object[] { "Определить автоматически", "Тёмные линии на светлом фоне", "Светлые линии на тёмном фоне" });
            _polarity.SelectedIndexChanged += (s, e) => Changed();
            t.Controls.Add(_polarity);

            t.Controls.Add(cap("Calculate / display using — модель дисторсии"));
            _order = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _order.Items.AddRange(new object[] { "3 — 3-й порядок: r_u = r_d + k1·r_d³ (1.4)", "5 — 5-й порядок: r_u = r_d + h1·r³ + h2·r⁵ (1.5)" });
            t.Controls.Add(_order);
            t.Controls.Add(sub("Рассчитываются обе модели; выбранная используется для рисунков и исправленного изображения. Чем выше порядок, тем точнее расчёт."));

            t.Controls.Add(cap("Decentering calculation"));
            _decenter = new CheckBox { Text = "Искать центр дисторсии (децентрировка)", AutoSize = true };
            t.Controls.Add(_decenter);
            t.Controls.Add(sub("Без флажка искажения считаются симметричными относительно геометрического центра кадра. Флажок рекомендуется для таблицы «сетка»."));

            t.Controls.Add(cap("Plot — какие рисунки построить"));
            _pFig = new CheckBox { Text = "Plot distortion figure — рисунок искажений", AutoSize = true, Checked = true };
            _pPoints = new CheckBox { Text = "Intersection points — узлы на рисунке", AutoSize = true, Checked = true };
            _pRadial = new CheckBox { Text = "Radial plot — дисторсия от радиуса", AutoSize = true, Checked = true };
            _pRadius = new CheckBox { Text = "Plot radius correction — коррекция радиуса", AutoSize = true, Checked = true };
            t.Controls.Add(_pFig);
            t.Controls.Add(_pPoints);
            t.Controls.Add(_pRadial);
            t.Controls.Add(_pRadius);

            t.Controls.Add(cap("Display corrected image — исправленное изображение"));
            _corrected = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = w };
            _corrected.Items.AddRange(new object[] { "always — всегда", "never — не строить" });
            t.Controls.Add(_corrected);

            t.Controls.Add(cap("Поиск узлов"));
            var g = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            g.Controls.Add(new Label { Text = "Масштаб σ, пикс. (0 — автоматически):", AutoSize = true, Margin = new Padding(0, Theme.S(6), Theme.S(6), 0) });
            _sigma = new NumBox { Decimals = 1, Min = 0, Max = 50, Caption = "Масштаб σ", Width = Theme.S(60) };
            _sigma.Value = 0;
            _sigma.TextChanged += (s, e) => Changed();
            g.Controls.Add(_sigma);
            t.Controls.Add(g);
            t.Controls.Add(sub("Радиусы нормированы на полудиагональ кадра (r = 1 в углу кадра), как в Imatest."));

            scroll.Controls.Add(t);
            outer.Controls.Add(scroll);
            outer.Controls.Add(Theme.MakeHeader("Distortion settings — настройки"));
            return outer;
        }

        private void ApplySettings(DistortionSettings s)
        {
            _loading = true;
            _pattern.SelectedIndex = s.Pattern == GridPattern.Lines ? 1 : 0;
            _polarity.SelectedIndex = s.Polarity > 0 ? 1 : (s.Polarity < 0 ? 2 : 0);
            _polarity.Enabled = s.Pattern == GridPattern.Lines;
            _order.SelectedIndex = s.Order == 5 ? 1 : 0;
            _decenter.Checked = s.Decentering;
            _pFig.Checked = s.PlotDistortion;
            _pPoints.Checked = s.PlotPoints;
            _pRadial.Checked = s.PlotRadial;
            _pRadius.Checked = s.PlotRadiusCorrection;
            _corrected.SelectedIndex = s.CorrectedImage == CorrectedImageMode.Never ? 1 : 0;
            _sigma.Value = s.Sigma;
            _loading = false;
        }

        private DistortionSettings ReadSettings()
        {
            return new DistortionSettings
            {
                Title = _title.Text.Trim(),
                Pattern = _pattern.SelectedIndex == 1 ? GridPattern.Lines : GridPattern.Checkerboard,
                Polarity = _polarity.SelectedIndex == 1 ? 1 : (_polarity.SelectedIndex == 2 ? -1 : 0),
                Order = _order.SelectedIndex == 1 ? 5 : 3,
                Decentering = _decenter.Checked,
                PlotDistortion = _pFig.Checked,
                PlotPoints = _pPoints.Checked,
                PlotRadial = _pRadial.Checked,
                PlotRadiusCorrection = _pRadius.Checked,
                CorrectedImage = _corrected.SelectedIndex == 1 ? CorrectedImageMode.Never : CorrectedImageMode.Always,
                Sigma = double.IsNaN(_sigma.Value) ? 0 : Math.Max(0, _sigma.Value)
            };
        }

        private void Changed()
        {
            if (_loading) return;
            Redetect();
        }

        /// <summary>Повторный поиск узлов с задержкой (после изменения зоны или настроек).</summary>
        private void Redetect()
        {
            _detDirty = true;
            _debounce.Stop();
            _debounce.Start();
        }

        // ------------------------------------------------------------------ изображение и узлы

        private void SetImage(ImageData img)
        {
            _img = img;
            if (_bmp != null) _bmp.Dispose();
            _bmp = ImageIO.ToBitmap(img);
            _viewer.SetImage(_bmp, false);
            _imgLabel.Text = img.Name + " — " + img.Description;
            if (string.IsNullOrEmpty(_title.Text) || _titleAuto)
            {
                _settingTitle = true;
                _title.Text = img.Name;
                _settingTitle = false;
                _titleAuto = true;
            }
            Rectangle roi = Rectangle.Empty;
            if (!_lastRoi.IsEmpty && _lastSize == new Size(img.Width, img.Height))
            {
                if (Ui.Ask(this, "Использовать ту же рабочую зону (ROI), что и для предыдущего изображения?\n(Do you want the same ROI as the previous image?)"))
                    roi = _lastRoi;
            }
            _viewer.Roi = roi;
            Redetect();
        }

        private void TakeActive(bool explicitClick)
        {
            var d = _main.Workspace.Active;
            if (d == null)
            {
                if (explicitClick) Ui.Info(this, "В главном окне нет активного изображения.");
                return;
            }
            SetImage(d.Data);
            if (!d.Roi.IsEmpty && _viewer.Roi.IsEmpty) _viewer.Roi = d.Roi;
        }

        private void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = "Снимок тестовой таблицы" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var img = ImageIO.Load(dlg.FileName);
                    Settings.LastDir = Path.GetDirectoryName(dlg.FileName);
                    SetImage(img);
                }
                catch (Exception ex) { Ui.Error(this, "Не удалось открыть файл: " + ex.Message); }
            }
        }

        private void Detect()
        {
            _viewer.Points.Clear();
            _viewer.Paths.Clear();
            _det = null;
            _detDirty = false;
            if (_img == null) { _status.Text = ""; _viewer.Invalidate(); return; }
            var s = ReadSettings();
            Cursor = Cursors.WaitCursor;
            try
            {
                _det = GridDetect.Detect(_img, _viewer.Roi, s.Pattern, s.Sigma, s.Polarity);
                foreach (var p in LatticePaths(_det)) _viewer.Paths.Add(new ViewerPath { Points = p, Color = Color.FromArgb(170, 0, 200, 255), Width = 1f });
                foreach (var n in _det.Nodes) _viewer.Points.Add(new ViewerPoint { X = n.P.X, Y = n.P.Y });
                Rectangle roi = _det.Roi;
                _status.Text = _det.Nodes.Count < 9
                    ? "Узлы не найдены (" + _det.Nodes.Count + "). Проверьте тип таблицы и рабочую зону; при необходимости задайте масштаб σ."
                    : "Найдено узлов: " + _det.Nodes.Count + " (решётка " + _det.Cols + "×" + _det.Rows + "), шаг ≈ " + Ui.F(_det.CellSize, 1) + " пикс., σ = " + Ui.F(_det.Sigma, 1) +
                      (s.Pattern == GridPattern.Lines ? (_det.Polarity > 0 ? ", тёмные линии" : ", светлые линии") : "") +
                      ".   Рабочая зона: " + roi.Width + "×" + roi.Height + " из " + _img.Width + "×" + _img.Height;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { Cursor = Cursors.Default; }
            _viewer.Invalidate();
        }

        /// <summary>Ломаные по строкам и столбцам решётки (разрыв там, где узел не найден).</summary>
        public static List<PointD[]> LatticePaths(GridDetection det)
        {
            var res = new List<PointD[]>();
            for (int j = 0; j < det.Rows; j++)
            {
                var row = new PointD[det.Cols];
                for (int i = 0; i < det.Cols; i++) { var n = det.At(i, j); row[i] = n != null ? n.P : PointD.NaN; }
                res.Add(row);
            }
            for (int i = 0; i < det.Cols; i++)
            {
                var col = new PointD[det.Rows];
                for (int j = 0; j < det.Rows; j++) { var n = det.At(i, j); col[j] = n != null ? n.P : PointD.NaN; }
                res.Add(col);
            }
            return res;
        }

        // ------------------------------------------------------------------ анализ

        internal void TourSetRoi(Rectangle r) { _viewer.Roi = r; Detect(); }
        internal void TourSettings(DistortionSettings s) { ApplySettings(s); Detect(); }
        internal DistortionResultsForm TourRun() { return Run(); }

        private DistortionResultsForm Run()
        {
            if (_img == null) { Ui.Info(this, "Откройте снимок тестовой таблицы."); return null; }
            if (_detDirty || _det == null) { _debounce.Stop(); Detect(); }
            var s = ReadSettings();
            if (_det == null || _det.Nodes.Count < 9)
            {
                Ui.Info(this, "Узлы таблицы не найдены. Проверьте тип таблицы (Pattern) и рабочую зону.");
                return null;
            }
            DistortionResult res = null;
            ImageData corrected = null;
            if (!Ui.Busy(this, () =>
            {
                res = DistortionAnalysis.Analyze(_img, _viewer.Roi, s, _det);
                if (s.CorrectedImage == CorrectedImageMode.Always)
                {
                    corrected = Warp.LensCorrect(_img, res.Selected.Model);
                    corrected.Name = Path.GetFileNameWithoutExtension(_img.Name) + " — исправлено (" + (s.Order == 5 ? "h1=" + Ui.F(res.H1, 4) + ", h2=" + Ui.F(res.H2, 4) : "k1=" + Ui.F(res.K1, 4)) + ")";
                }
            })) return null;
            _lastRoi = _viewer.Roi;
            _lastSize = new Size(_img.Width, _img.Height);
            _lastSettings = s;
            _main.AddDistortionResult(res);
            _main.LastDistortion = res;
            if (corrected != null) _main.AddImage(corrected).Modified = true;
            var f = new DistortionResultsForm(res, _img, corrected);
            f.Show(Owner ?? this);
            return f;
        }
    }
}
