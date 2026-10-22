using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SigmaLabCam.IO;

namespace SigmaLabCam.UI
{
    /// <summary>Элемент для показа графика (Plot) или рисунка (Figure) с контекстным меню экспорта.</summary>
    public sealed class PlotView : Control
    {
        private Plot _plot;
        private Figure _figure;
        private Bitmap _figBmp;

        public string ExportName = "график";
        public string EmptyText;
        public Size ExportSize = new Size(1000, 650);

        public PlotView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            var menu = new ContextMenuStrip { Renderer = Theme.Renderer };
            menu.Items.Add("Копировать изображение", null, (s, e) => CopyImage());
            menu.Items.Add("Сохранить изображение…", null, (s, e) => SaveImageDialog());
            menu.Items.Add("Сохранить данные (CSV)…", null, (s, e) => SaveCsvDialog());
            ContextMenuStrip = menu;
        }

        public Plot Plot
        {
            get { return _plot; }
            set { _plot = value; _figure = null; DisposeFig(); Invalidate(); }
        }

        public Figure Figure
        {
            get { return _figure; }
            set { _figure = value; _plot = null; DisposeFig(); if (value != null) _figBmp = value.Render(); Invalidate(); }
        }

        private void DisposeFig()
        {
            if (_figBmp != null) { _figBmp.Dispose(); _figBmp = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeFig();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_figBmp != null)
            {
                float k = Math.Min((float)ClientSize.Width / _figBmp.Width, (float)ClientSize.Height / _figBmp.Height);
                float w = _figBmp.Width * k, h = _figBmp.Height * k;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_figBmp, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
            }
            else if (_plot != null)
            {
                _plot.Draw(e.Graphics, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height));
            }
            else if (!string.IsNullOrEmpty(EmptyText))
            {
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(Theme.Muted))
                    e.Graphics.DrawString(EmptyText, Theme.UiFont, br, new RectangleF(20, 0, ClientSize.Width - 40, ClientSize.Height), sf);
            }
        }

        public Bitmap RenderImage()
        {
            if (_figure != null) return _figure.Render();
            var bmp = new Bitmap(ExportSize.Width, ExportSize.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                if (_plot != null) _plot.Draw(g, new RectangleF(0, 0, bmp.Width, bmp.Height), 1.25f);
            }
            return bmp;
        }

        public void CopyImage()
        {
            using (var b = RenderImage()) Clipboard.SetImage(b);
        }

        public void SaveImageDialog()
        {
            using (var dlg = new SaveFileDialog { Filter = ImageIO.BitmapSaveFilter, FileName = Safe(ExportName), Title = "Сохранить изображение" })
            {
                if (!string.IsNullOrEmpty(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                string path = ImageIO.EnsureExtension(dlg.FileName, dlg.FilterIndex);
                Settings.LastDir = Path.GetDirectoryName(path);
                try
                {
                    using (var b = RenderImage()) ImageIO.SaveBitmap(b, path);
                }
                catch (Exception ex) { Ui.Error(FindForm(), "Не удалось сохранить файл: " + ex.Message); }
            }
        }

        public void SaveCsvDialog()
        {
            string csv = _plot != null ? _plot.ToCsv() : null;
            if (_figure != null)
            {
                var sb = new StringBuilder();
                foreach (var p in _figure.Panels)
                {
                    var pp = p.Value as Figure.PlotPanel;
                    if (pp != null) { sb.AppendLine("# " + pp.Plot.Title); sb.Append(pp.Plot.ToCsv()); }
                }
                csv = sb.ToString();
            }
            if (csv == null) return;
            Ui.SaveText(FindForm(), csv, Safe(ExportName) + ".csv");
        }

        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "результат";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }
    }
}
