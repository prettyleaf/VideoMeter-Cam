using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.Core;
using SigmaLabCam.IO;

namespace SigmaLabCam.UI
{
    /// <summary>
    /// Калибровка камеры по снимку сетки точек и выпрямление изображения (аналог программы ASRix):
    /// расстановка точек по порядку, Camera Management, Corrected Source Image, Rectify, Point List.
    /// </summary>
    public sealed class CalibrationForm : Form
    {
        private readonly MainForm _main;
        private readonly ImageViewer _viewer;
        private ImageData _img, _corrected;
        private Bitmap _bmp, _corrBmp;
        private bool _showCorrected;
        private readonly Dictionary<int, PointD> _points = new Dictionary<int, PointD>();
        private readonly List<int> _order = new List<int>();     // порядок расстановки — для отмены
        private int _next = 1;
        private bool _snap = true, _labels = true;
        private GridTemplate _t;
        private CalibrationResult _lastCal;
        private CameraProfile _camera;
        private PointListForm _list;

        private readonly Label _info, _nextLabel, _camLabel;
        private readonly ToolStripLabel _tsNext;
        private readonly ToolStripButton _tbPoint, _tbPan, _tbLoupe, _tbSnap;
        private readonly ToolStripMenuItem _miSource, _miCorrected, _miLabels;
        private readonly ToolStripStatusLabel _stPos, _stInfo;

        public event EventHandler PointsChanged;

        public CalibrationForm(MainForm main)
        {
            _main = main;
            _t = Settings.Template.Clone();
            _camera = Settings.Default;
            Theme.ApplyForm(this);
            Text = "Калибровка камеры и выпрямление изображения";
            Size = new Size(Theme.S(1280), Theme.S(840));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);
            KeyPreview = true;

            // --- меню (как в ASRix: File, View, Tools)
            var ms = new MenuStrip { Renderer = Theme.Renderer, Font = Theme.UiFont, BackColor = Color.White };
            _miSource = new ToolStripMenuItem("Исходное изображение", null, (s, e) => SetView(false)) { Checked = true };
            _miCorrected = new ToolStripMenuItem("Исправленное изображение (Corrected Source Image)", Icons.Get("corrected"), (s, e) => SetView(true));
            _miLabels = new ToolStripMenuItem("Номера точек", null, (s, e) => { _labels = !_labels; _miLabels.Checked = _labels; _viewer.ShowPointLabels = _labels; _viewer.Invalidate(); }) { Checked = true };
            ms.Items.Add(SubMenu("Файл",
                Mi("Открыть изображение…", OpenFile, Keys.Control | Keys.O, "open"),
                Mi("Взять активное изображение из главного окна", () => TakeActive(true)),
                new ToolStripSeparator(),
                Mi("Сохранить список точек…", SavePoints, Keys.Control | Keys.S, "save"),
                Mi("Загрузить список точек…", LoadPoints),
                new ToolStripSeparator(),
                Mi("Закрыть", Close)));
            ms.Items.Add(SubMenu("Правка",
                Mi("Удалить последнюю точку\tBackspace", UndoPoint, Keys.None, "undo"),
                Mi("Выбрать номер следующей точки…", ChooseNext),
                Mi("Удалить все точки", ClearPoints)));
            ms.Items.Add(SubMenu("Вид",
                _miSource, _miCorrected,
                new ToolStripSeparator(),
                Mi("Список точек (Point List)", ShowList, Keys.F8, "list"),
                Mi("Лупа", ToggleLoupe, Keys.Control | Keys.L, "loupe"),
                _miLabels,
                Mi("Вписать в окно", () => _viewer.FitToWindow(), Keys.Control | Keys.D0, "fit")));
            ms.Items.Add(SubMenu("Инструменты",
                Mi("Шаблон сетки (Database Connection)…", TemplateDialog, Keys.None, "calib"),
                Mi("Управление камерами (Camera Management)…", CameraManager, Keys.Control | Keys.M, "camera"),
                new ToolStripSeparator(),
                Mi("Сохранить исправленное изображение (Save Corrected Image)…", SaveCorrected, Keys.None, "save"),
                Mi("Исправленное изображение — в главное окно", CorrectedToMain),
                Mi("Выпрямить (Rectify)…", Rectify, Keys.Control | Keys.R, "rectify"),
                new ToolStripSeparator(),
                Mi("Коэффициенты K1–K4 → столбец «Исходное изображение»", () => ToCoefficients(0), Keys.None, "coef"),
                Mi("Коэффициенты K1–K4 → столбец «Выпрямленное изображение»", () => ToCoefficients(1))));
            MainMenuStrip = ms;

