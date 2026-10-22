using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using VideoMeterCam.Core;

static class T
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what) { if (ok) passes++; else { fails++; Console.WriteLine("FAIL: " + what); } }
    static void Near(double a, double b, double tol, string what) { Check(Math.Abs(a - b) <= tol, what + $" (got {a:0.#####}, want {b:0.#####} ±{tol})"); }

    static int Main()
    {
        var sw = Stopwatch.StartNew();

        // ---- гомография: точное восстановление
        var hTrue = new Homography(new[] { 1.2, 0.1, 30, -0.05, 0.9, 12, 0.0004, -0.0002, 1 });
        var src = new List<PointD>(); var dst = new List<PointD>();
        for (int i = 0; i < 25; i++) { var p = new PointD(i % 5 * 40, i / 5 * 35); src.Add(p); dst.Add(hTrue.Map(p)); }
        var hFit = Homography.Fit(src, dst);
        double hd = 0; foreach (var p in src) hd = Math.Max(hd, PointD.Dist(hFit.Map(p), hTrue.Map(p)));
        Near(hd, 0, 1e-6, "homography DLT");
        var hInv = hTrue.Inverse(); Near(PointD.Dist(hInv.Map(hTrue.Map(new PointD(17, 23))), new PointD(17, 23)), 0, 1e-9, "homography inverse");

        // ---- модель объектива: обращение
        foreach (var kk in new[] { new[] { 0.12, 0.0 }, new[] { -0.08, 0.0 }, new[] { 0.1, 0.03 }, new[] { -0.05, -0.01 } })
        {
            var m = new LensModel(360, 288, 460, kk[0], kk[1]);
            double md = 0;
            for (int i = 0; i < 50; i++) { var p = new PointD(10 + i * 14, 5 + i * 11); var q = m.Correct(m.Distort(p)); if (q.IsValid) md = Math.Max(md, PointD.Dist(p, q)); }
            Near(md, 0, 1e-6, $"lens inverse k1={kk[0]} k2={kk[1]}");
        }
        { var m = LensModel.ForFrame(720, 576, 0.1, 0); Check(m.SmiaTvDistortion(720, 576) < 0, "SMIA barrel negative"); Check(m.LocalDistortionPercent(1) < 0, "local barrel negative"); }
        { var m = LensModel.ForFrame(720, 576, -0.08, 0); Check(m.SmiaTvDistortion(720, 576) > 0, "SMIA pincushion positive"); }

        // ---- подгонка по синтетическим точкам
        {
            var lens = LensModel.ForFrame(720, 576, 0.11, 0);
            var H = new Homography(new[] { 40.0, 2, 80, -1, 41, 60, 0.002, 0.0005, 1 });
            var g = new List<PointD>(); var o = new List<PointD>(); var rnd = new Random(5);
            for (int j = 0; j < 11; j++) for (int i = 0; i < 15; i++) { var p = lens.Distort(H.Map(i, j)); if (!p.IsValid || p.X < 0 || p.Y < 0 || p.X > 719 || p.Y > 575) continue; g.Add(new PointD(i, j)); o.Add(new PointD(p.X + (rnd.NextDouble() - 0.5) * 0.2, p.Y + (rnd.NextDouble() - 0.5) * 0.2)); }
            var f0 = LensFit.Fit(g, o, 720, 576, 0, false);
            var f1 = LensFit.Fit(g, o, 720, 576, 1, false, f0);
            var f2 = LensFit.Fit(g, o, 720, 576, 2, true, f1);
            Console.WriteLine($"synthetic fit: n={g.Count} rms0={f0.Rms:0.000} k1={f1.Model.K1:0.0000} rms1={f1.Rms:0.000} h1={f2.Model.K1:0.0000} h2={f2.Model.K2:0.0000} c=({f2.Model.Cx:0.0},{f2.Model.Cy:0.0}) rms2={f2.Rms:0.000}");
            Near(f1.Model.K1, 0.11, 0.002, "fit k1");
            Check(f1.Rms < 0.12, "fit rms small");
            Check(f0.Rms > 1, "no-distortion fit worse");
            Near(f2.Model.Cx, 359.5, 3, "fit center x");
        }

        // ---- шахматное поле: снимок с дисторсией, поиск узлов, Distortion
        var board = TestCharts.Checkerboard(12, 9, 60, 60);
        double trap0 = 0;
        foreach (double angle in new[] { 0.0, 30.0 })
        {
            var shot = TestCharts.Shoot(board, new ShotSpec { K1 = 0.12, AngleDeg = angle, Noise = 2, Seed = 3 });
            var t0 = sw.ElapsedMilliseconds;
            var det = GridDetect.Detect(shot, Rectangle.Empty, GridPattern.Checkerboard, 0, 0);
            Console.WriteLine($"checker α={angle}: nodes={det.Nodes.Count} grid {det.Cols}×{det.Rows} σ={det.Sigma:0.0} cell={det.CellSize:0.0} cand={det.Candidates} ({sw.ElapsedMilliseconds - t0} ms)");
            // внутренние углы 11×8 и, возможно, углы клеток на краю поля (13×10 без двух белых углов)
            Check((det.Nodes.Count == 88 && det.Cols == 11) || (det.Nodes.Count == 128 && det.Cols == 13), $"checker α={angle} nodes");
            var res = DistortionAnalysis.Analyze(shot, Rectangle.Empty, new DistortionSettings { Pattern = GridPattern.Checkerboard }, det);
            Console.WriteLine($"  k1={res.K1:0.0000} h1={res.H1:0.0000} h2={res.H2:0.0000} rms0={res.Fit0.Rms:0.000} rms3={res.Fit3.Rms:0.000} SMIA={res.SmiaTv:0.00}% corner={res.CornerDistortion:0.00}%");
            var gc = res.Grid;
            Console.WriteLine($"  K1..K4={gc.Sides.K1:0.00} {gc.Sides.K2:0.00} {gc.Sides.K3:0.00} {gc.Sides.K4:0.00} ({SideCoefficients.BulgeName(gc.Sides.Bulge1)}) trapH={gc.TrapH:0.00} trapV={gc.TrapV:0.00} Kv={gc.Kv:0.00} Kh={gc.Kh:0.00}");
            Near(res.K1, 0.12, 0.006, $"checker α={angle} k1");
            Check(res.Fit3.Rms < 0.25, $"checker α={angle} rms3 < 0.25 px");
            Check(res.SmiaTv < 0, "checker SMIA negative (barrel)");
            Check(gc.Sides.Bulge1 > 0 && gc.Sides.Bulge2 > 0, "barrel bulges outward");
            if (angle == 0) trap0 = Math.Abs(gc.TrapV); else Check(Math.Abs(gc.TrapV) > trap0 + 5, "keystone grows with angle");
        }
        {
            var shot = TestCharts.Shoot(board, new ShotSpec { K1 = -0.07, Noise = 1.5, Seed = 4, Fill = 0.75 });
            var res = DistortionAnalysis.Analyze(shot, Rectangle.Empty, new DistortionSettings { Pattern = GridPattern.Checkerboard, Decentering = true });
            Console.WriteLine($"checker pincushion: nodes={res.Detection.Nodes.Count} k1={res.K1:0.0000} SMIA={res.SmiaTv:0.00}% c=({res.Fit3.Model.Cx:0.0},{res.Fit3.Model.Cy:0.0})");
            Near(res.K1, -0.07, 0.006, "pincushion k1");
            Check(res.SmiaTv > 0, "pincushion SMIA positive");
            Check(res.Grid.Sides.Bulge1 < 0, "pincushion bulges inward");
        }

        // ---- сетка линий
        {
            var grid = TestCharts.Grid(12, 9, 60, 3, 60);
            var shot = TestCharts.Shoot(grid, new ShotSpec { K1 = 0.1, AngleDeg = -15, Noise = 2, Seed = 6, Color = true });
            var t0 = sw.ElapsedMilliseconds;
            var det = GridDetect.Detect(shot, Rectangle.Empty, GridPattern.Lines, 0, 0);
            Console.WriteLine($"grid lines: nodes={det.Nodes.Count} grid {det.Cols}×{det.Rows} polarity={det.Polarity} σ={det.Sigma:0.0} ({sw.ElapsedMilliseconds - t0} ms)");
            Check(det.Nodes.Count == 130 && det.Cols == 13 && det.Rows == 10, "grid all 13×10 nodes");
            var res = DistortionAnalysis.Analyze(shot, Rectangle.Empty, new DistortionSettings { Pattern = GridPattern.Lines }, det);
            Console.WriteLine($"  k1={res.K1:0.0000} rms3={res.Fit3.Rms:0.000}");
            Near(res.K1, 0.1, 0.004, "grid k1");
            Check(res.Fit3.Rms < 0.2, "grid rms3");
            var inv = TestCharts.Shoot(ImageOps.Invert(grid), new ShotSpec { K1 = 0.1, Noise = 2, Seed = 7 });
            var det2 = GridDetect.Detect(inv, Rectangle.Empty, GridPattern.Lines, 0, 0);
            Check(det2.Polarity == -1 && det2.Nodes.Count >= 120, "light lines on dark (got " + det2.Nodes.Count + ", polarity " + det2.Polarity + ")");
        }

        // ---- тяжёлые условия съёмки (как у аналоговой ТВ-камеры): сильная перспектива, размытие, шум,
        //      виньетирование, низкий контраст, чересстрочность
        {
            Func<ImageData, ImageData> degrade = im =>
            {
                var r = im.Clone();
                int w = r.Width;
                for (int c = 0; c < r.Channels; c++) for (int i = 0; i < r.PixelCount; i++) r.Planes[c][i] = (float)(128 + (r.Planes[c][i] - 128) * 0.5);
                for (int y = 1; y < r.Height; y += 2) for (int c = 0; c < r.Channels; c++) for (int x = w - 1; x > 0; x--) r.Planes[c][y * w + x] = r.Planes[c][y * w + x - 1];
                r.Conform();
                return r;
            };
            var charts = new[] { TestCharts.Checkerboard(12, 9, 60, 60), TestCharts.Grid(12, 9, 60, 3, 60), TestCharts.CalibrationGrid(13, 10, 60, 6, 2, 60) };
            var pats = new[] { GridPattern.Checkerboard, GridPattern.Lines, GridPattern.Lines };
            int[] expect = { 88, 130, 130 };
            int sd = 30;
            for (int c = 0; c < 3; c++)
                foreach (double k1 in new[] { 0.05, 0.25 })
                    foreach (double ang in new[] { -45.0, 45.0 })
                    {
                        var shot = degrade(TestCharts.Shoot(charts[c], new ShotSpec { K1 = k1, AngleDeg = ang, Blur = 1.8, Noise = 8, Vignetting = 0.45, Seed = sd++, Fill = 0.8, Distance = 2.2, Color = true }));
                        var res = DistortionAnalysis.Analyze(shot, Rectangle.Empty, new DistortionSettings { Pattern = pats[c] });
                        Check(res.Detection.Nodes.Count >= expect[c] - 3, $"harsh {pats[c]} #{c} k1={k1} α={ang}: nodes {res.Detection.Nodes.Count}/{expect[c]}");
                        Near(res.K1, k1, 0.008, $"harsh {pats[c]} #{c} k1={k1} α={ang}: k1");
                    }
        }

        // ---- калибровка по таблице 13×10 (ASRix), привязка точек, выпрямление
        {
            var tpl = new GridTemplate();
            var chart = TestCharts.CalibrationGrid(13, 10, 60, 6, 2, 60);
            var shot = TestCharts.Shoot(chart, new ShotSpec { K1 = 0.1, K2 = 0.02, AngleDeg = 20, TiltDeg = -8, Noise = 2, Seed = 8, Fill = 0.85, Distance = 2.5 });
            var det = GridDetect.Detect(shot, Rectangle.Empty, GridPattern.Lines, 0, 0);
            Console.WriteLine($"calib grid: nodes={det.Nodes.Count} {det.Cols}×{det.Rows}");
            Check(det.Cols == 13 && det.Rows == 10 && det.Nodes.Count == 130, "calibration grid detected 130");
            var pts = new Dictionary<int, PointD>();
            foreach (var n in det.Nodes) pts[tpl.Number(n.I, n.J)] = n.P;
            // ручная расстановка с ошибкой 3 пикс. и привязка
            var rnd = new Random(1);
            double snapErr = 0;
            Func<int, PointD> jit = k => new PointD(pts[k].X + (rnd.NextDouble() - 0.5) * 6, pts[k].Y + (rnd.NextDouble() - 0.5) * 6);
            double blobErr = 0;
            foreach (var kv in pts)
            {
                int c = tpl.Col(kv.Key), r = tpl.Row(kv.Key);
                var rough = jit(kv.Key);
                PointD L = c > 0 ? jit(kv.Key - 1) : PointD.NaN, R = c < 12 ? jit(kv.Key + 1) : PointD.NaN;
                PointD U = r > 0 ? jit(kv.Key - 13) : PointD.NaN, D = r < 9 ? jit(kv.Key + 13) : PointD.NaN;
                var s = GridDetect.SnapLines(shot, rough, L, R, U, D, 8, 0);
                snapErr = Math.Max(snapErr, PointD.Dist(s, kv.Value));
                blobErr = Math.Max(blobErr, PointD.Dist(GridDetect.Snap(shot, rough, 8, GridPattern.Lines, 0), kv.Value));
            }
            Console.WriteLine($"  snap by blob only: max {blobErr:0.000} px");
            Check(blobErr < 1.0, "blob snap");
            Console.WriteLine($"  snap max error vs detector: {snapErr:0.000} px");
            Check(snapErr < 0.1, "snap by lines");
            var cal = Calibration.Calibrate(pts, tpl, shot.Width, shot.Height, true, 2, "тест");
            Console.WriteLine($"  calibrate: k1={cal.Camera.K1:0.0000} k2={cal.Camera.K2:0.0000} c=({cal.Camera.Cx:0.0},{cal.Camera.Cy:0.0}) rms={cal.Camera.Rms:0.000} (без дисторсии {cal.FitNoDistortion.Rms:0.00})");
            Near(cal.Camera.K1, 0.1, 0.02, "calibration k1");
            Check(cal.Camera.Rms < 0.3, "calibration rms");
            var cam2 = CameraProfile.Parse(cal.Camera.Serialize());
            Check(cam2 != null && Math.Abs(cam2.K1 - cal.Camera.K1) < 1e-12 && cam2.Name == "тест", "camera profile roundtrip");
            var lens = cal.Camera.ModelFor(shot.Width, shot.Height);
            var Hp = Calibration.PlaneToImage(pts, tpl, lens);
            Homography o2p;
            var rect = Calibration.Rectify(shot, lens, Hp, tpl, 40, 0.5, out o2p);
            var det3 = GridDetect.Detect(rect, Rectangle.Empty, GridPattern.Lines, 0, 0);
            var gcR = GridCoefficients.Compute(det3);
            var gc0 = GridCoefficients.Compute(det);
            Console.WriteLine($"  rectified {rect.Width}×{rect.Height}: nodes={det3.Nodes.Count} cell={det3.CellSize:0.00} K1..K4 = {gcR.Sides.K1:0.000} {gcR.Sides.K2:0.000} {gcR.Sides.K3:0.000} {gcR.Sides.K4:0.000} (исходное {gc0.Sides.K1:0.000} {gc0.Sides.K2:0.000} {gc0.Sides.K3:0.000} {gc0.Sides.K4:0.000}) trapV {gcR.TrapV:0.000} (исх. {gc0.TrapV:0.00})");
            Near(det3.CellSize, 40, 0.3, "rectified cell size");
            Check(Math.Max(Math.Max(gcR.Sides.K1, gcR.Sides.K2), Math.Max(gcR.Sides.K3, gcR.Sides.K4)) < 0.06, "rectified K small");
            Check(Math.Abs(gcR.TrapV) < 0.3 && Math.Abs(gc0.TrapV) > 3, "rectified trapezoid removed");
        }

        // ---- формулы методички
        Near(Coef.Trapezoid(100, 110), 200.0 / 21, 1e-9, "(1.2)");
        Near(Coef.Nonlinearity(55, 45), 20, 1e-9, "(1.3)");
        Near(Coef.Geometric(3, 150), 2, 1e-9, "(1.1)");
        {
            var sc = SideCoefficients.Compute(new PointD(0, 0), new PointD(100, 0), new PointD(0, 80), new PointD(100, 80),
                                              new PointD(50, 3), new PointD(2, 40), new PointD(97, 40), new PointD(50, 76));
            Near(sc.K1, 3, 1e-9, "K1 (1.8)"); Near(sc.K2, 2.5, 1e-9, "K2 (1.9)"); Near(sc.K3, 3.75, 1e-9, "K3 (1.10)"); Near(sc.K4, 4, 1e-9, "K4 (1.11)");
            Check(sc.Bulge1 < 0 && sc.Bulge2 < 0 && sc.Bulge3 < 0 && sc.Bulge4 < 0, "inward sides = pincushion");
            var t = new GridTemplate();
            Check(t.PointB == 13 && t.PointC == 118 && t.PointD == 130 && t.PointE[0] == 7 && t.PointH[0] == 124, "template corner numbers");
            Check(t.PointF.Length == 2 && t.PointF[0] == 53 && t.PointF[1] == 66 && t.PointG[0] == 65 && t.PointG[1] == 78, "template side midpoints 53/66, 65/78");
        }

        // ---- модель сферы (1.12), (1.13) и рис. 1.9
        {
            Near(Warp.SphereRho1(100, 100), 100 / Math.Sqrt(2), 1e-9, "ρ1(R) = R/√2");
            Near(Warp.SphereRho(Warp.SphereRho1(37, 120), 120), 37, 1e-9, "(1.13) inverse of (1.12)");
            var img = TestCharts.Checkerboard(16, 12, 30, 0);
            for (int i = 0; i < img.PixelCount; i++) img.Planes[0][i] = img.Planes[0][i] * 0.6f + 60;
            double cx = (img.Width - 1) / 2.0, cy = (img.Height - 1) / 2.0, R = 300;
            var dI = Warp.SphereDistort(img, R, cx, cy, MapMethod.Inverse);
            var back = Warp.SphereCorrect(dI, R, cx, cy, MapMethod.Inverse);
            double err = 0; int cnt = 0;
            for (int y = 150; y < 210; y++) for (int x = 200; x < 280; x++) { err += Math.Abs(back.Planes[0][y * img.Width + x] - img.Planes[0][y * img.Width + x]); cnt++; }
            Console.WriteLine($"sphere inverse roundtrip mean abs err (center) = {err / cnt:0.00}");
            Check(err / cnt < 6, "sphere roundtrip");
            var dF = Warp.SphereDistort(img, R, cx, cy, MapMethod.Forward);
            var cF = Warp.SphereCorrect(dF, R, cx, cy, MapMethod.Forward);
            var inner = new Rectangle(100, 80, 280, 200);
            int gaps = 0, gapsFilled = 0;
            var filled = Warp.FillGaps(cF);
            for (int y = inner.Top; y < inner.Bottom; y++) for (int x = inner.Left; x < inner.Right; x++) { int i = y * img.Width + x; if (Warp.IsGap(cF, i)) gaps++; if (Warp.IsGap(filled, i)) gapsFilled++; }
            Console.WriteLine($"sphere forward: gaps in center {gaps}, after fill {gapsFilled}");
            Check(gaps > 100 && gapsFilled == 0, "forward correction leaves gaps, fill removes them");
        }

        // ---- TIFF
        foreach (PixelType pt in new[] { PixelType.Gray8, PixelType.Gray16, PixelType.Gray32, PixelType.Rgb24, PixelType.Rgb32 })
        {
            var s = new ImageData(31, 17, pt);
            var rng = new Random(9);
            for (int c = 0; c < s.Channels; c++)
                for (int i = 0; i < s.PixelCount; i++)
                    s.Planes[c][i] = pt == PixelType.Gray16 ? rng.Next(65536) : (ImageData.IsFloatType(pt) ? (float)(rng.NextDouble() * 1000 - 500) : rng.Next(256));
            var ms = new System.IO.MemoryStream();
            using (var bw = new System.IO.BinaryWriter(ms)) VideoMeterCam.IO.TiffIO.Write(s, bw);
            var back = VideoMeterCam.IO.TiffIO.TryRead(ms.ToArray());
            double md = 0;
            if (back != null) for (int c = 0; c < s.Channels; c++) for (int i = 0; i < s.PixelCount; i++) md = Math.Max(md, Math.Abs(s.Planes[c][i] - back.Planes[c][i]));
            Check(back != null && back.Type == pt && md == 0, "tiff roundtrip " + pt);
        }

        Console.WriteLine($"\n{passes} passed, {fails} failed ({sw.ElapsedMilliseconds} ms)");
        return fails == 0 ? 0 : 1;
    }
}
