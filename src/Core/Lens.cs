using System;
using System.Collections.Generic;

namespace VideoMeterCam.Core
{
    /// <summary>
    /// Радиальная дисторсия объектива в форме методички (1.4), (1.5) и модуля Distortion программы Imatest:
    /// r_u = r_d + k1·r_d³ + k2·r_d⁵, где r_d и r_u — искажённый и неискажённый радиусы, нормированные
    /// на полудиагональ кадра (r = 1 в углу кадра). k1 &gt; 0 — бочкообразная дисторсия, k1 &lt; 0 — подушкообразная.
    /// </summary>
    public sealed class LensModel
    {
        public readonly double Cx, Cy;   // центр дисторсии, пикс.
        public readonly double Rn;       // радиус нормировки, пикс.
        public readonly double K1, K2;   // k1 (или h1) и k2 (или h2)
        private readonly double _rdMax;  // граница области, где r_u(r_d) возрастает

        public LensModel(double cx, double cy, double rn, double k1, double k2)
        {
            Cx = cx; Cy = cy; Rn = rn > 0 ? rn : 1; K1 = k1; K2 = k2;
            _rdMax = 10;
            if (k1 < 0 || k2 < 0)
                for (double r = 0.001; r < 10; r += 0.001)
                    if (Derivative(r) <= 1e-3) { _rdMax = r - 0.001; break; }
        }

        /// <summary>Модель для кадра заданного размера: центр — центр кадра, нормировка — полудиагональ.</summary>
        public static LensModel ForFrame(int width, int height, double k1, double k2)
        {
            return new LensModel((width - 1) / 2.0, (height - 1) / 2.0, HalfDiagonal(width, height), k1, k2);
        }

        public static double HalfDiagonal(int width, int height) { return Math.Sqrt((double)width * width + (double)height * height) / 2; }

        public bool IsIdentity { get { return K1 == 0 && K2 == 0; } }

        /// <summary>Множитель r_u / r_d.</summary>
        public double Factor(double rd)
        {
            double r2 = rd * rd;
            return 1 + K1 * r2 + K2 * r2 * r2;
        }

        /// <summary>d r_u / d r_d.</summary>
        public double Derivative(double rd)
        {
            double r2 = rd * rd;
            return 1 + 3 * K1 * r2 + 5 * K2 * r2 * r2;
        }

        /// <summary>Неискажённый радиус по искажённому (прямая формула 1.4 / 1.5).</summary>
        public double Ru(double rd) { return rd * Factor(rd); }

