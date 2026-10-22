using System;
using System.Collections.Generic;
using System.IO;
using VideoMeterCam.Core;

namespace VideoMeterCam.IO
{
    /// <summary>
    /// Чтение и запись TIFF без сжатия: 8/16 бит, 32 бита float, RGB (8 бит и float).
    /// Сжатые и мозаичные TIFF читаются через GDI+ (см. <see cref="ImageIO"/>).
    /// </summary>
    public static class TiffIO
    {
        // ------------------------------------------------------------------ запись

        public static void Write(ImageData img, string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
                Write(img, w);
        }

        public static void Write(ImageData img, BinaryWriter w)
        {
            int spp = img.Channels;
            int bps;
            int sampleFormat;
            switch (img.Type)
            {
                case PixelType.Gray8:
                case PixelType.Rgb24: bps = 8; sampleFormat = 1; break;
                case PixelType.Gray16: bps = 16; sampleFormat = 1; break;
                default: bps = 32; sampleFormat = 3; break;
            }
            int bytesPerSample = bps / 8;
            long dataSize = (long)img.Width * img.Height * spp * bytesPerSample;

            const ushort SHORT = 3, LONG = 4, RATIONAL = 5;
            int nTags = 14;
            int ifdSize = 2 + nTags * 12 + 4;
            int extraStart = 8 + ifdSize;
            int bpsOffset = extraStart;                  // 3 SHORT для RGB
            int xresOffset = bpsOffset + 8;
            int yresOffset = xresOffset + 8;
            int dataOffset = yresOffset + 8;

            w.Write((byte)'I'); w.Write((byte)'I');
            w.Write((ushort)42);
            w.Write((uint)8);

            w.Write((ushort)nTags);
            WriteTag(w, 254, LONG, 1, 0);                               // NewSubfileType
            WriteTag(w, 256, LONG, 1, (uint)img.Width);
            WriteTag(w, 257, LONG, 1, (uint)img.Height);
            if (spp == 1) WriteTag(w, 258, SHORT, 1, (uint)bps);
            else WriteTag(w, 258, SHORT, 3, (uint)bpsOffset);
            WriteTag(w, 259, SHORT, 1, 1);                              // без сжатия
            WriteTag(w, 262, SHORT, 1, spp == 3 ? 2u : 1u);             // RGB / BlackIsZero
            WriteTag(w, 273, LONG, 1, (uint)dataOffset);
            WriteTag(w, 277, SHORT, 1, (uint)spp);
            WriteTag(w, 278, LONG, 1, (uint)img.Height);
            WriteTag(w, 279, LONG, 1, (uint)dataSize);
            WriteTag(w, 282, RATIONAL, 1, (uint)xresOffset);
            WriteTag(w, 283, RATIONAL, 1, (uint)yresOffset);
            WriteTag(w, 296, SHORT, 1, 2);                              // дюймы
            WriteTag(w, 339, SHORT, 1, (uint)sampleFormat);
            w.Write((uint)0);                                           // следующего IFD нет

            // дополнительные данные
            w.Write((ushort)bps); w.Write((ushort)bps); w.Write((ushort)bps); w.Write((ushort)0);
            w.Write((uint)72); w.Write((uint)1);
            w.Write((uint)72); w.Write((uint)1);

            var buf = new byte[img.Width * spp * bytesPerSample];
            for (int y = 0; y < img.Height; y++)
            {
                int k = 0;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = y * img.Width + x;
                    for (int c = 0; c < spp; c++)
                    {
                        float v = img.Planes[c][i];
                        if (bps == 8) buf[k++] = (byte)Math.Max(0, Math.Min(255, (int)v));
                        else if (bps == 16)
                        {
                            int u = Math.Max(0, Math.Min(65535, (int)v));
                            buf[k++] = (byte)(u & 0xff);
                            buf[k++] = (byte)(u >> 8);
                        }
                        else
                        {
                            byte[] b = BitConverter.GetBytes(v);
                            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
                            buf[k++] = b[0]; buf[k++] = b[1]; buf[k++] = b[2]; buf[k++] = b[3];
                        }
                    }
                }
                w.Write(buf, 0, k);
            }
        }

        private static void WriteTag(BinaryWriter w, ushort tag, ushort type, uint count, uint value)
        {
            w.Write(tag);
            w.Write(type);
            w.Write(count);
            if (type == 3 && count == 1) { w.Write((ushort)value); w.Write((ushort)0); }
            else w.Write(value);
        }

        // ------------------------------------------------------------------ чтение

