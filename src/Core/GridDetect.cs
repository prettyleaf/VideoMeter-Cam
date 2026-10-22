using System;
using System.Collections.Generic;
using System.Drawing;

namespace SigmaLabCam.Core
{
    /// <summary>Вид тестовой таблицы.</summary>
    public enum GridPattern
    {
        Checkerboard, // шахматное поле (squares): узлы — точки касания углов клеток
        Lines         // сетка линий (grid) или точек: узлы — пересечения линий
    }

    public sealed class GridNode
    {
        public int I, J;          // столбец и строка решётки
        public PointD P;          // положение на изображении, пикс.
        public double Strength;
    }

    /// <summary>Найденная решётка узлов тестовой таблицы.</summary>
    public sealed class GridDetection
    {
        public List<GridNode> Nodes = new List<GridNode>();
        public int Cols, Rows;           // I ∈ [0, Cols), J ∈ [0, Rows)
        public double Sigma;             // масштаб поиска, пикс.
        public int Candidates;           // число найденных кандидатов
        public double CellSize;          // медианный шаг решётки, пикс.
        public Rectangle Roi;
        public GridPattern Pattern;
        public int Polarity;             // для сетки: +1 — тёмные линии, −1 — светлые
        private Dictionary<long, GridNode> _map;

        private static long Key(int i, int j) { return ((long)i << 32) ^ (uint)j; }

        public GridDetection Clone()
        {
            var d = (GridDetection)MemberwiseClone();
            d._map = null;
            d.Nodes = new List<GridNode>();
            foreach (var n in Nodes) d.Nodes.Add(new GridNode { I = n.I, J = n.J, P = n.P, Strength = n.Strength });
            return d;
        }

        public void Reindex()
        {
            _map = new Dictionary<long, GridNode>();
            foreach (var n in Nodes) _map[Key(n.I, n.J)] = n;
        }

        public GridNode At(int i, int j)
        {
            if (_map == null) Reindex();
            GridNode n;
            return _map.TryGetValue(Key(i, j), out n) ? n : null;
        }

        /// <summary>
        /// Наибольший прямоугольник решётки, у которого все узлы на сторонах найдены
        /// (нужен для расчёта коэффициентов по крайним линиям).
        /// </summary>
        public bool FullRectangle(out int i0, out int i1, out int j0, out int j1)
        {
            i0 = 0; i1 = Cols - 1; j0 = 0; j1 = Rows - 1;
            while (i1 - i0 >= 2 && j1 - j0 >= 2)
            {
                int mt = MissingRow(j0, i0, i1), mb = MissingRow(j1, i0, i1);
                int ml = MissingCol(i0, j0, j1), mr = MissingCol(i1, j0, j1);
                int worst = Math.Max(Math.Max(mt, mb), Math.Max(ml, mr));
                if (worst == 0) return true;
                // убираем сторону с наибольшей долей пропусков
                double ft = mt / (double)(i1 - i0 + 1), fb = mb / (double)(i1 - i0 + 1);
                double fl = ml / (double)(j1 - j0 + 1), fr = mr / (double)(j1 - j0 + 1);
                double f = Math.Max(Math.Max(ft, fb), Math.Max(fl, fr));
                if (f == ft) j0++;
                else if (f == fb) j1--;
                else if (f == fl) i0++;
                else i1--;
            }
            return false;
        }

        private int MissingRow(int j, int i0, int i1)
        {
            int m = 0;
            for (int i = i0; i <= i1; i++) if (At(i, j) == null) m++;
            return m;
        }

        private int MissingCol(int i, int j0, int j1)
        {
            int m = 0;
            for (int j = j0; j <= j1; j++) if (At(i, j) == null) m++;
            return m;
        }
    }

    /// <summary>Поиск узлов шахматного поля и сетки, уточнение их положения до долей пикселя.</summary>
    public static class GridDetect
    {
        private static readonly double[] AutoSigmas = { 1.0, 1.5, 2.2, 3.2, 4.6, 6.6 };

        // ------------------------------------------------------------------ общий поиск

        /// <param name="sigma">масштаб поиска, пикс.; 0 — подобрать автоматически</param>
        /// <param name="polarity">для сетки: +1 — тёмные линии на светлом, −1 — светлые на тёмном, 0 — определить</param>
        public static GridDetection Detect(ImageData img, Rectangle roi, GridPattern pattern, double sigma, int polarity)
        {
            roi = Rectangle.Intersect(roi.Width > 0 && roi.Height > 0 ? roi : new Rectangle(0, 0, img.Width, img.Height),
                                      new Rectangle(0, 0, img.Width, img.Height));
            if (roi.Width < 16 || roi.Height < 16) throw new ArgumentException("Слишком маленькая область поиска.");
            float[] full = Luma(img);
            int W = img.Width;
            float[] lum = Crop(full, W, roi);
            int w = roi.Width, h = roi.Height;
            int factor = 1;
            while ((double)(w / factor) * (h / factor) > 1.5e6) factor *= 2;
            float[] small = factor > 1 ? Downsample(lum, w, h, factor) : lum;
            int sw = w / factor, sh = h / factor;

            double[] sigmas = sigma > 0 ? new[] { Math.Max(0.7, sigma / factor) } : AutoSigmas;
            double noise = NoiseSigma(small, sw, sh);
            double bestSigma;
            int bestCand, bestQ;
            List<GridNode> best;
            if (pattern == GridPattern.Lines && polarity == 0)
            {
                polarity = LinePolarity(small, sw, sh);
                best = Search(small, sw, sh, sigmas, pattern, polarity, noise, out bestSigma, out bestCand, out bestQ);
                if (bestQ < 9)
                {
                    double s2; int c2, q2;
                    List<GridNode> inv = Search(small, sw, sh, sigmas, pattern, -polarity, noise, out s2, out c2, out q2);
                    if (q2 > bestQ) { best = inv; bestSigma = s2; bestCand = c2; bestQ = q2; polarity = -polarity; }
                }
            }
            else best = Search(small, sw, sh, sigmas, pattern, polarity, noise, out bestSigma, out bestCand, out bestQ);
            if (best != null && bestQ < 4) best = null;
            var det = new GridDetection { Roi = roi, Pattern = pattern, Polarity = polarity, Sigma = bestSigma * factor, Candidates = bestCand };
            if (best == null) return det;

            // слабые узлы — ложные срабатывания (край таблицы, надписи)
            var str = new List<double>();
            foreach (var n in best) str.Add(n.Strength);
            str.Sort();
            double medStr = str[str.Count / 2];
            best.RemoveAll(n => n.Strength < 0.3 * medStr);

            // перевод в координаты полного разрешения и уточнение
            foreach (var n in best) n.P = new PointD(n.P.X * factor + (factor - 1) / 2.0, n.P.Y * factor + (factor - 1) / 2.0);
            det.Nodes.AddRange(best);
            Finish(det);
            double cell = det.CellSize;
            foreach (var n in det.Nodes)
            {
                PointD p = pattern == GridPattern.Checkerboard
                    ? CornerSubPix(lum, w, h, n.P, Clamp((int)Math.Round(0.35 * cell), 3, 30))
                    : BlobCentroid(lum, w, h, n.P, det.Sigma, polarity, 0.25 * cell);
                if (p.IsValid && PointD.Dist(p, n.P) < 0.3 * cell) n.P = p;
            }
            if (pattern == GridPattern.Lines)
                for (int pass = 0; pass < 2; pass++) LineRefine(lum, w, h, det, polarity);
            foreach (var n in det.Nodes) n.P = new PointD(n.P.X + roi.X, n.P.Y + roi.Y);
            RemoveOutliers(det);
            Finish(det);
            return det;
        }

