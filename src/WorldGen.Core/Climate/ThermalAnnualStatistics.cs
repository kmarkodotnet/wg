using System;
using System.Collections.ObjectModel;

namespace WorldGen.Core.Climate
{
    /// <summary>
    /// Egy teljes keringési periódus cellánkénti hőstatisztikája (ND-158).
    /// A mintanapok napi átlagából/szélsőértékéből áll össze; a napi réteget
    /// a <see cref="ThermalDailyStatisticsCalculator"/> adja (ND-144).
    /// </summary>
    public sealed class ThermalAnnualStatistics
    {
        /// <summary>A mintavételezett napok indexei, növekvő sorrendben.</summary>
        public ReadOnlyCollection<long> SampleDays { get; }

        /// <summary>ND-143: annak a mezőnek a modellazonosítója, amelyből készült.</summary>
        public string ModelIdentity { get; }

        public ReadOnlyCollection<double> MeanSurfaceK { get; }
        public ReadOnlyCollection<double> MeanAirK { get; }
        public ReadOnlyCollection<double> MinSurfaceK { get; }
        public ReadOnlyCollection<double> MaxSurfaceK { get; }
        public ReadOnlyCollection<double> MinAirK { get; }
        public ReadOnlyCollection<double> MaxAirK { get; }

        internal ThermalAnnualStatistics(long[] sampleDays, string modelIdentity,
            double[] meanSurfaceK, double[] meanAirK, double[] minSurfaceK, double[] maxSurfaceK,
            double[] minAirK, double[] maxAirK)
        {
            SampleDays = Array.AsReadOnly(sampleDays);
            ModelIdentity = modelIdentity;
            MeanSurfaceK = Array.AsReadOnly(meanSurfaceK);
            MeanAirK = Array.AsReadOnly(meanAirK);
            MinSurfaceK = Array.AsReadOnly(minSurfaceK);
            MaxSurfaceK = Array.AsReadOnly(maxSurfaceK);
            MinAirK = Array.AsReadOnly(minAirK);
            MaxAirK = Array.AsReadOnly(maxAirK);
        }
    }

    /// <summary>
    /// Éves (keringési periódusra vett) hőstatisztika a napi adatútból (ND-158).
    ///
    /// MIÉRT KELL. A biome- és a jégosztályozás nem a pillanatnyi, sőt nem is a
    /// napi hőmérsékletre épül: a tartós jég kritériuma ÉVES átlag és ÉVES
    /// minimum (<see cref="Hydrology.LakesIceErosion.ClassifyIce"/>), a
    /// Whittaker-jellegű biome-tábla pedig éves középhőmérsékletre van
    /// kalibrálva. Az ND-144 napi adatútja önmagában egyiket sem elégíti ki.
    ///
    /// MINTAVÉTEL. Az évet <c>sampleDays</c> darab EGÉSZ modellnap képviseli:
    /// <code>
    ///   nap_j = firstDay + floor(j · keringési_periódus_nap / sampleDays),  j = 0 … n−1
    /// </code>
    /// Csak szorzás, osztás és <see cref="Math.Floor(double)"/> — mind bitpontos
    /// IEEE-754 művelet, tehát a mintanapok platformfüggetlenül azonosak.
    /// A napokat NÖVEKVŐ sorrendben dolgozzuk fel; ez nem az eredmény, hanem a
    /// KÖLTSÉG miatt fontos: a solver így a 30 napos bucketen belül folytatható
    /// (<see cref="SurfaceTemperatureField.StateAt"/>), és csak bucket-váltáskor
    /// kell kanonikusan újraindítani. Az eredmény a feldolgozási sorrendtől
    /// független, mert minden nap kanonikus állapotból indul.
    ///
    /// AGGREGÁCIÓ. <c>mean = (Σ_j napi_átlag_j) / sampleDays</c> — a napok
    /// egyenlő súlyt kapnak, és az összegzés rögzített (növekvő) sorrendben
    /// történik, tehát a lebegőpontos összeadás nem-asszociativitása sem hozhat
    /// eltérést. A <c>min</c>/<c>max</c> a napi szélsőértékek szélsőértéke.
    ///
    /// AMI EZ NEM. Ez nem évszakosan súlyozott klímanormál és nem több éves
    /// átlag: egy pálya-körülfordulás <c>sampleDays</c> mintája. A mintasűrűség
    /// és a költség viszonya mért kérdés — lásd ND-158 és a
    /// <c>worldgen thermal-climate</c> CLI-mérést.
    ///
    /// Nem szálbiztos: egy mező és egy állapot egyszerre egy számításé.
    /// </summary>
    public static class ThermalAnnualStatisticsCalculator
    {
        /// <summary>
        /// Az alapértelmezett mintaszám. Szándékosan azonos a
        /// <see cref="Hydrology.LakesIceErosion.NumAnnualSamples"/> és a
        /// <see cref="ThermalBaseline"/> éves ablakszámával — egyféle éves
        /// mintavételi konvenció legyen a projektben.
        /// </summary>
        public const int DefaultSampleDays = 12;

