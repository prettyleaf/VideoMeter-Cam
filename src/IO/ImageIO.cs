using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SigmaLabCam.Core;

namespace SigmaLabCam.IO
{
    /// <summary>Открытие и сохранение изображений, преобразование в Bitmap для отображения.</summary>
    public static class ImageIO
    {
        public const string OpenFilter =
            "Изображения (*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
            "TIFF (*.tif;*.tiff)|*.tif;*.tiff|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|Все файлы (*.*)|*.*";

        public const string SaveFilter =
            "TIFF без сжатия (*.tif)|*.tif|PNG (*.png)|*.png|BMP (*.bmp)|*.bmp|JPEG (*.jpg)|*.jpg";

        public const string BitmapSaveFilter =
            "TIFF без сжатия (*.tif)|*.tif|PNG (*.png)|*.png|BMP (*.bmp)|*.bmp|JPEG (*.jpg)|*.jpg";

        public static ImageData Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            ImageData img = null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff" || (bytes.Length > 4 && ((bytes[0] == 'I' && bytes[1] == 'I') || (bytes[0] == 'M' && bytes[1] == 'M'))))
                img = TiffIO.TryRead(bytes);
            if (img == null)
            {
                using (var ms = new MemoryStream(bytes))
                using (var im = Image.FromStream(ms))
                {
                    var bmp = im as Bitmap ?? new Bitmap(im);
                    img = FromBitmap(bmp);
                    if (!ReferenceEquals(bmp, im)) bmp.Dispose();
                }
            }
            img.Name = Path.GetFileName(path);
            img.FilePath = path;
            return img;
        }

