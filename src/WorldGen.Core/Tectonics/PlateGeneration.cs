using WorldGen.Core.Random;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M4 lemez-generálás (§4.1, docs/05-milestones.md): N lemez-mag pont +
    /// gömbi Voronoi tile-hozzárendelés.
    ///
    /// HATÓKÖR: statikus pillanatkép — nincs lemezmozgás (a
    /// <c>P(t) = R(ωt)P₀</c> Euler-rotáció M10-re, "Deep time"-ra marad,
    /// ld. a milestones-dokumentum "M4 hatókör" szakasza).
    ///
    /// A magpontok a már verifikált <see cref="DeterministicRandom.SampleUnitVector3"/>-ból
    /// jönnek — bitpontos, mert csak <c>Math.Sqrt</c>-et használ (ND-23a).
    /// </summary>
    public static class PlateGeneration
    {
        /// <summary>N lemez-mag egységvektor, egyenletesen a gömb felületén.</summary>
        public static (double X, double Y, double Z)[] GenerateSeeds(ulong worldSeed, int plateCount)
        {
            var seeds = new (double X, double Y, double Z)[plateCount];
            for (int plateId = 0; plateId < plateCount; plateId++)
            {
                DeterministicRandom.SampleUnitVector3(
                    worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                    out double x, out double y, out double z,
                    RandomProperty.PlateSeedPoint);
                seeds[plateId] = (x, y, z);
            }
            return seeds;
        }

        /// <summary>A legközelebbi mag lemez-azonosítója (legnagyobb dot product = legkisebb gömbi távolság).</summary>
        public static int AssignPlate(double x, double y, double z, (double X, double Y, double Z)[] seeds)
        {
            int bestId = -1;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < seeds.Length; i++)
            {
                double dot = x * seeds[i].X + y * seeds[i].Y + z * seeds[i].Z;
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestId = i;
                }
            }
            return bestId;
        }

        /// <summary>
        /// A KANONIKUS „melyik lemez van ezen a ponton" kérdés: előbb
        /// <see cref="DomainWarp.WarpPosition"/>, utána <see cref="AssignPlate"/>.
        ///
        /// MIÉRT KELL KÜLÖN FÜGGVÉNY (2026-09-21, ND-125). A nyers
        /// <see cref="AssignPlate"/> a gömbi Voronoi-felosztást adja, ami
        /// MATEMATIKAILAG KONVEX: minden cella nagykör-ívekkel határolt
        /// sokszög. A világmodell ezért SOHA nem a nyers pozícióval kérdez
        /// (ld. <see cref="SeaLevelCalibration.ComputeElevationField"/>,
        /// <see cref="Hydrology.RiverPathTracing"/>), hanem a warpolttal — a
        /// warp töri meg a határok geometrikus jellegét (ND-36).
        ///
        /// A tektonikus overlay viszont a NYERS pozícióval kérdezett, tehát
        /// egy MÁSIK felosztást rajzolt, mint amit a domborzat használ. MÉRVE
        /// (level 7, 98 304 tile, három seed): a két hozzárendelés a tile-ok
        /// <b>19,5–26,4%-án</b> tér el, és a határ-hullámzás
        /// (kerület/√terület) 4,9–5,2 helyett 7,2–7,9 — a nyers felosztás
        /// láthatóan sokszögekből áll, a warpolt nem. Ez I3-sértés volt,
        /// ugyanaz az osztály, mint az ND-119 (a szél-overlay nem a
        /// szimuláció szelét mutatta).
        ///
        /// Aki lemez-hovatartozást akar MEGJELENÍTENI vagy lekérdezni, ezt
        /// hívja. A nyers <see cref="AssignPlate"/> csak ott marad helyes,
        /// ahol a hívó a warpot MAGA már elvégezte, és a warpolt pozíciót
        /// másra is újrahasznosítja (ND-39 „C" warp-hoisting).
        /// </summary>
        public static int AssignPlateWarped(
            ulong worldSeed, double x, double y, double z, (double X, double Y, double Z)[] seeds)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            return AssignPlate(wx, wy, wz, seeds);
        }
    }
}
