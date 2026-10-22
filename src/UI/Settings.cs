using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VideoMeterCam.Core;

namespace VideoMeterCam.UI
{
    /// <summary>Настройки пользователя (%APPDATA%\VideoMeterCam\settings.ini) и профили камер (cameras.ini).</summary>
    public static class Settings
    {
        public static string LastDir = "";
        public static readonly List<string> Recent = new List<string>();

        // захват с камеры
        public static string CaptureDevice = "";
        public static string CaptureFormat = "";
        public static string CaptureDir = "";
        public static bool CaptureAutoSave;
        public static string CaptureExt = ".png";
        public static int CaptureAverage = 1;

        // калибровка
        public static GridTemplate Template = new GridTemplate();
        public static string DefaultCamera = "";
        public static readonly List<CameraProfile> Cameras = new List<CameraProfile>();

        private static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoMeterCam"); }
        }

        private static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }
        private static string CamerasPath { get { return Path.Combine(Dir, "cameras.ini"); } }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Переносит настройки и профили камер из папки прежнего названия программы (SigmaLabCam).</summary>
        private static void MigrateOldDir()
        {
            string old = Path.Combine(Path.GetDirectoryName(Dir), "SigmaLabCam");
            if (!Directory.Exists(Dir) && Directory.Exists(old)) Directory.Move(old, Dir);
        }

        public static void Load()
        {
            try
            {
                MigrateOldDir();
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        int iv;
                        double dv;
                        switch (k)
                        {
                            case "LastDir": LastDir = v; break;
                            case "Luma": LabOptions.Luma = v == "Unweighted" ? LumaMode.Unweighted : LumaMode.Weighted; break;
                            case "CaptureDevice": CaptureDevice = v; break;
                            case "CaptureFormat": CaptureFormat = v; break;
                            case "CaptureDir": CaptureDir = v; break;
                            case "CaptureAutoSave": CaptureAutoSave = v == "1"; break;
                            case "CaptureExt": if (v == ".png" || v == ".tif" || v == ".bmp" || v == ".jpg") CaptureExt = v; break;
                            case "CaptureAverage": if (int.TryParse(v, NumberStyles.Integer, Inv, out iv)) CaptureAverage = Math.Max(1, Math.Min(64, iv)); break;
                            case "GridCols": if (int.TryParse(v, NumberStyles.Integer, Inv, out iv) && iv >= 3 && iv <= 60) Template.Cols = iv; break;
                            case "GridRows": if (int.TryParse(v, NumberStyles.Integer, Inv, out iv) && iv >= 3 && iv <= 60) Template.Rows = iv; break;
                            case "GridStep": if (double.TryParse(v, NumberStyles.Float, Inv, out dv) && dv > 0) Template.Step = dv; break;
                            case "GridName": if (v.Length > 0) Template.Name = v; break;
                            case "DefaultCamera": DefaultCamera = v; break;
                            case "Recent":
                                Recent.Clear();
                                foreach (string p in v.Split('|')) if (p.Length > 0) Recent.Add(p);
                                break;
                        }
                    }
                if (File.Exists(CamerasPath))
                {
                    Cameras.Clear();
                    foreach (string line in File.ReadAllLines(CamerasPath, Encoding.UTF8))
                    {
                        if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                        var c = CameraProfile.Parse(line);
                        if (c != null) Cameras.Add(c);
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("LastDir=" + LastDir);
                sb.AppendLine("Luma=" + LabOptions.Luma);
                sb.AppendLine("CaptureDevice=" + CaptureDevice);
                sb.AppendLine("CaptureFormat=" + CaptureFormat);
                sb.AppendLine("CaptureDir=" + CaptureDir);
                sb.AppendLine("CaptureAutoSave=" + (CaptureAutoSave ? "1" : "0"));
                sb.AppendLine("CaptureExt=" + CaptureExt);
                sb.AppendLine("CaptureAverage=" + CaptureAverage.ToString(Inv));
                sb.AppendLine("GridCols=" + Template.Cols.ToString(Inv));
                sb.AppendLine("GridRows=" + Template.Rows.ToString(Inv));
                sb.AppendLine("GridStep=" + Template.Step.ToString("R", Inv));
                sb.AppendLine("GridName=" + Template.Name);
                sb.AppendLine("DefaultCamera=" + DefaultCamera);
                sb.AppendLine("Recent=" + string.Join("|", Recent.ToArray()));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                SaveCameras();
            }
            catch { }
        }

        public static void SaveCameras()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# Профили камер ВидеоМетр-Кам: имя|ширина|высота|cx|cy|k1|k2|СКО|точек|дата");
                foreach (var c in Cameras) sb.AppendLine(c.Serialize());
                File.WriteAllText(CamerasPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static CameraProfile FindCamera(string name)
        {
            return Cameras.Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static CameraProfile Default
        {
            get { return FindCamera(DefaultCamera) ?? (Cameras.Count > 0 ? Cameras[Cameras.Count - 1] : null); }
        }

        public static void AddRecent(string path)
        {
            Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            while (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1);
        }
    }
}