        /// <summary>Искажённый радиус по неискажённому (обращение формулы); NaN — вне области однозначности.</summary>
        public double Rd(double ru)
        {
            if (ru <= 0) return 0;
            if (IsIdentity) return ru;
            double rd = ru;
            for (int i = 0; i < 40; i++)
            {
                if (rd > _rdMax) break;
                double fv = Ru(rd) - ru, fd = Derivative(rd);
                if (fd < 1e-3) break;
                double nrd = rd - fv / fd;
                if (nrd <= 0) nrd = rd / 2;
                if (Math.Abs(nrd - rd) < 1e-13) return nrd <= _rdMax ? nrd : double.NaN;
                rd = nrd;
            }
            // запасной путь — деление пополам на участке монотонности
            if (Ru(_rdMax) < ru) return double.NaN;
            double lo = 0, hi = _rdMax;
            for (int i = 0; i < 64; i++)
            {
                double mid = (lo + hi) / 2;
                if (Ru(mid) < ru) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        /// <summary>Исправление: точка искажённого изображения → точка неискажённого.</summary>
        public PointD Correct(PointD pd)
        {
            double dx = pd.X - Cx, dy = pd.Y - Cy;
            double rd = Math.Sqrt(dx * dx + dy * dy) / Rn;
            double f = Factor(rd);
            return new PointD(Cx + dx * f, Cy + dy * f);
        }

        /// <summary>Внесение дисторсии: точка неискажённого изображения → точка искажённого.</summary>
        public PointD Distort(PointD pu)
        {
            double dx = pu.X - Cx, dy = pu.Y - Cy;
            double ru = Math.Sqrt(dx * dx + dy * dy) / Rn;
            if (ru == 0 || IsIdentity) return pu;
            double rd = Rd(ru);
            if (double.IsNaN(rd)) return PointD.NaN;
            double k = rd / ru;
            return new PointD(Cx + dx * k, Cy + dy * k);
        }

        /// <summary>Локальная геометрическая дисторсия в процентах: 100·(r_d − r_u)/r_u (отрицательна для «бочки»).</summary>
        public double LocalDistortionPercent(double rd)
        {
            double ru = Ru(rd);
            return ru == 0 ? 0 : 100 * (rd - ru) / ru;
        }

        /// <summary>Модель для кадра другого размера (центр и нормировка масштабируются пропорционально).</summary>
        public LensModel Scaled(int fromW, int fromH, int toW, int toH)
        {
            double sx = (double)toW / fromW, sy = (double)toH / fromH;
            double cx = (Cx + 0.5) * sx - 0.5, cy = (Cy + 0.5) * sy - 0.5;
            return new LensModel(cx, cy, HalfDiagonal(toW, toH), K1, K2);
        }

        /// <summary>
        /// ТВ-дисторсия по SMIA: 100·(A − B)/B, где A — высота кадра у левого и правого краёв, B — в центре
        /// для изображения прямоугольника, вписанного в кадр по углам. Отрицательна для бочкообразной дисторсии.
        /// </summary>
        public double SmiaTvDistortion(int width, int height)
        {
            var tl = Correct(new PointD(0, 0)); var tr = Correct(new PointD(width - 1, 0));
            var bl = Correct(new PointD(0, height - 1)); var br = Correct(new PointD(width - 1, height - 1));
            PointD top = Distort(PointD.Mid(tl, tr)), bottom = Distort(PointD.Mid(bl, br));
            if (!top.IsValid || !bottom.IsValid) return double.NaN;
            double a = height - 1, b = PointD.Dist(top, bottom);
            return b > 0 ? 100 * (a - b) / b : double.NaN;
        }
    }

    /// <summary>Результат подгонки модели «проективная сетка + радиальная дисторсия».</summary>
    public sealed class LensFitResult
    {
        public LensModel Model;          // в пикселях исходного изображения
        public Homography H;             // плоскость таблицы → неискажённое изображение, пикс.
        public int Order;                // число коэффициентов дисторсии: 0, 1 (k1) или 2 (k1, k2)
        public bool FitCenter;
        public double Rms;               // СКО отклонения модели от измеренных точек, пикс.
        public double MaxError;
        public double[] Errors;          // по точкам, пикс.
        public PointD[] Predicted;       // положение точек по модели (на искажённом изображении)
        public int Iterations;
    }

    /// <summary>
    /// Подгонка: точки таблицы (координаты на плоскости таблицы) проецируются гомографией на плоскость
    /// изображения, затем искажаются радиальной дисторсией; параметры подбираются так, чтобы
    /// модельные положения совпали с измеренными (минимум суммы квадратов отклонений в пикселях).
    /// Гомография учитывает поворот таблицы относительно камеры (трапецеидальные искажения),
    /// дисторсия — искривление прямых линий.
    /// </summary>
    public static class LensFit
    {
        public static LensFitResult Fit(IList<PointD> ground, IList<PointD> observed, int width, int height,
                                        int order, bool fitCenter, LensFitResult start = null)
        {
            int n = Math.Min(ground.Count, observed.Count);
            if (n < 6) throw new ArgumentException("Для расчёта нужно не меньше 6 точек (найдено " + n + ").");
            order = Math.Max(0, Math.Min(2, order));
            if (order == 0) fitCenter = false;
            double c0x = (width - 1) / 2.0, c0y = (height - 1) / 2.0, rn = LensModel.HalfDiagonal(width, height);

            // нормировка: изображение — к полудиагонали, плоскость таблицы — к единичному среднему радиусу
            var o = new PointD[n];
            var g = new PointD[n];
            double gx = 0, gy = 0;
            for (int i = 0; i < n; i++) { gx += ground[i].X; gy += ground[i].Y; }
            gx /= n; gy /= n;
            double gs = 0;
            for (int i = 0; i < n; i++) gs += PointD.Dist(ground[i], new PointD(gx, gy));
            gs = gs > 0 ? n / gs : 1;
            for (int i = 0; i < n; i++)
            {
                o[i] = new PointD((observed[i].X - c0x) / rn, (observed[i].Y - c0y) / rn);
                g[i] = new PointD((ground[i].X - gx) * gs, (ground[i].Y - gy) * gs);
            }
            var imgN = Homography.Similarity(1 / rn, -c0x / rn, -c0y / rn);
            var grN = Homography.Similarity(gs, -gx * gs, -gy * gs);

            Homography h0;
            if (start != null && start.H != null)
                h0 = Homography.Product(imgN, Homography.Product(start.H, grN.Inverse()));
            else
            {
                // начальное приближение — по исправленным (если известна дисторсия) или по измеренным точкам
                h0 = Homography.Fit(g, o);
            }
            if (h0 == null) throw new InvalidOperationException("Не удалось найти проективное преобразование: точки лежат на одной прямой?");

            int np = 8 + order + (fitCenter ? 2 : 0);
            var p0 = new double[np];
            for (int i = 0; i < 8; i++) p0[i] = h0.H[i];
            if (start != null && start.Model != null)
            {
                if (order >= 1) p0[8] = start.Model.K1;
                if (order >= 2) p0[9] = start.Model.K2;
                if (fitCenter) { p0[8 + order] = (start.Model.Cx - c0x) / rn; p0[9 + order] = (start.Model.Cy - c0y) / rn; }
            }
            var step = new double[np];
            for (int i = 0; i < np; i++) step[i] = 1e-6;
            double regW = 0.05 * rn;

            Func<double[], double[]> res = p =>
            {
                var hh = new double[9];
                for (int i = 0; i < 8; i++) hh[i] = p[i];
                hh[8] = 1;
                var H = new Homography(hh);
                double k1 = order >= 1 ? p[8] : 0, k2 = order >= 2 ? p[9] : 0;
                double dcx = fitCenter ? p[8 + order] : 0, dcy = fitCenter ? p[9 + order] : 0;
                var lens = new LensModel(dcx, dcy, 1, k1, k2);
                var r = new double[2 * n + (fitCenter ? 2 : 0)];
                for (int i = 0; i < n; i++)
                {
                    PointD q = lens.Distort(H.Map(g[i]));
                    if (!q.IsValid) { r[2 * i] = r[2 * i + 1] = 1e3; continue; }
                    r[2 * i] = (q.X - o[i].X) * rn;
                    r[2 * i + 1] = (q.Y - o[i].Y) * rn;
                }
                if (fitCenter) { r[2 * n] = dcx * regW; r[2 * n + 1] = dcy * regW; }
                return r;
            };

            Lm.Result lm = Lm.Minimize(res, p0, step, 300);
            double[] P = lm.P;
            var hn = new double[9];
            for (int i = 0; i < 8; i++) hn[i] = P[i];
            hn[8] = 1;
            var Hn = new Homography(hn);
            double K1 = order >= 1 ? P[8] : 0, K2 = order >= 2 ? P[9] : 0;
            double cx = c0x + (fitCenter ? P[8 + order] * rn : 0), cy = c0y + (fitCenter ? P[9 + order] * rn : 0);

            var result = new LensFitResult
            {
                Model = new LensModel(cx, cy, rn, K1, K2),
                H = Homography.Product(imgN.Inverse(), Homography.Product(Hn, grN)),
                Order = order,
                FitCenter = fitCenter,
                Errors = new double[n],
                Predicted = new PointD[n],
                Iterations = lm.Iterations
            };
            double sum = 0, max = 0;
            for (int i = 0; i < n; i++)
            {
                PointD q = result.Model.Distort(result.H.Map(ground[i]));
                result.Predicted[i] = q;
                double e = q.IsValid ? PointD.Dist(q, observed[i]) : double.NaN;
                result.Errors[i] = e;
                if (!double.IsNaN(e)) { sum += e * e; max = Math.Max(max, e); }
            }
            result.Rms = Math.Sqrt(sum / n);
            result.MaxError = max;
            return result;
        }
    }
}