            // --- панель инструментов
            var ts = new ToolStrip { Renderer = Theme.Renderer, GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(Theme.S(16), Theme.S(16)), Padding = new Padding(Theme.S(6), Theme.S(3), 0, Theme.S(3)), Font = Theme.UiFont };
            _tbPoint = new ToolStripButton("Точки", Icons.Get("point")) { Checked = true, ToolTipText = "Расстановка точек: щелчок — поставить следующую точку, перетаскивание — переместить" };
            _tbPan = new ToolStripButton(null, Icons.Get("hand")) { ToolTipText = "Перемещение (также правая кнопка мыши или пробел)", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbLoupe = new ToolStripButton(null, Icons.Get("loupe")) { Checked = true, ToolTipText = "Лупа (Ctrl+L)", DisplayStyle = ToolStripItemDisplayStyle.Image };
            _tbSnap = new ToolStripButton("Привязка", Icons.Get("snap")) { Checked = true, ToolTipText = "Уточнять положение точки по изображению (центр точки / пересечение линий)" };
            _tbPoint.Click += (s, e) => SetTool(ImageViewer.ToolMode.Point);
            _tbPan.Click += (s, e) => SetTool(ImageViewer.ToolMode.Pan);
            _tbLoupe.Click += (s, e) => ToggleLoupe();
            _tbSnap.Click += (s, e) => { _snap = !_snap; _tbSnap.Checked = _snap; };
            _tsNext = new ToolStripLabel("Следующая: № 1") { Font = Theme.BoldFont, ForeColor = Theme.Primary };
            ts.Items.AddRange(new ToolStripItem[]
            {
                Tb(null, "open", "Открыть изображение (Ctrl+O)", OpenFile),
                Tb(null, "save", "Сохранить список точек (Ctrl+S)", SavePoints),
                Tb(null, "undo", "Удалить последнюю точку (Backspace)", UndoPoint),
                new ToolStripSeparator(),
                _tbPoint, _tbPan,
                Tb(null, "zoomin", "Увеличить (колесо мыши)", () => _viewer.ZoomIn()),
                Tb(null, "zoomout", "Уменьшить", () => _viewer.ZoomOut()),
                Tb(null, "fit", "Вписать в окно (Ctrl+0)", () => _viewer.FitToWindow()),
                _tbLoupe, _tbSnap,
                new ToolStripSeparator(),
                Tb("‹ пред.", null, "Предыдущий номер (PageUp)", () => StepNext(-1)),
                _tsNext,
                Tb("след. ›", null, "Следующий номер (PageDown)", () => StepNext(+1)),
                new ToolStripSeparator(),
                Tb("Point List", "list", "Список точек (F8)", ShowList),
                Tb("Камеры", "camera", "Управление камерами: калибровка по точкам (Ctrl+M)", CameraManager),
                Tb("Исправленное", "corrected", "Показать исправленное изображение (Corrected Source Image)", () => SetView(!_showCorrected)),
                Tb("Выпрямить", "rectify", "Выпрямление (Rectify) (Ctrl+R)", Rectify),
                Tb("K1–K4", "coef", "Передать точки в расчёт коэффициентов (табл. 1.1)", () => ToCoefficients(-1)),
            });

            // --- правая панель
            var right = new Panel { Dock = DockStyle.Right, Width = Theme.S(310), BackColor = Color.White };
            var flow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(10)) };
            _nextLabel = new Label { AutoSize = true, Font = Theme.HeaderFont, ForeColor = Theme.Primary, MaximumSize = new Size(Theme.S(285), 0) };
            _info = new Label { AutoSize = true, MaximumSize = new Size(Theme.S(285), 0), Margin = new Padding(0, Theme.S(6), 0, 0) };
            _camLabel = new Label { AutoSize = true, MaximumSize = new Size(Theme.S(285), 0), Margin = new Padding(0, Theme.S(10), 0, 0), ForeColor = Theme.Text };
            var steps = new Label
            {
                AutoSize = true, MaximumSize = new Size(Theme.S(285), 0), ForeColor = Theme.Muted, Margin = new Padding(0, Theme.S(12), 0, 0),
                Text = "Порядок работы:\n" +
                       "1. Откройте снимок таблицы (рис. 1.5), сетка должна заполнять кадр.\n" +
                       "2. Проверьте шаблон сетки (Инструменты › Шаблон сетки).\n" +
                       "3. Начиная с точки 1 в левом верхнем углу, щёлкайте по точкам по порядку, увеличив изображение колесом мыши или с лупой. " +
                       "Пунктирный кружок — ожидаемое место следующей точки.\n" +
                       "4. Когда все точки расставлены — «Камеры» › «Калибровать по точкам».\n" +
                       "5. «Исправленное» — изображение без дисторсии; «Выпрямить» — фронтальный вид таблицы.\n" +
                       "6. «K1–K4» — коэффициенты искажений по точкам 1, 7, 13, 53/66, 65/78, 118, 124, 130 (табл. 1.1).\n\n" +
                       "Правая кнопка мыши по точке — удалить или поставить заново. Backspace — удалить последнюю точку."
            };
            flow.Controls.Add(_nextLabel);
            flow.Controls.Add(_info);
            flow.Controls.Add(_camLabel);
            flow.Controls.Add(steps);
            right.Controls.Add(flow);
            right.Controls.Add(Theme.MakeHeader("Калибровка (ASRix)"));

            _viewer = new ImageViewer
            {
                Dock = DockStyle.Fill, Tool = ImageViewer.ToolMode.Point, ShowLoupe = true, AllowRoi = false,
                EmptyText = "Откройте снимок калибровочной таблицы (Файл › Открыть изображение)\nили возьмите активное изображение из главного окна"
            };
            _viewer.PointPlaced += (s, e) => PlacePoint(new PointD(e.X, e.Y));
            _viewer.PointMoving += (s, e) => MovePoint(e.Index, new PointD(e.X, e.Y), false);
            _viewer.PointMoved += (s, e) => MovePoint(e.Index, new PointD(e.X, e.Y), true);
            _viewer.PointClicked += (s, e) => PointMenu(e);
            _viewer.PixelHover += (s, e) => _stPos.Text = e.Inside ? "Pixel Coordinates:  x = " + e.X + "   y = " + e.Y : "";

            var st = new StatusStrip { Renderer = Theme.Renderer, BackColor = Theme.Primary };
            _stInfo = new ToolStripStatusLabel { ForeColor = Color.White, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _stPos = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(260) };
            st.Items.AddRange(new ToolStripItem[] { _stInfo, _stPos });

            Controls.Add(_viewer);
            Controls.Add(right);
            Controls.Add(st);
            Controls.Add(ts);
            Controls.Add(ms);