        public static ImageData FromBitmap(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            if (bmp.PixelFormat == PixelFormat.Format8bppIndexed)
            {
                Color[] pal = bmp.Palette.Entries;
                bool gray = pal.Length >= 256;
                if (gray)
                    for (int i = 0; i < 256; i++)
                        if (pal[i].R != i || pal[i].G != i || pal[i].B != i) { gray = false; break; }
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                var row = new byte[Math.Abs(data.Stride)];
                var img = new ImageData(w, h, gray ? PixelType.Gray8 : PixelType.Rgb24);
                bool allGray = true;
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (gray) img.Planes[0][i] = row[x];
                        else
                        {
                            Color c = row[x] < pal.Length ? pal[row[x]] : Color.Black;
                            img.Planes[0][i] = c.R; img.Planes[1][i] = c.G; img.Planes[2][i] = c.B;
                            if (c.R != c.G || c.G != c.B) allGray = false;
                        }
                    }
                }
                bmp.UnlockBits(data);
                // индексированное изображение с серой (но не тождественной) палитрой → 8 бит
                if (!gray && allGray) img = ImageOps.ConvertTo(img, PixelType.Gray8);
                return img;
            }

            Bitmap src = bmp;
            bool tmp = false;
            if (bmp.PixelFormat != PixelFormat.Format24bppRgb && bmp.PixelFormat != PixelFormat.Format32bppRgb && bmp.PixelFormat != PixelFormat.Format32bppArgb)
            {
                src = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(src))
                {
                    g.Clear(Color.White);
                    g.DrawImage(bmp, new Rectangle(0, 0, w, h));
                }
                tmp = true;
            }
            var d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var line = new byte[Math.Abs(d.Stride)];
            var res = new ImageData(w, h, PixelType.Rgb24);
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), line, 0, line.Length);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    res.Planes[2][i] = line[x * 3];
                    res.Planes[1][i] = line[x * 3 + 1];
                    res.Planes[0][i] = line[x * 3 + 2];
                }
            }
            src.UnlockBits(d);
            if (tmp) src.Dispose();
            return res;
        }

        /// <summary>Bitmap для отображения (24 бита). Для 16/32-битных — с масштабированием диапазона отображения.</summary>
        public static Bitmap ToBitmap(ImageData img)
        {
            int w = img.Width, h = img.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var line = new byte[Math.Abs(d.Stride)];
            bool scale = !(img.Type == PixelType.Gray8 || img.Type == PixelType.Rgb24);
            double min = img.DisplayMin, k = img.DisplayMax > img.DisplayMin ? 255.0 / (img.DisplayMax - img.DisplayMin) : 1;
            float[] r = img.Planes[0], g = img.IsRgb ? img.Planes[1] : r, b = img.IsRgb ? img.Planes[2] : r;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    line[x * 3] = ToByte(b[i], scale, min, k);
                    line[x * 3 + 1] = ToByte(g[i], scale, min, k);
                    line[x * 3 + 2] = ToByte(r[i], scale, min, k);
                }
                Marshal.Copy(line, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), line.Length);
            }
            bmp.UnlockBits(d);
            return bmp;
        }

        private static byte ToByte(float v, bool scale, double min, double k)
        {
            double f = scale ? (v - min) * k : v;
            if (double.IsNaN(f)) return 0;
            if (f <= 0) return 0;
            if (f >= 255) return 255;
            return (byte)(f + 0.5);
        }

        /// <summary>Сохранение по расширению файла. 32-битные изображения сохраняются без потерь только в TIFF.</summary>
        public static void Save(ImageData img, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff")
            {
                TiffIO.Write(img, path);
                return;
            }
            ImageData src = img;
            if (img.IsFloat || img.Type == PixelType.Gray16)
                src = ImageOps.ConvertTo(img, img.IsRgb ? PixelType.Rgb24 : PixelType.Gray8, true);
            Bitmap bmp;
            if (src.Type == PixelType.Gray8 && ext != ".jpg" && ext != ".jpeg") bmp = ToGray8Bitmap(src);
            else bmp = ToBitmap(src);
            using (bmp) SaveBitmap(bmp, path);
        }

        public static Bitmap ToGray8Bitmap(ImageData img)
        {
            int w = img.Width, h = img.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
            ColorPalette pal = bmp.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            var line = new byte[Math.Abs(d.Stride)];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float v = img.Planes[0][y * w + x];
                    line[x] = (byte)(v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f));
                }
                Marshal.Copy(line, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), line.Length);
            }
            bmp.UnlockBits(d);
            return bmp;
        }

        /// <summary>Сохранение растра (графики, гистограммы). TIFF — без сжатия.</summary>
        public static void SaveBitmap(Bitmap bmp, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".tif":
                case ".tiff":
                    TiffIO.Write(FromBitmap(bmp), path);
                    break;
                case ".png":
                    bmp.Save(path, ImageFormat.Png);
                    break;
                case ".bmp":
                    bmp.Save(path, ImageFormat.Bmp);
                    break;
                case ".gif":
                    bmp.Save(path, ImageFormat.Gif);
                    break;
                case ".jpg":
                case ".jpeg":
                    {
                        ImageCodecInfo codec = null;
                        foreach (var c in ImageCodecInfo.GetImageEncoders())
                            if (c.FormatID == ImageFormat.Jpeg.Guid) codec = c;
                        if (codec == null) { bmp.Save(path, ImageFormat.Jpeg); break; }
                        using (var ps = new EncoderParameters(1))
                        {
                            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                            bmp.Save(path, codec, ps);
                        }
                        break;
                    }
                default:
                    bmp.Save(path, ImageFormat.Png);
                    break;
            }
        }

        /// <summary>Расширение по индексу фильтра SaveFilter (1-based), если пользователь не указал его сам.</summary>
        public static string EnsureExtension(string path, int filterIndex)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff" || ext == ".png" || ext == ".bmp" || ext == ".jpg" || ext == ".jpeg") return path;
            string[] exts = { ".tif", ".png", ".bmp", ".jpg" };
            int k = Math.Max(1, Math.Min(exts.Length, filterIndex)) - 1;
            return path + exts[k];
        }
    }
}
