using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VideoMeterCam.Core;
using VideoMeterCam.IO;

namespace VideoMeterCam.UI
{
    /// <summary>Просмотр кадров камеры с вспомогательными линиями для установки таблицы.</summary>
    internal sealed class LivePreview : Control
    {
        public Bitmap Frame;
        public bool Cross = true, Thirds;
        public string Message = "";

        public LivePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Canvas;
        }

        public RectangleF ImageRect
        {
            get
            {
                if (Frame == null) return RectangleF.Empty;
                float k = Math.Min((float)ClientSize.Width / Frame.Width, (float)ClientSize.Height / Frame.Height);
                float w = Frame.Width * k, h = Frame.Height * k;
                return new RectangleF((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Frame == null)
            {
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(Color.FromArgb(225, 225, 240)))
                    g.DrawString(Message, Theme.UiFont, br, new RectangleF(20, 0, ClientSize.Width - 40, ClientSize.Height), sf);
                return;
            }
            RectangleF r = ImageRect;
            g.InterpolationMode = r.Width >= Frame.Width ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try { g.DrawImage(Frame, r); } catch (ArgumentException) { }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            if (Cross)
                using (var p = new Pen(Color.FromArgb(200, 255, 230, 0), 1f))
                {
                    float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    g.DrawLine(p, cx, r.Top, cx, r.Bottom);
                    g.DrawLine(p, r.Left, cy, r.Right, cy);
                    g.DrawEllipse(p, cx - 12, cy - 12, 24, 24);
                }
            if (Thirds)
                using (var p = new Pen(Color.FromArgb(150, 0, 220, 255), 1f) { DashStyle = DashStyle.Dash })
                {
                    for (int i = 1; i < 6; i++)
                    {
                        float x = r.X + r.Width * i / 6, y = r.Y + r.Height * i / 6;
                        g.DrawLine(p, x, r.Top, x, r.Bottom);
                        g.DrawLine(p, r.Left, y, r.Right, y);
                    }
                    // рамка 90% кадра — таблица должна заполнять кадр, не выходя за неё
                    g.DrawRectangle(p, r.X + r.Width * 0.05f, r.Y + r.Height * 0.05f, r.Width * 0.9f, r.Height * 0.9f);
                }
        }
    }

    /// <summary>Захват изображения с ТВ-камеры через устройство ввода (замена программы AverTV6).</summary>
    public sealed class CaptureForm : Form
    {
        private readonly MainForm _main;
        private readonly ComboBox _device, _format, _input, _ext;
        private readonly Button _props, _inputProps, _shot;
        private readonly LivePreview _preview;
        private readonly CheckBox _cross, _thirds, _autosave;
        private readonly NumBox _avg;
        private readonly TextBox _dir;
        private readonly Label _status;
        private readonly Timer _timer;
        private CaptureSession _session;
        private List<CaptureDevice> _devices = new List<CaptureDevice>();
        private List<CaptureFormat> _formats = new List<CaptureFormat>();
        private List<CaptureInput> _inputs = new List<CaptureInput>();
        private Bitmap _bmp;
        private byte[] _buf;
        private long _lastFrame;
        private DateTime _fpsTime = DateTime.Now;
        private long _fpsFrames;
        private double _fps;
        private bool _loading;
        private int _shotNo;
        // накопление кадров для усреднения
        private float[][] _acc;
        private int _accLeft, _accCount;
        private long _accLastFrame;

