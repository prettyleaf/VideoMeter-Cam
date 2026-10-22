using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace SigmaLabCam.UI
{
    public enum Marker { None, Circle, Square, Diamond, Triangle }

    /// <summary>Угол области графика для легенды и примечаний. Auto — угол, где меньше всего данных.</summary>
    public enum Corner { Auto, TopLeft, TopRight, BottomLeft, BottomRight }

    public sealed class Series
    {
        public string Name;
        public double[] X, Y;
        public Color Color = Theme.Primary;
        public float Width = 1.6f;
        public Marker Marker = Marker.None;
        public DashStyle Dash = DashStyle.Solid;
        public bool Bars;            // столбцы (гистограмма)
        public bool ShowInLegend = true;

        public Series(string name, double[] x, double[] y) { Name = name; X = x; Y = y; }
    }

    public sealed class RefLine
    {
        public double Value;
        public bool Horizontal = true;
        public Color Color = Color.Gray;
        public string Label;
        public DashStyle Dash = DashStyle.Dash;
    }

    /// <summary>Двумерный график с осями, сеткой, легендой и подписями.</summary>
    public sealed class Plot
    {
        public string Title, XLabel, YLabel;
        public readonly List<Series> Series = new List<Series>();
        public readonly List<RefLine> Lines = new List<RefLine>();
        public readonly List<string> Notes = new List<string>();
        public double? XMin, XMax, YMin, YMax;
        public bool LogY;
        public string[] XCategories;     // подписи по оси X в позициях 0..n−1
        public bool ShowLegend = true;
        public Corner LegendCorner = Corner.Auto;
        public Corner NotesCorner = Corner.Auto;
        public Color Background = Color.White;
        public string XFormat, YFormat;

        public static readonly Color[] Palette =
        {
            Theme.Primary, Color.FromArgb(0xE0, 0x6A, 0x1E), Theme.AccentDark, Color.FromArgb(0xC0, 0x30, 0x60),
            Color.FromArgb(0x00, 0x8C, 0xC8), Color.FromArgb(0x6B, 0x4C, 0x2A), Color.FromArgb(0x7A, 0x7A, 0x7A)
        };

        public Series Add(string name, double[] x, double[] y, Color color, Marker marker = Marker.None)
        {
            var s = new Series(name, x, y) { Color = color, Marker = marker };
            Series.Add(s);
            return s;
        }

        // ------------------------------------------------------------------ отрисовка

        public void Draw(Graphics g, RectangleF bounds, float fontScale = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var bg = new SolidBrush(Background)) g.FillRectangle(bg, bounds);

            using (var fTitle = new Font(Theme.BoldFont.FontFamily, 9.5f * fontScale, FontStyle.Bold))
            using (var fAxis = new Font(Theme.UiFont.FontFamily, 8.5f * fontScale))
            using (var fTick = new Font(Theme.UiFont.FontFamily, 8f * fontScale))
            using (var fNote = new Font(Theme.UiFont.FontFamily, 8f * fontScale))
            {
                double xmin, xmax, ymin, ymax;
                ComputeRange(out xmin, out xmax, out ymin, out ymax);

                float titleH = string.IsNullOrEmpty(Title) ? 4 : g.MeasureString(Title, fTitle, (int)bounds.Width).Height + 4;
                float tickH = g.MeasureString("0", fTick).Height;
                float xlabH = string.IsNullOrEmpty(XLabel) ? 0 : g.MeasureString(XLabel, fAxis).Height;

                List<double> yt = LogY ? LogTicks(ymin, ymax) : NiceTicks(ymin, ymax, Math.Max(3, (int)(bounds.Height / (tickH * 3.2f))));
                float ytw = 0;
                foreach (double t in yt) ytw = Math.Max(ytw, g.MeasureString(Fmt(t, yt, YFormat), fTick).Width);
                float ylabW = string.IsNullOrEmpty(YLabel) ? 0 : g.MeasureString("X", fAxis).Height;

                var area = new RectangleF(bounds.X + ylabW + ytw + 8, bounds.Y + titleH, 0, 0);
                area.Width = bounds.Right - area.X - 12;
                area.Height = bounds.Bottom - area.Y - tickH - xlabH - 8;
                if (area.Width < 20 || area.Height < 20) return;

                if (!string.IsNullOrEmpty(Title))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    using (var br = new SolidBrush(Theme.Text))
                        g.DrawString(Title, fTitle, br, new RectangleF(bounds.X, bounds.Y + 2, bounds.Width, titleH), sf);

                Func<double, float> X = v => (float)(area.Left + (v - xmin) / (xmax - xmin) * area.Width);
                Func<double, float> Y = v =>
                {
                    double t = LogY ? (Math.Log10(Math.Max(v, 1e-12)) - Math.Log10(ymin)) / (Math.Log10(ymax) - Math.Log10(ymin))
                                    : (v - ymin) / (ymax - ymin);
                    return (float)(area.Bottom - t * area.Height);
                };

                // сетка и подписи
                using (var grid = new Pen(Color.FromArgb(225, 225, 235)))
                using (var tickBr = new SolidBrush(Theme.Muted))
                {
                    foreach (double t in yt)
                    {
                        float y = Y(t);
                        if (y < area.Top - 1 || y > area.Bottom + 1) continue;
                        g.DrawLine(grid, area.Left, y, area.Right, y);
                        string s = Fmt(t, yt, YFormat);
                        SizeF sz = g.MeasureString(s, fTick);
                        g.DrawString(s, fTick, tickBr, area.Left - sz.Width - 3, y - sz.Height / 2);
                    }
                    if (XCategories != null)
                    {
                        for (int i = 0; i < XCategories.Length; i++)
                        {
                            float x = X(i);
                            g.DrawLine(grid, x, area.Top, x, area.Bottom);
                            SizeF sz = g.MeasureString(XCategories[i], fTick);
                            g.DrawString(XCategories[i], fTick, tickBr, x - sz.Width / 2, area.Bottom + 3);
                        }
                    }
                    else
                    {
                        List<double> xt = NiceTicks(xmin, xmax, Math.Max(3, (int)(area.Width / (g.MeasureString("00000", fTick).Width * 1.4f))));
                        foreach (double t in xt)
                        {
                            float x = X(t);
                            if (x < area.Left - 1 || x > area.Right + 1) continue;
                            g.DrawLine(grid, x, area.Top, x, area.Bottom);
                            string s = Fmt(t, xt, XFormat);
                            SizeF sz = g.MeasureString(s, fTick);
                            g.DrawString(s, fTick, tickBr, x - sz.Width / 2, area.Bottom + 3);
                        }
                    }
                }
                using (var axis = new Pen(Color.FromArgb(150, 150, 170)))
                    g.DrawRectangle(axis, area.X, area.Y, area.Width, area.Height);

                using (var lb = new SolidBrush(Theme.Text))
                {
                    if (!string.IsNullOrEmpty(XLabel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                            g.DrawString(XLabel, fAxis, lb, new RectangleF(area.Left, bounds.Bottom - xlabH - 2, area.Width, xlabH + 2), sf);
                    if (!string.IsNullOrEmpty(YLabel))
                    {
                        var st = g.Save();
                        g.TranslateTransform(bounds.X + 1, area.Top + area.Height / 2);
                        g.RotateTransform(-90);
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                            g.DrawString(YLabel, fAxis, lb, new RectangleF(-area.Height / 2, 0, area.Height, ylabW + 2), sf);
                        g.Restore(st);
                    }
                }

                var clip = g.Clip;
                g.SetClip(area);
                // опорные линии
                foreach (var l in Lines)
                {
                    using (var p = new Pen(l.Color, 1.2f) { DashStyle = l.Dash })
                    {
                        if (l.Horizontal)
                        {
                            float y = Y(l.Value);
                            g.DrawLine(p, area.Left, y, area.Right, y);
                            if (!string.IsNullOrEmpty(l.Label))
                                using (var br = new SolidBrush(l.Color))
                                    g.DrawString(l.Label, fNote, br, area.Left + 3, y - g.MeasureString(l.Label, fNote).Height);
                        }
                        else
                        {
                            float x = X(l.Value);
                            g.DrawLine(p, x, area.Top, x, area.Bottom);
                            if (!string.IsNullOrEmpty(l.Label))
                                using (var br = new SolidBrush(l.Color))
                                    g.DrawString(l.Label, fNote, br, x + 2, area.Top + 2);
                        }
                    }
                }
                // ряды
                foreach (var s in Series) DrawSeries(g, s, X, Y, area, xmin, xmax);
                g.Clip = clip;

                // примечания и легенда — в углы, где меньше всего данных
                List<PointF> samples = SamplePoints(X, Y, area);
                Corner notesAt = Corner.Auto;
                if (Notes.Count > 0)
                {
                    float maxW = 0, h = 0;
                    foreach (string n in Notes)
                    {
                        SizeF sz = g.MeasureString(n, fNote);
                        maxW = Math.Max(maxW, sz.Width);
                        h += sz.Height;
                    }
                    notesAt = NotesCorner != Corner.Auto ? NotesCorner
                        : BestCorner(area, maxW + 6, h + 4, samples, Corner.Auto, new[] { Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft });
                    RectangleF nb = CornerRect(area, maxW + 6, h + 4, notesAt);
                    using (var bg = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
                        g.FillRectangle(bg, nb);
                    float y = nb.Top + 2;
                    using (var br = new SolidBrush(Theme.Text))
                        foreach (string n in Notes)
                        {
                            g.DrawString(n, fNote, br, nb.Left + 3, y);
                            y += g.MeasureString(n, fNote).Height;
                        }
                }

                // легенда
                if (ShowLegend)
                {
                    var items = Series.FindAll(s => s.ShowInLegend && !string.IsNullOrEmpty(s.Name));
                    if (items.Count > 0)
                    {
                        float lh = g.MeasureString("Ag", fNote).Height;
                        float lw = 0;
                        foreach (var s in items) lw = Math.Max(lw, g.MeasureString(s.Name, fNote).Width);
                        float bw = lw + 34, bh = items.Count * lh + 6;
                        Corner legAt = LegendCorner != Corner.Auto ? LegendCorner
                            : BestCorner(area, bw, bh, samples, notesAt, new[] { Corner.TopRight, Corner.BottomRight, Corner.TopLeft, Corner.BottomLeft });
                        RectangleF lr = CornerRect(area, bw, bh, legAt);
                        float bx = lr.X, by = lr.Y;
                        using (var bg = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                            g.FillRectangle(bg, bx, by, bw, bh);
                        using (var bp = new Pen(Theme.Tint)) g.DrawRectangle(bp, bx, by, bw, bh);
                        float yy = by + 3;
                        using (var tb = new SolidBrush(Theme.Text))
                            foreach (var s in items)
                            {
                                using (var p = new Pen(s.Color, 2f) { DashStyle = s.Dash })
                                {
                                    if (s.Bars) using (var b = new SolidBrush(s.Color)) g.FillRectangle(b, bx + 6, yy + lh / 2 - 4, 20, 8);
                                    else g.DrawLine(p, bx + 5, yy + lh / 2, bx + 27, yy + lh / 2);
                                }
                                if (s.Marker != Marker.None) DrawMarker(g, s.Marker, s.Color, bx + 16, yy + lh / 2, 3.2f);
                                g.DrawString(s.Name, fNote, tb, bx + 30, yy);
                                yy += lh;
                            }
                    }
                }
            }
        }

        private static RectangleF CornerRect(RectangleF area, float w, float h, Corner c)
        {
            float x = c == Corner.TopLeft || c == Corner.BottomLeft ? area.Left + 6 : area.Right - w - 6;
            float y = c == Corner.TopLeft || c == Corner.TopRight ? area.Top + 6 : area.Bottom - h - 6;
            return new RectangleF(x, y, w, h);
        }

        private static Corner BestCorner(RectangleF area, float w, float h, List<PointF> pts, Corner taken, Corner[] order)
        {
            Corner best = order[0];
            int bestCount = int.MaxValue;
            foreach (Corner c in order)
            {
                if (c == taken) continue;
                RectangleF r = CornerRect(area, w, h, c);
                r.Inflate(4, 4);
                int n = 0;
                foreach (PointF p in pts) if (r.Contains(p)) n++;
                if (n < bestCount) { bestCount = n; best = c; }
            }
            return best;
        }

        /// <summary>Точки рядов (с промежуточными точками на отрезках) в экранных координатах — для выбора места легенды.</summary>
        private List<PointF> SamplePoints(Func<double, float> X, Func<double, float> Y, RectangleF area)
        {
            var pts = new List<PointF>();
            foreach (var s in Series)
            {
                if (s.X == null || s.Y == null) continue;
                int n = Math.Min(s.X.Length, s.Y.Length);
                PointF? prev = null;
                for (int i = 0; i < n; i++)
                {
                    if (double.IsNaN(s.Y[i]) || double.IsInfinity(s.Y[i]) || (LogY && s.Y[i] <= 0)) { prev = null; continue; }
                    var p = new PointF(X(s.X[i]), s.Bars ? area.Bottom - 1 : Y(s.Y[i]));
                    if (s.Bars)
                    {
                        // столбец занимает высоту от оси до значения
                        float top = Y(s.Y[i]);
                        for (int k = 0; k <= 6; k++) pts.Add(new PointF(p.X, area.Bottom - (area.Bottom - top) * k / 6f));
                        continue;
                    }
                    if (prev.HasValue)
                        for (int k = 1; k < 10; k++)
                            pts.Add(new PointF(prev.Value.X + (p.X - prev.Value.X) * k / 10f, prev.Value.Y + (p.Y - prev.Value.Y) * k / 10f));
                    pts.Add(p);
                    prev = p;
                }
            }
            return pts;
        }

        private void DrawSeries(Graphics g, Series s, Func<double, float> X, Func<double, float> Y, RectangleF area, double xmin, double xmax)
        {
            if (s.X == null || s.Y == null) return;
            int n = Math.Min(s.X.Length, s.Y.Length);
            if (s.Bars)
            {
                double bw = n > 1 ? (s.X[n - 1] - s.X[0]) / (n - 1) : 1;
                using (var b = new SolidBrush(s.Color))
                    for (int i = 0; i < n; i++)
                    {
                        if (double.IsNaN(s.Y[i]) || s.Y[i] <= 0) continue;
                        float x0 = X(s.X[i] - bw / 2), x1 = X(s.X[i] + bw / 2);
                        float y = Y(s.Y[i]);
                        float w = Math.Max(1f, x1 - x0);
                        g.FillRectangle(b, x0, y, w, area.Bottom - y);
                    }
                return;
            }
            var pts = new List<PointF>();
            using (var p = new Pen(s.Color, s.Width) { DashStyle = s.Dash, LineJoin = LineJoin.Round })
            {
                for (int i = 0; i <= n; i++)
                {
                    bool ok = i < n && !double.IsNaN(s.X[i]) && !double.IsNaN(s.Y[i]) && !double.IsInfinity(s.Y[i]) && !(LogY && s.Y[i] <= 0);
                    if (ok) pts.Add(new PointF(X(s.X[i]), Clamp(Y(s.Y[i]), area)));
                    if (!ok || i == n)
                    {
                        if (pts.Count > 1 && s.Width > 0) g.DrawLines(p, pts.ToArray());
                        if (s.Marker != Marker.None) foreach (var pt in pts) DrawMarker(g, s.Marker, s.Color, pt.X, pt.Y, 3.4f);
                        pts.Clear();
                    }
                }
            }
        }

        private static float Clamp(float y, RectangleF a) { return Math.Max(a.Top - 2000, Math.Min(a.Bottom + 2000, y)); }

        private static void DrawMarker(Graphics g, Marker m, Color c, float x, float y, float r)
        {
            using (var b = new SolidBrush(Color.White))
            using (var p = new Pen(c, 1.5f))
            {
                switch (m)
                {
                    case Marker.Circle:
                        g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
                        g.DrawEllipse(p, x - r, y - r, 2 * r, 2 * r);
                        break;
                    case Marker.Square:
                        g.FillRectangle(b, x - r, y - r, 2 * r, 2 * r);
                        g.DrawRectangle(p, x - r, y - r, 2 * r, 2 * r);
                        break;
                    case Marker.Diamond:
                        var d = new[] { new PointF(x, y - r * 1.3f), new PointF(x + r * 1.3f, y), new PointF(x, y + r * 1.3f), new PointF(x - r * 1.3f, y) };
                        g.FillPolygon(b, d);
                        g.DrawPolygon(p, d);
                        break;
                    case Marker.Triangle:
                        var t = new[] { new PointF(x, y - r * 1.3f), new PointF(x + r * 1.2f, y + r), new PointF(x - r * 1.2f, y + r) };
                        g.FillPolygon(b, t);
                        g.DrawPolygon(p, t);
                        break;
                }
            }
        }

        private void ComputeRange(out double xmin, out double xmax, out double ymin, out double ymax)
        {
            xmin = ymin = double.MaxValue;
            xmax = ymax = double.MinValue;
            foreach (var s in Series)
            {
                if (s.X == null || s.Y == null) continue;
                int n = Math.Min(s.X.Length, s.Y.Length);
                for (int i = 0; i < n; i++)
                {
                    double x = s.X[i], y = s.Y[i];
                    if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                    xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x);
                    if (double.IsNaN(y) || double.IsInfinity(y) || (LogY && y <= 0)) continue;
                    ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
                }
                if (s.Bars) ymin = Math.Min(ymin, 0);
            }
            foreach (var l in Lines)
            {
                if (l.Horizontal) { ymin = Math.Min(ymin, l.Value); ymax = Math.Max(ymax, l.Value); }
            }
            if (XCategories != null) { xmin = -0.5; xmax = XCategories.Length - 0.5; }
            if (xmin > xmax) { xmin = 0; xmax = 1; }
            if (ymin > ymax) { ymin = 0; ymax = 1; }
            if (XCategories == null && !XMin.HasValue && !XMax.HasValue && !Series.Exists(s => s.Bars) && xmax > xmin)
            {
                double px = (xmax - xmin) * 0.03;
                xmin -= px;
                xmax += px;
            }
            if (XMin.HasValue) xmin = XMin.Value;
            if (XMax.HasValue) xmax = XMax.Value;
            if (xmax <= xmin) { xmax = xmin + 1; xmin -= 1; }
            if (LogY)
            {
                if (ymin <= 0) ymin = 1e-3;
                if (ymax <= ymin) ymax = ymin * 10;
                ymin = Math.Pow(10, Math.Floor(Math.Log10(ymin) * 2) / 2);
                ymax = Math.Pow(10, Math.Ceiling(Math.Log10(ymax) * 2) / 2);
            }
            else
            {
                double pad = (ymax - ymin) * 0.06;
                if (pad == 0) pad = Math.Abs(ymax) * 0.1 + 1;
                bool zeroBase = ymin >= 0 && Series.Exists(s => s.Bars);
                ymin = zeroBase ? 0 : ymin - pad;
                ymax += pad;
            }
            if (YMin.HasValue) ymin = YMin.Value;
            if (YMax.HasValue) ymax = YMax.Value;
            if (ymax <= ymin) ymax = ymin + 1;
        }

        public static List<double> NiceTicks(double min, double max, int target)
        {
            var r = new List<double>();
            double range = max - min;
            if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range)) { r.Add(min); return r; }
            double rough = range / Math.Max(2, target);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double norm = rough / mag;
            double step = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
            step *= mag;
            double start = Math.Ceiling(min / step) * step;
            for (double v = start; v <= max + step * 1e-9; v += step)
            {
                r.Add(Math.Abs(v) < step * 1e-9 ? 0 : v);
                if (r.Count > 200) break;
            }
            return r;
        }

        private static List<double> LogTicks(double min, double max)
        {
            var r = new List<double>();
            int e0 = (int)Math.Floor(Math.Log10(min)), e1 = (int)Math.Ceiling(Math.Log10(max));
            bool dense = e1 - e0 <= 3;
            for (int e = e0; e <= e1; e++)
                foreach (double m in dense ? new[] { 1.0, 2.0, 5.0 } : new[] { 1.0 })
                {
                    double v = m * Math.Pow(10, e);
                    if (v >= min * 0.999 && v <= max * 1.001) r.Add(v);
                }
            return r;
        }

        private static string Fmt(double v, List<double> ticks, string fmt)
        {
            if (!string.IsNullOrEmpty(fmt)) return v.ToString(fmt, CultureInfo.CurrentCulture);
            double step = ticks.Count > 1 ? Math.Abs(ticks[1] - ticks[0]) : Math.Abs(v);
            if (step == 0) step = 1;
            int dec = Math.Max(0, Math.Min(6, (int)Math.Ceiling(-Math.Log10(step) + 0.0001)));
            if (Math.Abs(v) >= 1e6 || (Math.Abs(v) < 1e-4 && v != 0)) return v.ToString("0.##E+0", CultureInfo.CurrentCulture);
            return v.ToString("F" + dec, CultureInfo.CurrentCulture);
        }

        /// <summary>Данные всех рядов в CSV (разделитель «;», десятичная запятая — для русского Excel).</summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            foreach (var s in Series)
            {
                sb.AppendLine((s.Name ?? "Ряд") + ";");
                sb.AppendLine((XLabel ?? "X") + ";" + (YLabel ?? "Y"));
                int n = Math.Min(s.X.Length, s.Y.Length);
                for (int i = 0; i < n; i++)
                {
                    string xs = XCategories != null && i < XCategories.Length && s.X[i] == i ? XCategories[i] : Csv.Num(s.X[i]);
                    sb.AppendLine(xs + ";" + Csv.Num(s.Y[i]));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>Рисунок из нескольких панелей (графики, текст, картинки) — для отчётов Stepchart.</summary>
    public sealed class Figure
    {
        public interface IPanel { void Draw(Graphics g, RectangleF r, float fontScale); }

        public sealed class PlotPanel : IPanel
        {
            public Plot Plot;
            public void Draw(Graphics g, RectangleF r, float fs) { Plot.Draw(g, r, fs); }
        }

        public sealed class TextPanel : IPanel
        {
            public string Title;
            public readonly List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
            public Color Back = Theme.SurfaceAlt;
            public void Draw(Graphics g, RectangleF r, float fs)
            {
                using (var bg = new SolidBrush(Back)) g.FillRectangle(bg, r);
                using (var bp = new Pen(Theme.Tint)) g.DrawRectangle(bp, r.X, r.Y, r.Width - 1, r.Height - 1);
                using (var ft = new Font(Theme.BoldFont.FontFamily, 9f * fs, FontStyle.Bold))
                using (var f = new Font(Theme.UiFont.FontFamily, 8.3f * fs))
                using (var br = new SolidBrush(Theme.Text))
                using (var mb = new SolidBrush(Theme.Muted))
                {
                    float y = r.Y + 6;
                    if (!string.IsNullOrEmpty(Title))
                    {
                        g.DrawString(Title, ft, br, new RectangleF(r.X + 8, y, r.Width - 16, 200));
                        y += g.MeasureString(Title, ft, (int)(r.Width - 16)).Height + 4;
                    }
                    float keyW = 0;
                    foreach (var kv in Rows) if (kv.Value != null) keyW = Math.Max(keyW, g.MeasureString(kv.Key, f).Width);
                    keyW = Math.Min(keyW, (r.Width - 20) * 0.5f);
                    float valX = r.X + 12 + keyW, valW = Math.Max(20, r.Right - 8 - valX);
                    foreach (var kv in Rows)
                    {
                        if (y > r.Bottom - 8) break;
                        if (kv.Value == null)
                        {
                            float hh = g.MeasureString(kv.Key, f, (int)(r.Width - 16)).Height;
                            g.DrawString(kv.Key, f, mb, new RectangleF(r.X + 8, y, r.Width - 16, hh + 2));
                            y += hh + 1;
                        }
                        else
                        {
                            // длинные подписи и значения переносятся по словам в своих колонках
                            SizeF ks = g.MeasureString(kv.Key, f, (int)keyW + 1), vs = g.MeasureString(kv.Value, f, (int)valW);
                            g.DrawString(kv.Key, f, mb, new RectangleF(r.X + 8, y, keyW + 2, ks.Height + 2));
                            g.DrawString(kv.Value, f, br, new RectangleF(valX, y, valW, vs.Height + 2));
                            y += Math.Max(ks.Height, vs.Height) + 1;
                        }
                    }
                }
            }
        }

        public sealed class ImagePanel : IPanel
        {
            public Bitmap Image;
            public string Caption;
            public void Draw(Graphics g, RectangleF r, float fs)
            {
                g.FillRectangle(Brushes.White, r);
                if (Image == null) return;
                float capH = 0;
                if (!string.IsNullOrEmpty(Caption))
                    using (var f = new Font(Theme.UiFont.FontFamily, 8f * fs))
                    {
                        capH = g.MeasureString(Caption, f, (int)(r.Width - 8)).Height;
                        g.DrawString(Caption, f, Brushes.Black, new RectangleF(r.X + 4, r.Bottom - capH, r.Width - 8, capH + 2));
                    }
                float avW = r.Width - 8, avH = r.Height - capH - 4;
                float k = Math.Min(avW / Image.Width, avH / Image.Height);
                float w = Image.Width * k, h = Image.Height * k;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(Image, r.X + (r.Width - w) / 2, r.Y + 2, w, h);
            }
        }

        public string Title;
        public Size Size = new Size(1200, 900);
        public readonly List<KeyValuePair<RectangleF, IPanel>> Panels = new List<KeyValuePair<RectangleF, IPanel>>();

        /// <summary>Добавляет панель; прямоугольник — в долях (0..1) от области рисунка под заголовком.</summary>
        public void Add(float x, float y, float w, float h, IPanel p) { Panels.Add(new KeyValuePair<RectangleF, IPanel>(new RectangleF(x, y, w, h), p)); }

        public void Draw(Graphics g, RectangleF bounds, float fontScale)
        {
            g.FillRectangle(Brushes.White, bounds);
            float top = bounds.Y;
            if (!string.IsNullOrEmpty(Title))
            {
                using (var f = new Font(Theme.BoldFont.FontFamily, 11f * fontScale, FontStyle.Bold))
                using (var br = new SolidBrush(Theme.Primary))
                {
                    SizeF sz = g.MeasureString(Title, f);
                    g.DrawString(Title, f, br, bounds.X + 10, bounds.Y + 6);
                    top += sz.Height + 10;
                }
            }
            var area = new RectangleF(bounds.X + 6, top, bounds.Width - 12, bounds.Bottom - top - 6);
            foreach (var kv in Panels)
            {
                var rr = new RectangleF(area.X + kv.Key.X * area.Width + 3, area.Y + kv.Key.Y * area.Height + 3,
                                        kv.Key.Width * area.Width - 6, kv.Key.Height * area.Height - 6);
                kv.Value.Draw(g, rr, fontScale);
            }
        }

        public Bitmap Render()
        {
            var bmp = new Bitmap(Size.Width, Size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp)) Draw(g, new RectangleF(0, 0, Size.Width, Size.Height), 1.15f);
            return bmp;
        }
    }

    public static class Csv
    {
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return "";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "-∞";
            return v.ToString("0.######", CultureInfo.GetCultureInfo("ru-RU"));
        }

        public static string Esc(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ';', '"', '\n' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