        private sealed class Reader
        {
            private readonly byte[] _d;
            public bool Big;
            public Reader(byte[] d) { _d = d; }
            public int Length { get { return _d.Length; } }
            public ushort U16(long o)
            {
                return Big ? (ushort)((_d[o] << 8) | _d[o + 1]) : (ushort)(_d[o] | (_d[o + 1] << 8));
            }
            public uint U32(long o)
            {
                return Big
                    ? (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3])
                    : (uint)(_d[o] | (_d[o + 1] << 8) | (_d[o + 2] << 16) | (_d[o + 3] << 24));
            }
            public float F32(long o)
            {
                uint u = U32(o);
                return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
            }
            public byte B(long o) { return _d[o]; }
        }

        private sealed class Tag
        {
            public ushort Type;
            public uint Count;
            public long ValueOffset; // смещение самого значения (внутри записи или вне её)
        }

        /// <summary>Возвращает null, если формат не поддерживается этим читателем.</summary>
        public static ImageData TryRead(byte[] data)
        {
            try { return Read(data); }
            catch { return null; }
        }

        private static ImageData Read(byte[] data)
        {
            if (data.Length < 8) return null;
            var r = new Reader(data);
            if (data[0] == 'I' && data[1] == 'I') r.Big = false;
            else if (data[0] == 'M' && data[1] == 'M') r.Big = true;
            else return null;
            if (r.U16(2) != 42) return null;
            long ifd = r.U32(4);
            int n = r.U16(ifd);
            var tags = new Dictionary<int, Tag>();
            for (int i = 0; i < n; i++)
            {
                long e = ifd + 2 + i * 12;
                var t = new Tag { Type = r.U16(e + 2), Count = r.U32(e + 4) };
                int size = TypeSize(t.Type) * (int)t.Count;
                t.ValueOffset = size <= 4 ? e + 8 : r.U32(e + 8);
                tags[r.U16(e)] = t;
            }
            Func<int, int, uint> get = (tag, def) =>
            {
                Tag t;
                if (!tags.TryGetValue(tag, out t)) return (uint)def;
                return t.Type == 3 ? r.U16(t.ValueOffset) : r.U32(t.ValueOffset);
            };
            Func<int, uint[]> arr = tag =>
            {
                Tag t;
                if (!tags.TryGetValue(tag, out t)) return null;
                var a = new uint[t.Count];
                for (int i = 0; i < t.Count; i++)
                    a[i] = t.Type == 3 ? r.U16(t.ValueOffset + 2 * i) : r.U32(t.ValueOffset + 4 * i);
                return a;
            };

            int width = (int)get(256, 0), height = (int)get(257, 0);
            if (width <= 0 || height <= 0) return null;
            if (get(259, 1) != 1) return null;               // только без сжатия
            if (tags.ContainsKey(322)) return null;          // мозаичные — через GDI+
            int spp = (int)get(277, 1);
            int planar = (int)get(284, 1);
            if (planar != 1 && spp > 1) return null;
            uint[] bpsArr = arr(258);
            int bps = bpsArr != null ? (int)bpsArr[0] : 1;
            int photometric = (int)get(262, 1);
            int sampleFormat = (int)get(339, 1);
            uint[] offsets = arr(273);
            if (offsets == null) return null;
            int rowsPerStrip = (int)get(278, height);
            if (rowsPerStrip <= 0 || rowsPerStrip > height) rowsPerStrip = height;
            if (bps != 8 && bps != 16 && bps != 32) return null;
            if (bps == 32 && sampleFormat != 3) return null;
            if (photometric == 3 && bps != 8) return null;

            bool rgb = spp >= 3 && photometric == 2;
            PixelType type;
            if (photometric == 3) type = PixelType.Rgb24;
            else if (rgb) type = bps == 8 ? PixelType.Rgb24 : PixelType.Rgb32;
            else type = bps == 8 ? PixelType.Gray8 : (bps == 16 ? PixelType.Gray16 : PixelType.Gray32);
            if (!rgb && spp != 1 && photometric != 3) return null;

            var img = new ImageData(width, height, type);
            int bpsBytes = bps / 8;
            int rowBytes = width * spp * bpsBytes;
            uint[] cmap = photometric == 3 ? arr(320) : null;
            bool grayPalette = true;
            if (cmap != null)
                for (int i = 0; i < 256 && i + 512 < cmap.Length; i++)
                    if (cmap[i] >> 8 != i || cmap[i + 256] >> 8 != i || cmap[i + 512] >> 8 != i) { grayPalette = false; break; }
            if (photometric == 3 && grayPalette) img = new ImageData(width, height, PixelType.Gray8);

            for (int y = 0; y < height; y++)
            {
                int strip = y / rowsPerStrip;
                if (strip >= offsets.Length) break;
                long rowStart = offsets[strip] + (long)(y - strip * rowsPerStrip) * rowBytes;
                if (rowStart + rowBytes > r.Length) return null;
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    long p = rowStart + (long)x * spp * bpsBytes;
                    if (photometric == 3)
                    {
                        int idx = r.B(p);
                        if (grayPalette) img.Planes[0][i] = idx;
                        else
                        {
                            img.Planes[0][i] = cmap[idx] >> 8;
                            img.Planes[1][i] = cmap[idx + 256] >> 8;
                            img.Planes[2][i] = cmap[idx + 512] >> 8;
                        }
                        continue;
                    }
                    int chn = rgb ? 3 : 1;
                    for (int c = 0; c < chn; c++)
                    {
                        long q = p + c * bpsBytes;
                        float v = bps == 8 ? r.B(q) : (bps == 16 ? r.U16(q) : r.F32(q));
                        if (photometric == 0) v = (bps == 8 ? 255 : (bps == 16 ? 65535 : 0)) - v; // WhiteIsZero
                        img.Planes[c][i] = v;
                    }
                }
            }
            img.ResetDisplayRange();
            return img;
        }

        private static int TypeSize(ushort type)
        {
            switch (type)
            {
                case 1: case 2: case 6: case 7: return 1;
                case 3: case 8: return 2;
                case 4: case 9: case 11: return 4;
                case 5: case 10: case 12: return 8;
            }
            return 1;
        }
    }
}
