using System;

namespace VideoMeterCam.Core
{
    /// <summary>Формулы методички для коэффициентов координатных искажений, %.</summary>
    public static class Coef
    {
        /// <summary>(1.6) Длина отрезка AB.</summary>
        public static double Length(PointD a, PointD b) { return PointD.Dist(a, b); }

        /// <summary>(1.7) Середина отрезка AB.</summary>
        public static PointD Mid(PointD a, PointD b) { return PointD.Mid(a, b); }

        /// <summary>(1.1) Коэффициент геометрических искажений Kг = Δh / b · 100%.</summary>
        public static double Geometric(double dh, double b) { return b != 0 ? dh / b * 100 : double.NaN; }

        /// <summary>(1.2) Трапецеидальные искажения Kтг = 2·(L2 − L1)/(L1 + L2) · 100%.</summary>
        public static double Trapezoid(double l1, double l2) { return l1 + l2 != 0 ? 2 * (l2 - l1) / (l1 + l2) * 100 : double.NaN; }

        /// <summary>(1.3) Нелинейность развёртки K = 2·(max − min)/(max + min) · 100%.</summary>
        public static double Nonlinearity(double max, double min) { return max + min != 0 ? 2 * (max - min) / (max + min) * 100 : double.NaN; }

        /// <summary>
        /// (1.8)–(1.11) Коэффициент искажения стороны AB: K = |EE₁| / |AB| · 100%, где E — средняя точка стороны
        /// на изображении, E₁ — середина отрезка AB.
        /// </summary>
        public static double Side(PointD a, PointD b, PointD e, out PointD e1, out double dh)
        {
            e1 = Mid(a, b);
            dh = Length(e, e1);
            return Geometric(dh, Length(a, b));
        }

        /// <summary>
        /// С какой стороны от хорды AB лежит точка E: +1 — дальше от центра фигуры (сторона выгнута, «бочка»),
        /// −1 — ближе к центру (вогнута, «подушка»), 0 — на хорде.
        /// </summary>
        public static int Bulge(PointD a, PointD b, PointD e, PointD center)
        {
            double cross = (b.X - a.X) * (e.Y - a.Y) - (b.Y - a.Y) * (e.X - a.X);
            double crossC = (b.X - a.X) * (center.Y - a.Y) - (b.Y - a.Y) * (center.X - a.X);
            double len = Length(a, b);
            if (len == 0 || Math.Abs(cross) / len < 1e-6) return 0;
            return Math.Sign(cross) == Math.Sign(crossC) ? -1 : 1;
        }
    }

    /// <summary>
    /// Коэффициенты K1…K4 по восьми точкам рис. 1.7: A, B, C, D — углы (точки 1, 13, 118, 130 таблицы 13×10),
    /// E, F, G, H — средние точки верхней, левой, правой и нижней сторон.
    /// </summary>
    public sealed class SideCoefficients
    {
        public PointD A, B, C, D, E, F, G, H;
        public PointD E1, F1, G1, H1;
        public double AB, AC, BD, CD;
        public double Dh1, Dh2, Dh3, Dh4;
        public double K1, K2, K3, K4;
        public int Bulge1, Bulge2, Bulge3, Bulge4;

        public static SideCoefficients Compute(PointD a, PointD b, PointD c, PointD d, PointD e, PointD f, PointD g, PointD h)
        {
            var r = new SideCoefficients { A = a, B = b, C = c, D = d, E = e, F = f, G = g, H = h };
            r.AB = Coef.Length(a, b); r.AC = Coef.Length(a, c); r.BD = Coef.Length(b, d); r.CD = Coef.Length(c, d);
            r.K1 = Coef.Side(a, b, e, out r.E1, out r.Dh1);
            r.K2 = Coef.Side(a, c, f, out r.F1, out r.Dh2);
            r.K3 = Coef.Side(b, d, g, out r.G1, out r.Dh3);
            r.K4 = Coef.Side(c, d, h, out r.H1, out r.Dh4);
            var center = new PointD((a.X + b.X + c.X + d.X) / 4, (a.Y + b.Y + c.Y + d.Y) / 4);
            r.Bulge1 = Coef.Bulge(a, b, e, center);
            r.Bulge2 = Coef.Bulge(a, c, f, center);
            r.Bulge3 = Coef.Bulge(b, d, g, center);
            r.Bulge4 = Coef.Bulge(c, d, h, center);
            return r;
        }

        public static string BulgeName(int b) { return b > 0 ? "бочка" : (b < 0 ? "подушка" : "прямая"); }

        /// <summary>Трапеция по горизонтальным сторонам (L1 = |AB| — верх, L2 = |CD| — низ), формула (1.2).</summary>
        public double TrapezoidHorizontal { get { return Coef.Trapezoid(AB, CD); } }

        /// <summary>Трапеция по вертикальным сторонам (L1 = |AC| — лево, L2 = |BD| — право).</summary>
        public double TrapezoidVertical { get { return Coef.Trapezoid(AC, BD); } }
    }
}

namespace VideoMeterCam.Core
{
    /// <summary>Разбор чисел и с точкой, и с запятой.</summary>
    public static class NumParse
    {
        public static bool TryParse(string s, out double v)
        {
            v = 0;
            if (s == null) return false;
            s = s.Trim().Replace(',', '.').Replace('−', '-');
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }
    }
}
