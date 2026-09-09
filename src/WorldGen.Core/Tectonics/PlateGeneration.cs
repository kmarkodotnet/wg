using WorldGen.Core.Random;

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
    }
}