        private static List<GridNode> Search(float[] small, int sw, int sh, double[] sigmas, GridPattern pattern, int polarity, double noise,
                                             out double bestSigma, out int bestCand, out int bestQ)
        {
            List<GridNode> best = null;
            bestSigma = 0; bestCand = 0; bestQ = -1;
            foreach (double s in sigmas)
            {
                if (s * 6 > Math.Min(sw, sh)) break;
                float[] resp = Response(small, sw, sh, s, pattern, polarity);
                // у пересечения тонких линий на мелком масштабе несколько близких максимумов — подавляются в большем радиусе
                int nms = pattern == GridPattern.Lines ? (int)Math.Round(2.5 * s + 1) : Math.Max(2, (int)Math.Round(1.5 * s));
                List<Peak> peaks = Peaks(resp, sw, sh, nms);
                // проверка структуры вокруг кандидата отсекает шум, края таблицы и надписи
                double sb = Math.Max(0.6, 0.5 * s);
                float[] vb = Gaussian(small, sw, sh, sb);
                double nb = noise / (2 * Math.Sqrt(Math.PI) * Math.Max(0.5, sb)) + 0.5;
                peaks = peaks.FindAll(pk => Plausible(vb, sw, sh, pk, s, pattern, polarity, nb));
                if (peaks.Count < 9) continue;
                List<GridNode> lat = Lattice(peaks);
                if (lat == null) continue;
                int q = LatticeScore(lat);
                if (q > bestQ || (q == bestQ && lat.Count > best.Count))
                {
                    best = lat; bestSigma = s; bestCand = peaks.Count; bestQ = q;
                }
            }
            return best;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>Нормализация индексов (I — слева направо, J — сверху вниз, с нуля) и шаг решётки.</summary>
        private static void Finish(GridDetection det)
        {
            var nodes = det.Nodes;
            if (nodes.Count == 0) return;
            det.Reindex();
            // средние векторы шагов по I и по J
            PointD si = new PointD(), sj = new PointD();
            int ni = 0, nj = 0;
            var steps = new List<double>();
            foreach (var n in nodes)
            {
                var a = det.At(n.I + 1, n.J);
                if (a != null) { si += a.P - n.P; ni++; steps.Add(PointD.Dist(a.P, n.P)); }
                var b = det.At(n.I, n.J + 1);
                if (b != null) { sj += b.P - n.P; nj++; steps.Add(PointD.Dist(b.P, n.P)); }
            }
            if (ni > 0) si = si * (1.0 / ni);
            if (nj > 0) sj = sj * (1.0 / nj);
            bool swap = Math.Abs(si.X) < Math.Abs(sj.X);
            if (swap)
            {
                foreach (var n in nodes) { int t = n.I; n.I = n.J; n.J = t; }
                var t2 = si; si = sj; sj = t2;
            }
            bool flipI = si.X < 0, flipJ = sj.Y < 0;
            int minI = int.MaxValue, minJ = int.MaxValue, maxI = int.MinValue, maxJ = int.MinValue;
            foreach (var n in nodes)
            {
                if (flipI) n.I = -n.I;
                if (flipJ) n.J = -n.J;
                minI = Math.Min(minI, n.I); minJ = Math.Min(minJ, n.J);
                maxI = Math.Max(maxI, n.I); maxJ = Math.Max(maxJ, n.J);
            }
            foreach (var n in nodes) { n.I -= minI; n.J -= minJ; }
            det.Cols = maxI - minI + 1;
            det.Rows = maxJ - minJ + 1;
            steps.Sort();
            det.CellSize = steps.Count > 0 ? steps[steps.Count / 2] : 0;
            nodes.Sort((a, b) => a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I));
            det.Reindex();
        }

        /// <summary>Удаляет узлы, выпадающие из гладкой решётки (ложные срабатывания рядом с пропущенным узлом).</summary>
        private static void RemoveOutliers(GridDetection det)
        {
            det.Reindex();
            double lim = 0.25 * det.CellSize;
            var bad = new List<GridNode>();
            foreach (var n in det.Nodes)
            {
                double dev = 0;
                int votes = 0;
                GridNode l = det.At(n.I - 1, n.J), r = det.At(n.I + 1, n.J), u = det.At(n.I, n.J - 1), d = det.At(n.I, n.J + 1);
                if (l != null && r != null) { dev = Math.Max(dev, PointD.Dist(n.P, PointD.Mid(l.P, r.P))); votes++; }
                if (u != null && d != null) { dev = Math.Max(dev, PointD.Dist(n.P, PointD.Mid(u.P, d.P))); votes++; }
                if (votes > 0 && dev > lim) bad.Add(n);
            }
            foreach (var n in bad) det.Nodes.Remove(n);
        }

        // ------------------------------------------------------------------ отклик

        public static float[] Luma(ImageData img)
        {
            if (!img.IsRgb) return img.Planes[0];
            return img.LumaPlane();
        }

        private static float[] Crop(float[] src, int W, Rectangle r)
        {
            var d = new float[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++) Array.Copy(src, (r.Y + y) * W + r.X, d, y * r.Width, r.Width);
            return d;
        }

        private static float[] Downsample(float[] src, int w, int h, int f)
        {
            int sw = w / f, sh = h / f;
            var d = new float[sw * sh];
            float k = 1f / (f * f);
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    float s = 0;
                    for (int v = 0; v < f; v++)
                    {
                        int row = (y * f + v) * w + x * f;
                        for (int u = 0; u < f; u++) s += src[row + u];
                    }
                    d[y * sw + x] = s * k;
                }
            return d;
        }

