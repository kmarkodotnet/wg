using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorldGen.Core.Grid;
using WorldGen.Core.Numerics;
using WorldGen.Core.Random;

namespace WorldGen.Core.Events
{
    /// <summary>
    /// M11 becsapódás-események (spec §22, docs/04-decisions.md ND-28).
    ///
    /// HATÓKÖR (tudatosan szűkítve, ld. ND-28): csak becsapódás — vulkán,
    /// rift, lemez-hasadás/egyesülés halasztva. Csak kráter átmérő+mélység
    /// — rimHeight/ejectaRadius (spec §22.4) halasztva.
    ///
    /// FIZIKA — forrásból ellenőrzött állandók (ND-28 részletezi a forrásokat):
    ///   - Tranziens kráter átmérő: Schmidt &amp; Housen (1987) / Collins,
    ///     Melosh &amp; Marcus (2005) skálázás.
    ///   - Mélység: mélység/átmérő ≈ 1:5 egyszerű kráterekre.
    ///   - Gyakoriság: ρ(≥D) = 20·D^-2.4 [1/év], D méterben.
    ///   - Sebesség: 15-25 km/s.
    ///   - Szög: P(θ) ∝ sin(2θ) — geometriai tény.
    ///
    /// ND-27 LEZÁRVA: nincs Math.Sin/Cos/Pow/Acos a kritikus úton.
    ///   - A Pow-hívások <see cref="DeterministicMath.Pow"/>-ra cserélve.
    ///   - A szög-mintavételezés ÁTTERVEZVE: az eredeti
    ///     <c>angle = 0.5*Math.Acos(1-2u)</c> helyett korong-alapú
    ///     elutasításos mintavétel (Malley-módszer, ugyanaz az elv, mint
    ///     <see cref="DeterministicRandom.SampleUnitVector3"/>-nál) - ez
    ///     KÖZVETLENÜL sin(θ)-t adja (nincs szükség Acos-ra vagy utólagos
    ///     Sin-re): (x,y) egyenletes az egységkorongon → sin(θ)=√(1-x²-y²).
    ///     Levezetés: (x,y) egyenletes eloszlású sűrűsége 1/π a korongon;
    ///     polárkoordinátákra váltva a sugár (r=sinθ) sűrűsége 2r/π·(egység-
    ///     kör kerülete miatt ×2π) = 2r → a θ szerinti sűrűség (r=sinθ,
    ///     dr/dθ=cosθ helyettesítéssel) 2·sinθ·cosθ = sin(2θ) — PONTOSAN
    ///     ugyanaz, mint az eredeti acos-inverzióé. Ez ALGORITMUS-VÁLTÁS
    ///     (más véletlenszám-fogyasztás, más seed→esemény leképezés, mint a
    ///     korábbi verzióban) — dokumentált, szándékos, mert még nincs
    ///     perzisztált világ, amit védeni kellene.
    /// </summary>
    public static class ImpactCratering
    {
        public const double EpochYears = 10_000.0;
        public const double MinDiameterMeters = 1_000.0;
        public const double MaxDiameterMeters = 100_000.0;
        public const double ParetoAlpha = 2.4;
        public const double RateCoefficientPerYear = 20.0;
        public const double VelocityMinMetersPerSecond = 15_000.0;
        public const double VelocityMaxMetersPerSecond = 25_000.0;
        public const double ImpactorDensityKgM3 = 3000.0;
        public const double TargetDensityKgM3 = 2700.0;
        public const double GravityMetersPerSecond2 = 9.81;
        public const double DepthToDiameterRatio = 0.2;

        // Fix bemenetű Pow-eredmények egyszer kiszámolva (statikus
        // inicializáláskor) - NEM kézzel kiszámolt/beírt konstansok (ami a
        // CLAUDE.md "ne bízz emlékezetben" elve szerint kockázatos lenne),
        // hanem maga a verifikált DeterministicMath.Pow futtatva egyszer,
        // gyorsítótárazva - elkerüli, hogy EpochProbability minden egyes
        // epoch-ellenőrzésnél újraszámolja ugyanazt a fix eredményt.
        private static readonly double MinDiameterRateFactor =
            DeterministicMath.Pow(MinDiameterMeters, -ParetoAlpha);
        private static readonly double DensityRatioCubeRoot =
            DeterministicMath.Pow(ImpactorDensityKgM3 / TargetDensityKgM3, 1.0 / 3.0);
        private static readonly double GravityTerm =
            DeterministicMath.Pow(GravityMetersPerSecond2, -0.22);

