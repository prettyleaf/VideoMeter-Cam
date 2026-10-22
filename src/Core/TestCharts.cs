using System;

namespace VideoMeterCam.Core
{
    /// <summary>Параметры имитации съёмки тестовой таблицы камерой.</summary>
    public sealed class ShotSpec
    {
        public int Width = 720, Height = 576;  // кадр камеры (по умолчанию — PAL)
        public double AngleDeg;                // поворот таблицы вокруг вертикальной оси, °
        public double TiltDeg;                 // наклон вокруг горизонтальной оси, °
        public double Fill = 0.8;              // доля ширины кадра, занятая таблицей при угле 0°
        public double Distance = 3;            // расстояние до таблицы в ширинах таблицы (определяет силу перспективы)
        public double K1, K2;                  // дисторсия объектива (1.4), (1.5)
        public double Blur = 0.7;              // размытие, пикс.
        public double Noise = 2;               // СКО шума, уровней
        public double Vignetting = 0.15;       // спад освещённости к углам
        public double Background = 70;         // яркость фона вокруг таблицы
        public bool Color;
        public int? Seed;
    }

    /// <summary>Тестовые таблицы для измерения искажений и имитация их съёмки.</summary>
    public static class TestCharts
    {
        /// <summary>Шахматное поле: cols × rows клеток размером cell, белое поле margin.</summary>
        public static ImageData Checkerboard(int cols, int rows, int cell, int margin)
        {
            int w = cols * cell + 2 * margin, h = rows * cell + 2 * margin;
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Шахматное поле " + cols + "×" + rows;
            float[] p = img.Planes[0];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x - margin, cy = y - margin;
                    bool inside = cx >= 0 && cy >= 0 && cx < cols * cell && cy < rows * cell;
                    p[y * w + x] = inside && (((cx / cell) + (cy / cell)) & 1) == 0 ? 0 : 255;
                }
            return img;
        }

        /// <summary>Сетка: cols × rows клеток, тёмные линии толщиной line на белом.</summary>
        public static ImageData Grid(int cols, int rows, int cell, int line, int margin)
        {
            int w = cols * cell + 2 * margin + line, h = rows * cell + 2 * margin + line;
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Сетка " + cols + "×" + rows;
            float[] p = img.Planes[0];
            for (int i = 0; i < p.Length; i++) p[i] = 255;
            for (int k = 0; k <= cols; k++)
                for (int y = margin; y < margin + rows * cell + line; y++)
                    for (int t = 0; t < line; t++) p[y * w + margin + k * cell + t] = 0;
            for (int k = 0; k <= rows; k++)
                for (int x = margin; x < margin + cols * cell + line; x++)
                    for (int t = 0; t < line; t++) p[(margin + k * cell + t) * w + x] = 0;
            return img;
        }

