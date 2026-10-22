using System;

namespace SigmaLabCam.Core
{
    /// <summary>Способ пересчёта координат при геометрическом преобразовании.</summary>
    public enum MapMethod
    {
        Forward, // прямой: каждый пиксель исходного изображения переносится в новое положение (как в Mathcad; появляются пропуски)
        Inverse  // обратный: для каждого пикселя результата берётся значение исходного с билинейной интерполяцией
    }

    /// <summary>Геометрические преобразования изображений: модель сферы (1.12), (1.13), радиальная дисторсия, заполнение пропусков.</summary>
    public static class Warp
    {
        /// <summary>Обратное отображение: для пикселя результата (x, y) функция возвращает точку исходного изображения.</summary>
        public static ImageData Remap(ImageData src, int outW, int outH, Func<double, double, PointD> toSource)
        {
            var r = new ImageData(outW, outH, src.Type);
            r.Name = src.Name;
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    PointD p = toSource(x, y);
                    if (!p.IsValid) continue;
                    for (int c = 0; c < src.Channels; c++)
                    {
                        float v = ImageOps.Bilinear(src, c, p.X, p.Y);
                        if (!float.IsNaN(v)) r.Planes[c][y * outW + x] = v;
                    }
                }
            r.Conform();
            return r;
        }

        /// <summary>Прямое отображение: пиксель (x, y) исходного переносится в точку, которую вернёт функция (с округлением).</summary>
        public static ImageData ForwardMap(ImageData src, Func<double, double, PointD> toTarget)
        {
            int w = src.Width, h = src.Height;
            var r = new ImageData(w, h, src.Type);
            r.Name = src.Name;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    PointD p = toTarget(x, y);
                    if (!p.IsValid) continue;
                    int tx = (int)Math.Round(p.X), ty = (int)Math.Round(p.Y);
                    if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                    for (int c = 0; c < src.Channels; c++) r.Planes[c][ty * w + tx] = src.Planes[c][y * w + x];
                }
            r.Conform();
            return r;
        }

        // ------------------------------------------------------------------ модель сферы

        /// <summary>(1.12) ρ1 = R·ρ / √(R² + ρ²).</summary>
        public static double SphereRho1(double rho, double R) { return R * rho / Math.Sqrt(R * R + rho * rho); }

        /// <summary>(1.13) ρ = R·ρ1 / √(R² − ρ1²); NaN при ρ1 ≥ R.</summary>
        public static double SphereRho(double rho1, double R) { return rho1 < R ? R * rho1 / Math.Sqrt(R * R - rho1 * rho1) : double.NaN; }

        /// <summary>Сферические искажения по формуле (1.12); R и центр — в пикселях.</summary>
        public static ImageData SphereDistort(ImageData src, double R, double cx, double cy, MapMethod m)
        {
            if (R <= 0) throw new ArgumentException("Радиус сферы R должен быть положительным.");
            if (m == MapMethod.Forward)
                return ForwardMap(src, (x, y) => Scale(x, y, cx, cy, rho => SphereRho1(rho, R)));
            return Remap(src, src.Width, src.Height, (x, y) => Scale(x, y, cx, cy, rho1 => SphereRho(rho1, R)));
        }

        /// <summary>Коррекция сферических искажений по формуле (1.13).</summary>
        public static ImageData SphereCorrect(ImageData src, double R, double cx, double cy, MapMethod m)
        {
            if (R <= 0) throw new ArgumentException("Радиус сферы R должен быть положительным.");
            if (m == MapMethod.Forward)
                return ForwardMap(src, (x, y) => Scale(x, y, cx, cy, rho1 => SphereRho(rho1, R)));
            return Remap(src, src.Width, src.Height, (x, y) => Scale(x, y, cx, cy, rho => SphereRho1(rho, R)));
        }

        private static PointD Scale(double x, double y, double cx, double cy, Func<double, double> radius)
        {
            double dx = x - cx, dy = y - cy;
            double rho = Math.Sqrt(dx * dx + dy * dy);
            if (rho < 1e-9) return new PointD(x, y);
            double r2 = radius(rho);
            if (double.IsNaN(r2)) return PointD.NaN;
            double k = r2 / rho;
            return new PointD(cx + dx * k, cy + dy * k);
        }

        // ------------------------------------------------------------------ радиальная дисторсия (1.4), (1.5)

        /// <summary>Вносит дисторсию: результат — изображение, снятое объективом с этой дисторсией.</summary>
        public static ImageData LensDistort(ImageData src, LensModel lens)
        {
            return Remap(src, src.Width, src.Height, (x, y) => lens.Correct(new PointD(x, y)));
        }

        /// <summary>Исправляет дисторсию объектива (Corrected Source Image).</summary>
        public static ImageData LensCorrect(ImageData src, LensModel lens)
        {
            return Remap(src, src.Width, src.Height, (x, y) => lens.Distort(new PointD(x, y)));
        }

        // ------------------------------------------------------------------ рис. 1.9

        /// <summary>Пиксель считается незаполненным, если все его каналы равны нулю.</summary>
        public static bool IsGap(ImageData img, int i)
        {
            for (int c = 0; c < img.Channels; c++) if (img.Planes[c][i] != 0) return false;
            return true;
        }

        public static int CountGaps(ImageData img)
        {
            int n = 0;
            for (int i = 0; i < img.PixelCount; i++) if (IsGap(img, i)) n++;
            return n;
        }

        /// <summary>
        /// Адаптивная линейная интерполяция (рис. 1.9): изображение обходится по строкам и столбцам;
        /// нулевому элементу присваивается среднее ненулевых элементов из окрестности 3×3.
        /// Обход ведётся «на месте», как в алгоритме: уже заполненные элементы участвуют в следующих.
        /// </summary>
        public static ImageData FillGaps(ImageData src)
        {
            var r = src.Clone();
            int w = r.Width, h = r.Height, ch = r.Channels;
            var sum = new double[ch];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!IsGap(r, i)) continue;
                    int k = 0;
                    for (int c = 0; c < ch; c++) sum[c] = 0;
                    for (int n = -1; n <= 1; n++)
                        for (int m = -1; m <= 1; m++)
                        {
                            int xx = x + m, yy = y + n;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int j = yy * w + xx;
                            if (IsGap(r, j)) continue;
                            for (int c = 0; c < ch; c++) sum[c] += r.Planes[c][j];
                            k++;
                        }
                    if (k > 0)
                        for (int c = 0; c < ch; c++) r.Planes[c][i] = (float)(sum[c] / k);
                }
            r.Conform();
            return r;
        }
    }
}
