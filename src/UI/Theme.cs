using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SigmaLabCam.UI
{
    /// <summary>Оформление в фирменных цветах ТУСУР (основной — PANTONE 2104 C, #3C388D).</summary>
    public static class Theme
    {
        public static readonly Color Primary = Color.FromArgb(0x3C, 0x38, 0x8D);
        public static readonly Color PrimaryDark = Color.FromArgb(0x2B, 0x28, 0x6A);
        public static readonly Color PrimaryLight = Color.FromArgb(0x9E, 0x9C, 0xC6);
        public static readonly Color Tint = Color.FromArgb(0xC2, 0xC1, 0xDB);
        public static readonly Color TintLight = Color.FromArgb(0xE1, 0xE0, 0xED);
        public static readonly Color Surface = Color.FromArgb(0xEC, 0xEC, 0xF4);
        public static readonly Color SurfaceAlt = Color.FromArgb(0xF6, 0xF6, 0xFA);
        public static readonly Color Accent = Color.FromArgb(0x9A, 0xBF, 0x38);
        public static readonly Color AccentDark = Color.FromArgb(0x7A, 0x9A, 0x26);
        public static readonly Color Text = Color.FromArgb(0x2B, 0x2B, 0x33);
        public static readonly Color Muted = Color.FromArgb(0x6B, 0x6A, 0x80);
        public static readonly Color Canvas = Color.FromArgb(0x5A, 0x59, 0x6B);
        public static readonly Color RoiColor = Color.FromArgb(255, 230, 0);
        public static readonly Color DarkRoi = Color.FromArgb(0x00, 0xB2, 0xF2);
        public static readonly Color LightRoi = Color.FromArgb(0xFF, 0x76, 0x32);

        private static Font _ui, _bold, _small, _header, _mono, _title;
        private static float _scale = -1;

        public static Font UiFont { get { return _ui ?? (_ui = MakeFont(9f, FontStyle.Regular)); } }
        public static Font BoldFont { get { return _bold ?? (_bold = MakeFont(9f, FontStyle.Bold)); } }
        public static Font SmallFont { get { return _small ?? (_small = MakeFont(8f, FontStyle.Regular)); } }
        public static Font HeaderFont { get { return _header ?? (_header = MakeFont(9.75f, FontStyle.Bold)); } }
        public static Font TitleFont { get { return _title ?? (_title = MakeFont(16f, FontStyle.Bold)); } }
        public static Font MonoFont
        {
            get
            {
                return _mono ?? (_mono = SafeFont(MonoNames, 10f, FontStyle.Regular));
            }
        }

        /// <summary>Шрифт с запасными вариантами: на системе без нужного шрифта не будет исключения.</summary>
        public static Font SafeFont(string[] names, float size, FontStyle style, GraphicsUnit unit = GraphicsUnit.Point)
        {
            foreach (string name in names)
            {
                try
                {
                    var f = new Font(name, size, style, unit);
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            try { return new Font(FontFamily.GenericSansSerif, size, style, unit); }
            catch { return new Font(SystemFonts.DefaultFont.FontFamily, size, style, unit); }
        }

        public static readonly string[] SerifNames = { "Georgia", "Cambria", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
        public static readonly string[] SansNames = { "Arial", "Segoe UI", "Tahoma", "Liberation Sans", "DejaVu Sans" };
        public static readonly string[] MonoNames = { "Consolas", "Lucida Console", "Courier New", "Liberation Mono", "DejaVu Sans Mono" };

        private static Font MakeFont(float size, FontStyle style)
        {
            foreach (string name in new[] { "Segoe UI", "Tahoma", "Verdana" })
            {
                try
                {
                    var f = new Font(name, size, style);
                    if (f.Name == name) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style);
        }

        /// <summary>Коэффициент масштабирования экрана (1.0 = 96 dpi).</summary>
        public static float Scale
        {
            get
            {
                if (_scale < 0)
                {
                    try
                    {
                        using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                    }
                    catch { _scale = 1; }
                    if (_scale <= 0) _scale = 1;
                }
                return _scale;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }
        public static float S(float px) { return px * Scale; }

        public static void ApplyForm(Form f)
        {
            f.Font = UiFont;
            f.BackColor = Surface;
            f.ForeColor = Text;
            f.AutoScaleMode = AutoScaleMode.None;
            if (AppIcon != null) f.Icon = AppIcon;
        }

        public static Icon AppIcon;

        /// <summary>Ограничивает размер окна рабочей областью экрана (важно для ноутбуков и масштаба 125–150%).</summary>
        public static bool FitToScreen(Form f)
        {
            Rectangle wa;
            try { wa = Screen.PrimaryScreen.WorkingArea; }
            catch { return false; }
            int w = Math.Min(f.Width, (int)(wa.Width * 0.96)), h = Math.Min(f.Height, (int)(wa.Height * 0.96));
            bool clipped = w < f.Width || h < f.Height;
            if (f.MinimumSize.Width > w || f.MinimumSize.Height > h)
                f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, w), Math.Min(f.MinimumSize.Height, h));
            f.Size = new Size(w, h);
            return clipped;
        }

        public static Button MakeButton(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(S(88), S(28)),
                Padding = new Padding(S(6), 0, S(6), 0),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Margin = new Padding(S(3)),
                Cursor = Cursors.Hand
            };
            StyleButton(b, primary);
            return b;
        }

        public static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            if (primary)
            {
                b.BackColor = Primary;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = PrimaryDark;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x4C, 0x48, 0xA8);
                b.FlatAppearance.MouseDownBackColor = PrimaryDark;
                b.Font = BoldFont;
            }
            else
            {
                b.BackColor = Color.White;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = PrimaryLight;
                b.FlatAppearance.MouseOverBackColor = TintLight;
                b.FlatAppearance.MouseDownBackColor = Tint;
            }
        }

        /// <summary>Заголовок панели: полоса фирменного цвета с белым текстом.</summary>
        public static Label MakeHeader(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = S(26),
                BackColor = Primary,
                ForeColor = Color.White,
                Font = HeaderFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(S(8), 0, 0, 0)
            };
        }

        public static Label MakeLabel(string text, bool bold = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = bold ? BoldFont : UiFont,
                ForeColor = Text,
                Margin = new Padding(S(3), S(6), S(3), S(3))
            };
        }

        public static ToolStripRenderer Renderer { get { return new ToolStripProfessionalRenderer(new Colors()) { RoundedEdges = false }; } }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin { get { return Color.White; } }
            public override Color MenuStripGradientEnd { get { return Color.White; } }
            public override Color ToolStripGradientBegin { get { return SurfaceAlt; } }
            public override Color ToolStripGradientMiddle { get { return SurfaceAlt; } }
            public override Color ToolStripGradientEnd { get { return Surface; } }
            public override Color ToolStripBorder { get { return Tint; } }
            public override Color MenuItemSelected { get { return TintLight; } }
            public override Color MenuItemSelectedGradientBegin { get { return TintLight; } }
            public override Color MenuItemSelectedGradientEnd { get { return TintLight; } }
            public override Color MenuItemBorder { get { return PrimaryLight; } }
            public override Color MenuItemPressedGradientBegin { get { return Tint; } }
            public override Color MenuItemPressedGradientMiddle { get { return Tint; } }
            public override Color MenuItemPressedGradientEnd { get { return Tint; } }
            public override Color MenuBorder { get { return PrimaryLight; } }
            public override Color ToolStripDropDownBackground { get { return Color.White; } }
            public override Color ImageMarginGradientBegin { get { return SurfaceAlt; } }
            public override Color ImageMarginGradientMiddle { get { return SurfaceAlt; } }
            public override Color ImageMarginGradientEnd { get { return SurfaceAlt; } }
            public override Color ButtonSelectedHighlight { get { return TintLight; } }
            public override Color ButtonSelectedGradientBegin { get { return TintLight; } }
            public override Color ButtonSelectedGradientMiddle { get { return TintLight; } }
            public override Color ButtonSelectedGradientEnd { get { return TintLight; } }
            public override Color ButtonSelectedBorder { get { return PrimaryLight; } }
            public override Color ButtonPressedGradientBegin { get { return Tint; } }
            public override Color ButtonPressedGradientMiddle { get { return Tint; } }
            public override Color ButtonPressedGradientEnd { get { return Tint; } }
            public override Color ButtonCheckedGradientBegin { get { return Tint; } }
            public override Color ButtonCheckedGradientMiddle { get { return Tint; } }
            public override Color ButtonCheckedGradientEnd { get { return Tint; } }
            public override Color ButtonCheckedHighlight { get { return Tint; } }
            public override Color ButtonCheckedHighlightBorder { get { return Primary; } }
            public override Color SeparatorDark { get { return Tint; } }
            public override Color SeparatorLight { get { return Color.White; } }
            public override Color StatusStripGradientBegin { get { return Primary; } }
            public override Color StatusStripGradientEnd { get { return Primary; } }
            public override Color GripDark { get { return Tint; } }
            public override Color GripLight { get { return Color.White; } }
        }

        /// <summary>
        /// Логотип: сетка с бочкообразной дисторсией на фирменном фоне и «точечная волна» (мотив айдентики ТУСУР).
        /// Тот же рисунок — в значке программы (tools/make_icon.py).
        /// </summary>
        public static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = size * 0.18f;
                using (var path = RoundRect(new RectangleF(0, 0, size - 1, size - 1), r))
                using (var br = new SolidBrush(Primary))
                    g.FillPath(br, path);
                using (var pen = new Pen(Color.White, Math.Max(1f, size * 0.045f)) { LineJoin = LineJoin.Round })
                {
                    foreach (PointF[] line in BarrelGrid(size))
                        g.DrawLines(pen, line);
                }
                using (var br = new SolidBrush(Accent))
                {
                    int n = 9;
                    for (int i = 0; i < n; i++)
                    {
                        float x = size * (0.14f + 0.72f * i / (n - 1));
                        float y = size * (0.82f + 0.05f * (float)Math.Sin(i * 0.95));
                        float d = size * (0.045f + 0.02f * (float)Math.Abs(Math.Cos(i * 0.6)));
                        g.FillEllipse(br, x - d / 2, y - d / 2, d, d);
                    }
                }
            }
            return bmp;
        }

        /// <summary>Линии сетки 4×4 клетки с бочкообразной дисторсией в верхней части логотипа.</summary>
        private static List<PointF[]> BarrelGrid(int size)
        {
            var lines = new List<PointF[]>();
            float cx = size * 0.5f, cy = size * 0.40f, half = size * 0.32f;
            const int N = 4, S = 24;
            Func<float, float, PointF> map = (u, v) =>
            {
                // u, v ∈ [−1; 1]; бочка: r_d = r_u / (1 + k·r_u²)
                float r2 = (u * u + v * v) / 2f, k = 1f / (1 + 0.42f * r2);
                return new PointF(cx + u * half * k, cy + v * half * k);
            };
            for (int i = 0; i <= N; i++)
            {
                float t = -1 + 2f * i / N;
                var a = new PointF[S + 1];
                var b = new PointF[S + 1];
                for (int s = 0; s <= S; s++)
                {
                    float q = -1 + 2f * s / S;
                    a[s] = map(t, q);
                    b[s] = map(q, t);
                }
                lines.Add(a);
                lines.Add(b);
            }
            return lines;
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
