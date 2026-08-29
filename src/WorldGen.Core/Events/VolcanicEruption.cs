using System;
using WorldGen.Core.Numerics;
using WorldGen.Core.Random;
using WorldGen.Core.Tectonics;

namespace WorldGen.Core.Events
{
    /// <summary>
    /// M11 szuper-vulkáni (VEI8) események (spec §21, docs/04-decisions.md ND-29).
    ///
    /// HATÓKÖR (tudatosan szűkítve, ld. ND-29): CSAK szuper-vulkáni (VEI8,
    /// ≥10¹² m³ tefra) események — a "háttér" vulkánosság (VEI3-7, sokkal
    /// gyakoribb) NAGYSÁGRENDEKKEL túl gyakori egy epoch-Bernoulli
    /// modellhez (pl. VEI5 ~1500 várható esemény/10 000 éves epoch),
    /// strukturálisan más (folytonos/perzisztens vulkáni központ) modellt
    /// igényelne — külön, jövőbeli feladat. A spec maga is külön
    /// kategóriaként kezeli a SuperVolcanicEruption/SuperVolcano fogalmat.
    ///
    /// FIZIKA — forrásból ellenőrzött állandók (ND-29 részletezi a forrásokat):
    ///   - VEI8 küszöb: ≥10¹² m³ tefra (hivatalos VEI-skála alsó határa).
    ///   - Méret-eloszlás: minden VEI-lépés (10x térfogat) kb. 6-7x ritkább.
    ///   - Gyakoriság: VEI8 ~1-2/millió év.
    ///   - Felső sapka: 5×10¹² m³ (La Garita Caldera/Fish Canyon Tuff, a
    ///     valaha ismert legnagyobb kitörés).
    ///   - Geometria: pajzsvulkán-kúp, lejtőszög ~6°.
    ///   - Pozíció: <c>PlateBoundaryEffect.TwoBestDots</c> "gap"
    ///     ÚJRAFELHASZNÁLVA — elutasításos mintavétel a lemezhatárok köré.
    ///
    /// Nincs Math.Sin/Cos/Pow/Acos a kritikus úton (ND-27 lezárt
    /// megoldása: <see cref="DeterministicMath"/>).
    /// </summary>
    public static class VolcanicEruption
    {
        public const double EpochYears = 10_000.0;
        public const double Vei8MinVolumeCubicMeters = 1.0e12;
        public const double MaxVolumeCubicMeters = 5.0e12;
        public const double VolumeFrequencyExponent = 0.8129133566428556; // log10(6.5)
        public const double Vei8RatePerYear = 1.5e-6;

        public const double SlopeAngleDegrees = 6.0;
        // Konstrukciós idejű konstans (nem futásidejű trigonometria - ld.
        // ND-24/ND-29 "baked" mintája) - Math.Tan(6° in radians)-ből
        // számolva egyszer, offline (Python-referenciával verifikálva).
        public const double TanSlope = 0.10510423526567647;

        public const double GapScale = PlateBoundaryEffect.DefaultGapScale;

        // Fix bemenetű Pow-eredmény egyszer kiszámolva - ld. ImpactCratering
        // ugyanezen mintája (nem kézzel kiszámolt konstans, hanem a
        // verifikált DeterministicMath.Pow futtatva egyszer, gyorsítótárazva).
        private static readonly double RateCoefficient =
            Vei8RatePerYear * DeterministicMath.Pow(Vei8MinVolumeCubicMeters, VolumeFrequencyExponent);

        /// <summary>Várható eseményszám / epoch a VEI8 küszöbre (Bernoulli-közelítés).</summary>
        public static double EpochProbability()
        {
            double ratePerYear = RateCoefficient *
                DeterministicMath.Pow(Vei8MinVolumeCubicMeters, -VolumeFrequencyExponent);
            return ratePerYear * EpochYears;
        }

