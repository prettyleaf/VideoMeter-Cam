using System;
using System.Drawing;

namespace VideoMeterCam.Core
{
    /// <summary>Простые преобразования изображения: тип, обрезка, отражение, поворот.</summary>
    public static class ImageOps
    {
        /// <summary>Тип, в который переводится изображение (Изображение › Тип).</summary>
        public static ImageData ConvertTo(ImageData src, PixelType type, bool scale = true)
        {
            if (src.Type == type) return src.Clone();
            ImageData r = new ImageData(src.Width, src.Height, type);
            r.Name = src.Name;
            r.FilePath = src.FilePath;
            int n = src.PixelCount;
            bool dstRgb = ImageData.IsRgbType(type);

            if (src.IsRgb && !dstRgb)
            {
                // RGB → полутоновое: яркость
                float[] l = src.LumaPlane();
                Array.Copy(l, r.Planes[0], n);
                if (type == PixelType.Gray8 && src.Type == PixelType.Rgb32 && scale)
                    ScaleInto(src, l, r.Planes[0], 255);
            }
            else if (!src.IsRgb && dstRgb)
            {
                float[] g = src.Planes[0];
                float[] tmp = g;
                if (type == PixelType.Rgb24 && (src.Type == PixelType.Gray32 || src.Type == PixelType.Gray16) && scale)
                {
                    tmp = new float[n];
                    ScaleInto(src, g, tmp, 255);
                }
                for (int c = 0; c < 3; c++) Array.Copy(tmp, r.Planes[c], n);
            }
            else if (src.IsRgb && dstRgb)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (type == PixelType.Rgb24 && scale && src.Type == PixelType.Rgb32) ScaleInto(src, src.Planes[c], r.Planes[c], 255);
                    else Array.Copy(src.Planes[c], r.Planes[c], n);
                }
            }
            else
            {
                if ((type == PixelType.Gray8 && src.Type != PixelType.Gray8) && scale)
                    ScaleInto(src, src.Planes[0], r.Planes[0], 255);
                else if (type == PixelType.Gray16 && src.Type == PixelType.Gray32 && scale)
                    ScaleInto(src, src.Planes[0], r.Planes[0], 65535);
                else
                    Array.Copy(src.Planes[0], r.Planes[0], n);
            }
            r.Conform();
            return r;
        }

        /// <summary>Линейное масштабирование диапазона отображения в 0..max.</summary>
        private static void ScaleInto(ImageData src, float[] from, float[] to, float max)
        {
            double min = src.DisplayMin, dmax = src.DisplayMax;
            double k = dmax > min ? max / (dmax - min) : 1;
            for (int i = 0; i < from.Length; i++)
            {
                double v = (from[i] - min) * k;
                if (double.IsNaN(v)) v = 0;
                to[i] = (float)v;
            }
        }

        public static ImageData Crop(ImageData src, Rectangle rect)
        {
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, src.Width, src.Height));
            if (rect.Width <= 0 || rect.Height <= 0) throw new ArgumentException("Пустая область обрезки.");
            var r = new ImageData(rect.Width, rect.Height, src.Type);
            r.Name = src.Name;
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < rect.Height; y++)
                    Array.Copy(src.Planes[c], (rect.Y + y) * src.Width + rect.X, r.Planes[c], y * rect.Width, rect.Width);
            r.DisplayMin = src.DisplayMin;
            r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData Invert(ImageData src)
        {
            var r = src.Clone();
            double hi = src.IsInteger ? src.MaxValue : src.DisplayMax, lo = src.IsInteger ? 0 : src.DisplayMin;
            for (int c = 0; c < r.Channels; c++)
            {
                float[] p = r.Planes[c];
                for (int i = 0; i < p.Length; i++) p[i] = (float)(hi + lo - p[i]);
            }
            r.Conform();
            return r;
        }

        public static ImageData FlipHorizontal(ImageData src)
        {
            var r = src.CreateEmpty();
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < src.Height; y++)
                {
                    int row = y * src.Width;
                    for (int x = 0; x < src.Width; x++) r.Planes[c][row + x] = src.Planes[c][row + src.Width - 1 - x];
                }
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData FlipVertical(ImageData src)
        {
            var r = src.CreateEmpty();
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < src.Height; y++)
                    Array.Copy(src.Planes[c], (src.Height - 1 - y) * src.Width, r.Planes[c], y * src.Width, src.Width);
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        public static ImageData Rotate90(ImageData src, bool clockwise)
        {
            var r = new ImageData(src.Height, src.Width, src.Type);
            r.Name = src.Name;
            int W = src.Width, H = src.Height;
            for (int c = 0; c < src.Channels; c++)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int nx, ny;
                        if (clockwise) { nx = H - 1 - y; ny = x; }
                        else { nx = y; ny = W - 1 - x; }
                        r.Planes[c][ny * H + nx] = src.Planes[c][y * W + x];
                    }
            r.DisplayMin = src.DisplayMin; r.DisplayMax = src.DisplayMax;
            return r;
        }

        /// <summary>Значение канала c в точке (x, y) с билинейной интерполяцией; вне изображения — NaN.</summary>
        public static float Bilinear(ImageData img, int c, double x, double y)
        {
            int w = img.Width, h = img.Height;
            if (x < -0.5 || y < -0.5 || x > w - 0.5 || y > h - 0.5) return float.NaN;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x > w - 1) x = w - 1;
            if (y > h - 1) y = h - 1;
            int x0 = (int)x, y0 = (int)y;
            int x1 = x0 + 1 < w ? x0 + 1 : x0, y1 = y0 + 1 < h ? y0 + 1 : y0;
            double fx = x - x0, fy = y - y0;
            float[] p = img.Planes[c];
            double a = p[y0 * w + x0] + (p[y0 * w + x1] - p[y0 * w + x0]) * fx;
            double b = p[y1 * w + x0] + (p[y1 * w + x1] - p[y1 * w + x0]) * fx;
            return (float)(a + (b - a) * fy);
        }
    }
}