            FormClosing += (s, e) =>
            {
                if (_order.Count > 0 && _points.Count > 0 && !_saved && e.CloseReason == CloseReason.UserClosing &&
                    !Ui.Ask(this, "Список точек не сохранён. Закрыть окно калибровки?")) e.Cancel = true;
            };
            FormClosed += (s, e) =>
            {
                if (_list != null && !_list.IsDisposed) _list.Close();
                if (_bmp != null) _bmp.Dispose();
                if (_corrBmp != null) _corrBmp.Dispose();
            };
            TakeActive(false);
            UpdateInfo();
        }

        private bool _saved = true;

        private static ToolStripMenuItem SubMenu(string text, params ToolStripItem[] items)
        {
            var m = new ToolStripMenuItem(text);
            m.DropDownItems.AddRange(items);
            return m;
        }

        private static ToolStripMenuItem Mi(string text, Action a, Keys keys = Keys.None, string icon = null)
        {
            var m = new ToolStripMenuItem(text, icon != null ? Icons.Get(icon) : null, (s, e) => a());
            if (keys != Keys.None) m.ShortcutKeys = keys;
            return m;
        }

        private static ToolStripButton Tb(string text, string icon, string tip, Action a)
        {
            var b = new ToolStripButton(text, icon != null ? Icons.Get(icon) : null)
            {
                ToolTipText = tip,
                DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : (icon == null ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.ImageAndText)
            };
            b.Click += (s, e) => a();
            return b;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!(ActiveControl is TextBoxBase))
            {
                if (keyData == Keys.Back) { UndoPoint(); return true; }
                if (keyData == Keys.PageDown) { StepNext(+1); return true; }
                if (keyData == Keys.PageUp) { StepNext(-1); return true; }
                if (keyData == Keys.Oemplus || keyData == Keys.Add) { _viewer.ZoomIn(); return true; }
                if (keyData == Keys.OemMinus || keyData == Keys.Subtract) { _viewer.ZoomOut(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public IDictionary<int, PointD> Points { get { return _points; } }
        public GridTemplate Template { get { return _t; } }
        public ImageData Image { get { return _img; } }

        // ------------------------------------------------------------------ изображение

        public void SetImage(ImageData img, bool keepPoints = false)
        {
            _img = img;
            if (_bmp != null) _bmp.Dispose();
            _bmp = ImageIO.ToBitmap(img);
            DropCorrected();
            _showCorrected = false;
            _miSource.Checked = true;
            _miCorrected.Checked = false;
            if (!keepPoints) { _points.Clear(); _order.Clear(); _next = 1; _lastCal = null; _saved = true; }
            _viewer.SetImage(_bmp, false);
            Text = "Калибровка камеры и выпрямление — " + img.Name;
            RefreshPoints();
        }

        private void TakeActive(bool explicitClick)
        {
            var d = _main.Workspace.Active;
            if (d == null)
            {
                if (explicitClick) Ui.Info(this, "В главном окне нет активного изображения.");
                return;
            }
            if (explicitClick && _points.Count > 0 && !Ui.Ask(this, "Расставленные точки будут удалены. Продолжить?")) return;
            SetImage(d.Data);
        }

        private void OpenFile()
        {
            if (_points.Count > 0 && !_saved && !Ui.Ask(this, "Расставленные точки будут удалены. Продолжить?")) return;
            using (var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter, Title = "Снимок калибровочной таблицы" })
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

        private bool RequireImage()
        {
            if (_img != null) return true;
            Ui.Info(this, "Откройте снимок калибровочной таблицы.");
            return false;
        }

        // ------------------------------------------------------------------ точки

        private void SetTool(ImageViewer.ToolMode m)
        {
            _viewer.Tool = _showCorrected && m == ImageViewer.ToolMode.Point ? ImageViewer.ToolMode.Pan : m;
            _tbPoint.Checked = _viewer.Tool == ImageViewer.ToolMode.Point;
            _tbPan.Checked = _viewer.Tool == ImageViewer.ToolMode.Pan;
        }

        private void ToggleLoupe()
        {
            _viewer.ShowLoupe = !_viewer.ShowLoupe;
            _tbLoupe.Checked = _viewer.ShowLoupe;
            _viewer.Invalidate();
        }

        /// <summary>Ожидаемое положение точки по уже расставленным соседям (локальная гомография).</summary>
        private PointD Predict(int number)
        {
            if (_points.Count < 4) return PointD.NaN;
            int c = _t.Col(number), r = _t.Row(number);
            var near = new List<KeyValuePair<int, double>>();
            foreach (var kv in _points)
            {
                if (kv.Key == number) continue;
                int dc = _t.Col(kv.Key) - c, dr = _t.Row(kv.Key) - r;
                near.Add(new KeyValuePair<int, double>(kv.Key, dc * dc + dr * dr));
            }
            near.Sort((a, b) => a.Value.CompareTo(b.Value));
            var g = new List<PointD>();
            var p = new List<PointD>();
            for (int k = 0; k < near.Count && k < 12; k++) { g.Add(_t.Ground(near[k].Key)); p.Add(_points[near[k].Key]); }
            // точки не должны лежать на одной прямой
            bool rowsDiffer = false, colsDiffer = false;
            for (int k = 1; k < g.Count; k++) { if (g[k].Y != g[0].Y) rowsDiffer = true; if (g[k].X != g[0].X) colsDiffer = true; }
            if (!rowsDiffer || !colsDiffer) return PointD.NaN;
            var h = Homography.Fit(g, p);
            if (h == null) return PointD.NaN;
            PointD q = h.Map(_t.Ground(number));
            if (!q.IsValid || q.X < -_img.Width || q.Y < -_img.Height || q.X > 2 * _img.Width || q.Y > 2 * _img.Height) return PointD.NaN;
            return q;
        }

        private PointD Neighbor(int number, int dc, int dr)
        {
            int c = _t.Col(number) + dc, r = _t.Row(number) + dr;
            if (c < 0 || r < 0 || c >= _t.Cols || r >= _t.Rows) return PointD.NaN;
            int n = _t.Number(c, r);
            PointD p;
            return _points.TryGetValue(n, out p) ? p : Predict(n);
        }

        private PointD SnapPoint(int number, PointD p)
        {
            if (!_snap || _img == null) return p;
            double cell = EstimateCell();
            double radius = Math.Max(3, Math.Min(0.3 * cell, Theme.S(14) / _viewer.Zoom));
            try
            {
                PointD l = Neighbor(number, -1, 0), r = Neighbor(number, 1, 0), u = Neighbor(number, 0, -1), d = Neighbor(number, 0, 1);
                PointD q = (l.IsValid || r.IsValid) && (u.IsValid || d.IsValid)
                    ? GridDetect.SnapLines(_img, p, l, r, u, d, radius, 0)
                    : GridDetect.Snap(_img, p, radius, GridPattern.Lines, 0);
                return q.IsValid && PointD.Dist(q, p) <= radius * 1.6 ? q : p;
            }
            catch { return p; }
        }

        private double EstimateCell()
        {
            var steps = new List<double>();
            foreach (var kv in _points)
            {
                PointD q;
                int c = _t.Col(kv.Key), r = _t.Row(kv.Key);
                if (c + 1 < _t.Cols && _points.TryGetValue(kv.Key + 1, out q)) steps.Add(PointD.Dist(q, kv.Value));
                if (r + 1 < _t.Rows && _points.TryGetValue(kv.Key + _t.Cols, out q)) steps.Add(PointD.Dist(q, kv.Value));
            }
            if (steps.Count == 0) return _img != null ? Math.Min(_img.Width / (double)_t.Cols, _img.Height / (double)_t.Rows) : 40;
            steps.Sort();
            return steps[steps.Count / 2];
        }

        private void PlacePoint(PointD p)
        {
            if (_img == null || _showCorrected) return;
            if (_points.Count >= _t.Count && _points.ContainsKey(_next)) { _stInfo.Text = "Все точки шаблона расставлены. Чтобы переставить точку, перетащите её или щёлкните по ней правой кнопкой."; return; }
            int n = _next;
            _points[n] = SnapPoint(n, p);
            _order.Remove(n);
            _order.Add(n);
            _saved = false;
            _lastCal = null;
            AdvanceNext();
            RefreshPoints();
            // если ожидаемое место следующей точки за краем окна — сдвинуть вид
            PointD q = Predict(_next);
            if (q.IsValid && !_points.ContainsKey(_next) && !_viewer.IsVisible(q.X, q.Y, Theme.S(40))) _viewer.CenterOn(q.X, q.Y);
        }

        private void AdvanceNext()
        {
            for (int k = 1; k <= _t.Count; k++)
            {
                int n = (_next - 1 + k) % _t.Count + 1;
                if (!_points.ContainsKey(n)) { _next = n; return; }
            }
        }

        private void MovePoint(int index, PointD p, bool done)
        {
            if (index < 0 || index >= _viewer.Points.Count || _showCorrected) return;
            int n = (int)_viewer.Points[index].Tag;
            if (done) p = SnapPoint(n, p);
            _points[n] = p;
            _saved = false;
            _lastCal = null;
            if (done) RefreshPoints();
            else UpdateInfo();
        }

        private void PointMenu(ViewerPointEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.Index < 0 || _showCorrected) return;
            int n = (int)_viewer.Points[e.Index].Tag;
            var m = new ContextMenuStrip { Renderer = Theme.Renderer };
            m.Items.Add("Точка " + n + ":  x = " + Ui.F(_points[n].X, 2) + ",  y = " + Ui.F(_points[n].Y, 2)).Enabled = false;
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Удалить точку " + n, null, (s, a) => { _points.Remove(n); _order.Remove(n); _lastCal = null; RefreshPoints(); });
            m.Items.Add("Поставить точку " + n + " заново", null, (s, a) => { _points.Remove(n); _order.Remove(n); _next = n; _lastCal = null; RefreshPoints(); });
            m.Show(_viewer, e.Screen);
        }

        private void UndoPoint()
        {
            if (_order.Count == 0) return;
            int n = _order[_order.Count - 1];
            _order.RemoveAt(_order.Count - 1);
            _points.Remove(n);
            _next = n;
            _lastCal = null;
            RefreshPoints();
        }

        private void StepNext(int dir)
        {
            for (int k = 1; k <= _t.Count; k++)
            {
                int n = ((_next - 1 + dir * k) % _t.Count + _t.Count) % _t.Count + 1;
                if (!_points.ContainsKey(n) || k == _t.Count) { _next = n; break; }
            }
            RefreshPoints();
        }

        private void ChooseNext()
        {
            var dlg = new ParamDialog("Номер следующей точки", "Точки нумеруются слева направо и сверху вниз, с 1 до " + _t.Count + ".");
            var n = dlg.AddNumber("Номер:", _next, 0, 1, _t.Count);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _next = n.IntValue;
            RefreshPoints();
        }

        private void ClearPoints()
        {
            if (_points.Count == 0) return;
            if (!Ui.Ask(this, "Удалить все " + _points.Count + " точек?")) return;
            _points.Clear();
            _order.Clear();
            _next = 1;
            _lastCal = null;
            RefreshPoints();
        }

        internal void TourSetPoints(IDictionary<int, PointD> pts)
        {
            _points.Clear();
            _order.Clear();
            foreach (var kv in pts) { _points[kv.Key] = kv.Value; _order.Add(kv.Key); }
            _next = 1;
            AdvanceNext();
            RefreshPoints();
        }

        private void RefreshPoints()
        {
            _viewer.Points.Clear();
            LensModel lens = _showCorrected ? CurrentLens() : null;
            foreach (var kv in _points)
            {
                PointD p = lens != null ? lens.Correct(kv.Value) : kv.Value;
                _viewer.Points.Add(new ViewerPoint { X = p.X, Y = p.Y, Label = kv.Key.ToString(), Tag = kv.Key, Selected = kv.Key == _next });
            }
            if (!_showCorrected && _img != null && !_points.ContainsKey(_next))
            {
                PointD q = Predict(_next);
                if (q.IsValid) _viewer.Points.Add(new ViewerPoint { X = q.X, Y = q.Y, Label = _next.ToString(), Ghost = true, Color = Color.FromArgb(255, 200, 0) });
            }
            _viewer.Invalidate();
            UpdateInfo();
            if (PointsChanged != null) PointsChanged(this, EventArgs.Empty);
            if (_list != null && !_list.IsDisposed) _list.Fill();
        }

        private void UpdateInfo()
        {
            _tsNext.Text = "Следующая: № " + _next;
            _nextLabel.Text = _points.Count >= _t.Count ? "Все " + _t.Count + " точек расставлены" : "Следующая точка: № " + _next +
                              "  (столбец " + (_t.Col(_next) + 1) + ", строка " + (_t.Row(_next) + 1) + ")";
            _info.Text = "Шаблон: " + _t.Description + "\n" +
                         "Расставлено точек: " + _points.Count + " из " + _t.Count +
                         (_img != null ? "\nИзображение: " + _img.Name + ", " + _img.Width + "×" + _img.Height : "");
            var cam = _lastCal != null ? _lastCal.Camera : _camera;
            if (cam == null) _camLabel.Text = "Камера не откалибрована. После расстановки точек: «Камеры» › «Калибровать по точкам».";
            else
                _camLabel.Text = (_lastCal != null ? "Калибровка по этим точкам" : "Камера для коррекции") + ": «" + cam.Name + "»\n" +
                                 "k1 = " + Ui.F(cam.K1, 5) + ",  k2 = " + Ui.F(cam.K2, 5) + "\n" +
                                 "центр: " + Ui.F(cam.Cx, 1) + "; " + Ui.F(cam.Cy, 1) + " (кадр " + cam.Width + "×" + cam.Height + ")\n" +
                                 (_lastCal != null ? "СКО: без дисторсии " + Ui.F(_lastCal.FitNoDistortion.Rms, 3) + " → " + Ui.F(cam.Rms, 3) + " пикс."
                                                   : "СКО калибровки: " + Ui.F(cam.Rms, 3) + " пикс., точек " + cam.Points);
            _stInfo.Text = _showCorrected ? "Показано исправленное изображение (камера «" + (cam != null ? cam.Name : "—") + "»). Точки ставятся на исходном изображении."
                                          : (_img == null ? "" : "Щёлкните по точке № " + _next + (_snap ? " (с привязкой)" : ""));
        }

        // ------------------------------------------------------------------ список точек: файл

        private void SavePoints()
        {
            if (_points.Count == 0) { Ui.Info(this, "Точек нет."); return; }
            using (var dlg = new SaveFileDialog { Filter = "Список точек CSV (*.csv)|*.csv", FileName = PlotView.Safe("Точки " + (_img != null ? Path.GetFileNameWithoutExtension(_img.Name) : "")) + ".csv", Title = "Сохранить список точек" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, PointsCsv(), new UTF8Encoding(true));
                    Settings.LastDir = Path.GetDirectoryName(dlg.FileName);
                    _saved = true;
                }
                catch (Exception ex) { Ui.Error(this, "Не удалось сохранить файл: " + ex.Message); }
            }
        }

        public string PointsCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("№;x, пикс.;y, пикс.;столбец;строка;X, мм;Y, мм");
            var keys = new List<int>(_points.Keys);
            keys.Sort();
            foreach (int n in keys)
            {
                PointD g = _t.Ground(n);
                sb.AppendLine(n + ";" + Csv.Num(_points[n].X) + ";" + Csv.Num(_points[n].Y) + ";" + (_t.Col(n) + 1) + ";" + (_t.Row(n) + 1) + ";" + Csv.Num(g.X) + ";" + Csv.Num(g.Y));
            }
            return sb.ToString();
        }

        private void LoadPoints()
        {
            if (!RequireImage()) return;
            using (var dlg = new OpenFileDialog { Filter = "Список точек (*.csv;*.txt)|*.csv;*.txt|Все файлы (*.*)|*.*", Title = "Загрузить список точек" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var pts = new Dictionary<int, PointD>();
                    foreach (string line in File.ReadAllLines(dlg.FileName, Encoding.UTF8))
                    {
                        string[] p = line.Split(';', '\t');
                        if (p.Length < 3) continue;
                        int n;
                        double x, y;
                        if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                        if (!NumParse.TryParse(p[1], out x) || !NumParse.TryParse(p[2], out y)) continue;
                        if (n >= 1 && n <= _t.Count) pts[n] = new PointD(x, y);
                    }
                    if (pts.Count == 0) { Ui.Error(this, "В файле нет точек в формате «номер; x; y»."); return; }
                    TourSetPoints(pts);
                    _saved = true;
                }
                catch (Exception ex) { Ui.Error(this, "Не удалось прочитать файл: " + ex.Message); }
            }
        }

        private void ShowList()
        {
            if (_list == null || _list.IsDisposed) { _list = new PointListForm(this); _list.Show(this); }
            else _list.Activate();
            _list.Fill();
        }

        internal CalibrationResult LastCalibration { get { return _lastCal; } }

        // ------------------------------------------------------------------ шаблон

        private void TemplateDialog()
        {
            var dlg = new ParamDialog("Шаблон сетки (Database Connection)",
                "Геометрия калибровочной таблицы — аналог файла ASR-grid.dbf программы ASRix: число точек по горизонтали и вертикали и шаг между ними. " +
                "Таблица рис. 1.5: 13 × 10 = 130 точек.");
            var name = dlg.AddText("Название:", _t.Name, 1, 200);
            var cols = dlg.AddNumber("Точек по горизонтали:", _t.Cols, 0, 3, 60);
            var rows = dlg.AddNumber("Точек по вертикали:", _t.Rows, 0, 3, 60);
            var step = dlg.AddNumber("Шаг сетки:", _t.Step, 3, 0.001, 1e6, "мм");
            if (_points.Count > 0) dlg.AddNote("При изменении числа точек расставленные точки будут удалены.", true);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            bool resize = cols.IntValue != _t.Cols || rows.IntValue != _t.Rows;
            _t = new GridTemplate { Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "Сетка", Cols = cols.IntValue, Rows = rows.IntValue, Step = step.Value };
            Settings.Template = _t.Clone();
            Settings.Save();
            if (resize) { _points.Clear(); _order.Clear(); _next = 1; }
            _lastCal = null;
            RefreshPoints();
        }

        // ------------------------------------------------------------------ камеры

        private void CameraManager()
        {
            using (var f = new CameraManagerForm(this)) f.ShowDialog(this);
            if (_camera != null && Settings.FindCamera(_camera.Name) == null) _camera = Settings.Default;
            DropCorrected();
            if (_showCorrected) SetView(true);
            UpdateInfo();
        }

        internal CalibrationResult Calibrate(string name, int order, bool center)
        {
            var res = Calibration.Calibrate(_points, _t, _img.Width, _img.Height, center, order, name);
            _lastCal = res;
            int i = Settings.Cameras.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Settings.Cameras[i] = res.Camera; else Settings.Cameras.Add(res.Camera);
            Settings.DefaultCamera = res.Camera.Name;
            Settings.SaveCameras();
            Settings.Save();
            _camera = res.Camera;
            DropCorrected();
            RefreshPoints();
            return res;
        }

        internal void SelectCamera(CameraProfile c)
        {
            _camera = c;
            DropCorrected();
            if (_showCorrected) SetView(true);
            UpdateInfo();
        }

        private LensModel CurrentLens()
        {
            var cam = _lastCal != null ? _lastCal.Camera : _camera;
            return cam != null && _img != null ? cam.ModelFor(_img.Width, _img.Height) : null;
        }

        private void DropCorrected()
        {
            _corrected = null;
            if (_corrBmp != null) { _corrBmp.Dispose(); _corrBmp = null; }
        }

        private ImageData Corrected()
        {
            if (_corrected != null) return _corrected;
            var lens = CurrentLens();
            if (lens == null) return null;
            ImageData c = null;
            Ui.Busy(this, () => c = Warp.LensCorrect(_img, lens));
            if (c != null)
            {
                var cam = _lastCal != null ? _lastCal.Camera : _camera;
                c.Name = Path.GetFileNameWithoutExtension(_img.Name) + " — исправлено (" + cam.Name + ")";
            }
            _corrected = c;
            return c;
        }

        private bool RequireCamera()
        {
            if (CurrentLens() != null) return true;
            Ui.Info(this, "Камера не откалибрована. Расставьте точки и выполните калибровку (Инструменты › Управление камерами › Калибровать по точкам) " +
                          "или выберите ранее откалиброванную камеру.");
            return false;
        }

        private void SetView(bool corrected)
        {
            if (_img == null) return;
            if (corrected && !RequireCamera()) return;
            if (corrected)
            {
                var c = Corrected();
                if (c == null) return;
                if (_corrBmp == null) _corrBmp = ImageIO.ToBitmap(c);
                _viewer.SetImage(_corrBmp, true);
            }
            else _viewer.SetImage(_bmp, true);
            _showCorrected = corrected;
            _miSource.Checked = !corrected;
            _miCorrected.Checked = corrected;
            SetTool(corrected ? ImageViewer.ToolMode.Pan : ImageViewer.ToolMode.Point);
            RefreshPoints();
        }

        private void SaveCorrected()
        {
            if (!RequireImage() || !RequireCamera()) return;
            var c = Corrected();
            if (c == null) return;
            using (var dlg = new SaveFileDialog { Filter = ImageIO.SaveFilter, FileName = PlotView.Safe(c.Name), Title = "Сохранить исправленное изображение" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                if (Ui.Busy(this, () => ImageIO.Save(c, path))) Settings.LastDir = Path.GetDirectoryName(path);
            }
        }

        private void CorrectedToMain()
        {
            if (!RequireImage() || !RequireCamera()) return;
            var c = Corrected();
            if (c != null) _main.AddImage(c.Clone()).Modified = true;
        }

        // ------------------------------------------------------------------ выпрямление

        private void Rectify()
        {
            if (!RequireImage()) return;
            if (_points.Count < 4) { Ui.Info(this, "Для выпрямления нужно расставить хотя бы 4 точки (лучше — все точки таблицы)."); return; }
            var cams = new List<CameraProfile>(Settings.Cameras);
            var items = new List<object> { "без коррекции дисторсии (только перспектива)" };
            int def = 0;
            if (_lastCal != null && Settings.FindCamera(_lastCal.Camera.Name) == null) cams.Add(_lastCal.Camera);
            var cur = _lastCal != null ? _lastCal.Camera : _camera;
            for (int i = 0; i < cams.Count; i++)
            {
                items.Add(cams[i].Name + "  (k1=" + Ui.F(cams[i].K1, 4) + ", k2=" + Ui.F(cams[i].K2, 4) + ")");
                if (cur != null && cams[i].Name == cur.Name) def = i + 1;
            }
            double cell = EstimateCell();
            var dlg = new ParamDialog("Выпрямление (Rectify) — Output Parameters",
                "Изображение пересчитывается во фронтальный вид плоскости таблицы: исправляется дисторсия объектива (по выбранной камере) " +
                "и перспектива (по расставленным точкам). Масштаб — пикселей на один шаг сетки (" + Ui.F(_t.Step, 2) + " мм).");
            var cam = dlg.AddChoice("Камера (Calibrate Camera):", items.ToArray(), def, 340);
            var scale = dlg.AddNumber("Масштаб:", Math.Round(cell), 1, 4, 2000, "пикс. на шаг сетки");
            var margin = dlg.AddNumber("Поле вокруг сетки:", 0.5, 2, 0, 10, "шагов");
            var again = dlg.AddCheck("Открыть выпрямленное изображение в новом окне калибровки — для расстановки точек (табл. 1.1)", true);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            CameraProfile camera = cam.SelectedIndex > 0 ? cams[cam.SelectedIndex - 1] : null;
            ImageData rect = null;
            if (!Ui.Busy(this, () =>
            {
                LensModel lens = camera != null ? camera.ModelFor(_img.Width, _img.Height) : null;
                Homography h = Calibration.PlaneToImage(_points, _t, lens);
                if (h == null) throw new InvalidOperationException("Не удалось найти проективное преобразование по точкам.");
                Homography o2p;
                rect = Calibration.Rectify(_img, lens, h, _t, scale.Value, margin.Value, out o2p);
                rect.Name = Path.GetFileNameWithoutExtension(_img.Name) + " — выпрямлено" + (camera != null ? " (" + camera.Name + ")" : " (без коррекции дисторсии)");
            })) return;
            _main.AddImage(rect).Modified = true;
            if (again.Checked)
            {
                var f = new CalibrationForm(_main);
                f.SetImage(rect);
                f.Text = "Калибровка — выпрямленное изображение: " + rect.Name;
                f.Show(_main);
                f.Location = new Point(Location.X + Theme.S(30), Location.Y + Theme.S(30));
            }
            else Ui.Info(this, "Выпрямленное изображение открыто в главном окне:\n" + rect.Name);
        }

        // ------------------------------------------------------------------ коэффициенты

        /// <summary>Точки рис. 1.6 для расчёта K1–K4: A, B, C, D, E, F, G, H (F и G — середины двух точек при чётном числе строк).</summary>
        public bool SidePoints(out PointD[] pts, out string missing)
        {
            pts = new PointD[8];
            var miss = new List<int>();
            Func<int, PointD> get = n => { PointD p; if (_points.TryGetValue(n, out p)) return p; miss.Add(n); return PointD.NaN; };
            Func<int[], PointD> mid = a => a.Length == 1 ? get(a[0]) : PointD.Mid(get(a[0]), get(a[1]));
            pts[0] = get(_t.PointA); pts[1] = get(_t.PointB); pts[2] = get(_t.PointC); pts[3] = get(_t.PointD);
            pts[4] = mid(_t.PointE); pts[5] = mid(_t.PointF); pts[6] = mid(_t.PointG); pts[7] = mid(_t.PointH);
            missing = miss.Count == 0 ? null : string.Join(", ", miss.ConvertAll(n => n.ToString()).ToArray());
            return miss.Count == 0;
        }

        private void ToCoefficients(int column)
        {
            if (!RequireImage()) return;
            PointD[] pts;
            string missing;
            if (!SidePoints(out pts, out missing)) { Ui.Info(this, "Для расчёта K1–K4 нужны точки: " + missing + "."); return; }
            if (column < 0)
            {
                bool rect = _img.Name.IndexOf("выпрямлено", StringComparison.OrdinalIgnoreCase) >= 0;
                column = rect ? 1 : 0;
            }
            var f = _main.CoefficientsWindow();
            f.SetColumn(column, _t, _points, _img.Name);
        }
    }

    /// <summary>Список точек (Point List, F8): номер, положение на изображении и на таблице, отклонение после калибровки.</summary>
    internal sealed class PointListForm : Form
    {
        private readonly CalibrationForm _owner;
        private readonly DataGridView _grid;

        public PointListForm(CalibrationForm owner)
        {
            _owner = owner;
            Theme.ApplyForm(this);
            Text = "Список точек (Point List)";
            Size = new Size(Theme.S(640), Theme.S(560));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            foreach (string c in new[] { "№", "Столбец", "Строка", "x, пикс.", "y, пикс.", "X, мм", "Y, мм", "Отклонение, пикс." }) _grid.Columns.Add(c, c);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(42), Padding = new Padding(Theme.S(4)), BackColor = Theme.SurfaceAlt };
            var copy = Theme.MakeButton("Копировать");
            copy.Click += (s, e) => { _grid.SelectAll(); var o = _grid.GetClipboardContent(); if (o != null) Clipboard.SetDataObject(o); };
            var csv = Theme.MakeButton("CSV…");
            csv.Click += (s, e) => Ui.SaveText(this, _owner.PointsCsv(), "Точки.csv");
            bar.Controls.AddRange(new Control[] { copy, csv });
            Controls.Add(_grid);
            Controls.Add(bar);
        }

        public void Fill()
        {
            _grid.Rows.Clear();
            var t = _owner.Template;
            var cal = _owner.LastCalibration;
            var err = new Dictionary<int, double>();
            if (cal != null) for (int i = 0; i < cal.Numbers.Length; i++) err[cal.Numbers[i]] = cal.Fit.Errors[i];
            var keys = new List<int>(_owner.Points.Keys);
            keys.Sort();
            foreach (int n in keys)
            {
                PointD p = _owner.Points[n], g = t.Ground(n);
                double e;
                _grid.Rows.Add(n, t.Col(n) + 1, t.Row(n) + 1, Ui.F(p.X, 2), Ui.F(p.Y, 2), Ui.F(g.X, 1), Ui.F(g.Y, 1), err.TryGetValue(n, out e) ? Ui.F(e, 3) : "");
            }
            Text = "Список точек (Point List) — " + keys.Count + " из " + t.Count;
        }
    }

    /// <summary>Управление камерами (Camera Management): калибровка по точкам, выбор камеры по умолчанию.</summary>
    internal sealed class CameraManagerForm : Form
    {
        private readonly CalibrationForm _owner;
        private readonly DataGridView _grid;
        private readonly Label _result;

        public CameraManagerForm(CalibrationForm owner)
        {
            _owner = owner;
            Theme.ApplyForm(this);
            Text = "Управление камерами (Camera Management)";
            Size = new Size(Theme.S(900), Theme.S(520));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            foreach (string c in new[] { "Камера", "Кадр", "k1", "k2", "Центр x", "Центр y", "СКО, пикс.", "Точек", "Дата", "По умолчанию" }) _grid.Columns.Add(c, c);
            _grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            _result = new Label { Dock = DockStyle.Top, Height = Theme.S(64), Padding = new Padding(Theme.S(10), Theme.S(6), Theme.S(10), 0), BackColor = Theme.TintLight };
            _result.Text = "Калибровка (Calibrate Camera) рассчитывает параметры дисторсии объектива по расставленным точкам: " +
                           "сетка таблицы проецируется на снимок (перспектива), отклонения точек от прямых линий описываются " +
                           "радиальной дисторсией r_u = r_d + k1·r_d³ + k2·r_d⁵ с центром дисторсии. Камера «по умолчанию» используется для всех исправлений.";

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(46), Padding = new Padding(Theme.S(6)), BackColor = Theme.SurfaceAlt, WrapContents = false };
            var cal = Theme.MakeButton("Калибровать по точкам…", true);
            cal.Click += (s, e) => Calibrate();
            var def = Theme.MakeButton("По умолчанию");
            def.Click += (s, e) => { var c = Selected(); if (c == null) return; Settings.DefaultCamera = c.Name; Settings.Save(); _owner.SelectCamera(c); Fill(); };
            var ren = Theme.MakeButton("Переименовать…");
            ren.Click += (s, e) => Rename();
            var del = Theme.MakeButton("Удалить");
            del.Click += (s, e) =>
            {
                var c = Selected();
                if (c == null || !Ui.Ask(this, "Удалить камеру «" + c.Name + "»?")) return;
                Settings.Cameras.Remove(c);
                Settings.SaveCameras();
                Fill();
            };
            var close = Theme.MakeButton("Закрыть");
            close.DialogResult = DialogResult.OK;
            bar.Controls.AddRange(new Control[] { cal, def, ren, del, close });
            Controls.Add(_grid);
            Controls.Add(_result);
            Controls.Add(bar);
            CancelButton = close;
            Fill();
        }

        private CameraProfile Selected()
        {
            if (_grid.SelectedRows.Count == 0) { Ui.Info(this, "Выберите камеру в списке."); return null; }
            return _grid.SelectedRows[0].Tag as CameraProfile;
        }

        private void Fill()
        {
            _grid.Rows.Clear();
            var d = Settings.Default;
            foreach (var c in Settings.Cameras)
            {
                int i = _grid.Rows.Add(c.Name, c.Width + "×" + c.Height, Ui.F(c.K1, 5), Ui.F(c.K2, 5), Ui.F(c.Cx, 1), Ui.F(c.Cy, 1), Ui.F(c.Rms, 3), c.Points,
                    c.Date.ToString("dd.MM.yyyy HH:mm"), c == d ? "да" : "");
                _grid.Rows[i].Tag = c;
                if (c == d) { _grid.Rows[i].DefaultCellStyle.Font = Theme.BoldFont; _grid.Rows[i].Selected = true; }
            }
        }

        private void Calibrate()
        {
            if (_owner.Image == null) { Ui.Info(this, "В окне калибровки нет изображения."); return; }
            int n = _owner.Points.Count;
            if (n < 12) { Ui.Info(this, "Для калибровки нужно расставить не меньше 12 точек (сейчас " + n + "). Лучше — все " + _owner.Template.Count + "."); return; }
            var dlg = new ParamDialog("Калибровать камеру (Calibrate Camera)", "Точек: " + n + " из " + _owner.Template.Count + ". Кадр " + _owner.Image.Width + "×" + _owner.Image.Height + ".");
            var name = dlg.AddText("Название камеры:", "Day Night Camera " + DateTime.Now.ToString("dd.MM HH-mm"), 1, 260);
            var model = dlg.AddChoice("Модель дисторсии:", new object[] { "k1 и k2 (5-й порядок)", "только k1 (3-й порядок)" }, 0, 260);
            var center = dlg.AddCheck("Искать центр дисторсии (децентрировка)", true);
            dlg.Validator = () => name.Text.Trim().Length == 0 ? "Введите название камеры." : null;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            CalibrationResult res = null;
            if (!Ui.Busy(this, () => res = _owner.Calibrate(name.Text.Trim(), model.SelectedIndex == 0 ? 2 : 1, center.Checked))) return;
            var c = res.Camera;
            _result.Text = "Камера «" + c.Name + "» откалибрована по " + c.Points + " точкам и выбрана по умолчанию:  k1 = " + Ui.F(c.K1, 5) + ",  k2 = " + Ui.F(c.K2, 5) +
                           ",  центр (" + Ui.F(c.Cx, 1) + "; " + Ui.F(c.Cy, 1) + ").\nСКО отклонения точек: без учёта дисторсии " + Ui.F(res.FitNoDistortion.Rms, 3) +
                           " пикс., с дисторсией " + Ui.F(c.Rms, 3) + " пикс. (максимум " + Ui.F(res.Fit.MaxError, 3) + ").";
            _result.ForeColor = Theme.Primary;
            Fill();
        }

        private void Rename()
        {
            var c = Selected();
            if (c == null) return;
            var dlg = new ParamDialog("Переименовать камеру");
            var t = dlg.AddText("Название:", c.Name, 1, 260);
            dlg.Validator = () => t.Text.Trim().Length == 0 ? "Название не может быть пустым." :
                (Settings.Cameras.Exists(x => x != c && string.Equals(x.Name, t.Text.Trim(), StringComparison.OrdinalIgnoreCase)) ? "Камера с таким названием уже есть." : null);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            bool wasDefault = Settings.Default == c;
            c.Name = t.Text.Trim();
            if (wasDefault) Settings.DefaultCamera = c.Name;
            Settings.Save();
            Fill();
        }
    }
}
