using System;
using System.Collections.Generic;
using System.Globalization;

namespace SigmaLabCam.Core
{
    /// <summary>
    /// Шаблон калибровочной таблицы (аналог файла ASR-grid.dbf программы ASRix): сетка Cols × Rows точек,
    /// пронумерованных слева направо и сверху вниз с единицы, с шагом Step (мм).
    /// </summary>
    public sealed class GridTemplate
    {
        public int Cols = 13, Rows = 10;
        public double Step = 20;
        public string Name = "ASR-grid";

        public int Count { get { return Cols * Rows; } }

        public int Col(int number) { return (number - 1) % Cols; }
        public int Row(int number) { return (number - 1) / Cols; }
        public int Number(int col, int row) { return row * Cols + col + 1; }

        /// <summary>Координаты точки на плоскости таблицы, мм (X — вправо, Y — вниз).</summary>
        public PointD Ground(int number) { return new PointD(Col(number) * Step, Row(number) * Step); }

        public string Description { get { return Name + " " + Cols + "×" + Rows + " точек, шаг " + Step.ToString("0.##", CultureInfo.CurrentCulture) + " мм"; } }

        public GridTemplate Clone() { return (GridTemplate)MemberwiseClone(); }

        // Точки рис. 1.6 / 1.7 для любой сетки: углы и середины сторон.
        public int PointA { get { return 1; } }
        public int PointB { get { return Cols; } }
        public int PointC { get { return (Rows - 1) * Cols + 1; } }
        public int PointD { get { return Rows * Cols; } }

        /// <summary>Середина верхней стороны: одна точка (нечётное число столбцов) или две соседние.</summary>
        public int[] PointE { get { return MidRow(0); } }
        public int[] PointH { get { return MidRow(Rows - 1); } }
        public int[] PointF { get { return MidCol(0); } }
        public int[] PointG { get { return MidCol(Cols - 1); } }

        private int[] MidRow(int row)
        {
            if (Cols % 2 == 1) return new[] { Number(Cols / 2, row) };
            return new[] { Number(Cols / 2 - 1, row), Number(Cols / 2, row) };
        }

        private int[] MidCol(int col)
        {
            if (Rows % 2 == 1) return new[] { Number(col, Rows / 2) };
            return new[] { Number(col, Rows / 2 - 1), Number(col, Rows / 2) };
        }
    }

    /// <summary>Параметры камеры, полученные калибровкой (Camera Management).</summary>
    public sealed class CameraProfile
    {
        public string Name = "Камера";
        public int Width, Height;        // размер кадра при калибровке
        public double Cx, Cy;            // центр дисторсии, пикс.
        public double K1, K2;
        public double Rms;               // СКО по точкам после калибровки, пикс.
        public int Points;
        public DateTime Date = DateTime.Now;