        /// <summary>
        /// Várható eseményszám / epoch a D≥MinDiameterMeters küszöbre.
        /// Bernoulli-közelítésben használva a Poisson-ráta helyett, mert
        /// ráta &lt;&lt; 1 (dokumentált egyszerűsítés, ritka eseményekre
        /// szokásos gyakorlat).
        /// </summary>
        public static double EpochProbability()
        {
            double ratePerYear = RateCoefficientPerYear * MinDiameterRateFactor;
            return ratePerYear * EpochYears;
        }

        /// <summary>
        /// Schmidt &amp; Housen (1987) / Collins, Melosh &amp; Marcus (2005)
        /// skálázás. <paramref name="sinAngle"/> = sin(becsapódási szög) -
        /// közvetlenül, nem radiánban mért szögből (ld. modul-fejléc).
        /// </summary>
        public static double TransientCraterDiameter(
            double impactorDiameterMeters, double velocityMetersPerSecond, double sinAngle)
        {
            double sizeTerm = DeterministicMath.Pow(impactorDiameterMeters, 0.78);
            double velocityTerm = DeterministicMath.Pow(velocityMetersPerSecond, 0.44);
            double angleTerm = DeterministicMath.Pow(sinAngle, 1.0 / 3.0);
            return 1.161 * DensityRatioCubeRoot * sizeTerm * velocityTerm * GravityTerm * angleTerm;
        }

        /// <summary>
        /// sin(becsapódási szög) mintavételezése P(θ)∝sin(2θ) sűrűséggel,
        /// Acos NÉLKÜL - korong-alapú elutasításos mintavétel (Malley-
        /// módszer, ld. modul-fejléc). Ugyanaz az elutasításos minta, mint
        /// <see cref="DeterministicRandom.SampleUnitVector3"/>: átlagosan
        /// ~1.27 iteráció (elfogadási arány π/4).
        /// </summary>
        public static double SampleSinAngle(ulong worldSeed, ulong epochBucket)
        {
            ulong i = 0;
            while (true)
            {
                DeterministicRandom.Sample4(
                    worldSeed, RandomDomain.Events, 0UL, epochBucket,
                    out double a, out double b, out _, out _,
                    RandomProperty.ImpactAngle, i);

                double px = 2.0 * a - 1.0;
                double py = 2.0 * b - 1.0;
                double r2 = px * px + py * py;
                if (r2 <= 1.0)
                    return Math.Sqrt(1.0 - r2); // sin(theta) = z a korong-vetitesben
                i++;
            }
        }

        /// <summary>
        /// Megkísérli legenerálni a <paramref name="epochIndex"/>-hez tartozó
        /// becsapódás-eseményt. Visszatér <c>false</c>-szal, ha ebben az
        /// epoch-ban nem történt esemény (a leggyakoribb eset).
        /// </summary>
        public static bool TryGenerateImpact(
            ulong worldSeed, long epochIndex,
            out double x, out double y, out double z,
            out double impactorDiameterMeters, out double velocityMetersPerSecond, out double sinAngle,
            out double craterDiameterMeters, out double craterDepthMeters)
        {
            x = y = z = 0.0;
            impactorDiameterMeters = velocityMetersPerSecond = sinAngle = 0.0;
            craterDiameterMeters = craterDepthMeters = 0.0;

            double probability = EpochProbability();
            ulong epochBucket = unchecked((ulong)epochIndex);

            bool occurred = DeterministicRandom.Chance(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                probability, RandomProperty.ImpactTrigger);
            if (!occurred)
                return false;

            double uMagnitude = DeterministicRandom.Sample(
                worldSeed, RandomDomain.Events, 0UL, epochBucket, RandomProperty.ImpactMagnitude);
            double diameter = MinDiameterMeters / DeterministicMath.Pow(1.0 - uMagnitude, 1.0 / ParetoAlpha);
            if (diameter > MaxDiameterMeters)
                diameter = MaxDiameterMeters; // dokumentált biztonsági sapka, nem újra-mintavétel (ND-28)

            DeterministicRandom.SampleUnitVector3(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                out x, out y, out z, RandomProperty.ImpactPosition);

            double velocity = DeterministicRandom.SampleRange(
                worldSeed, RandomDomain.Events, 0UL, epochBucket,
                VelocityMinMetersPerSecond, VelocityMaxMetersPerSecond, RandomProperty.ImpactVelocity);

            double sinTheta = SampleSinAngle(worldSeed, epochBucket);

            double craterDiameter = TransientCraterDiameter(diameter, velocity, sinTheta);

            impactorDiameterMeters = diameter;
            velocityMetersPerSecond = velocity;
            sinAngle = sinTheta;
            craterDiameterMeters = craterDiameter;
            craterDepthMeters = craterDiameter * DepthToDiameterRatio;
            return true;
        }