        /// <summary>
        /// A mintanapok indexei. Szigorúan növekvőnek kell lenniük: ha a
        /// keringési periódus rövidebb, mint a kért mintaszám, két minta
        /// ugyanarra a napra esne, és az átlag CSENDBEN kétszer számolná
        /// ugyanazt a napot — ezért az explicit hiba.
        /// </summary>
        public static long[] SampleDayIndices(double orbitalPeriodDays, int sampleDays, long firstDay = 0)
        {
            if (sampleDays <= 0) throw new ArgumentOutOfRangeException(nameof(sampleDays), "Legalább egy mintanap kell.");
            if (!(orbitalPeriodDays > 0.0) || double.IsInfinity(orbitalPeriodDays))
                throw new ArgumentOutOfRangeException(nameof(orbitalPeriodDays), "Pozitív, véges keringési periódus kell.");

            var days = new long[sampleDays];
            for (int j = 0; j < sampleDays; j++)
            {
                double offset = Math.Floor(j * orbitalPeriodDays / sampleDays);
                if (offset > long.MaxValue / 2.0) throw new ArgumentOutOfRangeException(nameof(orbitalPeriodDays));
                days[j] = checked(firstDay + (long)offset);
                if (j > 0 && days[j] <= days[j - 1])
                    throw new ArgumentException(
                        $"A mintanapok nem szigorúan növekvők ({days[j - 1]} → {days[j]}): a keringési periódus " +
                        $"({orbitalPeriodDays} nap) rövidebb, mint a kért {sampleDays} minta.", nameof(sampleDays));
            }
            return days;
        }

        public static ThermalAnnualStatistics Compute(SurfaceTemperatureField field, ThermalSnapshot state,
            int sampleDays = DefaultSampleDays, long firstDay = 0)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (state == null) throw new ArgumentNullException(nameof(state));

            long[] days = SampleDayIndices(field.Orbit.OrbitalPeriodDays, sampleDays, firstDay);

            int count = field.Grid.CellCount;
            var meanSurface = new double[count];
            var meanAir = new double[count];
            var minSurface = new double[count];
            var maxSurface = new double[count];
            var minAir = new double[count];
            var maxAir = new double[count];
            Array.Fill(minSurface, double.PositiveInfinity);
            Array.Fill(minAir, double.PositiveInfinity);
            Array.Fill(maxSurface, double.NegativeInfinity);
            Array.Fill(maxAir, double.NegativeInfinity);

            for (int j = 0; j < days.Length; j++)
            {
                ThermalDailyStatistics day = ThermalDailyStatisticsCalculator.Compute(field, state, days[j]);
                for (int cell = 0; cell < count; cell++)
                {
                    meanSurface[cell] += day.MeanSurfaceK[cell];
                    meanAir[cell] += day.MeanAirK[cell];
                    if (day.MinSurfaceK[cell] < minSurface[cell]) minSurface[cell] = day.MinSurfaceK[cell];
                    if (day.MaxSurfaceK[cell] > maxSurface[cell]) maxSurface[cell] = day.MaxSurfaceK[cell];
                    if (day.MinAirK[cell] < minAir[cell]) minAir[cell] = day.MinAirK[cell];
                    if (day.MaxAirK[cell] > maxAir[cell]) maxAir[cell] = day.MaxAirK[cell];
                }
            }

            for (int cell = 0; cell < count; cell++)
            {
                meanSurface[cell] /= days.Length;
                meanAir[cell] /= days.Length;
            }
            return new ThermalAnnualStatistics(days, field.ModelIdentity, meanSurface, meanAir,
                minSurface, maxSurface, minAir, maxAir);
        }
    }
}