        public LensModel ModelFor(int width, int height)
        {
            var m = new LensModel(Cx, Cy, LensModel.HalfDiagonal(Width, Height), K1, K2);
            if (width == Width && height == Height) return m;
            return m.Scaled(Width, Height, width, height);
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Serialize()
        {
            return string.Join("|", new[]
            {
                Name.Replace("|", "/"), Width.ToString(Inv), Height.ToString(Inv), Cx.ToString("R", Inv), Cy.ToString("R", Inv),
                K1.ToString("R", Inv), K2.ToString("R", Inv), Rms.ToString("R", Inv), Points.ToString(Inv), Date.ToString("yyyy-MM-dd HH:mm", Inv)
            });
        }

        public static CameraProfile Parse(string s)
        {
            string[] p = s.Split('|');
            if (p.Length < 9) return null;
            try
            {
                var c = new CameraProfile
                {
                    Name = p[0],
                    Width = int.Parse(p[1], Inv), Height = int.Parse(p[2], Inv),
                    Cx = double.Parse(p[3], Inv), Cy = double.Parse(p[4], Inv),
                    K1 = double.Parse(p[5], Inv), K2 = double.Parse(p[6], Inv),
                    Rms = double.Parse(p[7], Inv), Points = int.Parse(p[8], Inv)
                };
                DateTime d;
                if (p.Length > 9 && DateTime.TryParseExact(p[9], "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out d)) c.Date = d;
                return c.Width > 0 && c.Height > 0 ? c : null;
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }
    }

    public sealed class CalibrationResult
    {
        public CameraProfile Camera;
        public LensFitResult Fit;
        public LensFitResult FitNoDistortion;
        public int[] Numbers;            // номера точек в порядке Fit.Errors
    }

    /// <summary>Калибровка камеры по снимку плоской сетки и выпрямление изображения (аналог ASRix).</summary>
    public static class Calibration
    {
        public static CalibrationResult Calibrate(IDictionary<int, PointD> points, GridTemplate t, int width, int height,
                                                  bool fitCenter, int order, string name)
        {
            var nums = new List<int>();
            var ground = new List<PointD>();
            var obs = new List<PointD>();
            foreach (var kv in points)
            {
                if (kv.Key < 1 || kv.Key > t.Count || !kv.Value.IsValid) continue;
                nums.Add(kv.Key);
                ground.Add(t.Ground(kv.Key));
                obs.Add(kv.Value);
            }
            if (nums.Count < 12) throw new InvalidOperationException("Для калибровки нужно расставить не меньше 12 точек (сейчас " + nums.Count + ").");
            var f0 = LensFit.Fit(ground, obs, width, height, 0, false);
            var f1 = LensFit.Fit(ground, obs, width, height, 1, fitCenter, f0);
            var f = order >= 2 ? LensFit.Fit(ground, obs, width, height, 2, fitCenter, f1) : f1;
            var cam = new CameraProfile
            {
                Name = string.IsNullOrEmpty(name) ? "Камера" : name,
                Width = width, Height = height,
                Cx = f.Model.Cx, Cy = f.Model.Cy, K1 = f.Model.K1, K2 = f.Model.K2,
                Rms = f.Rms, Points = nums.Count
            };
            return new CalibrationResult { Camera = cam, Fit = f, FitNoDistortion = f0, Numbers = nums.ToArray() };
        }

        /// <summary>
        /// Проективное преобразование «плоскость таблицы → исправленное изображение» по точкам,
        /// исправленным заданной моделью объектива.
        /// </summary>
        public static Homography PlaneToImage(IDictionary<int, PointD> points, GridTemplate t, LensModel lens)
        {
            var ground = new List<PointD>();
            var und = new List<PointD>();
            foreach (var kv in points)
            {
                if (kv.Key < 1 || kv.Key > t.Count || !kv.Value.IsValid) continue;
                ground.Add(t.Ground(kv.Key));
                und.Add(lens != null ? lens.Correct(kv.Value) : kv.Value);
            }
            return Homography.Fit(ground, und);
        }

        /// <summary>
        /// Выпрямление (Rectify): изображение плоскости таблицы «во фронтальном виде» — без дисторсии и перспективы.
        /// pxPerStep — пикселей на шаг сетки; margin — поле вокруг сетки в шагах сетки.
        /// </summary>
        public static ImageData Rectify(ImageData src, LensModel lens, Homography planeToImage, GridTemplate t,
                                        double pxPerStep, double margin, out Homography outputToPlane)
        {
            double s = pxPerStep / t.Step;                   // пикс./мм
            double x0 = -margin * t.Step, y0 = -margin * t.Step;
            int w = (int)Math.Round(((t.Cols - 1) + 2 * margin) * pxPerStep) + 1;
            int h = (int)Math.Round(((t.Rows - 1) + 2 * margin) * pxPerStep) + 1;
            if (w < 8 || h < 8) throw new ArgumentException("Слишком маленький масштаб выпрямленного изображения.");
            if ((double)w * h > 64e6) throw new ArgumentException("Слишком большой размер выпрямленного изображения (" + w + "×" + h + "): уменьшите масштаб.");
            outputToPlane = Homography.Similarity(1 / s, x0, y0);
            Homography o2p = outputToPlane;
            ImageData r = Warp.Remap(src, w, h, (x, y) =>
            {
                PointD pu = planeToImage.Map(o2p.Map(x, y));
                return lens != null ? lens.Distort(pu) : pu;
            });
            r.Name = src.Name;
            return r;
        }
    }
}
