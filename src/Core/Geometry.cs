using System;
using System.Collections.Generic;

namespace SigmaLabCam.Core
{
    /// <summary>Точка с вещественными координатами (пиксели: центр пикселя (i, j) — точка (i, j)).</summary>
    public struct PointD
    {
        public double X, Y;
        public PointD(double x, double y) { X = x; Y = y; }
        public static PointD operator +(PointD a, PointD b) { return new PointD(a.X + b.X, a.Y + b.Y); }
        public static PointD operator -(PointD a, PointD b) { return new PointD(a.X - b.X, a.Y - b.Y); }
        public static PointD operator *(PointD a, double k) { return new PointD(a.X * k, a.Y * k); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public static double Dist(PointD a, PointD b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        public static PointD Mid(PointD a, PointD b) { return new PointD((a.X + b.X) / 2, (a.Y + b.Y) / 2); }
        public bool IsValid { get { return !double.IsNaN(X) && !double.IsNaN(Y) && !double.IsInfinity(X) && !double.IsInfinity(Y); } }
        public static readonly PointD NaN = new PointD(double.NaN, double.NaN);
        public override string ToString() { return X.ToString("0.##") + "; " + Y.ToString("0.##"); }
    }

    /// <summary>Решение линейных систем (метод Гаусса с выбором главного элемента).</summary>
    public static class LinAlg
    {
        /// <summary>Решает A·x = b. Матрица и вектор не изменяются. Возвращает null для вырожденной системы.</summary>
        public static double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] = a[i, j];
                m[i, n] = b[i];
            }
            double scale = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) scale = Math.Max(scale, Math.Abs(m[i, j]));
            if (scale == 0) return null;
            for (int c = 0; c < n; c++)
            {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
                if (Math.Abs(m[p, c]) < scale * 1e-14) return null;
                if (p != c)
                    for (int j = c; j <= n; j++) { double t = m[c, j]; m[c, j] = m[p, j]; m[p, j] = t; }
                for (int r = c + 1; r < n; r++)
                {
                    double f = m[r, c] / m[c, c];
                    if (f == 0) continue;
                    for (int j = c; j <= n; j++) m[r, j] -= f * m[c, j];
                }
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = m[i, n];
                for (int j = i + 1; j < n; j++) s -= m[i, j] * x[j];
                x[i] = s / m[i, i];
            }
            return x;
        }

        /// <summary>Линейный МНК: строки A (rows × n) и правые части b — через нормальные уравнения.</summary>
        public static double[] LeastSquares(List<double[]> rows, List<double> b)
        {
            int n = rows[0].Length;
            var ata = new double[n, n];
            var atb = new double[n];
            for (int k = 0; k < rows.Count; k++)
            {
                double[] r = rows[k];
                for (int i = 0; i < n; i++)
                {
                    atb[i] += r[i] * b[k];
                    for (int j = 0; j < n; j++) ata[i, j] += r[i] * r[j];
                }
            }
            return Solve(ata, atb);
        }
    }

    /// <summary>Проективное преобразование плоскости (гомография), h33 = 1.</summary>
    public sealed class Homography
    {
        public readonly double[] H = new double[9];

        public Homography() { H[0] = H[4] = H[8] = 1; }
        public Homography(double[] h) { Array.Copy(h, H, 9); }

        public PointD Map(double x, double y)
        {
            double w = H[6] * x + H[7] * y + H[8];
            if (Math.Abs(w) < 1e-15) return PointD.NaN;
            return new PointD((H[0] * x + H[1] * y + H[2]) / w, (H[3] * x + H[4] * y + H[5]) / w);
        }

        public PointD Map(PointD p) { return Map(p.X, p.Y); }

        public Homography Inverse()
        {
            double[] h = H;
            double a = h[4] * h[8] - h[5] * h[7], b = h[5] * h[6] - h[3] * h[8], c = h[3] * h[7] - h[4] * h[6];
            double det = h[0] * a + h[1] * b + h[2] * c;
            if (Math.Abs(det) < 1e-300) return null;
            var r = new double[]
            {
                a, h[2] * h[7] - h[1] * h[8], h[1] * h[5] - h[2] * h[4],
                b, h[0] * h[8] - h[2] * h[6], h[2] * h[3] - h[0] * h[5],
                c, h[1] * h[6] - h[0] * h[7], h[0] * h[4] - h[1] * h[3]
            };
            for (int i = 0; i < 9; i++) r[i] /= det;
            if (Math.Abs(r[8]) > 1e-300) for (int i = 0; i < 9; i++) r[i] /= r[8];
            return new Homography(r);
        }

        /// <summary>
        /// Гомография src → dst по методу DLT с нормализацией координат (Хартли). Нужно не меньше 4 точек
        /// общего положения. Возвращает null, если решение не найдено.
        /// </summary>
        public static Homography Fit(IList<PointD> src, IList<PointD> dst)
        {
            int n = Math.Min(src.Count, dst.Count);
            if (n < 4) return null;
            double[] ts = NormTransform(src, n), td = NormTransform(dst, n);
            var rows = new List<double[]>();
            var rhs = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double X = (src[i].X - ts[0]) * ts[2], Y = (src[i].Y - ts[1]) * ts[2];
                double x = (dst[i].X - td[0]) * td[2], y = (dst[i].Y - td[1]) * td[2];
                rows.Add(new[] { X, Y, 1, 0, 0, 0, -X * x, -Y * x }); rhs.Add(x);
                rows.Add(new[] { 0, 0, 0, X, Y, 1, -X * y, -Y * y }); rhs.Add(y);
            }
            double[] h = LinAlg.LeastSquares(rows, rhs);
            if (h == null) return null;
            // H = Td⁻¹ · Hn · Ts
            var hn = new[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 };
            var Ts = new[] { ts[2], 0, -ts[0] * ts[2], 0, ts[2], -ts[1] * ts[2], 0, 0, 1 };
            var TdInv = new[] { 1 / td[2], 0, td[0], 0, 1 / td[2], td[1], 0, 0, 1 };
            double[] m = Mul(TdInv, Mul(hn, Ts));
            if (Math.Abs(m[8]) < 1e-300) return null;
            for (int i = 0; i < 9; i++) m[i] /= m[8];
            return new Homography(m);
        }

        private static double[] NormTransform(IList<PointD> p, int n)
        {
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += p[i].X; my += p[i].Y; }
            mx /= n; my /= n;
            double d = 0;
            for (int i = 0; i < n; i++) d += Math.Sqrt((p[i].X - mx) * (p[i].X - mx) + (p[i].Y - my) * (p[i].Y - my));
            d /= n;
            return new[] { mx, my, d > 0 ? Math.Sqrt(2) / d : 1 };
        }

        /// <summary>Композиция: сначала b, затем a.</summary>
        public static Homography Product(Homography a, Homography b)
        {
            double[] m = Mul(a.H, b.H);
            if (Math.Abs(m[8]) > 1e-300) for (int i = 0; i < 9; i++) m[i] /= m[8];
            return new Homography(m);
        }

        /// <summary>Подобие: p → p·s + t.</summary>
        public static Homography Similarity(double s, double tx, double ty)
        {
            return new Homography(new[] { s, 0, tx, 0, s, ty, 0, 0, 1.0 });
        }

        private static double[] Mul(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return r;
        }
    }

    /// <summary>Нелинейный метод наименьших квадратов (Левенберг — Марквардт, численный якобиан).</summary>
    public static class Lm
    {
        public sealed class Result
        {
            public double[] P;
            public double Cost;     // сумма квадратов невязок
            public int Iterations;
        }

        /// <param name="f">функция невязок</param>
        /// <param name="p0">начальное приближение</param>
        /// <param name="step">шаг численного дифференцирования по каждому параметру</param>
        public static Result Minimize(Func<double[], double[]> f, double[] p0, double[] step, int maxIter = 200)
        {
            int n = p0.Length;
            var p = (double[])p0.Clone();
            double[] r = f(p);
            double cost = SumSq(r);
            double lambda = 1e-3;
            int it = 0;
            bool done = false;
            for (; it < maxIter && !done; it++)
            {
                int m = r.Length;
                var J = new double[m, n];
                for (int k = 0; k < n; k++)
                {
                    double h = step[k];
                    var pp = (double[])p.Clone();
                    var pm = (double[])p.Clone();
                    pp[k] += h; pm[k] -= h;
                    double[] rp = f(pp), rm = f(pm);
                    for (int i = 0; i < m; i++) J[i, k] = (rp[i] - rm[i]) / (2 * h);
                }
                var jtj = new double[n, n];
                var jtr = new double[n];
                for (int i = 0; i < m; i++)
                    for (int a = 0; a < n; a++)
                    {
                        double ja = J[i, a];
                        if (ja == 0) continue;
                        jtr[a] += ja * r[i];
                        for (int b = a; b < n; b++) jtj[a, b] += ja * J[i, b];
                    }
                for (int a = 0; a < n; a++) for (int b = 0; b < a; b++) jtj[a, b] = jtj[b, a];

                bool improved = false;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var A = new double[n, n];
                    for (int a = 0; a < n; a++)
                    {
                        for (int b = 0; b < n; b++) A[a, b] = jtj[a, b];
                        A[a, a] += lambda * (jtj[a, a] > 0 ? jtj[a, a] : 1e-12);
                    }
                    var g = new double[n];
                    for (int a = 0; a < n; a++) g[a] = -jtr[a];
                    double[] d = LinAlg.Solve(A, g);
                    if (d == null) { lambda *= 10; continue; }
                    var pn = new double[n];
                    for (int a = 0; a < n; a++) pn[a] = p[a] + d[a];
                    double[] rn = f(pn);
                    double cn = SumSq(rn);
                    if (!double.IsNaN(cn) && cn < cost)
                    {
                        double rel = (cost - cn) / Math.Max(cost, 1e-300);
                        p = pn; r = rn; cost = cn;
                        lambda = Math.Max(lambda / 10, 1e-12);
                        improved = true;
                        if (rel < 1e-12) done = true;
                        break;
                    }
                    lambda *= 10;
                }
                if (!improved) break;
            }
            return new Result { P = p, Cost = cost, Iterations = it };
        }

        public static double SumSq(double[] r)
        {
            double s = 0;
            foreach (double v in r) s += v * v;
            return double.IsNaN(s) ? double.PositiveInfinity : s;
        }
    }
}