        /// <summary>Pajzsvulkán-kúp magassága és sugara a kitörés térfogatából.</summary>
        public static void EdificeGeometry(double volumeCubicMeters, out double heightMeters, out double radiusMeters)
        {
            double h = DeterministicMath.Pow(
                3.0 * volumeCubicMeters * TanSlope * TanSlope / Math.PI, 1.0 / 3.0);
            heightMeters = h;
            radiusMeters = h / TanSlope;
        }

        /// <summary>
        /// Elutasításos mintavétel: egyenletes gömbi pont, elfogadva a
        /// lemezhatár-közelséggel arányos valószínűséggel (1 - gap/GapScale,
        /// 0 ha gap ≥ GapScale) - a <c>PlateBoundaryEffect.TwoBestDots</c>
        /// ÚJRAFELHASZNÁLÁSÁVAL, nincs duplikált határ-közelség logika.
        /// </summary>
        public static void SamplePositionNearBoundary(
            ulong worldSeed, ulong epochBucket, (double X, double Y, double Z)[] seeds,
            out double x, out double y, out double z)
        {
            ulong i = 0;
            while (true)
            {
                DeterministicRandom.Sample4(
                    worldSeed, RandomDomain.Events, 0UL, epochBucket,
                    out double a, out double b, out double c, out double d,
                    RandomProperty.VolcanicPosition, i);

                double px = 2.0 * a - 1.0;
                double py = 2.0 * b - 1.0;
                double pz = 2.0 * c - 1.0;
                double lenSq = px * px + py * py + pz * pz;

                if (lenSq > 1e-12 && lenSq <= 1.0)
                {
                    double inv = 1.0 / Math.Sqrt(lenSq);
                    double cx = px * inv, cy = py * inv, cz = pz * inv;

                    PlateBoundaryEffect.TwoBestDots(cx, cy, cz, seeds, out double best, out double second);
                    double gap = best - second;
                    double acceptProb = gap < GapScale ? (1.0 - gap / GapScale) : 0.0;
                    if (d < acceptProb)
                    {
                        x = cx; y = cy; z = cz;
                        return;
                    }
                }
                i++;
            }
        }

        /// <summary>
        /// Megkísérli legenerálni a <paramref name="epochIndex"/>-hez tartozó
        /// szuper-vulkáni eseményt. Visszatér <c>false</c>-szal, ha ebben az
        /// epoch-ban nem történt esemény (a leggyakoribb eset).
        /// </summary>
        public static bool TryGenerateEruption(
            ulong worldSeed, long epochIndex, (double X, double Y, double Z)[] seeds,
            out double x, out double y, out double z,
            out double volumeCubicMeters, out double edificeHeightMeters, out double edificeRadiusMeters)
        {
            x = y = z = 0.0;
            volumeCubicMeters = edificeHeightMeters = edificeRadiusMeters = 0.0;

            double probability = EpochProbability();
            ulong epochBucket = unchecked((ulong)epochIndex);

            bool occurred = DeterministicRandom.Chance(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                probability, RandomProperty.VolcanicTrigger);
            if (!occurred)
                return false;

            double uMagnitude = DeterministicRandom.Sample(
                worldSeed, RandomDomain.Events, 0UL, epochBucket, RandomProperty.VolcanicMagnitude);
            double volume = Vei8MinVolumeCubicMeters /
                DeterministicMath.Pow(1.0 - uMagnitude, 1.0 / VolumeFrequencyExponent);
            if (volume > MaxVolumeCubicMeters)
                volume = MaxVolumeCubicMeters; // dokumentált biztonsági sapka, nem újra-mintavétel (ND-29)

            SamplePositionNearBoundary(worldSeed, epochBucket, seeds, out x, out y, out z);
            EdificeGeometry(volume, out double h, out double r);

            volumeCubicMeters = volume;
            edificeHeightMeters = h;
            edificeRadiusMeters = r;
            return true;
        }
    }
}
