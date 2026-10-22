using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.Core;
using SigmaLabCam.IO;

namespace SigmaLabCam.UI
{
    public sealed partial class MainForm
    {
        // ================================================================== Файл

        private void OpenDialog()
        {
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Multiselect = true, Title = "Открыть изображения" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                OpenFiles(dlg.FileNames);
            }
        }

        public List<ImageDoc> OpenFiles(IEnumerable<string> files)
        {
            var opened = new List<ImageDoc>();
            var errors = new StringBuilder();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (string f in files)
                {
                    try
                    {
                        ImageData d = ImageIO.Load(f);
                        opened.Add(AddImage(d));
                        Settings.LastDir = Path.GetDirectoryName(f);
                        Settings.AddRecent(f);
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine(Path.GetFileName(f) + ": " + ex.Message);
                    }
                }
            }
            finally { Cursor = Cursors.Default; }
            if (errors.Length > 0) Ui.Error(this, "Не удалось открыть:\n" + errors);
            return opened;
        }

        private void Save()
        {
            var doc = RequireImage();
            if (doc == null) return;
            string p = doc.Data.FilePath;
            if (string.IsNullOrEmpty(p) || !(p.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)))
            {
                SaveAs();
                return;
            }
            SaveTo(doc, p);
        }

        private void SaveAs()
        {
            var doc = RequireImage();
            if (doc == null) return;
            using (var dlg = new SaveFileDialog { Filter = ImageIO.SaveFilter, Title = "Сохранить изображение" })
            {
                dlg.FileName = PlotView.Safe(Path.GetFileNameWithoutExtension(doc.Name));
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((doc.Data.IsFloat || doc.Data.Type == PixelType.Gray16) && ext != ".tif" && ext != ".tiff")
                {
                    if (!Ui.Ask(this, "Изображение " + doc.Data.TypeName + " без потерь сохраняется только в TIFF. " +
                                      "В выбранном формате оно будет преобразовано в 8 бит. Продолжить?"))
                        return;
                }
                SaveTo(doc, path);
            }
        }

        private void SaveTo(ImageDoc doc, string path)
        {
            if (Ui.Busy(this, () => ImageIO.Save(doc.Data, path)))
            {
                doc.Data.FilePath = path;
                doc.Modified = false;
                Settings.LastDir = Path.GetDirectoryName(path);
                Settings.AddRecent(path);
                UpdateTitle();
                _list.Invalidate();
            }
        }

        private void SaveAllToFolder()
        {
            if (_ws.Docs.Count == 0) { Ui.Info(this, "Нет открытых изображений."); return; }
            using (var fb = new FolderBrowserDialog { Description = "Папка для сохранения всех изображений (TIFF без сжатия)" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) fb.SelectedPath = Settings.LastDir;
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                Ui.Busy(this, () =>
                {
                    foreach (var d in _ws.Docs)
                    {
                        string path = Path.Combine(fb.SelectedPath, PlotView.Safe(Path.GetFileNameWithoutExtension(d.Name)) + ".tif");
                        ImageIO.Save(d.Data, path);
                        d.Data.FilePath = path;
                        d.Modified = false;
                    }
                    Settings.LastDir = fb.SelectedPath;
                });
                _list.Invalidate();
                UpdateTitle();
            }
        }

        private void CloseActive()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            if (doc.Modified)
            {
                var r = Ui.AskCancel(this, "Сохранить изменения в «" + doc.Name + "»?");
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) { SaveAs(); if (doc.Modified) return; }
            }
            _ws.Close(doc);
        }

        private void CloseAll()
        {
            if (_ws.Docs.Exists(d => d.Modified) && !Ui.Ask(this, "Закрыть все изображения? Несохранённые изменения будут потеряны.")) return;
            while (_ws.Docs.Count > 0) _ws.Close(_ws.Docs[_ws.Docs.Count - 1]);
        }

        private void PrintActive()
        {
            var doc = RequireImage();
            if (doc == null) return;
            using (var pd = new PrintDocument())
            {
                pd.DocumentName = doc.Name;
                pd.DefaultPageSettings.Landscape = doc.Data.Width > doc.Data.Height;
                pd.PrintPage += (s, e) =>
                {
                    Rectangle m = e.MarginBounds;
                    Bitmap b = doc.Display;
                    float k = Math.Min((float)m.Width / b.Width, (float)m.Height / b.Height);
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(b, m.Left + (m.Width - b.Width * k) / 2, m.Top + (m.Height - b.Height * k) / 2, b.Width * k, b.Height * k);
                };
                using (var dlg = new PrintDialog { Document = pd, UseEXDialog = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try { pd.Print(); }
                    catch (Exception ex) { Ui.Error(this, "Ошибка печати: " + ex.Message); }
                }
            }
        }

        // ================================================================== Правка

        private void Undo()
        {
            var doc = _ws.Active;
            if (doc == null || !doc.CanUndo) return;
            doc.Undo();
        }

        private void Duplicate()
        {
            var doc = RequireImage();
            if (doc == null) return;
            ImageData d = doc.Roi.IsEmpty ? doc.Data.Clone() : ImageOps.Crop(doc.Data, doc.Roi);
            d.Name = Path.GetFileNameWithoutExtension(doc.Name) + "-копия";
            d.FilePath = null;
            AddImage(d).Modified = true;
        }

        private void Rename()
        {
            var doc = RequireImage();
            if (doc == null) return;
            var dlg = new ParamDialog("Переименовать");
            var t = dlg.AddText("Новое имя:", doc.Name, 1, 300);
            dlg.Validator = () => string.IsNullOrEmpty(t.Text.Trim()) ? "Имя не может быть пустым." :
                (_ws.Docs.Exists(d => d != doc && d.Name == t.Text.Trim()) ? "Изображение с таким именем уже открыто." : null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            doc.Name = t.Text.Trim();
            _ws.NotifyDocs();
            UpdateTitle();
        }

        private void SelectAll()
        {
            var doc = RequireImage();
            if (doc == null) return;
            _viewer.Roi = new Rectangle(0, 0, doc.Data.Width, doc.Data.Height);
        }

        private void SelectNone()
        {
            if (_ws.Active == null) return;
            _viewer.Roi = Rectangle.Empty;
        }

        private void SpecifyRoi()
        {
            var doc = RequireImage();
            if (doc == null) return;
            Rectangle r = doc.Roi.IsEmpty ? new Rectangle(doc.Data.Width / 4, doc.Data.Height / 4, doc.Data.Width / 2, doc.Data.Height / 2) : doc.Roi;
            var dlg = new ParamDialog("Задать выделение", "Координаты левого верхнего угла и размер в пикселях.");
            var x = dlg.AddNumber("X:", r.X, 0, 0, doc.Data.Width - 1);
            var y = dlg.AddNumber("Y:", r.Y, 0, 0, doc.Data.Height - 1);
            var w = dlg.AddNumber("Ширина:", r.Width, 0, 1, doc.Data.Width);
            var h = dlg.AddNumber("Высота:", r.Height, 0, 1, doc.Data.Height);
            var centered = dlg.AddCheck("X, Y — центр области", false);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            int X = x.IntValue, Y = y.IntValue;
            if (centered.Checked) { X -= w.IntValue / 2; Y -= h.IntValue / 2; }
            _viewer.Roi = new Rectangle(X, Y, w.IntValue, h.IntValue);
        }

        private void OptionsDialog()
        {
            var dlg = new ParamDialog("Настройки");
            var luma = dlg.AddChoice("Яркость для RGB:", new object[] { "0,299R + 0,587G + 0,114B (ТВ-яркость)", "(R+G+B)/3" },
                LabOptions.Luma == LumaMode.Weighted ? 0 : 1, 300);
            dlg.AddNote("Яркость используется при поиске узлов тестовых таблиц и при переводе RGB-изображений в 8 бит.");
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            LabOptions.Luma = luma.SelectedIndex == 0 ? LumaMode.Weighted : LumaMode.Unweighted;
            Settings.Save();
        }

        // ================================================================== Изображение

        private void ConvertType(PixelType t)
        {
            var doc = RequireImage();
            if (doc == null || doc.Data.Type == t) return;
            ApplyToActive("Тип: " + t, d => ImageOps.ConvertTo(d, t));
        }

        private void Crop()
        {
            var doc = RequireImage();
            if (doc == null) return;
            if (doc.Roi.IsEmpty) { Ui.Info(this, "Сначала выделите область мышью."); return; }
            Rectangle r = doc.Roi;
            ApplyToActive("Обрезка", d => ImageOps.Crop(d, r));
        }

        private void ShowImageInfo()
        {
            var doc = RequireImage();
            if (doc == null) return;
            var d = doc.Data;
            Ui.Info(this,
                "Имя: " + d.Name + "\n" +
                "Файл: " + (d.FilePath ?? "(не сохранён)") + "\n" +
                "Размер: " + d.Width + " × " + d.Height + " пикс.\n" +
                "Тип: " + d.TypeName + "\n" +
                "Центр кадра: " + Ui.F((d.Width - 1) / 2.0, 1) + "; " + Ui.F((d.Height - 1) / 2.0, 1) + "\n" +
                "Полудиагональ (радиус нормировки r = 1): " + Ui.F(LensModel.HalfDiagonal(d.Width, d.Height), 1) + " пикс.\n" +
                "Незаполненных (нулевых) пикселей: " + Warp.CountGaps(d).ToString("N0"));
        }

        // ================================================================== Геометрия

        private DistortionResult _lastDistortion;

        internal DistortionResult LastDistortion
        {
            get { return _lastDistortion; }
            set { _lastDistortion = value; }
        }

        private void LensDistortDialog()
        {
            if (RequireImage() == null) return;
            var dlg = new ParamDialog("Внести дисторсию объектива",
                "Изображение пересчитывается так, как его снял бы объектив с радиальной дисторсией r_u = r_d + k1·r_d³ + k2·r_d⁵ " +
                "(формулы 1.4, 1.5). Радиусы нормированы на полудиагональ кадра. k1 > 0 — бочкообразная, k1 < 0 — подушкообразная.");
            var k1 = dlg.AddNumber("k1:", 0.1, 5, -1, 1);
            var k2 = dlg.AddNumber("k2:", 0, 5, -1, 1);
            var toNew = dlg.AddCheck("Результат — в новое изображение", true);
            dlg.EnablePreview();
            RunFilterDialog(dlg, "Дисторсия", d => Warp.LensDistort(d, LensModel.ForFrame(d.Width, d.Height, k1.Value, k2.Value)), toNew,
                () => "дисторсия k1=" + Ui.G(k1.Value) + (k2.Value != 0 ? " k2=" + Ui.G(k2.Value) : ""));
        }

        private void LensCorrectDialog()
        {
            if (RequireImage() == null) return;
            var last = _lastDistortion;
            var dlg = new ParamDialog("Исправить дисторсию по коэффициентам",
                "Исправление радиальной дисторсии r_u = r_d + k1·r_d³ + k2·r_d⁵ (формулы 1.4, 1.5); центр — центр кадра." +
                (last != null ? "\nПоследнее измерение Distortion (" + last.Title + "): k1 = " + Ui.F(last.K1, 5) + "; h1 = " + Ui.F(last.H1, 5) + ", h2 = " + Ui.F(last.H2, 5) + "." : ""));
            var k1 = dlg.AddNumber("k1 (или h1):", last != null ? Math.Round(last.Selected.Model.K1, 5) : 0.1, 5, -1, 1);
            var k2 = dlg.AddNumber("k2 (или h2):", last != null ? Math.Round(last.Selected.Model.K2, 5) : 0, 5, -1, 1);
            var toNew = dlg.AddCheck("Результат — в новое изображение", true);
            dlg.EnablePreview();
            RunFilterDialog(dlg, "Коррекция дисторсии", d => Warp.LensCorrect(d, LensModel.ForFrame(d.Width, d.Height, k1.Value, k2.Value)), toNew,
                () => "исправлено k1=" + Ui.G(k1.Value));
        }

        private void CameraCorrectDialog()
        {
            if (RequireImage() == null) return;
            if (Settings.Cameras.Count == 0)
            {
                Ui.Info(this, "Профилей камер нет. Откалибруйте камеру: Измерения › Калибровка камеры и выпрямление (ASRix) › Камеры.");
                return;
            }
            var names = Settings.Cameras.ConvertAll(c => (object)(c.Name + "  (" + c.Width + "×" + c.Height + ", k1=" + Ui.F(c.K1, 4) + ")")).ToArray();
            int def = Math.Max(0, Settings.Cameras.IndexOf(Settings.Default));
            var dlg = new ParamDialog("Исправить дисторсию по профилю камеры", "Corrected Source Image: исправление дисторсии объектива по параметрам калибровки камеры.");
            var cam = dlg.AddChoice("Камера:", names, def, 360);
            var toNew = dlg.AddCheck("Результат — в новое изображение", true);
            dlg.EnablePreview();
            RunFilterDialog(dlg, "Коррекция по профилю", d => Warp.LensCorrect(d, Settings.Cameras[cam.SelectedIndex].ModelFor(d.Width, d.Height)), toNew,
                () => "исправлено (" + Settings.Cameras[cam.SelectedIndex].Name + ")");
        }

        private void SphereDialog(bool correct)
        {
            var doc = RequireImage();
            if (doc == null) return;
            double rho = LensModel.HalfDiagonal(doc.Data.Width, doc.Data.Height);
            var dlg = new ParamDialog(correct ? "Коррекция сферических искажений (1.13)" : "Сферические искажения (1.12)",
                correct
                    ? "ρ = R·ρ1 / √(R² − ρ1²). Точки с ρ1 ≥ R не восстанавливаются. При прямом пересчёте координат появляются " +
                      "незаполненные (чёрные) элементы — их заполняет адаптивная линейная интерполяция (рис. 1.9)."
                    : "ρ1 = R·sin(arctg(ρ/R)) = R·ρ / √(R² + ρ²). ρ — расстояние от центра, пикс.; R — радиус сферы, пикс. " +
                      "Полудиагональ кадра: " + Ui.F(rho, 1) + " пикс.");
            var R = dlg.AddNumber("Радиус сферы R:", Math.Round(rho), 1, 1, 1e6, "пикс.");
            var cx = dlg.AddNumber("Центр X:", (doc.Data.Width - 1) / 2.0, 1);
            var cy = dlg.AddNumber("Центр Y:", (doc.Data.Height - 1) / 2.0, 1);
            var method = dlg.AddChoice("Пересчёт координат:", new object[]
            {
                "Прямой — перенос пикселей (как в Mathcad)", "Обратный — с билинейной интерполяцией"
            }, 0, 300);
            CheckBox fill = correct ? dlg.AddCheck("Заполнить пропуски (адаптивная линейная интерполяция, рис. 1.9)", false) : null;
            var info = dlg.AddNote("");
            Action upd = () =>
            {
                double r = R.Value;
                if (double.IsNaN(r) || r <= 0) { info.Text = ""; return; }
                double r1 = Warp.SphereRho1(rho, r);
                info.Text = correct
                    ? "Восстанавливается область ρ1 < R = " + Ui.F(r, 0) + " пикс."
                    : "Угол кадра: ρ = " + Ui.F(rho, 0) + " → ρ1 = " + Ui.F(r1, 1) + " пикс. (сжатие в " + Ui.F(rho / r1, 2) + " раза)";
            };
            R.TextChanged += (s, e) => upd();
            upd();
            var toNew = dlg.AddCheck("Результат — в новое изображение", true);
            dlg.EnablePreview();
            Func<ImageData, ImageData> f = d =>
            {
                var m = method.SelectedIndex == 0 ? MapMethod.Forward : MapMethod.Inverse;
                ImageData r = correct ? Warp.SphereCorrect(d, R.Value, cx.Value, cy.Value, m) : Warp.SphereDistort(d, R.Value, cx.Value, cy.Value, m);
                if (fill != null && fill.Checked) r = Warp.FillGaps(r);
                return r;
            };
            RunFilterDialog(dlg, correct ? "Коррекция (1.13)" : "Сфера (1.12)", f, toNew,
                () => (correct ? "коррекция R=" : "сфера R=") + Ui.G(R.Value) + (method.SelectedIndex == 0 ? " прямой" : " обратный") + (fill != null && fill.Checked ? " + интерполяция" : ""));
        }

        private void FillGapsCommand()
        {
            var doc = RequireImage();
            if (doc == null) return;
            int before = Warp.CountGaps(doc.Data);
            if (before == 0) { Ui.Info(this, "Незаполненных (нулевых) элементов нет."); return; }
            ApplyToActive("Интерполяция", Warp.FillGaps);
            int after = Warp.CountGaps(_ws.Active.Data);
            _stInfo.Text = "Адаптивная линейная интерполяция: незаполненных элементов было " + before.ToString("N0") + ", осталось " + after.ToString("N0") +
                           (after > 0 ? " (повторите команду для больших пропусков)" : "");
        }

        // ================================================================== окна модулей

        private CaptureForm _capture;
        private CoefficientsForm _coef;

        private void CaptureWindow()
        {
            if (_capture != null && !_capture.IsDisposed) { _capture.Activate(); return; }
            _capture = new CaptureForm(this);
            _capture.Show(this);
        }

        public void DistortionWindow() { new DistortionForm(this).Show(this); }

        public CalibrationForm CalibrationWindow()
        {
            var f = new CalibrationForm(this);
            f.Show(this);
            return f;
        }

        public CoefficientsForm CoefficientsWindow()
        {
            if (_coef == null || _coef.IsDisposed) _coef = new CoefficientsForm(this);
            if (!_coef.Visible) _coef.Show(this);
            _coef.Activate();
            return _coef;
        }

        private void SphereWindow() { new SphereForm(this).Show(this); }

        private void PlotTableWindow() { new TablePlotForm().Show(this); }

        // ================================================================== Таблицы

        public void ChartGeneratorDialog()
        {
            var dlg = new ParamDialog("Генератор тестовых таблиц",
                "Таблицы для измерения дисторсии (рис. 1.4) и калибровки камеры (рис. 1.5). Печатайте без масштабирования " +
                "«по размеру страницы» с сохранением пропорций; таблица должна быть плоской.");
            var type = dlg.AddChoice("Таблица:", new object[]
            {
                "Шахматное поле (squares, checkerboard)", "Сетка (grid)", "Таблица калибровки с номерами точек (рис. 1.5)"
            }, 0, 300);
            var cols = dlg.AddNumber("Столбцов:", 12, 0, 2, 60);
            var rows = dlg.AddNumber("Строк:", 9, 0, 2, 60);
            var cell = dlg.AddNumber("Шаг (клетка):", 80, 0, 8, 1000, "пикс.");
            var line = dlg.AddNumber("Толщина линий:", 4, 0, 1, 50, "пикс.");
            var dot = dlg.AddNumber("Радиус точек:", 8, 0, 1, 100, "пикс.");
            var margin = dlg.AddNumber("Поле вокруг:", 80, 0, 0, 2000, "пикс.");
            var numbers = dlg.AddCheck("Номера точек", true);
            var info = dlg.AddNote("");
            Action upd = () =>
            {
                int t = type.SelectedIndex;
                line.Enabled = t >= 1;
                dot.Enabled = numbers.Enabled = t == 2;
                if (double.IsNaN(cols.Value) || double.IsNaN(rows.Value)) return;
                info.Text = t == 0 ? "Клеток " + cols.IntValue + "×" + rows.IntValue + ", внутренних узлов " + (cols.IntValue - 1) + "×" + (rows.IntValue - 1) + "."
                          : t == 1 ? "Клеток " + cols.IntValue + "×" + rows.IntValue + ", пересечений линий " + (cols.IntValue + 1) + "×" + (rows.IntValue + 1) + "."
                          : "Точек " + cols.IntValue + "×" + rows.IntValue + " = " + cols.IntValue * rows.IntValue + " (для ASR-grid: 13×10 = 130).";
            };
            type.SelectedIndexChanged += (s, e) =>
            {
                if (type.SelectedIndex == 2) { cols.Value = 13; rows.Value = 10; cell.Value = 100; line.Value = 3; }
                else if (cols.IntValue == 13 && rows.IntValue == 10) { cols.Value = 12; rows.Value = 9; cell.Value = 80; line.Value = 4; }
                upd();
            };
            foreach (Control c in new Control[] { cols, rows }) c.TextChanged += (s, e) => upd();
            upd();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Ui.Busy(this, () =>
            {
                ImageData img;
                switch (type.SelectedIndex)
                {
                    case 1: img = TestCharts.Grid(cols.IntValue, rows.IntValue, cell.IntValue, line.IntValue, margin.IntValue); break;
                    case 2:
                        img = TestCharts.CalibrationGrid(cols.IntValue, rows.IntValue, cell.IntValue, dot.IntValue, line.IntValue, Math.Max(margin.IntValue, cell.IntValue / 2));
                        if (numbers.Checked) img = NumberPoints(img, cols.IntValue, rows.IntValue, cell.IntValue, Math.Max(margin.IntValue, cell.IntValue / 2));
                        break;
                    default: img = TestCharts.Checkerboard(cols.IntValue, rows.IntValue, cell.IntValue, margin.IntValue); break;
                }
                AddImage(img).Modified = true;
            });
        }

        /// <summary>Номера точек таблицы калибровки (как на рис. 1.5: справа снизу от точки).</summary>
        private static ImageData NumberPoints(ImageData chart, int cols, int rows, int step, int margin)
        {
            using (Bitmap b = ImageIO.ToBitmap(chart))
            {
                using (var g = Graphics.FromImage(b))
                using (var f = Theme.SafeFont(Theme.SansNames, Math.Max(8f, step * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(Color.FromArgb(40, 40, 40)))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    for (int j = 0; j < rows; j++)
                        for (int i = 0; i < cols; i++)
                            g.DrawString((j * cols + i + 1).ToString(), f, br, margin + i * step + step * 0.14f, margin + j * step + step * 0.12f);
                }
                var r = ImageOps.ConvertTo(ImageIO.FromBitmap(b), PixelType.Gray8);
                r.Name = chart.Name;
                return r;
            }
        }

        private void ShootDialog()
        {
            var doc = RequireImage();
            if (doc == null) return;
            var dlg = new ParamDialog("Имитация съёмки таблицы камерой",
                "Активное изображение считается напечатанной таблицей. Таблица поворачивается вокруг вертикальной оси на угол α " +
                "(рис. 3.1) и снимается камерой с дисторсией объектива k1, k2. Пригодно для отработки методики без камеры; " +
                "результаты лабораторной работы получают по снимкам с настоящей камеры.");
            var w = dlg.AddNumber("Ширина кадра:", 720, 0, 64, 8000, "пикс.");
            var h = dlg.AddNumber("Высота кадра:", 576, 0, 64, 8000, "пикс.");
            var angle = dlg.AddNumber("Угол поворота α:", 0, 1, -80, 80, "°");
            var tilt = dlg.AddNumber("Наклон (вокруг горизонт. оси):", 0, 1, -80, 80, "°");
            var fill = dlg.AddNumber("Доля кадра, занятая таблицей:", 0.8, 2, 0.05, 3);
            var dist = dlg.AddNumber("Расстояние, в ширинах таблицы:", 3, 2, 0.3, 100);
            var k1 = dlg.AddNumber("k1:", 0.08, 5, -1, 1);
            var k2 = dlg.AddNumber("k2:", 0, 5, -1, 1);
            var blur = dlg.AddNumber("Размытие σ:", 0.7, 2, 0, 20, "пикс.");
            var noise = dlg.AddNumber("Шум σ:", 2, 2, 0, 100, "уровней");
            var color = dlg.AddCheck("Цветной снимок (RGB)", true);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Ui.Busy(this, () =>
            {
                var s = new ShotSpec
                {
                    Width = w.IntValue, Height = h.IntValue, AngleDeg = angle.Value, TiltDeg = tilt.Value, Fill = fill.Value,
                    Distance = dist.Value, K1 = k1.Value, K2 = k2.Value, Blur = blur.Value, Noise = noise.Value, Color = color.Checked
                };
                var img = TestCharts.Shoot(doc.Data, s);
                img.Name = "Имитация " + Path.GetFileNameWithoutExtension(doc.Name) + " α=" + angle.Value.ToString("0.#") + "° k1=" + k1.Value.ToString("0.#####");
                AddImage(img).Modified = true;
            });
        }

        // ================================================================== Справка

        private void ShowGuide() { ShowGuide(null); }

        private void ShowGuide(string anchor) { HelpForm.ShowHelp(this, anchor); }

        private void ShowAbout()
        {
            using (var f = new AboutForm()) f.ShowDialog(this);
        }
    }
}