        /// <summary>
        /// Полярность линий сетки: в текстурированной части кадра (рядом с линиями) пиксели линий — меньшинство.
        /// +1 — тёмные линии на светлом фоне, −1 — светлые на тёмном.
        /// </summary>
        private static int LinePolarity(float[] img, int w, int h)
        {
            int r = Math.Max(3, Math.Min(w, h) / 90);
            // интегральные изображения для локального СКО
            var s1 = new double[(w + 1) * (h + 1)];
            var s2 = new double[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                double a = 0, b = 0;
                for (int x = 0; x < w; x++)
                {
                    double v = img[y * w + x];
                    a += v; b += v * v;
                    s1[(y + 1) * (w + 1) + x + 1] = s1[y * (w + 1) + x + 1] + a;
                    s2[(y + 1) * (w + 1) + x + 1] = s2[y * (w + 1) + x + 1] + b;
                }
            }
            int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 120000));
            var sd = new List<float>();
            var val = new List<float>();
            for (int y = r; y < h - r; y += step)
                for (int x = r; x < w - r; x += step)
                {
                    int x0 = x - r, x1 = x + r + 1, y0 = y - r, y1 = y + r + 1;
                    double n = (double)(x1 - x0) * (y1 - y0);
                    double sa = s1[y1 * (w + 1) + x1] - s1[y0 * (w + 1) + x1] - s1[y1 * (w + 1) + x0] + s1[y0 * (w + 1) + x0];
                    double sb = s2[y1 * (w + 1) + x1] - s2[y0 * (w + 1) + x1] - s2[y1 * (w + 1) + x0] + s2[y0 * (w + 1) + x0];
                    double m = sa / n;
                    sd.Add((float)Math.Sqrt(Math.Max(0, sb / n - m * m)));
                    val.Add(img[y * w + x]);
                }
            if (sd.Count == 0) return 1;
            var sorted = new List<float>(sd);
            sorted.Sort();
            float lim = 0.4f * sorted[(int)(sorted.Count * 0.95)];
            var hist = new double[256];
            double total = 0;
            for (int i = 0; i < sd.Count; i++)
                if (sd[i] > lim) { hist[Math.Max(0, Math.Min(255, (int)val[i]))]++; total++; }
            if (total < 10) return 1;
            // порог Оцу
            double sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += i * hist[i];
            double wB = 0, sumB = 0, bestVar = -1;
            int t = 128;
            for (int i = 0; i < 256; i++)
            {
                wB += hist[i];
                if (wB == 0 || wB == total) continue;
                sumB += i * hist[i];
                double mB = sumB / wB, mF = (sumAll - sumB) / (total - wB);
                double bv = wB * (total - wB) * (mB - mF) * (mB - mF);
                if (bv > bestVar) { bestVar = bv; t = i; }
            }
            double dark = 0;
            for (int i = 0; i <= t; i++) dark += hist[i];
            return dark <= total - dark ? 1 : -1;
        }

        /// <summary>+1 — фон светлый (тёмные линии), −1 — фон тёмный.</summary>
        private static int DetectPolarity(float[] lum)
        {
            var hist = new int[256];
            foreach (float v in lum) hist[Math.Max(0, Math.Min(255, (int)v))]++;
            int n = lum.Length, acc = 0, p5 = 0, p50 = 0, p95 = 255;
            bool a = false, b = false, c = false;
            for (int i = 0; i < 256; i++)
            {
                acc += hist[i];
                if (!a && acc >= n * 0.05) { p5 = i; a = true; }
                if (!b && acc >= n * 0.5) { p50 = i; b = true; }
                if (!c && acc >= n * 0.95) { p95 = i; c = true; }
            }
            return p50 >= (p5 + p95) / 2.0 ? 1 : -1;
        }

        public static float[] Gaussian(float[] src, int w, int h, double sigma)
        {
            if (sigma <= 0.05) return (float[])src.Clone();
            int r = (int)Math.Ceiling(3 * sigma);
            var k = new float[2 * r + 1];
            double sum = 0;
            for (int i = -r; i <= r; i++) { k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma)); sum += k[i + r]; }
            for (int i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
            var tmp = new float[w * h];
            var dst = new float[w * h];
            var line = new float[Math.Max(w, h) + 2 * r];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = -r; x < w + r; x++) line[x + r] = src[row + (x < 0 ? 0 : (x >= w ? w - 1 : x))];
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int t = 0; t < k.Length; t++) s += k[t] * line[x + t];
                    tmp[row + x] = s;
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = -r; y < h + r; y++) line[y + r] = tmp[(y < 0 ? 0 : (y >= h ? h - 1 : y)) * w + x];
                for (int y = 0; y < h; y++)
                {
                    float s = 0;
                    for (int t = 0; t < k.Length; t++) s += k[t] * line[y + t];
                    dst[y * w + x] = s;
                }
            }
            return dst;
        }

        /// <summary>
        /// Отклик узла. Шахматное поле — седловая точка яркости: (Ixy² − Ixx·Iyy)·σ⁴.
        /// Сетка — пересечение линий: меньшее (по модулю) собственное число матрицы Гессе нужного знака, ·σ².
        /// </summary>
        public static float[] Response(float[] src, int w, int h, double sigma, GridPattern pattern, int polarity)
        {
            float[] b = Gaussian(src, w, h, sigma);
            var r = new float[w * h];
            double s2 = sigma * sigma, s4 = s2 * s2;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    double ixx = b[i + 1] - 2 * b[i] + b[i - 1];
                    double iyy = b[i + w] - 2 * b[i] + b[i - w];
                    double ixy = (b[i + w + 1] - b[i + w - 1] - b[i - w + 1] + b[i - w - 1]) * 0.25;
                    double v;
                    if (pattern == GridPattern.Checkerboard) v = (ixy * ixy - ixx * iyy) * s4;
                    else
                    {
                        double tr = (ixx + iyy) / 2, disc = Math.Sqrt((ixx - iyy) * (ixx - iyy) / 4 + ixy * ixy);
                        v = polarity >= 0 ? (tr - disc) * s2 : -(tr + disc) * s2;
                    }
                    r[i] = v > 0 ? (float)v : 0;
                }
            return r;
        }

        private struct Peak
        {
            public double X, Y, R;
        }

        /// <summary>СКО шума изображения по медиане модулей разностей соседних пикселей (устойчиво к краям).</summary>
        private static double NoiseSigma(float[] img, int w, int h)
        {
            int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 150000));
            var d = new List<float>();
            for (int y = 0; y < h; y += step)
                for (int x = 0; x + 1 < w; x += step)
                    d.Add(Math.Abs(img[y * w + x + 1] - img[y * w + x]));
            if (d.Count == 0) return 1;
            d.Sort();
            return Math.Max(0.3, 1.4826 * d[d.Count / 2] / Math.Sqrt(2));
        }

        private static bool Plausible(float[] b, int w, int h, Peak p, double s, GridPattern pattern, int polarity, double noise)
        {
            if (pattern == GridPattern.Checkerboard)
            {
                foreach (double r in new[] { 1.5 * s + 1, 2.5 * s + 1 })
                    if (CheckerScore(b, w, h, p.X, p.Y, r, noise) >= 0.4) return true;
                return false;
            }
            foreach (double r in new[] { 2.5 * s + 1, 4 * s + 1, 6 * s + 1 })
                if (CrossCheck(b, w, h, p.X, p.Y, r, polarity, noise)) return true;
            return false;
        }

        /// <summary>
        /// Точка касания клеток шахматного поля: яркость на окружности вокруг неё — два тёмных и два светлых
        /// сектора, т. е. преобладает вторая гармоника, а противоположные точки окружности одинаковы.
        /// Возвращает долю энергии второй гармоники (0…1), 0 — не узел.
        /// </summary>
        private static double CheckerScore(float[] b, int w, int h, double x, double y, double r, double noise)
        {
            const int N = 24;
            var v = new double[N];
            double m = 0;
            for (int k = 0; k < N; k++)
            {
                double a = 2 * Math.PI * k / N;
                v[k] = Sample(b, w, h, x + r * Math.Cos(a), y + r * Math.Sin(a));
                m += v[k];
            }
            m /= N;
            double e = 0, c2 = 0, s2 = 0, opp = 0;
            for (int k = 0; k < N; k++)
            {
                double d = v[k] - m, a = 2 * Math.PI * k / N;
                e += d * d;
                c2 += d * Math.Cos(2 * a);
                s2 += d * Math.Sin(2 * a);
                opp += d * (v[(k + N / 2) % N] - m);
            }
            if (e <= 0 || Math.Sqrt(e / N) < 3 * noise) return 0;
            if (opp / e < 0.3) return 0;
            return 2 * (c2 * c2 + s2 * s2) / N / e;
        }

        /// <summary>
        /// Пересечение линий сетки: на окружности вокруг узла 3–4 «провала» (плечи линий; на краю сетки — 3,
        /// в углу — 2 под углом около 90°). Одиночная линия даёт 2 противоположных провала, край таблицы — один.
        /// </summary>
        private static bool CrossCheck(float[] b, int w, int h, double x, double y, double r, int polarity, double noise)
        {
            const int N = 36;
            var v = new double[N];
            for (int k = 0; k < N; k++)
            {
                double a = 2 * Math.PI * k / N;
                double val = Sample(b, w, h, x + r * Math.Cos(a), y + r * Math.Sin(a));
                v[k] = polarity >= 0 ? val : -val;
            }
            var sorted = (double[])v.Clone();
            Array.Sort(sorted);
            double md = sorted[N / 2], vmin = sorted[0], depth = md - vmin;
            if (depth < 4 * noise) return false;
            double thr = md - 0.45 * depth;
            int start = -1;
            for (int k = 0; k < N; k++) if (v[k] >= thr) { start = k; break; }
            if (start < 0) return false;
            int dips = 0;
            for (int k = 0; k < N; k++) if (v[k] < thr) dips++;
            // провалы-линии узкие; если «провалов» больше половины окружности — это центр клетки (обратная полярность)
            if (dips > N / 2) return false;
            var centers = new List<double>();
            int runStart = -1;
            for (int t = 1; t <= N; t++)
            {
                int k = (start + t) % N;
                bool dip = v[k] < thr;
                if (dip && runStart < 0) runStart = t;
                if (!dip && runStart >= 0)
                {
                    centers.Add((start + (runStart + t - 1) / 2.0) * 360.0 / N);
                    runStart = -1;
                }
            }
            if (centers.Count >= 3 && centers.Count <= 6) return true;
            if (centers.Count == 2)
            {
                double da = Math.Abs(centers[1] - centers[0]) % 360;
                if (da > 180) da = 360 - da;
                return da >= 40 && da <= 140;
            }
            return false;
        }

        /// <summary>
        /// Качество решётки: число узлов, согласованных с соседями, умноженное на полноту решётки
        /// (доля найденных узлов в её ограничивающем прямоугольнике). У решётки из шумовых откликов полнота мала.
        /// </summary>
        private static int LatticeScore(List<GridNode> nodes)
        {
            int minI = int.MaxValue, maxI = int.MinValue, minJ = int.MaxValue, maxJ = int.MinValue;
            foreach (var n in nodes) { minI = Math.Min(minI, n.I); maxI = Math.Max(maxI, n.I); minJ = Math.Min(minJ, n.J); maxJ = Math.Max(maxJ, n.J); }
            double area = (double)(maxI - minI + 1) * (maxJ - minJ + 1);
            double completeness = nodes.Count / area;
            return (int)Math.Round(LatticeQuality(nodes) * completeness);
        }

        /// <summary>Число узлов, согласованных с соседями (лежат посередине между противоположными соседями).</summary>
        private static int LatticeQuality(List<GridNode> nodes)
        {
            var map = new Dictionary<long, GridNode>();
            foreach (var n in nodes) map[((long)n.I << 32) ^ (uint)n.J] = n;
            Func<int, int, GridNode> at = (i, j) => { GridNode g; return map.TryGetValue(((long)i << 32) ^ (uint)j, out g) ? g : null; };
            int q = 0;
            foreach (var n in nodes)
            {
                bool ok = false;
                foreach (var d in new[] { new[] { 1, 0 }, new[] { 0, 1 } })
                {
                    GridNode a = at(n.I - d[0], n.J - d[1]), b = at(n.I + d[0], n.J + d[1]);
                    if (a == null || b == null) continue;
                    double step = (PointD.Dist(a.P, n.P) + PointD.Dist(b.P, n.P)) / 2;
                    if (step > 1 && PointD.Dist(n.P, PointD.Mid(a.P, b.P)) < 0.2 * step &&
                        Math.Abs(PointD.Dist(a.P, n.P) - PointD.Dist(b.P, n.P)) < 0.35 * step) ok = true;
                }
                if (ok) q++;
            }
            return q;
        }

        private static List<Peak> Peaks(float[] r, int w, int h, int rad)
        {
            var all = new List<Peak>();
            int m = rad + 1;
            for (int y = m; y < h - m; y++)
                for (int x = m; x < w - m; x++)
                {
                    float v = r[y * w + x];
                    if (v <= 0) continue;
                    bool max = true;
                    for (int dy = -rad; dy <= rad && max; dy++)
                    {
                        int row = (y + dy) * w;
                        for (int dx = -rad; dx <= rad; dx++)
                        {
                            float u = r[row + x + dx];
                            if (u > v || (u == v && (dy < 0 || (dy == 0 && dx < 0)))) { max = false; break; }
                        }
                    }
                    if (!max) continue;
                    // уточнение по параболе
                    double fx = 0, fy = 0;
                    double l = r[y * w + x - 1], rr = r[y * w + x + 1], u2 = r[(y - 1) * w + x], d = r[(y + 1) * w + x];
                    double den = l - 2 * v + rr;
                    if (den < 0) fx = 0.5 * (l - rr) / den;
                    den = u2 - 2 * v + d;
                    if (den < 0) fy = 0.5 * (u2 - d) / den;
                    all.Add(new Peak { X = x + Math.Max(-0.5, Math.Min(0.5, fx)), Y = y + Math.Max(-0.5, Math.Min(0.5, fy)), R = v });
                }
            if (all.Count == 0) return all;
            all.Sort((a, b) => b.R.CompareTo(a.R));
            int k = Math.Min(all.Count - 1, Math.Max(3, Math.Min(all.Count, 30) / 2));
            double thr = 0.12 * all[k].R;
            var res = new List<Peak>();
            foreach (var p in all)
            {
                if (p.R < thr) break;
                res.Add(p);
                if (res.Count >= 20000) break;
            }
            return res;
        }

        // ------------------------------------------------------------------ сборка решётки

        private sealed class SpatialIndex
        {
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly double _size;
            private readonly List<Peak> _p;

            public SpatialIndex(List<Peak> p, double size)
            {
                _p = p;
                _size = Math.Max(1, size);
                for (int i = 0; i < p.Count; i++)
                {
                    long k = Key((int)Math.Floor(p[i].X / _size), (int)Math.Floor(p[i].Y / _size));
                    List<int> l;
                    if (!_cells.TryGetValue(k, out l)) _cells[k] = l = new List<int>();
                    l.Add(i);
                }
            }

            private static long Key(int a, int b) { return ((long)a << 32) ^ (uint)b; }

            public List<int> Near(double x, double y, double radius)
            {
                var res = new List<int>();
                int x0 = (int)Math.Floor((x - radius) / _size), x1 = (int)Math.Floor((x + radius) / _size);
                int y0 = (int)Math.Floor((y - radius) / _size), y1 = (int)Math.Floor((y + radius) / _size);
                double r2 = radius * radius;
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        List<int> l;
                        if (!_cells.TryGetValue(Key(cx, cy), out l)) continue;
                        foreach (int i in l)
                        {
                            double dx = _p[i].X - x, dy = _p[i].Y - y;
                            if (dx * dx + dy * dy <= r2) res.Add(i);
                        }
                    }
                return res;
            }
        }

        private static List<GridNode> Lattice(List<Peak> peaks)
        {
            int n = peaks.Count;
            // медианное расстояние до ближайшего соседа
            var probe = new SpatialIndex(peaks, 16);
            var nn = new List<double>();
            for (int i = 0; i < n; i += Math.Max(1, n / 400))
            {
                double best = double.MaxValue;
                for (double rad = 8; rad <= 4096 && best == double.MaxValue; rad *= 2)
                    foreach (int j in probe.Near(peaks[i].X, peaks[i].Y, rad))
                        if (j != i) best = Math.Min(best, Dist(peaks[i], peaks[j]));
                if (best < double.MaxValue) nn.Add(best);
            }
            if (nn.Count == 0) return null;
            nn.Sort();
            double d0 = nn[nn.Count / 2];
            var index = new SpatialIndex(peaks, d0);

            // затравки: сильные кандидаты ближе к центру масс
            double mx = 0, my = 0;
            foreach (var p in peaks) { mx += p.X; my += p.Y; }
            mx /= n; my /= n;
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            double rmax = peaks[0].R;
            order.Sort((a, b) =>
            {
                double sa = peaks[a].R / rmax - Math.Sqrt((peaks[a].X - mx) * (peaks[a].X - mx) + (peaks[a].Y - my) * (peaks[a].Y - my)) / (6 * d0);
                double sb = peaks[b].R / rmax - Math.Sqrt((peaks[b].X - mx) * (peaks[b].X - mx) + (peaks[b].Y - my) * (peaks[b].Y - my)) / (6 * d0);
                return sb.CompareTo(sa);
            });

            List<GridNode> best2 = null;
            int tries = 0;
            foreach (int seed in order)
            {
                if (tries >= 12) break;
                PointD u, v;
                if (!SeedBasis(peaks, index, seed, d0, out u, out v)) continue;
                tries++;
                var lat = Grow(peaks, index, seed, u, v);
                if (lat != null && (best2 == null || lat.Count > best2.Count)) best2 = lat;
                if (best2 != null && best2.Count > n * 0.8) break;
            }
            return best2 != null && best2.Count >= 9 ? best2 : null;
        }

        private static double Dist(Peak a, Peak b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        private static bool SeedBasis(List<Peak> peaks, SpatialIndex index, int seed, double d0, out PointD u, out PointD v)
        {
            u = v = new PointD();
            Peak s = peaks[seed];
            var near = index.Near(s.X, s.Y, d0 * 2.2);
            near.Remove(seed);
            if (near.Count < 3) return false;
            near.Sort((a, b) => Dist(peaks[a], s).CompareTo(Dist(peaks[b], s)));
            u = new PointD(peaks[near[0]].X - s.X, peaks[near[0]].Y - s.Y);
            bool found = false;
            foreach (int j in near)
            {
                var c = new PointD(peaks[j].X - s.X, peaks[j].Y - s.Y);
                double cos = (c.X * u.X + c.Y * u.Y) / (c.Length * u.Length);
                if (Math.Abs(cos) < 0.57 && c.Length < u.Length * 1.8 && c.Length > u.Length / 1.8) { v = c; found = true; break; }
            }
            if (!found) return false;
            // противоположные соседи должны существовать
            int ok = 0;
            foreach (var d in new[] { u * -1, v * -1 })
            {
                double tol = 0.35 * d.Length;
                foreach (int j in index.Near(s.X + d.X, s.Y + d.Y, tol)) { if (j != seed) { ok++; break; } }
            }
            return ok >= 1;
        }

        private static List<GridNode> Grow(List<Peak> peaks, SpatialIndex index, int seed, PointD u, PointD v)
        {
            var at = new Dictionary<long, int>();
            var used = new HashSet<int>();
            var queue = new Queue<long>();
            Func<int, int, long> key = (i, j) => ((long)i << 32) ^ (uint)j;
            Func<int, int, int> get = (i, j) => { int k; return at.TryGetValue(key(i, j), out k) ? k : -1; };
            Func<int, PointD> pos = k => new PointD(peaks[k].X, peaks[k].Y);
            var ij = new Dictionary<long, int[]>();
            at[key(0, 0)] = seed;
            ij[key(0, 0)] = new[] { 0, 0 };
            used.Add(seed);
            queue.Enqueue(key(0, 0));
            int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            while (queue.Count > 0)
            {
                int[] c = ij[queue.Dequeue()];
                int ci = c[0], cj = c[1];
                PointD p = pos(get(ci, cj));
                foreach (int[] d in dirs)
                {
                    int ni = ci + d[0], nj = cj + d[1];
                    if (get(ni, nj) >= 0) continue;
                    PointD step = new PointD();
                    bool have = false;
                    int opp = get(ci - d[0], cj - d[1]);
                    if (opp >= 0) { step = p - pos(opp); have = true; }
                    if (!have)
                    {
                        int pi = d[1], pj = d[0]; // перпендикулярное направление
                        for (int s = 1; s >= -1 && !have; s -= 2)
                        {
                            int a = get(ci + s * pi, cj + s * pj), b = get(ci + s * pi + d[0], cj + s * pj + d[1]);
                            if (a >= 0 && b >= 0) { step = pos(b) - pos(a); have = true; }
                        }
                    }
                    if (!have) step = d[0] != 0 ? u * d[0] : v * d[1];
                    double len = step.Length;
                    if (len < 2) continue;
                    PointD pred = p + step;
                    double tol = 0.35 * len;
                    int bestK = -1;
                    double bestScore = 0;
                    foreach (int k in index.Near(pred.X, pred.Y, tol))
                    {
                        if (used.Contains(k)) continue;
                        double dd = PointD.Dist(pos(k), pred) / tol;
                        double sc = peaks[k].R * (1 - dd * dd);
                        if (sc > bestScore) { bestScore = sc; bestK = k; }
                    }
                    if (bestK < 0) continue;
                    long nk = key(ni, nj);
                    at[nk] = bestK;
                    ij[nk] = new[] { ni, nj };
                    used.Add(bestK);
                    queue.Enqueue(nk);
                }
            }
            var res = new List<GridNode>();
            foreach (var kv in at)
            {
                int[] c = ij[kv.Key];
                res.Add(new GridNode { I = c[0], J = c[1], P = pos(kv.Value), Strength = peaks[kv.Value].R });
            }
            return res;
        }

        // ------------------------------------------------------------------ уточнение

        /// <summary>
        /// Уточнение точки касания клеток шахматного поля: градиент яркости в окрестности угла
        /// перпендикулярен направлению на угол (как cornerSubPix).
        /// </summary>
        public static PointD CornerSubPix(float[] lum, int w, int h, PointD p0, int win)
        {
            int pad = win + 3;
            int x0 = (int)Math.Floor(p0.X) - pad, y0 = (int)Math.Floor(p0.Y) - pad;
            int pw = 2 * pad + 2, ph = 2 * pad + 2;
            float[] patch = Patch(lum, w, h, x0, y0, pw, ph);
            float[] b = Gaussian(patch, pw, ph, 1.0);
            PointD q = p0;
            for (int it = 0; it < 10; it++)
            {
                double a11 = 0, a12 = 0, a22 = 0, b1 = 0, b2 = 0;
                double qx = q.X - x0, qy = q.Y - y0;
                int cx = (int)Math.Round(qx), cy = (int)Math.Round(qy);
                for (int y = cy - win; y <= cy + win; y++)
                    for (int x = cx - win; x <= cx + win; x++)
                    {
                        if (x < 1 || y < 1 || x >= pw - 1 || y >= ph - 1) continue;
                        double gx = (b[y * pw + x + 1] - b[y * pw + x - 1]) * 0.5;
                        double gy = (b[(y + 1) * pw + x] - b[(y - 1) * pw + x]) * 0.5;
                        double dx = x - qx, dy = y - qy;
                        double wgt = Math.Exp(-(dx * dx + dy * dy) / (2.0 * win * win * 0.5));
                        double gxx = gx * gx * wgt, gxy = gx * gy * wgt, gyy = gy * gy * wgt;
                        a11 += gxx; a12 += gxy; a22 += gyy;
                        b1 += gxx * x + gxy * y;
                        b2 += gxy * x + gyy * y;
                    }
                double det = a11 * a22 - a12 * a12;
                if (Math.Abs(det) < 1e-9) return q;
                double nx = (a22 * b1 - a12 * b2) / det, ny = (a11 * b2 - a12 * b1) / det;
                var nq = new PointD(nx + x0, ny + y0);
                double moved = PointD.Dist(nq, q);
                q = nq;
                if (PointD.Dist(q, p0) > win) return PointD.NaN;
                if (moved < 0.01) break;
            }
            return q;
        }

        /// <summary>
        /// Уточнение пересечения линий (или центра точки): центр тяжести отклика на масштабе, где отклик
        /// в узле наибольший (на слишком мелком масштабе отклик тёмной точки — кольцо, и центр смещается).
        /// </summary>
        public static PointD BlobCentroid(float[] lum, int w, int h, PointD p0, double sigma, int polarity, double maxSigma)
        {
            double bestS = sigma, bestV = -1;
            foreach (double f in new[] { 0.7, 1.0, 1.4, 2.0, 2.8, 4.0 })
            {
                double s = sigma * f;
                if (s < 0.8 || (s > maxSigma && f > 1)) continue;
                int pad0 = (int)Math.Ceiling(3 * s) + 4;
                int px0 = (int)Math.Floor(p0.X) - pad0, py0 = (int)Math.Floor(p0.Y) - pad0;
                int pw0 = 2 * pad0 + 2;
                float[] r0 = Response(Patch(lum, w, h, px0, py0, pw0, pw0), pw0, pw0, s, GridPattern.Lines, polarity);
                int cx0 = (int)Math.Round(p0.X) - px0, cy0 = (int)Math.Round(p0.Y) - py0;
                int rr = Math.Max(1, (int)Math.Round(0.6 * s));
                double v = 0;
                for (int y = cy0 - rr; y <= cy0 + rr; y++)
                    for (int x = cx0 - rr; x <= cx0 + rr; x++) v = Math.Max(v, r0[y * pw0 + x]);
                if (v > bestV) { bestV = v; bestS = s; }
            }
            sigma = bestS;
            int rad = Math.Max(2, (int)Math.Round(1.3 * sigma));
            int pad = rad + (int)Math.Ceiling(3 * sigma) + 3;
            int x0 = (int)Math.Floor(p0.X) - pad, y0 = (int)Math.Floor(p0.Y) - pad;
            int pw = 2 * pad + 2, ph = 2 * pad + 2;
            float[] patch = Patch(lum, w, h, x0, y0, pw, ph);
            float[] r = Response(patch, pw, ph, sigma, GridPattern.Lines, polarity);
            PointD q = p0;
            for (int it = 0; it < 4; it++)
            {
                int cx = (int)Math.Round(q.X - x0), cy = (int)Math.Round(q.Y - y0);
                // локальный максимум рядом с текущей оценкой
                float best = -1;
                int bx = cx, by = cy;
                for (int y = cy - rad; y <= cy + rad; y++)
                    for (int x = cx - rad; x <= cx + rad; x++)
                    {
                        if (x < 1 || y < 1 || x >= pw - 1 || y >= ph - 1) continue;
                        if (r[y * pw + x] > best) { best = r[y * pw + x]; bx = x; by = y; }
                    }
                if (best <= 0) return q;
                double sw = 0, sx = 0, sy = 0, floor = best * 0.3;
                for (int y = by - rad; y <= by + rad; y++)
                    for (int x = bx - rad; x <= bx + rad; x++)
                    {
                        if (x < 0 || y < 0 || x >= pw || y >= ph) continue;
                        double v = r[y * pw + x] - floor;
                        if (v <= 0) continue;
                        sw += v; sx += v * x; sy += v * y;
                    }
                if (sw <= 0) return q;
                var nq = new PointD(sx / sw + x0, sy / sw + y0);
                if (PointD.Dist(nq, q) < 0.01) { q = nq; break; }
                q = nq;
            }
            return q;
        }

        private sealed class LocalLine
        {
            public PointD Origin, Dir;   // точка на линии и направление
        }

        /// <summary>
        /// Уточнение узлов сетки линий: по профилям поперёк каждой из двух линий (по обе стороны от узла,
        /// вне зоны самого пересечения) находятся центры линий, линии аппроксимируются прямыми,
        /// узел — точка их пересечения. Так же точно определяются и узлы на краю сетки (Т-образные).
        /// </summary>
        private static void LineRefine(float[] lum, int w, int h, GridDetection det, int polarity)
        {
            det.Reindex();
            var upd = new List<KeyValuePair<GridNode, PointD>>();
            Func<GridNode, PointD> pos = g => g != null ? g.P : PointD.NaN;
            foreach (var n in det.Nodes)
            {
                PointD q = RefineCross(lum, w, h, n.P, pos(det.At(n.I - 1, n.J)), pos(det.At(n.I + 1, n.J)),
                                       pos(det.At(n.I, n.J - 1)), pos(det.At(n.I, n.J + 1)), polarity);
                if (q.IsValid) upd.Add(new KeyValuePair<GridNode, PointD>(n, q));
            }
            foreach (var kv in upd) kv.Key.P = kv.Value;
        }

        /// <summary>Пересечение двух линий сетки около p; соседние узлы задают направления и шаг (NaN — соседа нет).</summary>
        private static PointD RefineCross(float[] lum, int w, int h, PointD p, PointD left, PointD right, PointD up, PointD down, int polarity)
        {
            bool hl = left.IsValid, hr = right.IsValid, hu = up.IsValid, hd = down.IsValid;
            if ((!hl && !hr) || (!hu && !hd)) return PointD.NaN;
            PointD dh = (hr ? right : p) - (hl ? left : p);
            PointD dv = (hd ? down : p) - (hu ? up : p);
            double lh = dh.Length / (hl && hr ? 2 : 1), lv = dv.Length / (hu && hd ? 2 : 1);
            if (lh < 4 || lv < 4) return PointD.NaN;
            dh = dh * (1 / dh.Length);
            dv = dv * (1 / dv.Length);
            double cell = Math.Min(lh, lv);
            LocalLine a = FitLocalLine(lum, w, h, p, dh, lh, hl, hr, polarity, cell);
            LocalLine b = FitLocalLine(lum, w, h, p, dv, lv, hu, hd, polarity, cell);
            if (a == null || b == null) return PointD.NaN;
            // пересечение: a.Origin + t·a.Dir = b.Origin + s·b.Dir
            double det2 = a.Dir.X * (-b.Dir.Y) - a.Dir.Y * (-b.Dir.X);
            if (Math.Abs(det2) < 1e-6) return PointD.NaN;
            double rx = b.Origin.X - a.Origin.X, ry = b.Origin.Y - a.Origin.Y;
            double t = (rx * (-b.Dir.Y) - ry * (-b.Dir.X)) / det2;
            var q = new PointD(a.Origin.X + t * a.Dir.X, a.Origin.Y + t * a.Dir.Y);
            return PointD.Dist(q, p) < 0.25 * cell ? q : PointD.NaN;
        }

        /// <summary>
        /// Привязка точки сетки линий с известными (примерно) соседями: точка — пересечение линий,
        /// найденных по профилям (как при автоматическом поиске). NaN у соседа — соседа нет (край сетки).
        /// Если линии найти не удалось, используется привязка по отклику (<see cref="Snap"/>).
        /// </summary>
        public static PointD SnapLines(ImageData img, PointD p, PointD left, PointD right, PointD up, PointD down, double radius, int polarity)
        {
            PointD q = Snap(img, p, radius, GridPattern.Lines, polarity);
            float[] lum = Luma(img);
            int w = img.Width, h = img.Height;
            if (polarity == 0)
            {
                int r = Math.Max(8, (int)radius * 4);
                polarity = DetectPolarity(Patch(lum, w, h, (int)q.X - r, (int)q.Y - r, 2 * r, 2 * r));
            }
            for (int pass = 0; pass < 2; pass++)
            {
                PointD c = RefineCross(lum, w, h, q, left, right, up, down, polarity);
                if (!c.IsValid) break;
                q = c;
            }
            return q;
        }

        private static LocalLine FitLocalLine(float[] lum, int w, int h, PointD p, PointD dir, double step, bool neg, bool pos,
                                              int polarity, double cell)
        {
            var nrm = new PointD(-dir.Y, dir.X);
            double L = Math.Max(3, 0.3 * cell);
            int ns = (int)Math.Ceiling(L / 0.5);
            var prof = new double[2 * ns + 1];
            var ts = new List<double>();
            var cs = new List<double>();
            for (int side = -1; side <= 1; side += 2)
            {
                if ((side < 0 && !neg) || (side > 0 && !pos)) continue;
                for (int k = 0; k < 10; k++)
                {
                    double t = side * step * (0.18 + 0.27 * k / 9.0);
                    double cx = p.X + dir.X * t, cy = p.Y + dir.Y * t;
                    double vmin = double.MaxValue, vmax = double.MinValue;
                    for (int i = -ns; i <= ns; i++)
                    {
                        double s = i * 0.5;
                        double v = Sample(lum, w, h, cx + nrm.X * s, cy + nrm.Y * s);
                        if (polarity < 0) v = -v;   // светлая линия → тёмная
                        prof[i + ns] = v;
                        vmin = Math.Min(vmin, v); vmax = Math.Max(vmax, v);
                    }
                    if (vmax - vmin < 4) continue;
                    // фон — среднее верхних 30% значений профиля
                    var sorted = (double[])prof.Clone();
                    Array.Sort(sorted);
                    double bg = 0;
                    int top = Math.Max(1, (int)(sorted.Length * 0.3));
                    for (int i = sorted.Length - top; i < sorted.Length; i++) bg += sorted[i];
                    bg /= top;
                    double floor = 0.2 * (bg - vmin);
                    double sw = 0, sx = 0;
                    for (int i = -ns; i <= ns; i++)
                    {
                        double wt = bg - prof[i + ns] - floor;
                        if (wt <= 0) continue;
                        sw += wt; sx += wt * i * 0.5;
                    }
                    if (sw <= 0) continue;
                    ts.Add(t);
                    cs.Add(sx / sw);
                }
            }
            if (ts.Count < 4) return null;
            // c(t) = c0 + c1·t
            double st = 0, sc = 0, stt = 0, stc = 0;
            int m = ts.Count;
            for (int i = 0; i < m; i++) { st += ts[i]; sc += cs[i]; stt += ts[i] * ts[i]; stc += ts[i] * cs[i]; }
            double den = m * stt - st * st;
            if (Math.Abs(den) < 1e-9) return null;
            double c1 = (m * stc - st * sc) / den, c0 = (sc - c1 * st) / m;
            var origin = new PointD(p.X + nrm.X * c0, p.Y + nrm.Y * c0);
            var d2 = new PointD(dir.X + nrm.X * c1, dir.Y + nrm.Y * c1);
            return new LocalLine { Origin = origin, Dir = d2 };
        }

        private static double Sample(float[] img, int w, int h, double x, double y)
        {
            if (x < 0) x = 0; if (y < 0) y = 0;
            if (x > w - 1) x = w - 1; if (y > h - 1) y = h - 1;
            int x0 = (int)x, y0 = (int)y;
            int x1 = x0 + 1 < w ? x0 + 1 : x0, y1 = y0 + 1 < h ? y0 + 1 : y0;
            double fx = x - x0, fy = y - y0;
            double a = img[y0 * w + x0] + (img[y0 * w + x1] - img[y0 * w + x0]) * fx;
            double b = img[y1 * w + x0] + (img[y1 * w + x1] - img[y1 * w + x0]) * fx;
            return a + (b - a) * fy;
        }

        private static float[] Patch(float[] src, int w, int h, int x0, int y0, int pw, int ph)
        {
            var p = new float[pw * ph];
            for (int y = 0; y < ph; y++)
            {
                int sy = Math.Max(0, Math.Min(h - 1, y0 + y));
                for (int x = 0; x < pw; x++)
                {
                    int sx = Math.Max(0, Math.Min(w - 1, x0 + x));
                    p[y * pw + x] = src[sy * w + sx];
                }
            }
            return p;
        }

        /// <summary>
        /// «Привязка» точки, поставленной вручную: ищет рядом (в радиусе radius) центр пересечения линий,
        /// точки или угла клеток и возвращает уточнённое положение. Если ничего не найдено — исходную точку.
        /// </summary>
        public static PointD Snap(ImageData img, PointD p, double radius, GridPattern pattern, int polarity)
        {
            float[] lum = Luma(img);
            int w = img.Width, h = img.Height;
            int rad = Math.Max(3, (int)Math.Ceiling(radius));
            if (pattern == GridPattern.Lines && polarity == 0)
            {
                int x0 = (int)p.X - 4 * rad, y0 = (int)p.Y - 4 * rad;
                polarity = DetectPolarity(Patch(lum, w, h, x0, y0, 8 * rad, 8 * rad));
            }
            double bestR = 0, bestS = 0;
            PointD bestP = p;
            foreach (double s in new[] { 1.0, 1.6, 2.5, 4.0 })
            {
                int pad = rad + (int)Math.Ceiling(3 * s) + 3;
                int x0 = (int)Math.Floor(p.X) - pad, y0 = (int)Math.Floor(p.Y) - pad;
                int pw = 2 * pad + 2, ph = 2 * pad + 2;
                float[] r = Response(Patch(lum, w, h, x0, y0, pw, ph), pw, ph, s, pattern, polarity);
                int cx = (int)Math.Round(p.X) - x0, cy = (int)Math.Round(p.Y) - y0;
                for (int y = cy - rad; y <= cy + rad; y++)
                    for (int x = cx - rad; x <= cx + rad; x++)
                    {
                        if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > rad * rad) continue;
                        double v = r[y * pw + x];
                        if (v > bestR) { bestR = v; bestS = s; bestP = new PointD(x + x0, y + y0); }
                    }
            }
            if (bestR <= 1e-3) return p;
            PointD q = pattern == GridPattern.Checkerboard
                ? CornerSubPix(lum, w, h, bestP, Clamp((int)Math.Round(2.5 * bestS), 3, 15))
                : BlobCentroid(lum, w, h, bestP, bestS, polarity, Math.Max(bestS, radius / 2));
            return q.IsValid && PointD.Dist(q, p) <= rad * 1.5 ? q : bestP;
        }
    }
}
