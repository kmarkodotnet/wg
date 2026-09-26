using System;
using WorldGen.Core.Numerics;
using WorldGen.Core.Terrain;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M10 deep-time: erozio + eljegesedes-ciklusok. ND-44 (uplift-relaxacio),
    /// ND-137 / A20 (relief-erozio). Python referencia:
    /// tools/reference/erosion_glaciation_deep_time_ref.py.
    ///
    /// HAROM IDOFUGGO HATAS, mind ZART ALAKU:
    ///   1. a lemezhatar uplift-BONUSZ exponencialis relaxacioja (ND-44);
    ///   2. ND-137: a ket relief-zajtag AMPLITUDOJANAK hullamhossz-szelektiv,
    ///      hidrologia-vezerelt csillapitasa - ez a tulajdonkeppeni EROZIO;
    ///   3. a periodikus eljegesedesi forcing (jegvonal + glacialis erozio).
    ///
    /// TIMESTEP-INVARIANCIA (ND-04): minden tag ZART ALAKU (exp), NEM iterativ
    /// integrator - tetszoleges idofelbontasban lancolva ugyanazt adja (a
    /// felcsoport-tulajdonsag miatt), tehat lepeskoztol fuggetlen. Ehhez KELL,
    /// hogy a helyi eroziós hatekonysag t-FUGGETLEN legyen, ezert statikus
    /// zonalis profilbol szamolodik, nem a pillanatnyi (mar erodalt)
    /// domborzatbol.
    ///
    /// BIT-DETERMINIZMUS (ND-137 ota): a modul a DeterministicMath
    /// Exp/Sin/Asin-jat hasznalja, NEM a nyers System.Math-ot - tehat
    /// platformok kozott bitre azonos. (Korabban nyers Math.Exp/Math.Sin volt,
    /// ami a CLAUDE.md tablazata szerint nem garantalt.)
    /// </summary>
    public static class DeepTimeErosionGlaciation
    {
        // 1. Erozios relaxacio (a lemezhatar uplift-bonuszara)
        public const double OrogenicRelaxationTauMyr = 50.0;
        public const double EquilibriumFraction = 0.35;

        // 2. Eljegesedes
        public const double GlaciationPeriodMyr = 150.0;
        public const double GlaciationAmplitudeK = 6.0;
        public const double IceThresholdK = 273.15;
        public const double TEquatorK = 300.0;
        public const double LatitudeTempGradientKPerRad = 38.2;

        // 3. ND-137 / A20: relief-erozio
        //
        // A vizhajtotta erozio ZONALIS ("harom-cellas") csapadek-proxyja.
        // UGYANAZ a modellezesi szint, mint az IceLineAbsLatitude idealizalt,
        // szelesseg-alapu homerseklet-profilja: NEM a racs-alapu
        // MoisturePrecipitation-t hasznalja (az iterativ, racsra kotott, es
        // nem bit-egzakt), hanem egy pontonkent kiertekelheto, bit-egzakt
        // zonalis profilt. ILLUSZTRATIV konstansok - ld. ND-137.

        /// <summary>Sivatagi/polaris minimum (nem nulla: szel, fagyaprozodas).</summary>
        public const double ErosionWaterFloor = 0.15;

        /// <summary>ITCZ-suly; a profil cos^8 alaku, tehat keskeny egyenlitoi sav.</summary>
        public const double ErosionEquatorWeight = 1.0;

        /// <summary>A mersekelt ovi viharpalya sulya.</summary>
        public const double ErosionMidLatWeight = 0.55;

        /// <summary>A viharpalya kozepe (50 fok).</summary>
        public const double ErosionMidLatCenterRad = 50.0 * Math.PI / 180.0;

        /// <summary>A viharpalya Gauss-szelessege (12 fok).</summary>
        public const double ErosionMidLatWidthRad = 12.0 * Math.PI / 180.0;

        /// <summary>
        /// A jeg sokkal hatekonyabb eroziv agens, mint a folyo (gleccservolgyek,
        /// cirkuszok, lenyesett pajzsok). ILLUSZTRATIV nagysagrend - ld. ND-137.
        /// </summary>
        public const double GlacialErosivity = 3.0;

        /// <summary>
        /// Az elsodleges (rovid hullamhosszu, ridged) relief idoallandoja.
        /// ILLUSZTRATIV, vizualis kalibralast igenyel - ld. ND-137.
        /// </summary>
        public const double PrimaryReliefTauMyr = 250.0;

        /// <summary>A megmarado relief hanyada telitesben (elsodleges tag).</summary>
        public const double PrimaryReliefEqFraction = 0.30;

        /// <summary>A ridged multifractal alap-frekvenciaja (FractalNoise alapertelmezes).</summary>
        public const double PrimaryNoiseBaseFrequency = FractalNoise.DefaultBaseFrequency;

        /// <summary>
        /// A masodlagos (regionalis) relief idoallandoja: SZARMAZTATOTT, a ket
        /// zaj frekvenciajanak aranyabol.
        ///
        /// MIERT: az erozio HULLAMHOSSZ-SZELEKTIV. A rovid hullamhosszu relief
        /// (eles gerincek, volgyek) tobb nagysagrenddel gyorsabban kopik, mint
        /// a regionalis lepteku domborzati hullamzas - ezert nez ki egy 1
        /// milliard eves pajzs simanak, de nem teljesen laposnak. A linearis
        /// lejto-diffuzio (dh/dt = kappa*lap(h)) egy k hullamszamu komponensre
        /// exp(-kappa*k^2*t)-t ad; a linearis stream-power (n=1) ~exp(-t/tau)-t,
        /// tau ~ L-lel. A ket hatar kozott valasztottunk: tau ARANYOS a
        /// hullamhosszal. Nem szabad parameter - a mar meglevo zaj-frekvenciak
        /// hanyadosa (8,0 / 0,50929... = 5*pi).
        /// </summary>
        public static readonly double SecondaryReliefTauMyr =
            PrimaryReliefTauMyr * (PrimaryNoiseBaseFrequency / CrustElevation.SecondaryNoiseFrequency);

        /// <summary>A megmarado relief hanyada telitesben (masodlagos tag).</summary>
        public const double SecondaryReliefEqFraction = 0.60;

        /// <summary>H(t) = H_eq + (H0 - H_eq) * exp(-t/tau) - a dH/dt=(1/tau)(H_eq-H) ODE zart megoldasa (FIX H_eq).</summary>
        public static double RelaxTowards(double h0, double hEq, double timeMyr, double tau)
            => hEq + (h0 - hEq) * DeterministicMath.Exp(-timeMyr / tau);

        /// <summary>A hegyseg-relief relaxacioja: H_eq = eqFraction*upliftStatic, H0 = upliftStatic. t=0 -&gt; upliftStatic.</summary>
        public static double UpliftRelaxationElevation(double upliftBonusStatic, double timeMyr,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            double hEq = eqFraction * upliftBonusStatic;
            return RelaxTowards(upliftBonusStatic, hEq, timeMyr, tau);
        }

        /// <summary>
        /// A pozicio abszolut foldrajzi szelessege radianban. A z tengely a
        /// polaris tengely - ugyanaz a konvencio, mint
        /// <see cref="Astronomy.OrbitalMechanics.SubsolarPoint"/> (lat = asin(z)).
        /// </summary>
        public static double AbsLatitudeRad(double z)
        {
            double a = z < 0.0 ? -z : z;
            if (a > 1.0) a = 1.0;
            return DeterministicMath.Asin(a);
        }

        /// <summary>
        /// Dimenziotlan, t-FUGGETLEN eroziós hatekonysag a szelesseg alapjan:
        /// nedves egyenlito (ITCZ) + nedves mersekelt ov (viharpalya), koztuk
        /// szubtropusi sivatagov, a polusok fele szarazsag.
        ///
        /// A cos^8 tag csak SZORZASOKBOL all (cos^2 -&gt; ^4 -&gt; ^8), tehat
        /// bitpontos; a Gauss-tag a determinisztikus Exp-et hasznalja.
        /// </summary>
        public static double ZonalWaterFactor(double absLatitudeRad)
        {
            double c = DeterministicMath.Cos(absLatitudeRad);
            double c2 = c * c;
            double c4 = c2 * c2;
            double c8 = c4 * c4;
            double d = (absLatitudeRad - ErosionMidLatCenterRad) / ErosionMidLatWidthRad;
            double mid = ErosionMidLatWeight * DeterministicMath.Exp(-0.5 * d * d);
            return ErosionWaterFloor + ErosionEquatorWeight * c8 + mid;
        }

        /// <summary>
        /// Az ido azon HANYADA (egy teljes eljegesedesi ciklusra atlagolva),
        /// amikor az adott szelesseg jeggel fedett.
        ///
        /// A pont akkor jeges, ha IceLineAbsLatitude(s) &lt;= absLat, azaz
        /// <c>sin(theta) &lt;= u</c>, ahol
        /// <c>u = (G*absLat - T_EQ + T_ICE) / A</c>. Ennek idoaranya egy teljes
        /// perioduson ZART alakban az arkusz-szinuszbol adodik:
        /// <c>0,5 + asin(u)/pi</c>.
        ///
        /// KOZELITES (dokumentalt): reszperiodusra ez a SZEKULARIS atlag, nem
        /// az egzakt mertek - egesz sok periodusra egzakt. Cserebe LINEARIS
        /// t-ben, tehat a lancolhatosag (ND-04) egzaktul teljesul, es az erozio
        /// monoton no (nem "visszakopik" egy interglacialisban, ami fizikai
        /// keptelenseg lenne). A ciklus 150 Myr, a deep-time csuszka Gyr-lepteku.
        ///
        /// MEGJEGYZES: az alapertelmezett konstansokkal az IceLineAbsLatitude
        /// clamp-je SOHA nem aktiv (|u| = 1 hatarai 31,3 es 49,3 fok), tehat a
        /// zart alak PONTOSAN azt a feltetelt irja le, amit az
        /// <see cref="IsIced"/>.
        /// </summary>
        public static double GlaciatedFraction(double absLatitudeRad)
        {
            double u = (LatitudeTempGradientKPerRad * absLatitudeRad - TEquatorK + IceThresholdK)
                / GlaciationAmplitudeK;
            if (u <= -1.0) return 0.0;
            if (u >= 1.0) return 1.0;
            return 0.5 + DeterministicMath.Asin(u) / Math.PI;
        }

        /// <summary>A helyi, t-fuggetlen eroziós hatekonysag: folyovizi + jegaramlasi tag.</summary>
        public static double ErosionEfficiency(double absLatitudeRad)
            => ZonalWaterFactor(absLatitudeRad) + GlacialErosivity * GlaciatedFraction(absLatitudeRad);

        /// <summary>
        /// A pontban ERVENYES eroziós ido: a valos ido a helyi hatekonysaggal
        /// skalazva. Tiszta fuggvenye a (pozicio, t) parnak, es LINEARIS t-ben -
        /// ezert marad sertetlen a csillapitas felcsoport-tulajdonsaga (ND-04).
        /// </summary>
        public static double EffectiveErosionTimeMyr(double z, double erosionTimeMyr)
            => erosionTimeMyr == 0.0 ? 0.0 : ErosionEfficiency(AbsLatitudeRad(z)) * erosionTimeMyr;

        /// <summary>
        /// Az a kitevo-hatar, ahol az exp mar ugyis 1e-304 alatti, tehat a
        /// csillapito bitre az egyensulyi hanyad.
        ///
        /// MIERT KELL EXPLICIT KORLAT: a <see cref="DeterministicMath.Exp"/>
        /// a vegeredmenyt bit-manipulacioval skalazza (ScaleByPowerOfTwo), es
        /// NEM kezeli az exponens-alulcsordulast - kb. -710 alatt nem 0-hoz
        /// tart, hanem SZEMETET ad (a levont exponens atcsordul az
        /// elojelbitbe). A modellben ez sosem fordulna elo (a deep-time
        /// csuszka Gyr-lepteku, a kitevo igy ~80 alatt marad), de egy csendes,
        /// determinisztikus szemet rosszabb, mint egy explicit hatar.
        /// Kulon feljegyezve - ez a DeterministicMath sajat hianyossaga.
        /// </summary>
        private const double MaxDecayExponent = 700.0;

        /// <summary>
        /// D(t) = eq + (1 - eq) * exp(-t_eff / tau). t_eff = 0-nal EGZAKT 1,0
        /// (rovidzar) - igy a hivo oldalan a szorzas bitre valtozatlan marad.
        /// </summary>
        public static double ReliefDecay(double effectiveTimeMyr, double tau, double eqFraction)
        {
            if (effectiveTimeMyr == 0.0)
                return 1.0;
            double exponent = -effectiveTimeMyr / tau;
            if (exponent < -MaxDecayExponent)
                return eqFraction;
            if (exponent > MaxDecayExponent)
                exponent = MaxDecayExponent;
            return eqFraction + (1.0 - eqFraction) * DeterministicMath.Exp(exponent);
        }

        /// <summary>
        /// A ket relief-tag csillapito tenyezoje az adott pontban (a `z`
        /// koordinatabol szamolt szelesseg alapjan), adott eroziós idonel.
        /// </summary>
        public static void ReliefDecayFactors(
            double z, double erosionTimeMyr,
            out double primaryDecay, out double secondaryDecay)
        {
            double tEff = EffectiveErosionTimeMyr(z, erosionTimeMyr);
            primaryDecay = ReliefDecay(tEff, PrimaryReliefTauMyr, PrimaryReliefEqFraction);
            secondaryDecay = ReliefDecay(tEff, SecondaryReliefTauMyr, SecondaryReliefEqFraction);
        }

        /// <summary>
        /// A tile elevacioja timeMyr-nel: alap-elevacio (mar erodalt
        /// relief-tagokkal) + a lemezhatar uplift-bonusz relaxalt erteke.
        /// t=0 = statikus M4 (bitre).
        ///
        /// ND-136 (A19): az alap-elevacio zajtagjai a lemez SAJAT
        /// vonatkoztatasi rendszereben ertekelodnek ki, tehat a domborzat
        /// egyutt vandorol a kereggel. A `seeds` a MAR ELMOZDITOTT lemez-magokat
        /// varja (PlateMotion.MovedSeeds).
        ///
        /// ND-137 (A20): az `erosionTimeMyr` SZANDEKOSAN kulon allithato a
        /// `timeMyr`-tol - a viewer "Erozio (kopas)" kapcsoloja a kopast a
        /// lemezmozgas megtartasa mellett is kikapcsolhatja. A negativ ertek
        /// (= "hasznald a timeMyr-t") konvencioja helyett itt EXPLICIT
        /// parameter van, alapertelmezessel a `timeMyr`-re a regi
        /// tulterhelesen keresztul.
        /// </summary>
        public static double ElevationAtTime(
            ulong worldSeed, int plateId, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double timeMyr, out bool isOceanic,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
            => ElevationAtTime(worldSeed, plateId, x, y, z, seeds, timeMyr, timeMyr, out isOceanic, tau, eqFraction);

        /// <summary>
        /// Ld. a fenti tulterhelest; itt a lemez-ido es az eroziós ido KULON
        /// adhato meg.
        /// </summary>
        public static double ElevationAtTime(
            ulong worldSeed, int plateId, double x, double y, double z, (double X, double Y, double Z)[] seeds,
            double plateTimeMyr, double erosionTimeMyr, out bool isOceanic,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            DomainWarp.WarpPosition(worldSeed, x, y, z, out double wx, out double wy, out double wz);
            PlateBoundaryEffect.BaseAndUpliftFromWarpedAtTime(
                worldSeed, x, y, z, wx, wy, wz, seeds, plateTimeMyr,
                out double baseElev, out double upliftStatic, out isOceanic,
                PlateBoundaryEffect.DefaultGapScale, PlateBoundaryEffect.DefaultUpliftMaxMeters,
                CrustElevation.DefaultBoundaryBlendGap, erosionTimeMyr);
            double upliftT = UpliftRelaxationElevation(upliftStatic, erosionTimeMyr, tau, eqFraction);
            return baseElev + upliftT;
        }

        /// <summary>Ugyanaz a zart formula n_steps darab reszidokozre lancolva (a H_eq FIX, az EREDETI h0-bol) - timestep-invariancia bizonyitas.</summary>
        public static double ChainRelaxation(double h0, double totalTimeMyr, int nSteps,
            double tau = OrogenicRelaxationTauMyr, double eqFraction = EquilibriumFraction)
        {
            double hEq = eqFraction * h0;
            double dt = totalTimeMyr / nSteps;
            double h = h0;
            for (int i = 0; i < nSteps; i++) h = RelaxTowards(h, hEq, dt, tau);
            return h;
        }

        /// <summary>
        /// A relief-csillapitas nSteps resz-idokozre LANCOLVA - a
        /// timestep-invariancia (ND-04) bizonyitasa a relief-eroziora is.
        /// Ugyanaz a minta, mint a <see cref="ChainRelaxation"/>-nel.
        /// </summary>
        public static double ChainReliefDecay(
            double z, double totalTimeMyr, int nSteps, double tau, double eqFraction)
        {
            double tEffTotal = EffectiveErosionTimeMyr(z, totalTimeMyr);
            double dtEff = tEffTotal / nSteps;
            double a = 1.0;
            for (int i = 0; i < nSteps; i++)
                a = eqFraction + (a - eqFraction) * DeterministicMath.Exp(-dtEff / tau);
            return a;
        }

        /// <summary>Periodikus globalis homerseklet-forcing (szinuszos, zart t-ben). t=0 (phase0=0) -&gt; 0.</summary>
        public static double GlobalTempOffset(double timeMyr,
            double period = GlaciationPeriodMyr, double amplitude = GlaciationAmplitudeK, double phase0 = 0.0)
            => amplitude * DeterministicMath.Sin(2.0 * Math.PI * timeMyr / period + phase0);

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>Az abszolut szelesseg (rad), ahol az idealizalt profil pont IceThresholdK - ezen felul (polus fele) jeg. [0, pi/2].</summary>
        public static double IceLineAbsLatitude(double timeMyr)
        {
            double raw = (TEquatorK + GlobalTempOffset(timeMyr) - IceThresholdK) / LatitudeTempGradientKPerRad;
            return Clamp(raw, 0.0, Math.PI / 2.0);
        }

        public static bool IsIced(double absLatitudeRad, double timeMyr)
            => absLatitudeRad >= IceLineAbsLatitude(timeMyr);
    }
}
