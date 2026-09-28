using System;
using WorldGen.Core.Climate;
using WorldGen.Core.Grid;

namespace WorldGen.Viewer.Lod
{
    /// <summary>
    /// A TÉRFOGATI felhő (ND-154) és az égbolt-nyitottság (ND-155) közös
    /// prezentációs adatútja — UnityEngine-függés nélkül, hogy .NET-ből
    /// Unity Editor nélkül is tesztelhető legyen.
    ///
    /// AZ ATLASZ GEOMETRIÁJA NEM ÚJ. Pontosan ugyanaz a hat-lapos, laponként
    /// 66×66 texeles (64×64 cella + egycellás gutter) kocka-atlasz, amit a
    /// hőmérséklet-overlay (ND-104) használ, és ugyanazzal a
    /// <see cref="ThermalOverlayPacking.BuildTexelSourceMap"/> texel→cella
    /// leképezéssel: a gutter a szomszédos LAP értékét kapja, tehát a
    /// bilineáris mintavétel a kockalap-éleken sem kever idegen értéket. Ez a
    /// mechanizmus már KAT-szinten tesztelt, ezért nem írjuk újra — a shader
    /// oldalon is ugyanaz az <c>AtlasUv</c> képlet érvényes.
    ///
    /// NÉGY CSATORNA, MIND A VILÁGMODELLBŐL (I3):
    /// <list type="bullet">
    /// <item><b>R</b> — felhő-lefedettség [0,1] a csapadék-mezőből
    /// (<see cref="CloudVolume.ShapeCoverage"/>).</item>
    /// <item><b>G</b> — a felhőalap a tengerszint fölött, a
    /// <see cref="CloudVolume.MaxBaseAboveSeaLevelMeters"/>-re normálva. NEM a
    /// lefedettség függvénye csupán: a TALAJ magassága is benne van, ezért a
    /// felhőtakaró ráborul a hegyláncra.</item>
    /// <item><b>B</b> — a felhő vastagsága, a
    /// <see cref="CloudVolume.MaxThicknessMeters"/>-re normálva.</item>
    /// <item><b>A</b> — égbolt-nyitottság [0,1]
    /// (<see cref="WorldGen.Core.Terrain.SurfaceSkyOpenness"/>), a felszíni
    /// AO makro sávja.</item>
    /// </list>
    ///
    /// MIÉRT EGY TEXTÚRA. A négy mező ugyanazon a rácson, ugyanabban a
    /// háttérmunkában készül, és két KÜLÖN shader olvassa (a felhő-raymarch és
    /// a terep-shader felhőárnyéka). Egy RGBA32 atlasz 396×66 texel = 104 KB —
    /// egyetlen feltöltés, egyetlen uniform-készlet, nulla dead channel.
    /// </summary>
    public static class CloudSkyAtlas
    {
        /// <summary>Az atlasz-szint — a hő-overlayjel AZONOS (level 6, 24576 cella).</summary>
        public const int Level = ThermalOverlayPacking.Level;

        /// <summary>Lapélenkénti cellaszám.</summary>
        public const int Side = ThermalOverlayPacking.Side;

        /// <summary>Lapélenkénti texelszám (cella + gutter).</summary>
        public const int FaceTexels = ThermalOverlayPacking.FaceTexels;

        /// <summary>Az atlasz szélessége texelben.</summary>
        public const int AtlasWidth = ThermalOverlayPacking.AtlasWidth;

        /// <summary>Az atlasz magassága texelben.</summary>
        public const int AtlasHeight = ThermalOverlayPacking.AtlasHeight;

        /// <summary>Csatornák száma (RGBA32).</summary>
        public const int Channels = 4;

        /// <summary>
        /// [0,1] érték kvantálása egy byte-ra. Kerekítéssel (nem vágással),
        /// hogy a 0 és az 1 EGZAKTUL a 0 és a 255 legyen — a nulla
        /// lefedettségnek pontosan nullának kell dekódolódnia, erre épül az
        /// I3-garancia („nincs felhő, ahol a modell szerint nincs”).
        /// </summary>
        public static byte QuantizeUnit(double value)
        {
            if (double.IsNaN(value) || value <= 0.0) return 0;
            if (value >= 1.0) return 255;
            return (byte)Math.Floor(value * 255.0 + 0.5);
        }

        /// <summary>A <see cref="QuantizeUnit"/> inverze.</summary>
        public static double DecodeUnit(byte quantized) => quantized * (1.0 / 255.0);

        /// <summary>Méter-érték kvantálása egy adott plafonhoz normálva.</summary>
        public static byte QuantizeMeters(double meters, double maxMeters)
        {
            if (maxMeters <= 0.0) return 0;
            return QuantizeUnit(meters / maxMeters);
        }