        /// <summary>Hány 10 000-éves epoch telt el <paramref name="timeMyr"/> millió év alatt.</summary>
        public static long EpochCountForTime(double timeMyr)
        {
            return (long)(timeMyr * 1_000_000.0 / EpochYears);
        }

        /// <summary>
        /// Egy már megtörtént becsapódás krátere - a mezőre gyakorolt
        /// hatás kiszámításához szükséges minimális adat. A szög helyett a
        /// koszinuszát tároljuk, hogy a tile-onkénti teszt egyetlen
        /// pontszorzat-összehasonlítás legyen (nincs szükség Math.Acos-ra
        /// tile-onként, csak egyszer, krátertenként).
        /// </summary>
        public readonly struct CraterRecord
        {
            public readonly double X, Y, Z;
            public readonly double CosAngularRadius;
            public readonly double DepthMeters;

            public CraterRecord(double x, double y, double z, double cosAngularRadius, double depthMeters)
            {
                X = x; Y = y; Z = z;
                CosAngularRadius = cosAngularRadius;
                DepthMeters = depthMeters;
            }
        }

        /// <summary>
        /// Az összes becsapódás, ami <paramref name="timeMyr"/> millió évig
        /// (t=0-tól) megtörtént - tiszta függvény, mindig újraszámolva a
        /// worldSeed+idő alapján (nincs eltárolt "történelem").
        /// </summary>
        public static List<CraterRecord> GenerateCratersUpToTime(ulong worldSeed, double timeMyr)
        {
            long epochCount = EpochCountForTime(timeMyr);
            var craters = new List<CraterRecord>();
            for (long epoch = 0; epoch < epochCount; epoch++)
            {
                bool occurred = TryGenerateImpact(worldSeed, epoch,
                    out double x, out double y, out double z,
                    out _, out _, out _,
                    out double craterDiameterMeters, out double craterDepthMeters);
                if (!occurred)
                    continue;

                // Kis-szög közelítés (angularRadius ~ chord/R): a legnagyobb
                // modellezett kráter (100 km átmérő) is csak ~0.4 fokos
                // szögsugarat ad egy 7420 km sugarú bolygón - egzakt
                // Asin hozzáadása itt nem javítana érdemben a pontosságon.
                double angularRadius = (craterDiameterMeters / 2.0) / PlanetConstants.RadiusMeters;
                double cosAngularRadius = DeterministicMath.Cos(angularRadius);
                craters.Add(new CraterRecord(x, y, z, cosAngularRadius, craterDepthMeters));
            }
            return craters;
        }

        /// <summary>
        /// Egy adott pont (egységvektor) magasság-eltolása az összes átadott
        /// kráter hatásából - sima, körkörös "tál" profil (0 a peremen,
        /// -DepthMeters a középpontban), FELÜLETI KÖZELÍTÉS, nem valódi
        /// kráter-geometria (nincs perem-kiemelkedés - ld. ND-28, rimHeight
        /// halasztva).
        /// </summary>
        public static double ElevationDelta(double x, double y, double z, List<CraterRecord> craters)
            => ElevationDelta(x, y, z, craters, out _);

        /// <summary>
        /// Ugyanaz, mint a fenti, de <paramref name="insideAny"/>-ban EGY
        /// menetben (nem külön <see cref="IsInsideAnyCrater"/>-hívással)
        /// visszaadja azt is, hogy a pont legalább egy kráteren belül esik-e -
        /// ez elkerüli a kráter-lista KÉTSZERI végigjárását olyan hívóknál
        /// (pl. a Unity-viewer tile-klasszifikációja), akiknek mindkét
        /// eredmény kell ugyanarra a pontra.
        /// </summary>
        public static double ElevationDelta(double x, double y, double z, List<CraterRecord> craters, out bool insideAny)
        {
            double delta = 0.0;
            insideAny = false;
            foreach (CraterRecord crater in craters)
            {
                double dot = x * crater.X + y * crater.Y + z * crater.Z;
                if (dot < crater.CosAngularRadius)
                    continue; // a pont a kráteren kívül esik

                insideAny = true;
                double denom = 1.0 - crater.CosAngularRadius;
                double t = denom > 0.0 ? (dot - crater.CosAngularRadius) / denom : 1.0;
                delta -= crater.DepthMeters * t;
            }
            return delta;
        }

