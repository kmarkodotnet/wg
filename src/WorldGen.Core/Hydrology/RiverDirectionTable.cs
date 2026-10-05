using System;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// A folytonos nyomvonal-követés jelölt-irány tábláját állítja elő
    /// TRIGONOMETRIA NÉLKÜL (ND-186).
    ///
    /// MIÉRT. A korábbi követő minden lépésben `Math.Cos(2*PI*k/n)` /
    /// `Math.Sin(...)`-szel számolta a jelölt-irányokat. A CLAUDE.md
    /// lebegőpontos táblázata szerint a `Math.Sin`/`Math.Cos` NEM garantáltan
    /// bitpontos platformok között, tehát ez az I1 (determinizmus) csendes
    /// sérülése volt a folyóhálózat kritikus útján.
    ///
    /// A MEGOLDÁS: szögfelezés. Az első negyedet (0°-90°) az (1,0) és (0,1)
    /// vektorokból ismételt `normalize(a + b)` lépésekkel finomítjuk - ez
    /// EGZAKTUL a két irány szögfelezője, és kizárólag `+`, `/`,
    /// <see cref="Math.Sqrt"/> műveleteket használ. A többi három negyedet
    /// EGZAKT 90°-os forgatással ((c, s) -> (-s, c)) kapjuk, tehát a tábla
    /// bitre szimmetrikus: a k. és a (k + n/2). irány egymás pontos
    /// ellentettje. Ezért a jelölt-kör nem visz be iránytorzítást, és a
    /// belőle számolt gradiens-összeg (ld.
    /// <see cref="RiverPathTracing.DescentDirection"/>) átlagmentes.
    ///
    /// Python-referencia: tools/reference/river_continuous_ref.py
    /// (`ring_directions`).
    /// </summary>
    public static class RiverDirectionTable
    {
        /// <summary>
        /// `count` egyenletesen elosztott érintősíki egységvektor. A `count`
        /// 2 hatványa és legalább 4 - ez nem kényelmi megkötés: a szögfelezés
        /// és az egzakt negyed-szimmetria csak így ad bitre szimmetrikus
        /// táblát.
        /// </summary>
        public static (double X, double Y)[] Build(int count)
        {
            if (count < 4 || (count & (count - 1)) != 0)
                throw new ArgumentOutOfRangeException(
                    nameof(count), "A jelölt-irányok száma 2 hatványa és legalább 4 legyen.");

            int quarter = count / 4;

            // Első negyed: (1,0) és (0,1) között ismételt szögfelezéssel,
            // amíg `quarter` darab egyenlő szögosztás lesz.
            var arc = new (double X, double Y)[2];
            arc[0] = (1.0, 0.0);
            arc[1] = (0.0, 1.0);
            while (arc.Length - 1 < quarter)
            {
                var refined = new (double X, double Y)[(arc.Length - 1) * 2 + 1];
                refined[0] = arc[0];
                for (int k = 0; k < arc.Length - 1; k++)
                {
                    double sx = arc[k].X + arc[k + 1].X;
                    double sy = arc[k].Y + arc[k + 1].Y;
                    double len = Math.Sqrt(sx * sx + sy * sy);
                    refined[2 * k + 1] = (sx / len, sy / len);
                    refined[2 * k + 2] = arc[k + 1];
                }
                arc = refined;
            }

            var directions = new (double X, double Y)[count];
            for (int k = 0; k < count; k++)
            {
                int q = k / quarter;
                int r = k - q * quarter;
                double cx = arc[r].X, cy = arc[r].Y;
                for (int t = 0; t < q; t++)
                {
                    double nx = -cy;
                    cy = cx;
                    cx = nx;
                }
                directions[k] = (cx, cy);
            }
            return directions;
        }
    }
}