        /// <summary>
        /// A négy csatorna kiszámítása cellánként. A lefedettség és az
        /// eleváció a MÁR KISZÁMÍTOTT mezőkből jön (nulla új
        /// csapadék-/eleváció-kiértékelés); az alj és a vastagság a
        /// <see cref="CloudVolume"/> származtatott leképezései.
        /// </summary>
        public static void BuildChannels(
            double[] coverage, double[] elevationMeters, double seaLevelMeters,
            double[] baseMeters, double[] thicknessMeters,
            double deckBaseMeters = CloudVolume.MidLevelBaseMeters)
        {
            if (coverage == null) throw new ArgumentNullException(nameof(coverage));
            if (elevationMeters == null) throw new ArgumentNullException(nameof(elevationMeters));
            if (baseMeters == null) throw new ArgumentNullException(nameof(baseMeters));
            if (thicknessMeters == null) throw new ArgumentNullException(nameof(thicknessMeters));
            if (elevationMeters.Length != coverage.Length || baseMeters.Length != coverage.Length || thicknessMeters.Length != coverage.Length)
                throw new ArgumentException("A négy tömb hossza nem egyezik.", nameof(coverage));

            for (int c = 0; c < coverage.Length; c++)
            {
                double ground = elevationMeters[c] - seaLevelMeters;
                if (ground < 0.0) ground = 0.0;
                baseMeters[c] = CloudVolume.CloudBaseAboveSeaLevelMeters(coverage[c], ground, deckBaseMeters);
                thicknessMeters[c] = CloudVolume.CloudThicknessMeters(coverage[c], ground);
            }
        }

        /// <summary>
        /// FELSKÁLÁZÁS a forrás-mező szintjéről az atlasz szintjére,
        /// BILINEÁRISAN.
        ///
        /// MIÉRT KELL. A csapadék-mező a referencia-szinten él (jellemzően
        /// level 5, 364 km-es cella), az atlasz level 6-on. A legközelebbi
        /// cella átvétele a forrás cellaméretén HARD ÉLEKET hagy, amit a GPU
        /// bilineáris szűrése már nem tud elsimítani (az csak az atlasz
        /// texel-léptékén dolgozik) — MÉRVE: az első élő menetben a
        /// felhőárnyék szögletes, tengelyre állított foltokban jelent meg.
        ///
        /// MIÉRT NEM „kitalált adat”. Ez RESAMPLING, nem kiegészítés: a
        /// cella-középpontú mintákra illesztett bilineáris interpoláció a
        /// forrásmező értékeit adja vissza a forrás-cellaközepeken, és
        /// közöttük a szokásos, információt nem hozzáadó átmenetet. A
        /// csúcsértékek nem nőnek (a súlyok összege 1, mind nemnegatív).
        ///
        /// A LAPHATÁROKAT a <see cref="TileNeighbors"/> kezeli: az egy
        /// cellával kilógó indexek a SZOMSZÉD LAP valódi tile-jára mutatnak
        /// (ugyanaz az elv, mint a gutter kitöltésénél), tehát a kockagömb
        /// varratainál sem keveredik idegen érték.
        /// </summary>
        public sealed class UpsampleTable
        {
            /// <summary>Cellánként négy forrás-index (bilineáris sarkok).</summary>
            public int[] Index { get; }

            /// <summary>A négy sarok súlya, összegük cellánként 1.</summary>
            public double[] Weight { get; }

            /// <summary>A forrás-rács szintje.</summary>
            public int SourceLevel { get; }

            /// <summary>A forrás-rács cellaszáma.</summary>
            public int SourceCellCount { get; }

            private UpsampleTable(int[] index, double[] weight, int sourceLevel, int sourceCellCount)
            {
                Index = index;
                Weight = weight;
                SourceLevel = sourceLevel;
                SourceCellCount = sourceCellCount;
            }

            /// <summary>
            /// A tábla felépítése egy forrás-szinthez. A forrás-szint nem lehet
            /// nagyobb az atlasz szintjénél (lefelé-mintavételezést ez az út nem
            /// végez — ott a hívó a forrást eleve az atlasz szintjén adja).
            /// </summary>
            public static UpsampleTable Build(int sourceLevel)
            {
                if (sourceLevel < 0 || sourceLevel > Level)
                    throw new ArgumentOutOfRangeException(nameof(sourceLevel));

                int sourceSide = 1 << sourceLevel;
                int targetSide = Side;
                int scale = targetSide / sourceSide;
                int cells = 6 * targetSide * targetSide;
                var index = new int[cells * 4];
                var weight = new double[cells * 4];

                for (int face = 0; face < 6; face++)
                {
                    for (int u = 0; u < targetSide; u++)
                    {
                        // Cella-KÖZÉPPONTÚ minták: a cél-cella közepe a forrás
                        // rács folytonos koordinátájában.
                        double su = (u + 0.5) / scale - 0.5;
                        int i0 = (int)Math.Floor(su);
                        double fu = su - i0;
                        for (int v = 0; v < targetSide; v++)
                        {
                            double sv = (v + 0.5) / scale - 0.5;
                            int j0 = (int)Math.Floor(sv);
                            double fv = sv - j0;

                            int t = (DenseGridMetrics.Index(face, u, v, targetSide)) * 4;
                            index[t] = SourceCell(face, i0, j0, sourceLevel, sourceSide);
                            index[t + 1] = SourceCell(face, i0 + 1, j0, sourceLevel, sourceSide);
                            index[t + 2] = SourceCell(face, i0, j0 + 1, sourceLevel, sourceSide);
                            index[t + 3] = SourceCell(face, i0 + 1, j0 + 1, sourceLevel, sourceSide);
                            weight[t] = (1.0 - fu) * (1.0 - fv);
                            weight[t + 1] = fu * (1.0 - fv);
                            weight[t + 2] = (1.0 - fu) * fv;
                            weight[t + 3] = fu * fv;
                        }
                    }
                }
                return new UpsampleTable(index, weight, sourceLevel, 6 * sourceSide * sourceSide);
            }

