using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SigmaLabCam.Core;

namespace SigmaLabCam.UI
{
    /// <summary>
    /// Служебный режим «--shots папка»: программа сама проходит основные шаги работы на синтетических данных
    /// и сохраняет снимки окон. Используется для проверки интерфейса.
    /// </summary>
    internal static class ScreenshotTour
    {
        public static void Attach(MainForm main, string dir)
        {
            Directory.CreateDirectory(dir);
            var steps = new List<Action>();
            var timer = new Timer { Interval = 1500 };
            int k = 0;
            Form current = null;
            Action<string> log = s => File.AppendAllText(Path.Combine(dir, "log.txt"), s + "\r\n");
            Action<Form, string> shot = (f, name) =>
            {
                try
                {
                    f.Activate();
                    f.Refresh();
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        try
                        {
                            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
                        }
                        catch
                        {
                            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                        }
                        bmp.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                    }
                    log("ok " + name);
                }
                catch (Exception ex) { log("fail " + name + ": " + ex); }
            };

            ImageData board = null, shotImg = null, calibShot = null;
            steps.Add(() =>
            {
                main.WindowState = FormWindowState.Normal;
                main.Bounds = new Rectangle(0, 0, 1360, 860);
                board = TestCharts.Checkerboard(12, 9, 60, 60);
                board.Name = "Шахматное поле 12×9";
                main.AddImage(board);
                shotImg = TestCharts.Shoot(board, new ShotSpec { K1 = 0.12, AngleDeg = 30, Noise = 2, Seed = 3, Color = true });
                shotImg.Name = "снимок_шахматное_30см_30град.png";
                var doc = main.AddImage(shotImg);
                doc.Points.Add(new PointD(120.3, 140.8));
                doc.Points.Add(new PointD(600.1, 135.2));
                doc.Points.Add(new PointD(362.5, 120.6));
                main.TourRefresh();
                main.TourTool(ImageViewer.ToolMode.Point);
                main.TourSelectPoints(0, 1, 2);
            });
            steps.Add(() => shot(main, "01_main"));
            steps.Add(() =>
            {
                var f = new DistortionForm(main);
                f.Show(main);
                f.Bounds = new Rectangle(20, 20, 1240, 800);
                f.TourSettings(new DistortionSettings { Pattern = GridPattern.Checkerboard, Order = 3, Decentering = true });
                current = f;
            });
            steps.Add(() => shot(current, "02_distortion_settings"));
            steps.Add(() =>
            {
                var res = ((DistortionForm)current).TourRun();
                current.Close();
                current = res;
                if (res != null) res.Bounds = new Rectangle(10, 10, 1180, 880);
                var r = main.LastDistortion;
                if (r != null) log("distortion: nodes=" + r.Detection.Nodes.Count + " k1=" + r.K1.ToString("0.00000") + " h1=" + r.H1.ToString("0.00000") +
                                   " SMIA=" + r.SmiaTv.ToString("0.000") + " rms=" + r.Fit3.Rms.ToString("0.000") + " trapV=" + (r.Grid != null ? r.Grid.TrapV.ToString("0.00") : "-"));
            });
            for (int t = 0; t < 6; t++)
            {
                int tab = t;
                string[] names = { "03_distortion_fig1", "04_distortion_radial", "05_distortion_radius", "06_distortion_corrected", "07_distortion_summary", "08_distortion_nodes" };
                steps.Add(() => { var r = current as DistortionResultsForm; if (r != null) r.TourTab(tab); });
                steps.Add(() => { if (current != null) shot(current, names[tab]); });
            }
            steps.Add(() =>
            {
                if (current != null) current.Close();
                shot(main, "09_main_results");
            });
            CalibrationForm cal = null;
            steps.Add(() =>
            {
                var chart = TestCharts.CalibrationGrid(13, 10, 60, 6, 2, 60);
                calibShot = TestCharts.Shoot(chart, new ShotSpec { K1 = 0.1, K2 = 0.02, AngleDeg = 15, TiltDeg = -6, Noise = 2, Seed = 8, Fill = 0.85, Distance = 2.5, Color = true });
                calibShot.Name = "снимок_калибровка.png";
                main.AddImage(calibShot);
                cal = main.CalibrationWindow();
                cal.Bounds = new Rectangle(20, 20, 1280, 840);
                var det = GridDetect.Detect(calibShot, Rectangle.Empty, GridPattern.Lines, 0, 0);
                var tpl = new GridTemplate();
                var pts = new Dictionary<int, PointD>();
                foreach (var n in det.Nodes) if (tpl.Number(n.I, n.J) <= 57) pts[tpl.Number(n.I, n.J)] = n.P;
                cal.TourSetPoints(pts);
                current = cal;
            });
            steps.Add(() => shot(current, "10_calibration_points"));
            steps.Add(() =>
            {
                var det = GridDetect.Detect(calibShot, Rectangle.Empty, GridPattern.Lines, 0, 0);
                var tpl = new GridTemplate();
                var pts = new Dictionary<int, PointD>();
                foreach (var n in det.Nodes) pts[tpl.Number(n.I, n.J)] = n.P;
                cal.TourSetPoints(pts);
                try
                {
                    var res = cal.Calibrate("Day Night Camera (тест)", 2, true);
                    log("calibration: k1=" + res.Camera.K1.ToString("0.0000") + " k2=" + res.Camera.K2.ToString("0.0000") + " rms=" + res.Camera.Rms.ToString("0.000"));
                }
                catch (Exception ex) { log("calibration FAIL " + ex.Message); }
            });
            steps.Add(() => shot(current, "11_calibration_done"));
            Func<Form> topDialog = () =>
            {
                Form found = null;
                foreach (Form f in Application.OpenForms) if ((f is ParamDialog || f is CameraManagerForm) && f.Visible) found = f;
                return found;
            };
            // модальный диалог: снимок, затем OK или отмена
            Action<Action, string, bool> modal = (open, name, ok) =>
            {
                var t = new Timer { Interval = 1200 };
                int phase = 0;
                t.Tick += (s, e) =>
                {
                    var f = topDialog();
                    if (f == null) { if (++phase > 6) { t.Stop(); log("no dialog for " + name); } return; }
                    t.Stop();
                    t.Dispose();
                    shot(f, name);
                    if (ok && f is ParamDialog) ((ParamDialog)f).OkButton.PerformClick();
                    else { f.DialogResult = DialogResult.Cancel; f.Close(); }
                };
                t.Start();
                open();
            };
            steps.Add(() => modal(() => ((ToolStripMenuItem)FindMenu(cal, "Управление камерами (Camera Management)…")).PerformClick(), "12_camera_manager", false));
            steps.Add(() => { var mi = FindMenu(cal, "Исправленное изображение (Corrected Source Image)"); if (mi != null) mi.PerformClick(); });
            steps.Add(() => shot(current, "13_calibration_corrected"));
            steps.Add(() => { var mi = FindMenu(cal, "Исходное изображение"); if (mi != null) mi.PerformClick(); });
            steps.Add(() => modal(() => FindMenu(cal, "Выпрямить (Rectify)…").PerformClick(), "14_rectify_dialog", true));
            steps.Add(() =>
            {
                CalibrationForm second = null;
                foreach (Form f in Application.OpenForms) if (f is CalibrationForm && f != cal) second = f as CalibrationForm;
                if (second == null) { log("rectified calibration window not opened"); return; }
                second.Bounds = new Rectangle(40, 40, 1280, 840);
                var det = GridDetect.Detect(second.Image, Rectangle.Empty, GridPattern.Lines, 0, 0);
                var tpl = new GridTemplate();
                var pts = new Dictionary<int, PointD>();
                foreach (var n in det.Nodes) pts[tpl.Number(n.I, n.J)] = n.P;
                second.TourSetPoints(pts);
                log("rectified: " + second.Image.Width + "x" + second.Image.Height + " nodes=" + det.Nodes.Count);
                current = second;
            });
            steps.Add(() => shot(current, "15_rectified_points"));
            steps.Add(() =>
            {
                var mi = FindMenu(cal, "Коэффициенты K1–K4 → столбец «Исходное изображение»");
                if (mi != null) mi.PerformClick();
                var mi2 = FindMenu((CalibrationForm)current, "Коэффициенты K1–K4 → столбец «Выпрямленное изображение»");
                if (mi2 != null) mi2.PerformClick();
                foreach (Form f in Application.OpenForms) if (f is CoefficientsForm) { f.Bounds = new Rectangle(30, 30, 1160, 780); current = f; }
            });
            steps.Add(() => shot(current, "16_coefficients"));
            steps.Add(() =>
            {
                foreach (Form f in new List<Form>(OpenForms())) if (f is CalibrationForm) f.Close();
                current.Close();
                main.TourActivate(shotImg.Name);
                var f2 = new SphereForm(main);
                f2.Show(main);
                f2.Bounds = new Rectangle(20, 20, 1280, 840);
                current = f2;
            });
            steps.Add(() => { ((SphereForm)current).TourSetR(500); });
            steps.Add(() => shot(current, "17_sphere"));
            steps.Add(() =>
            {
                current.Close();
                var f = new TablePlotForm();
                f.Show(main);
                f.TourFill(new[] { "α, °", "R = 30 см", "R = 50 см" }, new[]
                {
                    new[] { -45.0, 1.9, 1.4 }, new[] { -30.0, 1.3, 1.0 }, new[] { -15.0, 0.9, 0.7 }, new[] { 0.0, 0.8, 0.6 },
                    new[] { 15.0, 0.9, 0.7 }, new[] { 30.0, 1.2, 0.9 }, new[] { 45.0, 1.8, 1.3 }
                });
                current = f;
            });
            steps.Add(() => shot(current, "18_table_plot"));
            bool noCapture = Environment.GetEnvironmentVariable("SIGMALAB_NO_CAPTURE") == "1";
            steps.Add(() =>
            {
                current.Close();
                if (noCapture) { current = new Form(); log("capture skipped"); return; }
                var f = new CaptureForm(main);
                f.Show(main);
                f.Bounds = new Rectangle(20, 20, 1100, 760);
                current = f;
            });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var c = current as CaptureForm;
                if (c == null) return;
                shot(current, "19_capture");
                var fr = c.TourFrame();
                log("capture: device=" + c.TourHasDevice + (fr != null ? " frame " + fr.Width + "x" + fr.Height : " no frame"));
            });
            steps.Add(() =>
            {
                current.Close();
                var f = new AboutForm();
                f.Show(main);
                current = f;
            });
            steps.Add(() => shot(current, "20_about"));
            steps.Add(() => { current.Close(); main.TourActivate(board.Name); });
            steps.Add(() => modal(() => main.TourCommand("chart"), "21_chart_generator", true));
            steps.Add(() => shot(main, "22_main_chart"));
            steps.Add(() => modal(() => main.TourCommand("shoot"), "23_shoot_dialog", true));
            steps.Add(() => modal(() => main.TourCommand("sphere"), "24_sphere_dialog", false));
            steps.Add(() => modal(() => main.TourCommand("lens"), "25_lens_dialog", false));
            steps.Add(() => shot(main, "26_main_final"));
            steps.Add(() =>
            {
                // ввод-вывод: сохранение и повторное чтение
                string tmp = Path.Combine(dir, "io");
                Directory.CreateDirectory(tmp);
                foreach (string ext in new[] { ".tif", ".png", ".bmp", ".jpg" })
                {
                    try
                    {
                        string p = Path.Combine(tmp, "rt" + ext);
                        IO.ImageIO.Save(shotImg, p);
                        var back = IO.ImageIO.Load(p);
                        double maxd = 0;
                        for (int c = 0; c < 3; c++)
                            for (int i = 0; i < shotImg.PixelCount; i++) maxd = Math.Max(maxd, Math.Abs(shotImg.Planes[c][i] - back.Planes[c][i]));
                        log("io " + ext + ": " + back.Description + " maxdiff=" + maxd);
                    }
                    catch (Exception ex) { log("io " + ext + " FAIL " + ex.Message); }
                }
                var doc = main.Workspace.Active;
                var before = doc.Data;
                doc.Replace(ImageOps.Invert(before), "инверсия");
                main.TourCommand("undo");
                log("undo restores: " + ReferenceEquals(main.Workspace.Active.Data, before));
            });
            steps.Add(() => { log("done"); Application.Exit(); });

            main.Shown += (s, e) => timer.Start();
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (k < steps.Count)
                {
                    try { steps[k](); }
                    catch (Exception ex) { log("step " + k + " error: " + ex); }
                    k++;
                    timer.Start();
                }
            };
        }

        private static IEnumerable<Form> OpenForms()
        {
            foreach (Form f in Application.OpenForms) yield return f;
        }

        private static ToolStripMenuItem FindMenu(Form f, string text)
        {
            if (f == null || f.MainMenuStrip == null) return null;
            foreach (ToolStripItem top in f.MainMenuStrip.Items)
            {
                var r = FindIn(top as ToolStripMenuItem, text);
                if (r != null) return r;
            }
            return null;
        }

        private static ToolStripMenuItem FindIn(ToolStripMenuItem m, string text)
        {
            if (m == null) return null;
            if (m.Text.StartsWith(text)) return m;
            foreach (ToolStripItem it in m.DropDownItems)
            {
                var r = FindIn(it as ToolStripMenuItem, text);
                if (r != null) return r;
            }
            return null;
        }
    }
}