        public CaptureForm(MainForm main)
        {
            _main = main;
            Theme.ApplyForm(this);
            Text = "Захват изображения с камеры";
            Size = new Size(Theme.S(1100), Theme.S(760));
            MinimumSize = new Size(Theme.S(800), Theme.S(560));
            StartPosition = FormStartPosition.CenterParent;
            Theme.FitToScreen(this);
            KeyPreview = true;

            // --- верхняя панель: устройство
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(4)), BackColor = Color.White };
            Func<string, Label> lab = s => new Label { Text = s, AutoSize = true, Margin = new Padding(0, Theme.S(7), Theme.S(4), 0) };
            _device = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(300) };
            _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(230) };
            _input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(190) };
            var refresh = Theme.MakeButton("Обновить");
            refresh.Click += (s, e) => LoadDevices();
            _props = Theme.MakeButton("Свойства устройства…");
            _props.Click += (s, e) => ShowProps(false);
            _inputProps = Theme.MakeButton("Свойства входа…");
            _inputProps.Click += (s, e) => ShowProps(true);
            top.Controls.Add(lab("Устройство:"));
            top.Controls.Add(_device);
            top.Controls.Add(lab("Формат:"));
            top.Controls.Add(_format);
            top.Controls.Add(refresh);
            top.Controls.Add(_props);
            top.Controls.Add(lab("Вход:"));
            top.Controls.Add(_input);
            _cross = new CheckBox { Text = "Перекрестие в центре", Checked = true, AutoSize = true, Margin = new Padding(0, Theme.S(6), Theme.S(8), 0) };
            _thirds = new CheckBox { Text = "Вспомогательная сетка", AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
            top.Controls.Add(_cross);
            top.Controls.Add(_thirds);
            top.Controls.Add(new Label());
            top.Controls.Add(_inputProps);
            _cross.CheckedChanged += (s, e) => { _preview.Cross = _cross.Checked; _preview.Invalidate(); };
            _thirds.CheckedChanged += (s, e) => { _preview.Thirds = _thirds.Checked; _preview.Invalidate(); };

            _preview = new LivePreview { Dock = DockStyle.Fill, Message = "Поиск устройств видеозахвата…" };

            // --- нижняя панель: снимок
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 8, Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(6)), BackColor = Theme.SurfaceAlt };
            _shot = Theme.MakeButton("Снимок  (пробел)", true);
            _shot.Image = Icons.Get("snapshot");
            _shot.TextImageRelation = TextImageRelation.ImageBeforeText;
            _shot.MinimumSize = new Size(Theme.S(170), Theme.S(36));
            _shot.Click += (s, e) => Snapshot();
            _avg = new NumBox { Decimals = 0, Min = 1, Max = 64, Caption = "Усреднять кадров", Width = Theme.S(50) };
            _avg.Value = Settings.CaptureAverage;
            _autosave = new CheckBox { Text = "Сохранять снимки в папку:", AutoSize = true, Checked = Settings.CaptureAutoSave, Margin = new Padding(Theme.S(12), Theme.S(8), 0, 0) };
            _dir = new TextBox { Width = Theme.S(260), Text = Settings.CaptureDir, Margin = new Padding(0, Theme.S(6), 0, 0) };
            var browse = Theme.MakeButton("Обзор…");
            browse.MinimumSize = new Size(Theme.S(70), Theme.S(26));
            browse.Click += (s, e) => BrowseDir();
            _ext = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(70), Margin = new Padding(Theme.S(4), Theme.S(6), 0, 0) };
            _ext.Items.AddRange(new object[] { ".png", ".tif", ".bmp", ".jpg" });
            _ext.SelectedItem = Settings.CaptureExt;
            if (_ext.SelectedIndex < 0) _ext.SelectedIndex = 0;
            bottom.Controls.Add(_shot);
            bottom.Controls.Add(new Label { Text = "Усреднять кадров:", AutoSize = true, Margin = new Padding(Theme.S(12), Theme.S(10), Theme.S(4), 0) });
            bottom.Controls.Add(_avg);
            bottom.Controls.Add(_autosave);
            bottom.Controls.Add(_dir);
            bottom.Controls.Add(browse);
            bottom.Controls.Add(_ext);
            _status = new Label { Dock = DockStyle.Bottom, Height = Theme.S(24), Padding = new Padding(Theme.S(10), Theme.S(4), 0, 0), ForeColor = Theme.Primary, Font = Theme.BoldFont, BackColor = Color.White };
            var hint = new Label
            {
                Dock = DockStyle.Top, Height = Theme.S(40), Padding = new Padding(Theme.S(10), Theme.S(4), Theme.S(10), 0), BackColor = Theme.TintLight,
                Text = "Установите таблицу на заданных расстоянии и угле (рис. 3.1), сфокусируйте изображение и отрегулируйте яркость (зум и фокус — на объективе, " +
                       "яркость и контраст — «Свойства устройства…»), затем нажмите «Снимок». Снимок открывается в главном окне и, если отмечено, сохраняется в папку."
            };

            Controls.Add(_preview);
            Controls.Add(hint);
            Controls.Add(top);
            Controls.Add(_status);
            Controls.Add(bottom);

            _device.SelectedIndexChanged += (s, e) => { if (!_loading) DeviceChanged(); };
            _format.SelectedIndexChanged += (s, e) => { if (!_loading) Restart(); };
            _input.SelectedIndexChanged += (s, e) => { if (!_loading) InputChanged(); };
            _autosave.CheckedChanged += (s, e) => SaveSettings();
            _ext.SelectedIndexChanged += (s, e) => SaveSettings();
            _dir.TextChanged += (s, e) => SaveSettings();
            _avg.TextChanged += (s, e) => SaveSettings();

            _timer = new Timer { Interval = 40 };
            _timer.Tick += (s, e) => Tick();
            Shown += (s, e) => { Application.DoEvents(); LoadDevices(); _timer.Start(); };
            FormClosed += (s, e) => { _timer.Stop(); StopSession(); if (_bmp != null) _bmp.Dispose(); SaveSettings(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == Keys.Space || keyData == Keys.F5) && !(ActiveControl is TextBoxBase))
            {
                Snapshot();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveSettings()
        {
            Settings.CaptureAutoSave = _autosave.Checked;
            Settings.CaptureDir = _dir.Text.Trim();
            Settings.CaptureExt = (_ext.SelectedItem as string) ?? ".png";
            if (!double.IsNaN(_avg.Value)) Settings.CaptureAverage = Math.Max(1, Math.Min(64, _avg.IntValue));
        }

        // ------------------------------------------------------------------ устройство

        private void LoadDevices()
        {
            StopSession();
            Cursor = Cursors.WaitCursor;
            try { _devices = CaptureSession.Devices(); }
            catch (Exception ex) { _devices = new List<CaptureDevice>(); _status.Text = ex.Message; }
            finally { Cursor = Cursors.Default; }
            _loading = true;
            _device.Items.Clear();
            foreach (var d in _devices) _device.Items.Add(d);
            _loading = false;
            if (_devices.Count == 0)
            {
                _preview.Frame = null;
                _preview.Message = "Устройства видеозахвата не найдены.\n\nПодключите камеру или устройство ввода (плату видеозахвата) и нажмите «Обновить».\n" +
                                   "Снимки, сохранённые другой программой (например, AverTV), можно открыть в главном окне: Файл › Открыть.";
                _preview.Invalidate();
                UpdateButtons();
                return;
            }
            int sel = _devices.FindIndex(d => d.Name == Settings.CaptureDevice);
            _device.SelectedIndex = sel >= 0 ? sel : 0;
        }

        private void DeviceChanged()
        {
            StopSession();
            var d = _device.SelectedItem as CaptureDevice;
            if (d == null) return;
            Settings.CaptureDevice = d.Name;
            Cursor = Cursors.WaitCursor;
            try { _formats = CaptureSession.Formats(d); }
            catch { _formats = new List<CaptureFormat>(); }
            finally { Cursor = Cursors.Default; }
            _loading = true;
            _format.Items.Clear();
            _format.Items.Add("по умолчанию");
            foreach (var f in _formats) _format.Items.Add(f);
            int fi = _formats.FindIndex(f => f.ToString() == Settings.CaptureFormat);
            _format.SelectedIndex = fi >= 0 ? fi + 1 : 0;
            _loading = false;
            Restart();
        }

        private void Restart()
        {
            StopSession();
            var d = _device.SelectedItem as CaptureDevice;
            if (d == null) return;
            var f = _format.SelectedItem as CaptureFormat;
            Settings.CaptureFormat = f != null ? f.ToString() : "";
            _preview.Message = "Подключение к «" + d.Name + "»…";
            _preview.Frame = null;
            _preview.Invalidate();
            Application.DoEvents();
            Cursor = Cursors.WaitCursor;
            try
            {
                _session = CaptureSession.Open(d, f != null ? f.Index : -1);
                _status.Text = d.Name + ": " + _session.Width + "×" + _session.Height + " " + _session.SubtypeName;
                _lastFrame = 0;
                _fpsFrames = 0;
                _fpsTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _session = null;
                _preview.Message = "Не удалось подключиться к устройству «" + d.Name + "».\n\n" + ex.Message +
                                   "\n\nПроверьте, что устройство не занято другой программой (например, AverTV), выберите другой формат или нажмите «Обновить».";
                _status.Text = "";
                _preview.Invalidate();
            }
            finally { Cursor = Cursors.Default; }
            FillInputs();
            UpdateButtons();
        }

        private void FillInputs()
        {
            _loading = true;
            _input.Items.Clear();
            _inputs = new List<CaptureInput>();
            int cur = -1;
            if (_session != null)
            {
                try { _inputs = _session.Inputs(out cur); }
                catch { _inputs = new List<CaptureInput>(); }
            }
            foreach (var i in _inputs) _input.Items.Add(i);
            if (_inputs.Count == 0) { _input.Items.Add("—"); _input.SelectedIndex = 0; }
            else _input.SelectedIndex = cur >= 0 ? cur : 0;
            _input.Enabled = _inputs.Count > 1;
            _loading = false;
        }

        private void InputChanged()
        {
            if (_session == null) return;
            var inp = _input.SelectedItem as CaptureInput;
            try { _session.SelectInput(inp); }
            catch (Exception ex) { Ui.Error(this, ex.Message); }
        }

        private void ShowProps(bool crossbar)
        {
            if (_session == null) { Ui.Info(this, "Устройство не подключено."); return; }
            try
            {
                if (!_session.ShowProperties(Handle, crossbar))
                    Ui.Info(this, crossbar ? "У устройства нет окна свойств входа." : "Драйвер устройства не предоставляет окно свойств.");
            }
            catch (Exception ex) { Ui.Error(this, "Не удалось открыть окно свойств: " + ex.Message); }
        }

        private void StopSession()
        {
            if (_session == null) return;
            try { _session.Dispose(); } catch { }
            _session = null;
            _acc = null;
            _accLeft = 0;
        }

        private void UpdateButtons()
        {
            bool on = _session != null;
            _shot.Enabled = on;
            _props.Enabled = on;
            _inputProps.Enabled = on && _session.HasCrossbar;
        }

        private void BrowseDir()
        {
            using (var fb = new FolderBrowserDialog { Description = "Папка для снимков", ShowNewFolderButton = true })
            {
                string d = _dir.Text.Trim();
                if (d.Length > 0 && Directory.Exists(d)) fb.SelectedPath = d;
                else fb.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                _dir.Text = fb.SelectedPath;
                _autosave.Checked = true;
            }
        }

        // ------------------------------------------------------------------ кадры

        private void Tick()
        {
            var s = _session;
            if (s == null) return;
            long n = s.FrameNumber;
            if (n == _lastFrame) return;
            _lastFrame = n;
            _fpsFrames++;
            double dt = (DateTime.Now - _fpsTime).TotalSeconds;
            if (dt >= 1)
            {
                _fps = _fpsFrames / dt;
                _fpsFrames = 0;
                _fpsTime = DateTime.Now;
            }
            if (_bmp == null || _bmp.Width != s.Width || _bmp.Height != s.Height)
            {
                if (_bmp != null) _bmp.Dispose();
                _bmp = new Bitmap(s.Width, s.Height, PixelFormat.Format24bppRgb);
                _buf = null;
            }
            var data = _bmp.LockBits(new Rectangle(0, 0, s.Width, s.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                if (_buf == null || _buf.Length != stride * s.Height) _buf = new byte[stride * s.Height];
                if (s.CopyBgr24(_buf, stride)) Marshal.Copy(_buf, 0, data.Scan0, _buf.Length);
            }
            finally { _bmp.UnlockBits(data); }
            _preview.Frame = _bmp;
            _preview.Invalidate();
            if (_accLeft > 0 && n != _accLastFrame) Accumulate(n);
            else if (_accLeft == 0)
                _status.Text = s.Device.Name + ": " + s.Width + "×" + s.Height + " " + s.SubtypeName + ",  " + _fps.ToString("0.0") + " к/с" +
                               (_shotNo > 0 ? "   ·   снимков: " + _shotNo : "");
        }

        internal ImageData TourFrame() { return _session != null ? _session.TakeFrame() : null; }
        internal bool TourHasDevice { get { return _session != null; } }

        private void Snapshot()
        {
            if (_session == null) return;
            if (_accLeft > 0) return;
            int n = double.IsNaN(_avg.Value) ? 1 : Math.Max(1, Math.Min(64, _avg.IntValue));
            if (n == 1)
            {
                var img = _session.TakeFrame();
                if (img == null) { _status.Text = "Кадров от устройства ещё не было."; return; }
                Finish(img, 1);
                return;
            }
            _acc = null;
            _accLeft = n;
            _accCount = 0;
            _accLastFrame = -1;
            _shot.Enabled = false;
        }

        private void Accumulate(long frame)
        {
            var img = _session.TakeFrame();
            if (img == null) return;
            _accLastFrame = frame;
            if (_acc == null)
            {
                _acc = new float[3][];
                for (int c = 0; c < 3; c++) _acc[c] = new float[img.PixelCount];
            }
            if (_acc[0].Length != img.PixelCount) { _accLeft = 0; _shot.Enabled = true; return; }
            for (int c = 0; c < 3; c++)
            {
                float[] a = _acc[c], p = img.Planes[c];
                for (int i = 0; i < a.Length; i++) a[i] += p[i];
            }
            _accCount++;
            _accLeft--;
            _status.Text = "Усреднение кадров: " + _accCount + " из " + (_accCount + _accLeft) + "…";
            if (_accLeft > 0) return;
            var res = new ImageData(img.Width, img.Height, PixelType.Rgb24);
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < res.PixelCount; i++) res.Planes[c][i] = _acc[c][i] / _accCount;
            res.Conform();
            _acc = null;
            _shot.Enabled = true;
            Finish(res, _accCount);
        }

        private void Finish(ImageData img, int frames)
        {
            _shotNo++;
            string stamp = DateTime.Now.ToString("HH-mm-ss");
            img.Name = "Снимок " + _shotNo.ToString("000") + " " + stamp + (frames > 1 ? " (" + frames + " кадров)" : "");
            string saved = null;
            if (_autosave.Checked)
            {
                string dir = _dir.Text.Trim();
                if (dir.Length == 0 || !Directory.Exists(dir))
                {
                    Ui.Info(this, "Выберите существующую папку для снимков (кнопка «Обзор…»).");
                }
                else
                {
                    string ext = (_ext.SelectedItem as string) ?? ".png";
                    string path;
                    int k = _shotNo;
                    do { path = Path.Combine(dir, "Снимок_" + DateTime.Now.ToString("yyyyMMdd") + "_" + k.ToString("000") + ext); k++; }
                    while (File.Exists(path));
                    try
                    {
                        ImageIO.Save(img, path);
                        img.FilePath = path;
                        img.Name = Path.GetFileName(path);
                        saved = path;
                    }
                    catch (Exception ex) { Ui.Error(this, "Не удалось сохранить снимок: " + ex.Message); }
                }
            }
            var doc = _main.AddImage(img);
            doc.Modified = saved == null;
            _status.Text = "Снимок " + _shotNo + (frames > 1 ? " (усреднение " + frames + " кадров)" : "") + (saved != null ? " сохранён: " + saved : " открыт в главном окне (не сохранён)");
        }
    }
}
