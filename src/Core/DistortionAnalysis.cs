using System;
using System.Collections.Generic;
using System.Drawing;

namespace VideoMeterCam.Core
{
    public enum CorrectedImageMode { Always, Never }

    /// <summary>Настройки модуля Distortion (названия — как в Imatest).</summary>
    public sealed class DistortionSettings
    {
        public string Title = "";
        public GridPattern Pattern = GridPattern.Checkerboard;
        public int Polarity;                       // для сетки: 0 — авто, +1 — тёмные линии, −1 — светлые
        public int Order = 3;                      // Calculate / display using: 3 — 3-й порядок, 5 — 5-й порядок
        public bool Decentering;                   // Decentering calculation
        public bool PlotDistortion = true;         // Plot distortion figure
        public bool PlotPoints = true;             // intersection points
        public bool PlotRadial = true;             // Radial plot
        public bool PlotRadiusCorrection = true;   // Plot radius correction
        public CorrectedImageMode CorrectedImage = CorrectedImageMode.Always; // Display corrected image
        public double Sigma;                       // масштаб поиска узлов, 0 — авто

        public DistortionSettings Clone() { return (DistortionSettings)MemberwiseClone(); }
    }

    /// <summary>Коэффициенты по формулам (1.1)–(1.3), рассчитанные по найденной решётке.</summary>
    public sealed class GridCoefficients
    {
        public int I0, I1, J0, J1;          // прямоугольник решётки, по которому считались
        public SideCoefficients Sides;
        public double CellHMax, CellHMin, CellBMax, CellBMin;
        public double Kv, Kh;               // (1.3): Kгв — по вертикали, Kгг — по горизонтали
        public double TrapH, TrapV;         // (1.2)

        public static GridCoefficients Compute(GridDetection det)
        {
            int i0, i1, j0, j1;
            if (!det.FullRectangle(out i0, out i1, out j0, out j1)) return null;
            Func<int, int, PointD> P = (i, j) => det.At(i, j).P;
            Func<int, int, int, int, PointD> mid = (ia, ja, ib, jb) => PointD.Mid(P(ia, ja), P(ib, jb));
            int im = (i0 + i1) / 2, jm = (j0 + j1) / 2;
            bool oddI = (i1 - i0) % 2 == 1, oddJ = (j1 - j0) % 2 == 1;
            PointD e = oddI ? mid(im, j0, im + 1, j0) : P(im, j0);
            PointD hh = oddI ? mid(im, j1, im + 1, j1) : P(im, j1);
            PointD f = oddJ ? mid(i0, jm, i0, jm + 1) : P(i0, jm);
            PointD g = oddJ ? mid(i1, jm, i1, jm + 1) : P(i1, jm);
            var r = new GridCoefficients { I0 = i0, I1 = i1, J0 = j0, J1 = j1 };
            r.Sides = SideCoefficients.Compute(P(i0, j0), P(i1, j0), P(i0, j1), P(i1, j1), e, f, g, hh);
            r.TrapH = r.Sides.TrapezoidHorizontal;
            r.TrapV = r.Sides.TrapezoidVertical;
            // размеры клеток вдоль средних линий (нелинейность, рис. 1.3)
            bool colOk = true, rowOk = true;
            for (int j = j0; j <= j1; j++) if (det.At(im, j) == null) colOk = false;
            for (int i = i0; i <= i1; i++) if (det.At(i, jm) == null) rowOk = false;
            r.CellHMax = r.CellBMax = double.NaN;
            r.CellHMin = r.CellBMin = double.NaN;
            if (colOk)
            {
                r.CellHMax = double.MinValue; r.CellHMin = double.MaxValue;
                for (int j = j0; j < j1; j++)
                {
                    double v = PointD.Dist(P(im, j), P(im, j + 1));
                    r.CellHMax = Math.Max(r.CellHMax, v); r.CellHMin = Math.Min(r.CellHMin, v);
                }
            }
            if (rowOk)
            {
                r.CellBMax = double.MinValue; r.CellBMin = double.MaxValue;
                for (int i = i0; i < i1; i++)
                {
                    double v = PointD.Dist(P(i, jm), P(i + 1, jm));
                    r.CellBMax = Math.Max(r.CellBMax, v); r.CellBMin = Math.Min(r.CellBMin, v);
                }
            }
            r.Kv = colOk ? Coef.Nonlinearity(r.CellHMax, r.CellHMin) : double.NaN;
            r.Kh = rowOk ? Coef.Nonlinearity(r.CellBMax, r.CellBMin) : double.NaN;
            return r;
        }
    }

