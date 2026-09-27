// M13 / ND-154: SAJAT, GOMBI TERFOGATI FELHO - raymarch egy gombheajban.
//
// MIERT SAJAT ES NEM A HDRP-E. Ld. ND-21 (ELUTASITVA, negy MERT blokkoloval):
// a HDRP volumetrikus felhojenek retegvastagsag-clampje a mi lepteukunkben
// 7420 km vastag hejat irt elo, a suruseg-normalizalasa a Fold sugarat
// drotozza be, a felhoterkepes ut a shaderben kizarja a fel bolygot, es ami
// renderel (Simple preset), annak a lefedettsege KONSTANS a shaderben - I3-sertes.
//
// HONNAN JON MINDEN SZAM (I3). A lefedettseg, a felho ALJA es a VASTAGSAGA a
// CPU-n szamolt, MODELLEZETT mezokbol jon egy kockagomb-atlaszban
// (CloudSkyAtlas: R=lefedettseg a csapadek-mezobol, G=alj, B=vastagsag,
// A=egbolt-nyitottsag). A shaderben CSAK (a) a fuggoleges profil ALAKJA es
// (b) a lefedettseg-mezo felbontasa ALATTI reszlet-oktavok vannak; utobbi
// MULTIPLIKATIV, tehat ahol a modell szerint nincs felho (lefedettseg=0), ott
// a shader EGZAKTUL nullat ad. Minden konstans uniformkent a Core
// CloudVolume-bol jon - egyetlen igazsagforras (ND-151 ota bevalt minta).
//
// MIERT NEM SERTI AZ I1-et. Kizarolag render-KIMENET; semmi nem csatolja
// vissza a vilagmodellbe.
//
// GEOMETRIA ES VAGAS. A raymarch egy BURKOLO gombhejon fut (CloudRaymarchPlan.
// ShellPaddingFactor). A lapvalasztas GEOMETRIAI, nem a haromszog-koruljarasbol
// jon: egy p felszinpont a hej KOZELI oldalan van, ha dot(p - kamera, p) < 0.
// Kivulrol a KOZELI oldalt tartjuk meg, belulrol a sugar amugy is csak egyszer
// metszi a burkolot - igy egy sugarra PONTOSAN egy fragment esik (se dupla
// blend, se "belul vagyok" kulon eset), es a koruljaras iranya (amit a
// Core->Unity tengelycsere amugy is tukroz) nem szamit.
//
// MERT HIBA, JAVITVA: az elso valtozat `Cull Front` + `ZTest Always` volt. Az
// igy megmarado HATSO lapok a bolygo MOGOTT vannak, es a HDRP melysegtesztje
// eldobta oket - a pass EGYALTALAN NEM rajzolodott (a diagnosztika 1-es modja,
// a burkolo tomor kitoltese, teljesen ures kepet adott). A mostani
// `Cull Off` + `ZTest LEqual` + geometriai lapvalasztas ezt megoldja, es
// RAADASUL ingyen ad melyseg-takarast: felszin-kozeli nezetben a terep
// eltakarja a mogotte levo tavoli felhot.
//
// ISMERT KORLAT (dokumentalt, ld. ND-154). Urbol nezve a kozeli hejlap van
// elol, ezert ott a melysegteszt nem segit: egy a felhodekkbe emelkedo HEGY
// nem takarja el a mogotte levo felhot. A domino takaro (a bolygo tuloldala)
// analitikusan kezelt.
Shader "WorldGen/CloudVolume"
{
    Properties
    {
        _SunDir ("Sun Direction (world, to-sun)", Vector) = (0.4, 0.6, 0.7, 0)
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        // Elore-multiplikalt alfa: a fragment MAR csillapitott radianciat ad ki,
        // az alfa pedig a felho takarasa (1 - atereszte).
        Blend One OneMinusSrcAlpha
        ZWrite Off
        // ZTest LEqual (NEM Always) es Cull Off - MERT hiba javitasa, ld. a
        // fejlec "GEOMETRIA ES VAGAS" pontjat.
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "CloudVolumeRaymarch"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Az ertekzaj egesz (uint) bitmuveletekkel hash-el, ahhoz kell a 3.5.
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "PlanetCubeAtlas.cginc"

            float4 _SunDir;
            float4 _SunColor;

            sampler2D _CloudSkyTex;
            float4x4 _CloudWorldToPlanet;
            // (belso hej-sugar, kulso hej-sugar, tengerszint-sugar, -) egysegben
            float4 _CloudShell;
            // (egyseg/meter, maxBaseMeters, maxThicknessMeters, extinctionPerMeter)
            float4 _CloudScale;
            // (baseFadeFrac, topFadeFrac, subGridEdgeWidth, verticalStretch)
            float4 _CloudProfile;
            // (detail alap-frekvencia, profil-atlag, HG-g, fazis-csucs vagas)
            float4 _CloudDetail;
            float4 _CloudDetailPhase;
            // (lepesszam, atereszte-kilepes, opacitas-skala, ambiens)
            float4 _CloudMarch;
            // DIAGNOSZTIKA (ugyanaz a szerep, mint a diagForceZeroLighting-nal):
            // 0 = ki, 1 = a burkolo-geometria tomor kitoltese (rendereodik-e
            // egyaltalan a pass), 2 = a menet ABLAKANAK hossza, 3 = a
            // lefedettseg-atlasz nyersen, 4 = a szamolt alfa szurkeben.
            // Nullan a kimenet BITRE a diagnosztika nelkuli.
            float _CloudDiagnostic;
            // CloudVolume.TwilightBandCos
            float _CloudTwilight;
            // (LogisticNormalSlope, DetailNoiseStdDev, -, -)
            float4 _CloudNoise;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS).xyz;
                return o;
            }

            // ---- Ertekzaj, ugyanaz a hash/fade, mint a mikro-reszletnel -----
            uint CloudHash(uint3 p)
            {
                p *= uint3(0x9E3779B1u, 0x85EBCA77u, 0xC2B2AE3Du);
                uint h = p.x ^ p.y ^ p.z;
                h ^= h >> 15; h *= 0x2545F491u; h ^= h >> 13;
                return h;
            }

            float CloudLattice(int3 cell)
            {
                return CloudHash(uint3(cell)) * (1.0 / 4294967296.0);
            }

            float CloudValueNoise(float3 x)
            {
                float3 fi = floor(x);
                float3 f = x - fi;
                float3 w = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                int3 c = int3(fi);
                float n000 = CloudLattice(c + int3(0, 0, 0));
                float n100 = CloudLattice(c + int3(1, 0, 0));
                float n010 = CloudLattice(c + int3(0, 1, 0));
                float n110 = CloudLattice(c + int3(1, 1, 0));
                float n001 = CloudLattice(c + int3(0, 0, 1));
                float n101 = CloudLattice(c + int3(1, 0, 1));
                float n011 = CloudLattice(c + int3(0, 1, 1));
                float n111 = CloudLattice(c + int3(1, 1, 1));
                float x00 = lerp(n000, n100, w.x);
                float x10 = lerp(n010, n110, w.x);
                float x01 = lerp(n001, n101, w.x);
                float x11 = lerp(n011, n111, w.x);
                return lerp(lerp(x00, x10, w.y), lerp(x01, x11, w.y), w.z) * 2.0 - 1.0;
            }

            // Negy oktav, persistence 0.5 / lacunarity 2 - a modell sajat
            // zajanak parameterei (CloudVolume.DetailPersistence/Lacunarity).
            float CloudFbm(float3 q)
            {
                float total = 0.0;
                float norm = 0.0;
                float amplitude = 1.0;
                float3 p = q;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    total += CloudValueNoise(p) * amplitude;
                    norm += amplitude;
                    amplitude *= 0.5;
                    p *= 2.0;
                }
                return norm > 1e-6 ? total / norm : 0.0;
            }

            // HG tetszoleges aszimmetriaval, IZOTROP = 1-re normalva, a csucs vagva.
            float CloudPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-6);
                return min((1.0 - g2) / (denom * sqrt(denom)), _CloudDetail.w);
            }

            // TOBBSZOROS SZORAS oktavos kozelitessel (Core: SunScatterGain).
            // MERT HIBA JAVITASA: tisztan egyszeres szorassal a felho SOTET
            // SZURKE folt volt, mert a dekk belsejeben a Nap iranyu optikai
            // melyseg 5-20, tehat exp(-tau) gyakorlatilag nulla. A valodi felho
            // FEHER, mert a vizcsepp egyszeres-szorasi albedoja ~1 es a fotonok
            // sokszor szorodva kijutnak - ezt egy egyszeres-szorasu modell
            // definicio szerint nem tartalmazza.
            //
            // _CloudScatter = (oktavszam, energia-arany, kioltas-arany, fazis-arany)
            float4 _CloudScatter;

            float CloudSunGain(float tau, float cosTheta)
            {
                float energy = 1.0;
                float extinction = 1.0;
                float eccentricity = 1.0;
                float total = 0.0;
                int octaves = max((int)_CloudScatter.x, 1);
                [loop]
                for (int k = 0; k < octaves; k++)
                {
                    total += energy * CloudPhase(cosTheta, _CloudDetail.z * eccentricity)
                        * exp(-tau * extinction);
                    energy *= _CloudScatter.y;
                    extinction *= _CloudScatter.z;
                    eccentricity *= _CloudScatter.w;
                }
                return total;
            }

            // A hejon beluli suruseg egy pontban. Visszaadja a mar
            // reszlettel modositott lefedettseget es a magassag-tortet is,
            // mert a Nap-iranyu optikai melyseghez kellenek.
            float CloudDensity(float3 planetPos, out float coverage, out float heightFraction, out float thicknessMeters)
            {
                coverage = 0.0;
                heightFraction = 0.0;
                thicknessMeters = 0.0;
                float r = length(planetPos);
                if (r <= 1e-6) return 0.0;
                float3 u = planetPos / r;

                // tex2Dlod es NEM tex2D: a mintavetel egy VARIALO iteracioszamu
                // ciklusban van, ahol a kepernyoteri derivaltak definialatlanok
                // ("gradient instruction used in a loop with varying iteration" -
                // MERT fordito-figyelmeztetes). Az atlasznak nincs mip-lanca
                // (EnsureCloudSkyTexture: mipChain=false), tehat a LOD 0 nem
                // kozelites, hanem az EGYETLEN helyes szint.
                float4 atlas = tex2Dlod(_CloudSkyTex, float4(PlanetAtlasUv(u), 0, 0));
                float cov = atlas.r;
                if (cov <= 0.0) return 0.0;

                float altitudeMeters = (r - _CloudShell.z) / max(_CloudScale.x, 1e-20);
                float baseMeters = atlas.g * _CloudScale.y;
                float thickness = max(atlas.b * _CloudScale.z, 1.0);
                thicknessMeters = thickness;
                float hf = (altitudeMeters - baseMeters) / thickness;
                if (hf <= 0.0 || hf >= 1.0) return 0.0;

                float profile = PlanetSmoothstep01(hf / max(_CloudProfile.x, 1e-4))
                    * PlanetSmoothstep01((1.0 - hf) / max(_CloudProfile.y, 1e-4));
                if (profile <= 0.0) return 0.0;

                // A reszlet-koordinata: a gombi irany a sav-frekvencian, plusz
                // a RADIALIS elhangolas (a felho vizszintesen nagyobb lepteku,
                // mint fuggolegesen - CloudVolume.DetailVerticalStretch).
                float3 q = u * (_CloudDetail.x + hf * _CloudProfile.w) + _CloudDetailPhase.xyz;
                // SUB-GRID SZETBONTAS (Core: CloudVolume.SubGridCoverage). NEM
                // szorzo: egy 364 km-es cella 0,1-es lefedettsege azt jelenti,
                // hogy a terulet 10%-an VAN felho, nem azt, hogy az egeszet egy
                // tizednyi suru fatyol fedi - a szorzos elso valtozat MERVE
                // egyenletes szurke fatyolt adott, derult teruletek nelkul.
                // A fBm-et ~EGYENLETES [0,1] valtozova kell hozni, mert a
                // kuszob zart alakja EGYENLETES zajra van levezetve. Negy
                // oktav osszege kozel NORMALIS es 0,5 kore tomorul (MERT
                // szoras 0,2537): a naiv 0.5+0.5*fbm a mintakat a
                // [0,37; 0,63] savba szoritja, es a kuszobozes szisztematikus
                // kontraszt-nyujtast okoz - vagyis a modell mennyisege NEM
                // maradna meg. A lekepezes a normalis eloszlasfuggveny
                // logisztikus kozelitese (Core: DetailNoiseToUniform).
                float n01 = saturate(1.0 / (1.0 + exp(-_CloudNoise.x * CloudFbm(q) / _CloudNoise.y)));
                float w = max(_CloudProfile.z, 1e-4);
                // A kuszob ZART ALAKBAN levezetve (Core: SubGridThreshold) -
                // haromagu, hogy a zajra vett varhato ertek PONTOSAN a
                // modellezett cella-atlag legyen, es a ket vegpont egzakt:
                // cov=0 -> ures, cov=1 -> mindenutt felho.
                float threshold;
                if (cov < 0.5 * w) threshold = sqrt(2.0 * w * cov);
                else if (cov > 1.0 - 0.5 * w) threshold = 1.0 + w - sqrt(2.0 * w * (1.0 - cov));
                else threshold = cov + 0.5 * w;
                cov = saturate((threshold - n01) / w);

                coverage = cov;
                heightFraction = hf;
                return cov * profile;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                if (_CloudDiagnostic > 0.5 && _CloudDiagnostic < 1.5)
                    return fixed4(1, 0, 0, 1);

                float3 camPlanet = mul(_CloudWorldToPlanet, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 fragPlanet = mul(_CloudWorldToPlanet, float4(i.positionWS, 1.0)).xyz;
                float3 rd = PlanetSafeNormalize(fragPlanet - camPlanet, float3(0, 0, 1));

                // GEOMETRIAI lapvalasztas (ld. a fejlec "GEOMETRIA ES VAGAS"
                // pontjat): kivulrol csak a burkolo KOZELI oldala marad, hogy
                // egy sugarra pontosan egy fragment essen.
                if (dot(camPlanet, camPlanet) > _CloudShell.w * _CloudShell.w
                    && dot(fragPlanet - camPlanet, fragPlanet) > 0.0)
                    discard;

                float ri = _CloudShell.x;
                float ro = _CloudShell.y;
                float b = dot(camPlanet, rd);
                float cc = dot(camPlanet, camPlanet);

                float discOuter = b * b - cc + ro * ro;
                if (discOuter <= 0.0) discard;
                float sOuter = sqrt(discOuter);
                float t0 = max(-b - sOuter, 0.0);
                float t1 = -b + sOuter;

                float discInner = b * b - cc + ri * ri;
                if (discInner > 0.0)
                {
                    float sInner = sqrt(discInner);
                    float tIn0 = -b - sInner;
                    float tIn1 = -b + sInner;
                    if (tIn0 > 0.0) t1 = min(t1, tIn0);   // a belso gombbe erkezunk: ott vege
                    else if (tIn1 > 0.0) t0 = max(t0, tIn1); // a belso gombben vagyunk: ott kezdjuk
                }
                if (t1 <= t0) discard;

                // ---------------------------------------------------------
                // A MENET ABLAKÁNAK SZŰKÍTÉSE a HELYI felhődekkre.
                //
                // MÉRT HIBA VOLT (első élő menet): a héj a LEGNAGYOBB
                // lehetséges felhőt fogja be (24 km), a tipikus dekk viszont
                // 500-2000 m vastag - 12 lépéssel a menet ÁTLÉPETT a felho
                // fölött, és a réteg láthatatlan maradt. A dekk alja és teteje
                // az atlaszban benne van, tehát a helyes megoldás nem a
                // lépésszám emelése, hanem az ABLAK szűkítése: a szakasz
                // középpontjának irányában kiolvassuk a helyi aljat/vastagságot,
                // és a menetet a KÉT HELYI gömbhéj közé szorítjuk. Egyetlen
                // extra textúraolvasás, és minden lépés a felhőbe esik.
                //
                // A mező lassan változik (egy atlasz-cella 182 km), ezért a
                // középponti becslés a sugár mentén is jó; a PADDING a súroló
                // sugarak menti elhangolódást fogja fel.
                // HAROM minta a hurhoz, nem egy. A kozeppont-only valtozat a
                // limbet surolo sugaraknal hibas: ott a hur ~1100 km (hat
                // atlasz-cella), es a felhoalap a TEREP magassagat koveti,
                // tehat a hur menten ezer metereket valtozhat - a dekk
                // kicsuszott volna az ablakbol, es a felho eltunt volna a
                // limb kozeleben es a terep-lepcsoknel.
                float minBase = 1e9;
                float maxTop = -1e9;
                float maxThick = 1.0;
                [unroll]
                for (int w = 0; w < 3; w++)
                {
                    float3 dir = normalize(camPlanet + rd * lerp(t0, t1, 0.25 + 0.25 * w));
                    float4 atl = tex2Dlod(_CloudSkyTex, float4(PlanetAtlasUv(dir), 0, 0));
                    float bm = atl.g * _CloudScale.y;
                    float tm = max(atl.b * _CloudScale.z, 1.0);
                    minBase = min(minBase, bm);
                    maxTop = max(maxTop, bm + tm);
                    maxThick = max(maxThick, tm);
                }
                float pad = maxThick * 0.25;
                float rBase = _CloudShell.z + max(minBase - pad, 0.0) * _CloudScale.x;
                float rTop = _CloudShell.z + (maxTop + pad) * _CloudScale.x;
                rBase = max(rBase, _CloudShell.x);
                rTop = min(rTop, _CloudShell.y);

                float discTop = b * b - cc + rTop * rTop;
                if (discTop <= 0.0) discard;
                float sTop = sqrt(discTop);
                float w0 = max(-b - sTop, t0);
                float w1 = min(-b + sTop, t1);
                float discBase = b * b - cc + rBase * rBase;
                if (discBase > 0.0)
                {
                    float sBase = sqrt(discBase);
                    float tb0 = -b - sBase;
                    float tb1 = -b + sBase;
                    if (tb0 > 0.0) w1 = min(w1, tb0);
                    else if (tb1 > 0.0) w0 = max(w0, tb1);
                }
                if (w1 <= w0) discard;
                t0 = w0;
                t1 = w1;

                int steps = max((int)_CloudMarch.x, 1);
                float tStep = (t1 - t0) / steps;
                float stepMeters = tStep / max(_CloudScale.x, 1e-20);
                float extinction = _CloudScale.w;

                float3 L = PlanetSafeNormalize(
                    mul((float3x3)_CloudWorldToPlanet, _SunDir.xyz), float3(0, 1, 0));
                float cosSunView = dot(rd, L);

                float transmittance = 1.0;
                float3 radiance = float3(0, 0, 0);
                float cutoff = _CloudMarch.y;

                // [loop]: a lepesszam UNIFORM (nezetfuggo, ld. CloudRaymarchPlan),
                // ezert a fordito nem tudja kigongyolni - enelkul
                // "unable to unroll loop" hibaval elszall (MERT hiba, elso
                // fordital 43 iteraciora).
                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float3 p = camPlanet + rd * (t0 + (s + 0.5) * tStep);
                    float coverage, hf, thicknessMeters;
                    float density = CloudDensity(p, coverage, hf, thicknessMeters);
                    if (density <= 0.0)
                        continue;

                    float tau = extinction * density * stepMeters;
                    if (tau <= 0.0)
                        continue;

                    // A NAP-IRANYU optikai melyseg ANALITIKUS (sik-parhuzamos
                    // oszlop): a dekk vekony a bolygo sugarahoz kepest, ezert a
                    // minta FOLOTTI maradek reteg atlagolt surusege elso
                    // rendben egzakt - es NULLA extra texturaolvasasba kerul
                    // (egy 4 lepeses feny-march 128 fetch/pixelt jelentene).
                    // Ugyanaz a keplet, mint a Core ShadowOpticalDepth-e.
                    float3 up = normalize(p);
                    float cosSunUp = dot(up, L);
                    // NAPPALI TENYEZO (Core: CloudVolume.DaylightFactor). A Nap
                    // iranyu optikai melyseg csak azt mondja meg, mennyi FELHO
                    // van a minta folott - azt nem, hogy a Nap a horizont
                    // FOLOTT van-e. Enelkul (MERT hiba) az EJSZAKAI oldal
                    // felhoi is teljes napfenyt kaptak es feheren vilagitottak.
                    // A sav szelessege szarmaztatott: egy h magassagu felhot a
                    // Nap meg sqrt(2h/R) szoggel a horizont alatt is megvilagit.
                    float daylight = PlanetSmoothstep01(
                        (cosSunUp + _CloudTwilight) / (2.0 * _CloudTwilight));
                    float cosSun = max(cosSunUp, 0.15);
                    float tauSun = extinction * coverage * _CloudDetail.y
                        * ((1.0 - hf) * thicknessMeters) / cosSun;

                    // A NAPPALI TENYEZO a TELJES szorasi tagot kapuzza, az
                    // ambiens (egbolt-) reszt is: az egboltfeny maga is SZORT
                    // NAPFENY, tehat ejszaka nincs. Enelkul a felho az
                    // ejszakai oldalon vilagosszurke foltkent latszott a
                    // majdnem fekete felszin folott (MERT hiba). Az alfa
                    // megmarad, tehat az ejszakai felho SZILUETTKENT takar.
                    float3 scatter = (_SunColor.rgb * CloudSunGain(tauSun, cosSunView) + _CloudMarch.w) * daylight;

                    float stepTransmittance = exp(-tau);
                    radiance += transmittance * scatter * (1.0 - stepTransmittance);
                    transmittance *= stepTransmittance;
                    if (transmittance < cutoff)
                        break;
                }

                if (_CloudDiagnostic > 1.5)
                {
                    if (_CloudDiagnostic < 2.5)
                        return fixed4(saturate((t1 - t0) / max(_CloudShell.y - _CloudShell.x, 1e-6)).xxx, 1);
                    if (_CloudDiagnostic < 3.5)
                    {
                        float3 midU = normalize(camPlanet + rd * (0.5 * (t0 + t1)));
                        return fixed4(tex2Dlod(_CloudSkyTex, float4(PlanetAtlasUv(midU), 0, 0)).rrr, 1);
                    }
                    return fixed4((1.0 - transmittance).xxx, 1);
                }

                float alpha = saturate((1.0 - transmittance) * _CloudMarch.z);
                if (alpha <= 0.0)
                    discard;
                float3 rgb = radiance * _CloudMarch.z;
                if (any(isnan(rgb)) || any(isinf(rgb)))
                    return fixed4(0, 1, 1, 1);
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
