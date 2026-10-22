using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    public sealed partial class MainForm : Form
    {
        private readonly Workspace _ws = new Workspace();
        private ImageViewer _viewer;
        private ListBox _list;
        private ToolStripStatusLabel _stPos, _stValue, _stInfo, _stZoom;
        private ToolStripButton _tbSelect, _tbPan, _tbPoint, _tbLoupe;
        private DataGridView _pointsGrid;
        private Label _pointInfo, _roiCaption;
        private DataGridView _results;
        private SplitContainer _splitMain, _splitLeft, _splitRight;
        private Bitmap _previewBmp;
        private int _resultNo;
        private bool _fillingPoints;

        public MainForm()
        {
            Theme.ApplyForm(this);
            Text = Ui.AppName;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1360), Theme.S(860));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            if (Theme.FitToScreen(this)) WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            AllowDrop = true;

            BuildLayout();
            var tool = BuildToolbar();
            var menu = BuildMenu();
            var status = BuildStatus();
            Controls.Add(_splitMain);
            Controls.Add(status);
            Controls.Add(tool);
            Controls.Add(menu);
            MainMenuStrip = menu;

            _ws.DocsChanged += (s, e) => RefreshList();
            _ws.ActiveChanged += (s, e) => OnActiveChanged();
            _ws.ActiveDataChanged += (s, e) => OnActiveDataChanged();

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) OpenFiles(files);
            };
            OnActiveChanged();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                _splitMain.SplitterDistance = Math.Max(Theme.S(200), _splitMain.Height - Theme.S(180));
                _splitLeft.SplitterDistance = Theme.S(210);
                _splitRight.SplitterDistance = Math.Max(Theme.S(300), _splitRight.Width - Theme.S(300));
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.CloseReason == CloseReason.UserClosing && _ws.Docs.Exists(d => d.Modified))
            {
                if (!Ui.Ask(this, "Есть изменённые и несохранённые изображения. Выйти из программы?"))
                {
                    e.Cancel = true;
                    return;
                }
            }
            Settings.Save();
        }

        // ------------------------------------------------------------------ раскладка

        private void BuildLayout()
        {
            _splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = Theme.S(5), BackColor = Theme.Tint };
            _splitLeft = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1, SplitterWidth = Theme.S(4), BackColor = Theme.Tint };
            _splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2, SplitterWidth = Theme.S(4), BackColor = Theme.Tint };
            _splitMain.Panel1.Controls.Add(_splitLeft);
            _splitLeft.Panel2.Controls.Add(_splitRight);

            // --- список изображений
            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.S(54),
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White
            };
            _list.DrawItem += DrawListItem;
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _ws.Docs.Count) _ws.Active = _ws.Docs[_list.SelectedIndex];
            };
            _list.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    int i = _list.IndexFromPoint(e.Location);
                    if (i >= 0) _list.SelectedIndex = i;
                }
            };
            var lm = new ContextMenuStrip { Renderer = Theme.Renderer };
            lm.Items.Add("Переименовать…", null, (s, e) => Rename());
            lm.Items.Add("Дублировать", null, (s, e) => Duplicate());
            lm.Items.Add("Сохранить как…", Icons.Get("save"), (s, e) => SaveAs());
            lm.Items.Add(new ToolStripSeparator());
            lm.Items.Add("Distortion…", Icons.Get("distortion"), (s, e) => DistortionWindow());
            lm.Items.Add("Калибровка камеры (ASRix)…", Icons.Get("calib"), (s, e) => CalibrationWindow());
            lm.Items.Add(new ToolStripSeparator());
            lm.Items.Add("Закрыть", null, (s, e) => CloseActive());
            _list.ContextMenuStrip = lm;
            var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            leftPanel.Controls.Add(_list);
            leftPanel.Controls.Add(Theme.MakeHeader("Изображения"));
            _splitLeft.Panel1.Controls.Add(leftPanel);

            // --- просмотр
            _viewer = new ImageViewer { Dock = DockStyle.Fill };
            _viewer.RoiChanged += (s, e) => { if (_ws.Active != null) _ws.Active.Roi = _viewer.Roi; UpdateRoiCaption(); };
            _viewer.RoiChanging += (s, e) => UpdateRoiCaption();
            _viewer.ZoomChanged += (s, e) => _stZoom.Text = "Масштаб: " + Math.Round(_viewer.Zoom * 100) + "%";
            _viewer.PixelHover += (s, e) =>
            {
                var d = _ws.Active;
                if (d == null || !e.Inside) { _stPos.Text = ""; _stValue.Text = ""; return; }
                _stPos.Text = "x=" + e.X + "  y=" + e.Y;
                _stValue.Text = "значение: " + d.Data.ValueString(e.X, e.Y);
            };
            _viewer.PointPlaced += (s, e) =>
            {
                var d = _ws.Active;
                if (d == null) return;
                d.Points.Add(new PointD(e.X, e.Y));
                SyncPoints(true);
            };
            _viewer.PointMoving += (s, e) =>
            {
                var d = _ws.Active;
                if (d == null || e.Index < 0 || e.Index >= d.Points.Count) return;
                d.Points[e.Index] = new PointD(e.X, e.Y);
                UpdatePointRow(e.Index);
            };
            _viewer.PointClicked += (s, e) =>
            {
                if (e.Index < 0 || _ws.Active == null) return;
                SelectPointRow(e.Index);
                if (e.Button == MouseButtons.Right)
                {
                    int idx = e.Index;
                    var m = new ContextMenuStrip { Renderer = Theme.Renderer };
                    m.Items.Add("Удалить точку " + (idx + 1), null, (a, b) => { if (_ws.Active != null && idx < _ws.Active.Points.Count) { _ws.Active.Points.RemoveAt(idx); SyncPoints(false); } });
                    m.Show(_viewer, e.Screen);
                }
            };
            _splitRight.Panel1.Controls.Add(_viewer);

            _splitRight.Panel2.Controls.Add(BuildRightPanel());
            _splitMain.Panel2.Controls.Add(BuildResults());
        }

        private Control BuildRightPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(4)) };
            _roiCaption = new Label { AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(Theme.S(270), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            top.Controls.Add(_roiCaption);
            top.Controls.Add(new Label
            {
                Text = "Инструмент «Точка» (кнопка с перекрестием на панели): щелчок ставит точку, перетаскивание — перемещает, " +
                       "правая кнопка — удалить. Выделите в списке 2 точки — расстояние между ними, 3 точки — отклонение третьей " +
                       "от прямой через первые две (Δh) и Kг = Δh / L · 100%.",
                AutoSize = true, MaximumSize = new Size(Theme.S(270), 0), ForeColor = Theme.Muted, Font = Theme.SmallFont
            });

            _pointsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.TintLight
            };
            _pointsGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _pointsGrid.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            _pointsGrid.DefaultCellStyle.SelectionBackColor = Theme.TintLight;
            _pointsGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _pointsGrid.Columns.Add("n", "№");
            _pointsGrid.Columns.Add("x", "X, пикс.");
            _pointsGrid.Columns.Add("y", "Y, пикс.");
            _pointsGrid.Columns[0].FillWeight = 40;
            _pointsGrid.SelectionChanged += (s, e) => { if (!_fillingPoints) { MarkSelectedPoints(); UpdatePointInfo(); } };
            _pointsGrid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelectedPoints(); e.Handled = true; } };

            _pointInfo = new Label { Dock = DockStyle.Bottom, Height = Theme.S(78), Padding = new Padding(Theme.S(8), Theme.S(4), Theme.S(4), 0), ForeColor = Theme.Primary, Font = Theme.BoldFont, BackColor = Theme.SurfaceAlt };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(62), Padding = new Padding(Theme.S(4)), BackColor = Theme.SurfaceAlt, WrapContents = true };
            Func<string, Action, Button> small = (text, act) =>
            {
                var b = Theme.MakeButton(text);
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                b.MinimumSize = new Size(Theme.S(60), Theme.S(24));
                b.Padding = new Padding(Theme.S(2), 0, Theme.S(2), 0);
                b.Margin = new Padding(Theme.S(2));
                b.Click += (s, e) => act();
                bar.Controls.Add(b);
                return b;
            };
            small("Удалить", DeleteSelectedPoints);
            small("Очистить", () => { if (_ws.Active != null) { _ws.Active.Points.Clear(); SyncPoints(false); } });
            small("Копировать", () => { string t = PointsText('\t'); if (t != null) Clipboard.SetText(t); });
            small("CSV…", () => { string t = PointsText(';'); if (t != null) Ui.SaveText(this, t, "Точки.csv"); else Ui.Info(this, "Точек нет."); });

            panel.Controls.Add(_pointsGrid);
            panel.Controls.Add(top);
            panel.Controls.Add(bar);
            panel.Controls.Add(_pointInfo);
            panel.Controls.Add(Theme.MakeHeader("Точки (Point List)"));
            return panel;
        }

        private static readonly string[] ResultCols =
        {
            "№", "Изображение", "Таблица", "Узлов", "k1 (3-й пор.)", "h1 (5-й пор.)", "h2 (5-й пор.)", "SMIA TV, %", "Дисторсия в углу, %",
            "K1 верх, %", "K2 лево, %", "K3 право, %", "K4 низ, %", "Трапеция гор., %", "Трапеция верт., %", "Kгв, %", "Kгг, %", "СКО, пикс."
        };

        private Control BuildResults()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _results = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
                EnableHeadersVisualStyles = false,
                GridColor = Theme.TintLight
            };
            _results.ColumnHeadersDefaultCellStyle.BackColor = Theme.Surface;
            _results.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            _results.ColumnHeadersDefaultCellStyle.Font = Theme.BoldFont;
            _results.DefaultCellStyle.SelectionBackColor = Theme.TintLight;
            _results.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _results.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            foreach (string c in ResultCols) _results.Columns.Add(c, c);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(32), BackColor = Theme.Surface, Padding = new Padding(Theme.S(4), 0, 0, 0), WrapContents = false };
            var title = new Label { Text = "Результаты измерений (Distortion)", Font = Theme.HeaderFont, ForeColor = Theme.Primary, AutoSize = true, Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(16), 0) };
            bar.Controls.Add(title);
            foreach (var t in new[]
            {
                new KeyValuePair<string, Action>("Копировать", CopyResults),
                new KeyValuePair<string, Action>("Сохранить CSV…", SaveResults),
                new KeyValuePair<string, Action>("Удалить строку", () => { foreach (DataGridViewRow r in _results.SelectedRows) _results.Rows.Remove(r); }),
                new KeyValuePair<string, Action>("Очистить", () => { _results.Rows.Clear(); _resultNo = 0; })
            })
            {
                var b = Theme.MakeButton(t.Key);
                b.MinimumSize = new Size(Theme.S(80), Theme.S(24));
                b.Margin = new Padding(Theme.S(2), Theme.S(4), Theme.S(2), 0);
                Action act = t.Value;
                b.Click += (s, e) => act();
                bar.Controls.Add(b);
            }
            panel.Controls.Add(_results);
            panel.Controls.Add(bar);
            return panel;
        }

        private StatusStrip BuildStatus()
        {
            var st = new StatusStrip { Renderer = Theme.Renderer, BackColor = Theme.Primary, SizingGrip = true };
            _stInfo = new ToolStripStatusLabel { ForeColor = Color.White, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _stPos = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(130) };
            _stValue = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(240), TextAlign = ContentAlignment.MiddleLeft };
            _stZoom = new ToolStripStatusLabel { ForeColor = Color.White, AutoSize = false, Width = Theme.S(120) };
            st.Items.AddRange(new ToolStripItem[] { _stInfo, _stPos, _stValue, _stZoom });
            return st;
        }

        private void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _ws.Docs.Count) return;
            var doc = _ws.Docs[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.TintLight : Color.White)) e.Graphics.FillRectangle(bg, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Primary)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, Theme.S(4), e.Bounds.Height);
            int ts = Theme.S(44);
            int pad = (e.Bounds.Height - ts) / 2;
            var tr = new Rectangle(e.Bounds.X + Theme.S(8), e.Bounds.Y + pad, ts, ts);
            using (var cb = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(cb, tr);
            try { e.Graphics.DrawImage(doc.Thumbnail(ts), tr); } catch { }
            int tx = tr.Right + Theme.S(8);
            var nameRect = new Rectangle(tx, e.Bounds.Y + pad, e.Bounds.Right - tx - Theme.S(4), Theme.S(20));
            TextRenderer.DrawText(e.Graphics, (doc.Modified ? "• " : "") + doc.Name, sel ? Theme.BoldFont : Theme.UiFont, nameRect, Theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            var infoRect = new Rectangle(tx, nameRect.Bottom, nameRect.Width, Theme.S(18));
            TextRenderer.DrawText(e.Graphics, doc.Data.Description, Theme.SmallFont, infoRect, Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            using (var p = new Pen(Theme.TintLight)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        // ------------------------------------------------------------------ синхронизация

        private bool _refreshingList;

        private void RefreshList()
        {
            _refreshingList = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var d in _ws.Docs) _list.Items.Add(d.Name);
            int i = _ws.Active == null ? -1 : _ws.Docs.IndexOf(_ws.Active);
            if (i >= 0) _list.SelectedIndex = i;
            _list.EndUpdate();
            _refreshingList = false;
            _list.Invalidate();
        }

        private ImageDoc _shownDoc;

        private void OnActiveChanged()
        {
            if (_refreshingList) return;
            // запомнить вид прежнего изображения
            if (_shownDoc != null && _ws.Docs.Contains(_shownDoc))
            {
                double z, ox, oy;
                _viewer.GetView(out z, out ox, out oy);
                _shownDoc.ViewZoom = z; _shownDoc.ViewOx = ox; _shownDoc.ViewOy = oy;
            }
            var doc = _ws.Active;
            _shownDoc = doc;
            if (doc == null)
            {
                _viewer.SetImage(null, false);
                _viewer.Roi = Rectangle.Empty;
                Text = Ui.AppName;
                _stInfo.Text = "Нет открытых изображений";
            }
            else
            {
                _viewer.SetImage(doc.Display, false);
                if (doc.ViewZoom > 0) _viewer.SetView(doc.ViewZoom, doc.ViewOx, doc.ViewOy);
                _viewer.Roi = doc.Roi;
                UpdateTitle();
            }
            int i = doc == null ? -1 : _ws.Docs.IndexOf(doc);
            if (_list.SelectedIndex != i && i < _list.Items.Count) _list.SelectedIndex = i;
            SyncPoints(false);
            UpdateRoiCaption();
            UpdateMenuState();
        }

        private void OnActiveDataChanged()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            _viewer.SetImage(doc.Display, true);
            _viewer.Roi = doc.Roi;
            UpdateTitle();
            SyncPoints(false);
            UpdateRoiCaption();
            UpdateMenuState();
        }

        private void UpdateTitle()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            Text = doc.Name + (doc.Modified ? " *" : "") + " — " + Ui.AppName;
            _stInfo.Text = doc.Name + ":  " + doc.Data.Description + (doc.Data.FilePath != null ? "   " + doc.Data.FilePath : "");
        }

        private void UpdateRoiCaption()
        {
            var doc = _ws.Active;
            if (doc == null) { _roiCaption.Text = "Нет изображения"; return; }
            Rectangle roi = _viewer.Roi;
            _roiCaption.Text = (roi.IsEmpty
                ? "Изображение " + doc.Data.Width + "×" + doc.Data.Height + ", выделения нет"
                : "Выделение: x=" + roi.X + ", y=" + roi.Y + ", " + roi.Width + "×" + roi.Height) +
                "\nЦентр кадра: x=" + Ui.F((doc.Data.Width - 1) / 2.0, 1) + ", y=" + Ui.F((doc.Data.Height - 1) / 2.0, 1);
        }

        // ------------------------------------------------------------------ точки

        /// <summary>Перерисовывает точки активного изображения и таблицу; selectLast — выделить последнюю.</summary>
        private void SyncPoints(bool selectLast)
        {
            var doc = _ws.Active;
            _viewer.Points.Clear();
            _fillingPoints = true;
            _pointsGrid.Rows.Clear();
            if (doc != null)
                for (int i = 0; i < doc.Points.Count; i++)
                {
                    _viewer.Points.Add(new ViewerPoint { X = doc.Points[i].X, Y = doc.Points[i].Y, Label = (i + 1).ToString() });
                    _pointsGrid.Rows.Add(i + 1, Ui.F(doc.Points[i].X, 2), Ui.F(doc.Points[i].Y, 2));
                }
            _pointsGrid.ClearSelection();
            if (selectLast && _pointsGrid.Rows.Count > 0)
            {
                _pointsGrid.Rows[_pointsGrid.Rows.Count - 1].Selected = true;
                _pointsGrid.FirstDisplayedScrollingRowIndex = _pointsGrid.Rows.Count - 1;
            }
            _fillingPoints = false;
            MarkSelectedPoints();
            UpdatePointInfo();
        }

        private void UpdatePointRow(int i)
        {
            var doc = _ws.Active;
            if (doc == null || i >= _pointsGrid.Rows.Count) return;
            _pointsGrid.Rows[i].Cells[1].Value = Ui.F(doc.Points[i].X, 2);
            _pointsGrid.Rows[i].Cells[2].Value = Ui.F(doc.Points[i].Y, 2);
            UpdatePointInfo();
        }

        private void SelectPointRow(int i)
        {
            if (i < 0 || i >= _pointsGrid.Rows.Count) return;
            if ((ModifierKeys & Keys.Control) == 0) _pointsGrid.ClearSelection();
            _pointsGrid.Rows[i].Selected = true;
        }

        private List<int> SelectedPointIndices()
        {
            var r = new List<int>();
            foreach (DataGridViewRow row in _pointsGrid.SelectedRows) r.Add(row.Index);
            r.Sort();
            return r;
        }

        private void MarkSelectedPoints()
        {
            var sel = SelectedPointIndices();
            for (int i = 0; i < _viewer.Points.Count; i++) _viewer.Points[i].Selected = sel.Contains(i);
            _viewer.Invalidate();
        }

        private void UpdatePointInfo()
        {
            var doc = _ws.Active;
            var sel = SelectedPointIndices();
            if (doc == null || doc.Points.Count == 0) { _pointInfo.Text = "Точек нет"; return; }
            if (sel.Count == 2)
            {
                PointD a = doc.Points[sel[0]], b = doc.Points[sel[1]];
                PointD m = Coef.Mid(a, b);
                _pointInfo.Text = "|P" + (sel[0] + 1) + "P" + (sel[1] + 1) + "| = " + Ui.F(Coef.Length(a, b), 2) + " пикс.\n" +
                                  "середина: " + Ui.F(m.X, 2) + "; " + Ui.F(m.Y, 2) + "\n" +
                                  "угол: " + Ui.F(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI, 2) + "°";
            }
            else if (sel.Count == 3)
            {
                PointD a = doc.Points[sel[0]], b = doc.Points[sel[1]], c = doc.Points[sel[2]];
                double len = Coef.Length(a, b);
                double dh = len > 0 ? Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / len : double.NaN;
                _pointInfo.Text = "L = |P" + (sel[0] + 1) + "P" + (sel[1] + 1) + "| = " + Ui.F(len, 2) + " пикс.\n" +
                                  "Δh (точка " + (sel[2] + 1) + " от прямой) = " + Ui.F(dh, 2) + " пикс.\n" +
                                  "Kг = Δh / L · 100% = " + Ui.F(Coef.Geometric(dh, len), 3) + " %";
            }
            else _pointInfo.Text = "Точек: " + doc.Points.Count + ". Выделите в списке 2 или 3 точки (Ctrl+щелчок).";
        }

        private void DeleteSelectedPoints()
        {
            var doc = _ws.Active;
            if (doc == null) return;
            var sel = SelectedPointIndices();
            if (sel.Count == 0) return;
            for (int k = sel.Count - 1; k >= 0; k--) doc.Points.RemoveAt(sel[k]);
            SyncPoints(false);
        }

        private string PointsText(char sep)
        {
            var doc = _ws.Active;
            if (doc == null || doc.Points.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("№" + sep + "X" + sep + "Y");
            for (int i = 0; i < doc.Points.Count; i++)
                sb.AppendLine((i + 1) + sep.ToString() + (sep == ';' ? Csv.Num(doc.Points[i].X) : Ui.F(doc.Points[i].X, 3)) + sep +
                              (sep == ';' ? Csv.Num(doc.Points[i].Y) : Ui.F(doc.Points[i].Y, 3)));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ результаты

        public void AddDistortionResult(DistortionResult r)
        {
            _resultNo++;
            var g = r.Grid;
            Func<double, string> pc = v => Ui.F(v, 3);
            _results.Rows.Add(_resultNo, r.Title, r.Settings.Pattern == GridPattern.Checkerboard ? "шахматное поле" : "сетка",
                r.Detection.Nodes.Count + " (" + r.Detection.Cols + "×" + r.Detection.Rows + ")",
                Ui.F(r.K1, 5), Ui.F(r.H1, 5), Ui.F(r.H2, 5), Ui.F(r.SmiaTv, 3), Ui.F(r.CornerDistortion, 3),
                g != null ? pc(g.Sides.K1) : "—", g != null ? pc(g.Sides.K2) : "—", g != null ? pc(g.Sides.K3) : "—", g != null ? pc(g.Sides.K4) : "—",
                g != null ? pc(g.TrapH) : "—", g != null ? pc(g.TrapV) : "—", g != null ? pc(g.Kv) : "—", g != null ? pc(g.Kh) : "—",
                Ui.F(r.Selected.Rms, 3));
            if (_results.Rows.Count > 0) _results.FirstDisplayedScrollingRowIndex = _results.Rows.Count - 1;
        }

        private string ResultsText(char sep)
        {
            var sb = new StringBuilder();
            for (int c = 0; c < _results.Columns.Count; c++) sb.Append(c > 0 ? sep.ToString() : "").Append(_results.Columns[c].HeaderText);
            sb.AppendLine();
            foreach (DataGridViewRow row in _results.Rows)
            {
                for (int c = 0; c < _results.Columns.Count; c++)
                {
                    object v = row.Cells[c].Value;
                    string s = v == null ? "" : v.ToString();
                    sb.Append(c > 0 ? sep.ToString() : "").Append(sep == ';' ? Csv.Esc(s) : s);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void CopyResults()
        {
            if (_results.Rows.Count == 0) return;
            Clipboard.SetText(ResultsText('\t'));
        }

        private void SaveResults()
        {
            if (_results.Rows.Count == 0) { Ui.Info(this, "Таблица результатов пуста."); return; }
            Ui.SaveText(this, ResultsText(';'), "Результаты Distortion.csv");
        }

        // ------------------------------------------------------------------ общие помощники

        public Workspace Workspace { get { return _ws; } }

        private ImageDoc RequireImage()
        {
            if (_ws.Active == null)
            {
                Ui.Info(this, "Сначала откройте изображение, захватите кадр с камеры или создайте тестовую таблицу (меню «Файл»).");
                return null;
            }
            return _ws.Active;
        }

        /// <summary>Применить обработку к активному изображению (целиком).</summary>
        private void ApplyToActive(string action, Func<ImageData, ImageData> f)
        {
            var doc = RequireImage();
            if (doc == null) return;
            Ui.Busy(this, () => doc.Replace(f(doc.Data), action));
        }

        /// <summary>
        /// Показывает диалог с предпросмотром и по OK применяет обработку.
        /// Если отмечен toNew — результат открывается отдельным изображением с именем newName().
        /// </summary>
        private void RunFilterDialog(ParamDialog dlg, string action, Func<ImageData, ImageData> f, CheckBox toNew = null, Func<string> newName = null)
        {
            var doc = RequireImage();
            if (doc == null) return;
            dlg.Changed += (s, e) =>
            {
                if (!dlg.PreviewOn)
                {
                    _viewer.SetImage(doc.Display, true);
                    return;
                }
                try
                {
                    var bmp = IO.ImageIO.ToBitmap(f(doc.Data));
                    _viewer.SetImage(bmp, true);
                    if (_previewBmp != null) _previewBmp.Dispose();
                    _previewBmp = bmp;
                    dlg.ShowError(null);
                }
                catch (Exception ex) { dlg.ShowError(ex.Message); }
            };
            bool ok = dlg.ShowDialog(this) == DialogResult.OK;
            _viewer.SetImage(doc.Display, true);
            if (_previewBmp != null) { _previewBmp.Dispose(); _previewBmp = null; }
            if (!ok) return;
            if (toNew != null && toNew.Checked)
            {
                Ui.Busy(this, () =>
                {
                    ImageData r = f(doc.Data);
                    r.Name = System.IO.Path.GetFileNameWithoutExtension(doc.Name) + " — " + (newName != null ? newName() : action);
                    r.FilePath = null;
                    AddImage(r).Modified = true;
                });
            }
            else ApplyToActive(action, f);
        }

        public ImageDoc AddImage(ImageData d, bool activate = true)
        {
            return _ws.Add(d, activate);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // + и − для масштаба, когда фокус не в поле ввода
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox))
            {
                if (keyData == Keys.Oemplus || keyData == Keys.Add) { _viewer.ZoomIn(); return true; }
                if (keyData == Keys.OemMinus || keyData == Keys.Subtract) { _viewer.ZoomOut(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal void TourRefresh() { OnActiveChanged(); }
        internal void TourActivate(string name) { var d = _ws.Find(name); if (d != null) _ws.Active = d; }
        internal void TourSelectPoints(params int[] idx) { _pointsGrid.ClearSelection(); foreach (int i in idx) if (i < _pointsGrid.Rows.Count) _pointsGrid.Rows[i].Selected = true; }
        internal void TourTool(ImageViewer.ToolMode m) { SetTool(m); }

        internal void TourCommand(string name)
        {
            switch (name)
            {
                case "chart": ChartGeneratorDialog(); break;
                case "shoot": ShootDialog(); break;
                case "sphere": SphereDialog(false); break;
                case "lens": LensDistortDialog(); break;
                case "undo": Undo(); break;
            }
        }

        public void OpenAtStartup(string[] files)
        {
            if (files != null && files.Length > 0) OpenFiles(files);
        }
    }
}