            /// <summary>A forrás-mező felskálázása az atlasz rácsára.</summary>
            public void Resample(double[] source, double[] target)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (target == null) throw new ArgumentNullException(nameof(target));
                if (source.Length != SourceCellCount)
                    throw new ArgumentException("A forrás-tömb hossza nem egyezik a forrás-rács cellaszámával.", nameof(source));
                if (target.Length * 4 != Index.Length)
                    throw new ArgumentException("A cél-tömb hossza nem egyezik az atlasz cellaszámával.", nameof(target));

                for (int c = 0; c < target.Length; c++)
                {
                    int t = c * 4;
                    target[c] = source[Index[t]] * Weight[t]
                        + source[Index[t + 1]] * Weight[t + 1]
                        + source[Index[t + 2]] * Weight[t + 2]
                        + source[Index[t + 3]] * Weight[t + 3];
                }
            }

            /// <summary>
            /// Egy (esetleg a lapon kilógó) forrás-index feloldása. A kilógás
            /// legfeljebb egy cella, ezért a szomszéd-tábla egyetlen lépése
            /// elég — a lapok közti tengely-elforgatást a
            /// <see cref="TileNeighbors"/> már helyesen kezeli.
            /// </summary>
            private static int SourceCell(int face, int u, int v, int level, int side)
            {
                bool uOut = u < 0 || u >= side;
                bool vOut = v < 0 || v >= side;
                int cu = u < 0 ? 0 : (u >= side ? side - 1 : u);
                int cv = v < 0 ? 0 : (v >= side ? side - 1 : v);
                if (!uOut && !vOut)
                    return DenseGridMetrics.Index(face, u, v, side);
                if (uOut && vOut)
                    return DenseGridMetrics.Index(face, cu, cv, side);

                TileDirection direction = u < 0 ? TileDirection.Left
                    : u >= side ? TileDirection.Right
                    : v < 0 ? TileDirection.Down
                    : TileDirection.Up;
                TileId neighbor = TileNeighbors.Neighbor(
                    TileId.FromFaceLevelUV(face, level, (uint)cu, (uint)cv), direction);
                neighbor.GetUV(out uint nu, out uint nv);
                return DenseGridMetrics.Index(neighbor.Face, (int)nu, (int)nv, side);
            }
        }

        /// <summary>
        /// Az atlasz kitöltése a cellánkénti csatorna-értékekből. A
        /// <paramref name="texelSourceMap"/> a
        /// <see cref="ThermalOverlayPacking.BuildTexelSourceMap"/> eredménye.
        /// </summary>
        public static void Pack(
            int[] texelSourceMap,
            double[] coverage, double[] baseMeters, double[] thicknessMeters, double[] openness,
            byte[] target)
        {
            if (texelSourceMap == null) throw new ArgumentNullException(nameof(texelSourceMap));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (texelSourceMap.Length != AtlasWidth * AtlasHeight)
                throw new ArgumentException("Az atlasz mérete 396×66 texel.", nameof(texelSourceMap));
            if (target.Length != texelSourceMap.Length * Channels)
                throw new ArgumentException("A cél-tömb RGBA32, texelenként 4 byte.", nameof(target));

            for (int i = 0; i < texelSourceMap.Length; i++)
            {
                int c = texelSourceMap[i];
                int o = i * Channels;
                target[o] = coverage == null ? (byte)0 : QuantizeUnit(coverage[c]);
                target[o + 1] = baseMeters == null ? (byte)0 : QuantizeMeters(baseMeters[c], CloudVolume.MaxBaseAboveSeaLevelMeters);
                target[o + 2] = thicknessMeters == null ? (byte)0 : QuantizeMeters(thicknessMeters[c], CloudVolume.MaxThicknessMeters);
                target[o + 3] = openness == null ? (byte)255 : QuantizeUnit(openness[c]);
            }
        }
    }
}