        /// <summary>
        /// Таблица для калибровки камеры (рис. 1.5): cols × rows точек с шагом step, соединённых тонкими линиями.
        /// Номера точек наносятся отдельно (в интерфейсе).
        /// </summary>
        public static ImageData CalibrationGrid(int cols, int rows, int step, int dot, int line, int margin)
        {
            int w = (cols - 1) * step + 2 * margin + 1, h = (rows - 1) * step + 2 * margin + 1;
            var img = new ImageData(w, h, PixelType.Gray8);
            img.Name = "Таблица калибровки " + cols + "×" + rows;
            float[] p = img.Planes[0];
            for (int i = 0; i < p.Length; i++) p[i] = 255;
            int half = line / 2;
            for (int k = 0; k < cols; k++)
            {
                int x0 = margin + k * step - half;
                for (int y = margin; y <= margin + (rows - 1) * step; y++)
                    for (int t = 0; t < Math.Max(1, line); t++) Set(p, w, h, x0 + t, y, 60);
            }
            for (int k = 0; k < rows; k++)
            {
                int y0 = margin + k * step - half;
                for (int x = margin; x <= margin + (cols - 1) * step; x++)
                    for (int t = 0; t < Math.Max(1, line); t++) Set(p, w, h, x, y0 + t, 60);
            }
            // точки с антиалиасингом
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    double cx = margin + i * step, cy = margin + j * step;
                    for (int y = (int)(cy - dot - 2); y <= cy + dot + 2; y++)
                        for (int x = (int)(cx - dot - 2); x <= cx + dot + 2; x++)
                        {
                            if (x < 0 || y < 0 || x >= w || y >= h) continue;
                            double cover = 0;
                            for (int sy = 0; sy < 4; sy++)
                                for (int sx = 0; sx < 4; sx++)
                                {
                                    double dx = x - 0.375 + sx * 0.25 - cx, dy = y - 0.375 + sy * 0.25 - cy;
                                    if (dx * dx + dy * dy <= dot * dot) cover += 1 / 16.0;
                                }
                            int k = y * w + x;
                            p[k] = (float)Math.Round(p[k] * (1 - cover));
                        }
                }
            return img;
        }

        private static void Set(float[] p, int w, int h, int x, int y, float v)
        {
            if (x >= 0 && y >= 0 && x < w && y < h) p[y * w + x] = Math.Min(p[y * w + x], v);
        }

        /// <summary>
        /// Имитация съёмки таблицы камерой: центральная проекция плоскости таблицы, повёрнутой на угол α
        /// вокруг вертикальной оси (и наклонённой вокруг горизонтальной), дисторсия объектива, размытие,
        /// виньетирование и шум.
        /// </summary>
        public static ImageData Shoot(ImageData chart, ShotSpec s)
        {
            ImageData src = chart.IsRgb ? ImageOps.ConvertTo(chart, PixelType.Gray8) : chart;
            int W = s.Width, H = s.Height;
            double cw = src.Width, ch = src.Height;
            double D = Math.Max(0.3, s.Distance);
            double f = s.Fill * W * D;                    // фокусное расстояние, пикс. (ширина таблицы = 1)
            double a = s.AngleDeg * Math.PI / 180, b = s.TiltDeg * Math.PI / 180;
            // оси таблицы в системе камеры: ex — вдоль строк, ey — вдоль столбцов, центр таблицы на оси z
            double exX = Math.Cos(a), exY = 0, exZ = Math.Sin(a);
            double eyX = Math.Sin(a) * Math.Sin(b), eyY = Math.Cos(b), eyZ = -Math.Cos(a) * Math.Sin(b);
            double nX = exY * eyZ - exZ * eyY, nY = exZ * eyX - exX * eyZ, nZ = exX * eyY - exY * eyX;
            var lens = LensModel.ForFrame(W, H, s.K1, s.K2);
            double cx = (W - 1) / 2.0, cy = (H - 1) / 2.0;
            var lin = new float[W * H];
            const int SS = 3;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double acc = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            var pd = new PointD(x - 0.5 + (sx + 0.5) / SS, y - 0.5 + (sy + 0.5) / SS);
                            PointD pu = lens.Correct(pd);
                            double dx = (pu.X - cx) / f, dy = (pu.Y - cy) / f, dz = 1;
                            double den = nX * dx + nY * dy + nZ * dz;
                            double val = s.Background;
                            if (Math.Abs(den) > 1e-12)
                            {
                                double t = nZ * D / den;
                                if (t > 0)
                                {
                                    double px = t * dx, py = t * dy, pz = t * dz - D;
                                    double u = px * exX + py * exY + pz * exZ, v = px * eyX + py * eyY + pz * eyZ;
                                    double ix = (u + 0.5) * cw - 0.5, iy = v * cw + ch / 2 - 0.5;
                                    float sv = ImageOps.Bilinear(src, 0, ix, iy);
                                    if (!float.IsNaN(sv)) val = sv;
                                }
                            }
                            acc += val;
                        }
                    double r2 = ((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (cx * cx + cy * cy);
                    lin[y * W + x] = (float)(acc / (SS * SS) * (1 - s.Vignetting * r2));
                }
            if (s.Blur > 0.05) lin = GridDetect.Gaussian(lin, W, H, s.Blur);
            var rnd = s.Seed.HasValue ? new Random(s.Seed.Value) : new Random();
            var img = new ImageData(W, H, s.Color ? PixelType.Rgb24 : PixelType.Gray8);
            img.Name = "Снимок: " + chart.Name;
            float[] tint = { 1.0f, 0.97f, 0.93f };
            for (int c = 0; c < img.Channels; c++)
            {
                float[] o = img.Planes[c];
                for (int i = 0; i < o.Length; i++)
                    o[i] = (float)(lin[i] * (s.Color ? tint[c] : 1) + Gauss(rnd) * s.Noise);
            }
            img.Conform();
            return img;
        }

        private static double Gauss(Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