        /// <summary>
        /// Igaz, ha a pont legalább egy kráteren belül esik - a
        /// megjelenítő oldal (Unity) ezzel jelölheti meg az érintett
        /// tile-okat kategóriaként, a felbontás-korlát miatt (ld.
        /// <c>ApplyToField</c> dokumentációja), anélkül hogy a
        /// szimulációs logikát duplikálná (a CLAUDE.md szerint a
        /// megjelenítő réteg nem tartalmazhat saját szimulációs matekot).
        /// </summary>
        public static bool IsInsideAnyCrater(double x, double y, double z, List<CraterRecord> craters)
        {
            foreach (CraterRecord crater in craters)
            {
                double dot = x * crater.X + y * crater.Y + z * crater.Z;
                if (dot >= crater.CosAngularRadius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A becsapódások alkalmazása egy elevation-mezőre
        /// <paramref name="timeMyr"/> időpontig.
        ///
        /// FONTOS FELBONTÁS-KORLÁT (dokumentálva, nem hiba): a rács
        /// tile-sarkai jelenlegi LOD-okon (5-7) tipikusan 90-3000 km-re
        /// vannak egymástól, míg a modellezett kráterek átmérője 1-100 km -
        /// a KIS kráterek (a leggyakoribbak) statisztikailag szinte soha nem
        /// találnak el egyetlen tile-sarkot sem, tehát a mezőn nem
        /// jelennek meg. Ez VÁRT viselkedés a jelenlegi rácsfelbontáson -
        /// csak a ritka, nagy (tíz-egynéhány km-es) becsapódások okoznak
        /// mérhető elmozdulást. Finomabb LOD-on (M9, kontinens/régió nézet)
        /// ez javulni fog. A vizuális checkpointnál emellett a Unity-oldal
        /// a becsapódás-érintett tile-okat KATEGÓRIÁKÉNT is megjelölheti
        /// (a folyó-highlight mintáját követve), hogy a ritka találatok se
        /// vesszenek el a felbontás miatt.
        /// </summary>
        public static Dictionary<TileId, double> ApplyToField(
            Dictionary<TileId, double> field, ulong worldSeed, double timeMyr)
            => ApplyToField(field, GenerateCratersUpToTime(worldSeed, timeMyr));

        /// <summary>
        /// Ugyanaz, mint a fenti, de MÁR KISZÁMOLT kráter-listával. A hívó (pl.
        /// a Unity-viewer) a listát máshoz is használja (sarok-geometria,
        /// <see cref="IsInsideAnyCrater"/> kategória-jelölés), ezért nem kell
        /// kétszer generálni - ÉS így a mező-hatás GARANTÁLTAN ugyanaz a
        /// függvény (<see cref="ElevationDelta(double, double, double, List{CraterRecord})"/>), mint a pontszerű
        /// (sarok/tile-közép) kiértékelés, tehát a viewer nem duplikál
        /// szimulációs matekot (CLAUDE.md).
        /// </summary>
        public static Dictionary<TileId, double> ApplyToField(
            Dictionary<TileId, double> field, List<CraterRecord> craters)
        {
            if (craters == null || craters.Count == 0)
                return field;

            // TELJESITMENY: O(tiles * craters.Count), tile-onkent FUGGETLEN
            // (nincs megosztott allapot) - hosszu deep-time-nal (sok epoch ->
            // sok krater) es finom hidrologia-szinten (pl. 393k tile) ez a
            // szorzat nagyra nohet. Parallel.For-ral kulon tombokbe irva, majd
            // egyszalu Dictionary-epitessel BITRE valtozatlan eredmenyt ad.
            var keys = new TileId[field.Count];
            var baseValues = new double[field.Count];
            int idx = 0;
            foreach (KeyValuePair<TileId, double> kv in field)
            {
                keys[idx] = kv.Key;
                baseValues[idx] = kv.Value;
                idx++;
            }

            var newValues = new double[field.Count];
            Parallel.For(0, keys.Length, i =>
            {
                TileGeometry.ToPosition(keys[i], out double x, out double y, out double z);
                newValues[i] = baseValues[i] + ElevationDelta(x, y, z, craters);
            });

            var result = new Dictionary<TileId, double>(field.Count);
            for (int i = 0; i < keys.Length; i++)
                result[keys[i]] = newValues[i];
            return result;
        }
    }
}
