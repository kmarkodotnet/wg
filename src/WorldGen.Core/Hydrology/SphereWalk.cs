using System;

namespace WorldGen.Core.Hydrology
{
    /// <summary>
    /// A folytonos (tile-rácstól független) nyomvonal-követés gömbi
    /// alapműveletei. KIEMELVE a <see cref="RiverPathTracing"/>-ből (ND-186),
    /// hogy az összefolyási térbeli index (<see cref="ClaimedRiverPoints"/>)
    /// és az escape-finomítás UGYANAZT az érintő-bázist és lépés-képletet
    /// használja - a kiemelés BIT-SEMLEGES, a képletek karakterre azonosak a
    /// korábbi privát változatokkal.
    ///
    /// BITPONTOSSÁG (I1, CLAUDE.md lebegőpontos táblázat): minden művelet
    /// `+ - * /` és <see cref="Math.Sqrt"/>, azaz IEEE-754 szerint korrekten
    /// kerekített. SEMMILYEN transzcendens függvény (sin/cos/tan/atan/exp/log)
    /// nincs ezen az úton - ez tudatos: a korábbi követő `Math.Cos`/`Math.Sin`-nel
    /// állította elő a jelölt-irányokat, ami az ND-23 szerint nem bitpontos.
    ///
    /// Python-referencia: tools/reference/river_continuous_ref.py.
    /// </summary>
    public static class SphereWalk
    {
        /// <summary>
        /// Két, a `p` egységvektorra merőleges, egymásra is merőleges
        /// érintő-irány (Gram-Schmidt egy tetszőleges referenciából).
        /// </summary>
        public static void GetTangentBasis(
            (double X, double Y, double Z) p,
            out (double X, double Y, double Z) t1, out (double X, double Y, double Z) t2)
        {
            (double X, double Y, double Z) reference = Math.Abs(p.Z) < 0.9 ? (0.0, 0.0, 1.0) : (0.0, 1.0, 0.0);
            double dot = p.X * reference.X + p.Y * reference.Y + p.Z * reference.Z;
            double rx = reference.X - dot * p.X, ry = reference.Y - dot * p.Y, rz = reference.Z - dot * p.Z;
            double len = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            t1 = (rx / len, ry / len, rz / len);
            t2 = (p.Y * t1.Z - p.Z * t1.Y, p.Z * t1.X - p.X * t1.Z, p.X * t1.Y - p.Y * t1.X); // p x t1
        }

        /// <summary>
        /// Érintősíki elmozdulás `angularStep` nagysággal a (dx, dy)
        /// irányban, majd EGZAKT visszanormalizálás a gömbre.
        /// </summary>
        public static (double X, double Y, double Z) StepInTangentDirection(
            (double X, double Y, double Z) p, (double X, double Y, double Z) t1, (double X, double Y, double Z) t2,
            double dx, double dy, double angularStep)
        {
            double nx = p.X + angularStep * (dx * t1.X + dy * t2.X);
            double ny = p.Y + angularStep * (dx * t1.Y + dy * t2.Y);
            double nz = p.Z + angularStep * (dx * t1.Z + dy * t2.Z);
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return (nx / len, ny / len, nz / len);
        }

        /// <summary>
        /// Az `a` és `b` közti nagykör-szakasz `t` paramétere - normalizált
        /// lineáris interpoláció. A szóba jövő léptéken (néhány km a 7420
        /// km-es sugáron) a chord- és a nagykör-paraméterezés eltérése
        /// elhanyagolható (a kettő közti maximális pozíció-eltérés egy 40
        /// km-es szakaszon is a milliméter alatt van), és a művelet
        /// bitpontos - a `Math.Sin`-es slerp NEM lenne az.
        /// </summary>
        public static (double X, double Y, double Z) GeodesicPoint(
            (double X, double Y, double Z) a, (double X, double Y, double Z) b, double t)
        {
            double nx = a.X + (b.X - a.X) * t;
            double ny = a.Y + (b.Y - a.Y) * t;
            double nz = a.Z + (b.Z - a.Z) * t;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return (nx / len, ny / len, nz / len);
        }

        /// <summary>Két egységgömb-pont EUKLIDESZI (chord) távolsága méterben.</summary>
        public static double ChordMeters((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) * PlanetConstants.RadiusMeters;
        }
    }
}