    public sealed class DistortionResult
    {
        public string FileName, Title;
        public int ImageWidth, ImageHeight;
        public Rectangle Roi;
        public DistortionSettings Settings;
        public GridDetection Detection;
        public LensFitResult Fit0, Fit3, Fit5;      // без дисторсии, 3-й и 5-й порядок
        public GridCoefficients Grid;
        public int Rejected;                         // узлов отброшено как выбросы
        public DateTime Time = DateTime.Now;

        public LensFitResult Selected { get { return Settings.Order == 5 ? Fit5 : Fit3; } }
        public double K1 { get { return Fit3.Model.K1; } }
        public double H1 { get { return Fit5.Model.K1; } }
        public double H2 { get { return Fit5.Model.K2; } }
        public double SmiaTv { get { return Selected.Model.SmiaTvDistortion(ImageWidth, ImageHeight); } }
        public double CornerDistortion { get { return Selected.Model.LocalDistortionPercent(1); } }

        /// <summary>Нормированный радиус r_d узла (от центра дисторсии выбранной модели).</summary>
        public double NodeRd(int k)
        {
            var m = Selected.Model;
            var p = Detection.Nodes[k].P;
            return Math.Sqrt((p.X - m.Cx) * (p.X - m.Cx) + (p.Y - m.Cy) * (p.Y - m.Cy)) / m.Rn;
        }

        /// <summary>Нормированный неискажённый радиус r_u узла по найденной проективной сетке.</summary>
        public double NodeRu(int k)
        {
            var m = Selected.Model;
            var n = Detection.Nodes[k];
            PointD q = Selected.H.Map(n.I, n.J);
            return Math.Sqrt((q.X - m.Cx) * (q.X - m.Cx) + (q.Y - m.Cy) * (q.Y - m.Cy)) / m.Rn;
        }
    }

    /// <summary>
    /// Аналог модуля Distortion программы Imatest: поиск узлов тестовой таблицы, подбор коэффициентов
    /// радиальной дисторсии 3-го (k1) и 5-го (h1, h2) порядка по формулам (1.4), (1.5) и расчёт
    /// ТВ-дисторсии SMIA и коэффициентов (1.1)–(1.3) по крайним линиям таблицы.
    /// </summary>
    public static class DistortionAnalysis
    {
        public static DistortionResult Analyze(ImageData img, Rectangle roi, DistortionSettings s, GridDetection det = null)
        {
            det = det == null ? GridDetect.Detect(img, roi, s.Pattern, s.Sigma, s.Polarity) : det.Clone();
            if (det.Nodes.Count < 9 || det.Cols < 3 || det.Rows < 3)
                throw new InvalidOperationException("Узлы тестовой таблицы не найдены (найдено " + det.Nodes.Count +
                    "). Проверьте тип таблицы (Pattern) и выделенную область: в ней должно быть не меньше 3×3 узлов.");
            int w = img.Width, h = img.Height;
            // узлы, плохо согласующиеся с моделью (неверно уточнённые или ложные), отбрасываются
            int rejected = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var g = new List<PointD>();
                var o = new List<PointD>();
                foreach (var n in det.Nodes) { g.Add(new PointD(n.I, n.J)); o.Add(n.P); }
                var f = LensFit.Fit(g, o, w, h, 1, s.Decentering);
                var errs = new List<double>(f.Errors);
                errs.Sort();
                double thr = Math.Max(0.75, 5 * 1.4826 * errs[errs.Count / 2]);
                var bad = new List<GridNode>();
                for (int i = 0; i < det.Nodes.Count; i++) if (!(f.Errors[i] <= thr)) bad.Add(det.Nodes[i]);
                if (bad.Count == 0 || bad.Count > det.Nodes.Count / 10) break;
                foreach (var n in bad) det.Nodes.Remove(n);
                rejected += bad.Count;
                det.Reindex();
            }
            var ground = new List<PointD>();
            var obs = new List<PointD>();
            foreach (var n in det.Nodes) { ground.Add(new PointD(n.I, n.J)); obs.Add(n.P); }
            var r = new DistortionResult
            {
                FileName = img.Name,
                Title = string.IsNullOrEmpty(s.Title) ? img.Name : s.Title,
                ImageWidth = w,
                ImageHeight = h,
                Roi = det.Roi,
                Settings = s.Clone(),
                Detection = det,
                Rejected = rejected
            };
            r.Fit0 = LensFit.Fit(ground, obs, w, h, 0, false);
            r.Fit3 = LensFit.Fit(ground, obs, w, h, 1, s.Decentering, r.Fit0);
            r.Fit5 = LensFit.Fit(ground, obs, w, h, 2, s.Decentering, r.Fit3);
            r.Grid = GridCoefficients.Compute(det);
            return r;
        }
    }
}
