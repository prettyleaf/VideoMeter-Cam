using System;

namespace SigmaLabCam.Core
{
    /// <summary>Тип пикселей.</summary>
    public enum PixelType
    {
        Gray8,   // 8 бит, 0..255, значения округляются и ограничиваются после каждой операции
        Gray16,  // 16 бит, 0..65535
        Gray32,  // 32 бита (float), без ограничений
        Rgb24,   // RGB по 8 бит на канал
        Rgb32    // RGB, каждый канал float
    }

    /// <summary>Способ получения яркости из RGB.</summary>
    public enum LumaMode
    {
        Unweighted, // (R+G+B)/3
        Weighted    // 0.299R + 0.587G + 0.114B (ТВ-яркость)
    }

    /// <summary>Глобальные настройки поведения, влияющие на расчёты.</summary>
    public static class LabOptions
    {
        public static LumaMode Luma = LumaMode.Weighted;
    }

    /// <summary>
    /// Изображение: 1 (полутоновое) или 3 (RGB) плоскости float.
    /// Для целочисленных типов значения после операций приводятся методом <see cref="Conform"/>.
    /// </summary>
    public sealed class ImageData
    {
        public readonly int Width;
        public readonly int Height;
        public PixelType Type { get; private set; }
        public float[][] Planes { get; private set; }

        public string Name = "Без имени";
        public string FilePath;

        /// <summary>Диапазон отображения (для 16/32-битных изображений).</summary>
        public double DisplayMin, DisplayMax = 255;

        public ImageData(int width, int height, PixelType type)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Размер изображения должен быть положительным.");
            Width = width;
            Height = height;
            Type = type;
            int n = width * height;
            int ch = IsRgbType(type) ? 3 : 1;
            Planes = new float[ch][];
            for (int c = 0; c < ch; c++) Planes[c] = new float[n];
            ResetDisplayRange();
        }

        public static bool IsRgbType(PixelType t) { return t == PixelType.Rgb24 || t == PixelType.Rgb32; }
        public static bool IsFloatType(PixelType t) { return t == PixelType.Gray32 || t == PixelType.Rgb32; }

        public int Channels { get { return Planes.Length; } }
        public int PixelCount { get { return Width * Height; } }
        public bool IsRgb { get { return IsRgbType(Type); } }
        public bool IsFloat { get { return IsFloatType(Type); } }
        public bool IsInteger { get { return !IsFloat; } }

        /// <summary>Максимум шкалы для целочисленных типов.</summary>
        public double MaxValue
        {
            get
            {
                switch (Type)
                {
                    case PixelType.Gray16: return 65535;
                    case PixelType.Gray32:
                    case PixelType.Rgb32: return double.NaN;
                    default: return 255;
                }
            }
        }

        public string TypeName
        {
            get
            {
                switch (Type)
                {
                    case PixelType.Gray8: return "8 бит";
                    case PixelType.Gray16: return "16 бит";
                    case PixelType.Gray32: return "32 бита (float)";
                    case PixelType.Rgb24: return "RGB";
                    case PixelType.Rgb32: return "RGB 32 бита (float)";
                }
                return Type.ToString();
            }
        }

        public string Description
        {
            get { return Width + "×" + Height + ", " + TypeName; }
        }

        public ImageData Clone()
        {
            var r = new ImageData(Width, Height, Type);
            for (int c = 0; c < Channels; c++) Array.Copy(Planes[c], r.Planes[c], Planes[c].Length);
            r.Name = Name;
            r.FilePath = FilePath;
            r.DisplayMin = DisplayMin;
            r.DisplayMax = DisplayMax;
            return r;
        }

        /// <summary>Новое изображение того же размера и типа (пустое).</summary>
        public ImageData CreateEmpty(PixelType? type = null)
        {
            var r = new ImageData(Width, Height, type ?? Type);
            r.Name = Name;
            return r;
        }

        /// <summary>
        /// Приводит значения к допустимому для типа диапазону: округление и ограничение
        /// для 8/16 бит (как ImageJ при обратном преобразовании из float), пересчёт диапазона отображения.
        /// </summary>
        public void Conform()
        {
            if (IsInteger)
            {
                float max = Type == PixelType.Gray16 ? 65535f : 255f;
                for (int c = 0; c < Channels; c++)
                {
                    float[] p = Planes[c];
                    for (int i = 0; i < p.Length; i++)
                    {
                        float v = p[i];
                        if (float.IsNaN(v)) v = 0;
                        else if (v <= 0) v = 0;
                        else if (v >= max) v = max;
                        else v = (float)Math.Floor(v + 0.5f);
                        p[i] = v;
                    }
                }
            }
            ResetDisplayRange();
        }

        public void ResetDisplayRange()
        {
            if (Type == PixelType.Gray8 || Type == PixelType.Rgb24)
            {
                DisplayMin = 0;
                DisplayMax = 255;
                return;
            }
            double min = double.MaxValue, max = double.MinValue;
            for (int c = 0; c < Channels; c++)
            {
                float[] p = Planes[c];
                for (int i = 0; i < p.Length; i++)
                {
                    float v = p[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
            if (min > max) { min = 0; max = 1; }
            if (max == min) { max = min + 1; }
            DisplayMin = min;
            DisplayMax = max;
        }

        /// <summary>Яркость пикселя i по правилам ImageJ.</summary>
        public float Luma(int i)
        {
            if (!IsRgb) return Planes[0][i];
            float r = Planes[0][i], g = Planes[1][i], b = Planes[2][i];
            if (LabOptions.Luma == LumaMode.Weighted)
            {
                float v = r * 0.299f + g * 0.587f + b * 0.114f;
                return Type == PixelType.Rgb24 ? (float)Math.Floor(v + 0.5f) : v;
            }
            // ImageJ: (r+g+b)/3 целочисленным делением для 8-битного RGB
            return Type == PixelType.Rgb24 ? (float)(((int)r + (int)g + (int)b) / 3) : (r + g + b) / 3f;
        }

        /// <summary>Плоскость яркости (для RGB) или ссылка на единственную плоскость.</summary>
        public float[] LumaPlane()
        {
            if (!IsRgb) return Planes[0];
            var res = new float[PixelCount];
            for (int i = 0; i < res.Length; i++) res[i] = Luma(i);
            return res;
        }

        /// <summary>Значение в точке в виде строки для строки состояния.</summary>
        public string ValueString(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return "";
            int i = y * Width + x;
            if (IsRgb)
            {
                if (IsFloat)
                    return string.Format("R={0:0.###} G={1:0.###} B={2:0.###}", Planes[0][i], Planes[1][i], Planes[2][i]);
                return string.Format("R={0} G={1} B={2}", Planes[0][i], Planes[1][i], Planes[2][i]);
            }
            return IsFloat ? Planes[0][i].ToString("0.####") : Planes[0][i].ToString("0");
        }

        public static PixelType RgbOf(PixelType t) { return IsFloatType(t) ? PixelType.Rgb32 : PixelType.Rgb24; }
        public static PixelType GrayOf(PixelType t)
        {
            if (t == PixelType.Rgb24) return PixelType.Gray8;
            if (t == PixelType.Rgb32) return PixelType.Gray32;
            return t;
        }
    }
}
