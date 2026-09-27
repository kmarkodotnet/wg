// M13 folytonos arnyalas: minimalis, HDRP alatt is (best-effort) forditando
// vertex-szin shader. TORTENET: eredetileg UNLIT volt (csak a vertex-szint adta
// ki), ezert a folytonos felszin - a lapos HDRP/Lit kategoriakkal ellentetben -
// NEM reagalt a fenyre (nincs spekularis, nem kovette a mozgo Napot). 2026-09-05:
// a felhasznaloi eszrevetel ("megszunt a feny-visszaverodes") alapjan a shader
// mostantol EGYSZERU, valos ideju vilagitast szamol: Lambert-diffuz +
// Blinn-Phong spekularis.
//
// A HDRP legacy fenyforras-valtozoi (pl. _WorldSpaceLightPos0) nem garantaltan
// toltodnek ki SRP alatt, EZERT a Nap iranyat/szinet a C# (PlanetGridMesh.
// UpdateSurfaceLightingUniforms) explicit MATERIAL-UNIFORMKENT adja at
// (_SunDir/_SunColor) - ez SRP alatt is megbizhato. A _WorldSpaceCameraPos
// beepitett globalis, minden pipeline-ban ki van toltve (spekularishoz kell).
// A Properties-alapertelmezesek gondoskodnak rola, hogy a shader C#-beallitas
// NELKUL is ertelmes (fix iranyu) fenyt adjon, ne legyen fekete.
//
// A deklaralt shader-nev SZANDEKOSAN valtozatlan ("WorldGen/VertexColorUnlit"),
// hogy a C# Shader.Find hivas es a szerializalt referenciak ne toorjenek el.
Shader "WorldGen/VertexColorUnlit"
{
    Properties
    {
        // Vilag-teri irany a felszintol a Nap fele (a C# feluliirja a valos Nappal).
        _SunDir ("Sun Direction (world, to-sun)", Vector) = (0.4, 0.6, 0.7, 0)
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
        // FELHASZNALOI VISSZAJELZES (2026-09-06): 0.35-tel az ejszakai oldal
        // "homalyosnak/kodosnek" tunt (a felszin sose sotetedett rendesen) -
        // 0.04-re csokkentve. Ez csak FALLBACK ertek, ha a C# nem allitja be
        // (ld. PlanetGridMesh.surfaceAmbient) - normal esetben AZ a
        // tenyleges, elo-hangolhato ertek.
        _Ambient ("Ambient", Range(0, 1)) = 0.04
        // 2026-09-07: felhasznaloi visszajelzes szerint 0.30/24-nel a
        // csillanas "mintha villamlana" - tul eros ES foltos (a durva LOD-mesh
        // diszkret normaljain egy keskeny fenyfolt tile-rol tile-ra "pattog").
        // 0.12/8-ra csokkentve. A csillanas VALODI oka vegul a HDRP Bloom
        // tul alacsony kuszobe volt (DefaultSettingsVolumeProfile.asset,
        // ld. docs/04-decisions.md), nem ez a shader - visszaallitva
        // 0.12/8-ra a Bloom-javitas utan. Csak FALLBACK, ha a C# nem
        // allitja be (ld. PlanetGridMesh.surfaceSpecularStrength/
        // surfaceShininess) - normal esetben azok a tenyleges,
        // elo-hangolhato ertekek.
        _SpecStrength ("Specular Strength", Range(0, 2)) = 0.12
        _Shininess ("Shininess", Range(1, 128)) = 8
        // ND-151: ANYAG-szintu kapu a mikro-reszlethez. A terep-anyag 1-et kap,
        // a vizfelszin 0-t (egy oceanfelszin nem kozetes szemcses). A GLOBALIS
        // erosseg (_MicroDetailStrength) es a sav-uniformok a C#-bol jonnek;
        // ha nincsenek beallitva, a reszlet automatikusan ki van kapcsolva.
        _MicroDetailEnable ("Micro Detail Enable", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // ND-151: a mikro-reszlet ertek-zaja EGESZ (uint) bitmuveletekkel
            // hash-el; a CG alapertelmezett 2.5-es shader modell ezeket nem
            // tamogatja, ezert kell a 3.5.
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _SunDir;
            float4 _SunColor;
            float _Ambient;
            float _SpecStrength;
            float _Shininess;

            // Pillanatnyi hőmérséklet-overlay (ND-104). Globális property-k,
            // a PlanetGridMesh.ThermalOverlay állítja be. A shader NEM számol
            // hőmérsékletet: a CPU-n fixpontosra kvantált, hat lapos 66×66-os
            // atlaszból (egycellás gutterrel) mintavételez és palettáz. Ha a
            // C# nem állítja be, _ThermalMode = 0, az overlay ki van kapcsolva.
            sampler2D _ThermalSurfaceTex;
            sampler2D _ThermalAirTex;
            float _ThermalMode;
            float _ThermalMinK;
            float _ThermalMaxK;
            float4x4 _ThermalWorldToPlanet;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionPlanet : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.positionPlanet = mul(_ThermalWorldToPlanet, float4(o.positionWS, 1.0)).xyz;
                return o;
            }

            // Core-irányból (Unity-lokál (x, z, y) tengelycserével) atlasz-UV.
            // A TileGeometry lapkonvencióját és a tan-warp inverzét tükrözi;
            // a CPU-oldali pár: ThermalOverlayPacking.AtlasCoordinate.
            float2 ThermalAtlasUv(float3 planetLocal)
            {
                float3 u = normalize(planetLocal);
                float3 p = float3(u.x, u.z, u.y);
                float3 a = abs(p);
                int axis = 0;
                if (a.y > a.x) axis = 1;
                if (a.z > (axis == 0 ? a.x : a.y)) axis = 2;
                float dominant = axis == 0 ? p.x : (axis == 1 ? p.y : p.z);
                int face = axis * 2 + (dominant >= 0.0 ? 0 : 1);
                float inv = 1.0 / max(abs(dominant), 1e-6);
                float wx, wy;
                if (face == 0)      { wx = -p.z; wy = p.y; }
                else if (face == 1) { wx = p.z;  wy = p.y; }
                else if (face == 2) { wx = p.x;  wy = p.z; }
                else if (face == 3) { wx = p.x;  wy = -p.z; }
                else if (face == 4) { wx = p.x;  wy = p.y; }
                else                { wx = -p.x; wy = p.y; }
                float uc = atan(wx * inv) * 4.0 / UNITY_PI;
                float vc = atan(wy * inv) * 4.0 / UNITY_PI;
                float x = (66.0 * face + 1.0 + (uc + 1.0) * 32.0) / 396.0;
                float y = (1.0 + (vc + 1.0) * 32.0) / 66.0;
                return float2(x, y);
            }

            // Fix, abszolút skála: min → kék → cián → 0 °C világos semleges →
            // sárga → piros ← max. A C# jelmagyarázat ugyanezt a függvényt
            // tükrözi (ThermalOverlayPacking.Palette).
            float3 ThermalPalette(float k)
            {
                const float zeroK = 273.15;
                float3 cold0 = float3(0.08, 0.16, 0.62);
                float3 cold1 = float3(0.20, 0.72, 0.92);
                float3 neutral = float3(0.93, 0.93, 0.89);
                float3 warm1 = float3(0.98, 0.80, 0.24);
                float3 warm0 = float3(0.78, 0.12, 0.08);
                if (k <= zeroK)
                {
                    float t = saturate((k - _ThermalMinK) / max(zeroK - _ThermalMinK, 1e-3));
                    return t < 0.5 ? lerp(cold0, cold1, t * 2.0) : lerp(cold1, neutral, t * 2.0 - 1.0);
                }
                float w = saturate((k - zeroK) / max(_ThermalMaxK - zeroK, 1e-3));
                return w < 0.5 ? lerp(neutral, warm1, w * 2.0) : lerp(warm1, warm0, w * 2.0 - 1.0);
            }

            // Biztonsagos normalize: nulla-kozeli (degeneralt) bemenetre a
            // megadott tartalek-iranyt adja NaN helyett (HLSL normalize(0,0,0)
            // = NaN, 0/0 miatt).
            float3 SafeNormalize(float3 v, float3 fallback)
            {
                float lenSq = dot(v, v);
                return lenSq > 1e-10 ? v * rsqrt(lenSq) : fallback;
            }

            // ---------------------------------------------------------------
            // ND-151 (M13 4. fazis): PER-PIXEL MIKRO-RESZLET.
            //
            // MIT AD. A legfinomabb mesh-quad is sok pixelt fed kozelbol; ott
            // a felszin ma teljesen sima. Ez a blokk a modell sajat fBm-letrajat
            // folytatja a mesh-felbontas ALATT: perturbalja a normalt (reszlet-
            // domborzat arnyalasa) es modulalja az albedot. UJ GEOMETRIA NELKUL.
            //
            // MINDEN SZAM A CORE-BOL JON (SurfaceMicroDetail + MicroDetailBand,
            // uniformkent) - itt CSAK a lekepezes ALAKJA van. Ez a valasz az
            // ND-128 hibaosztalyara: nincs ket, egymastol elcsuszo szamkeszlet.
            //
            // SEMMI NEM CSATOLJA VISSZA a vilagmodellbe (I1 erintetlen), es
            // minden bemenete a modellbol jon: a lejto a mar renderelt
            // geometria normaljabol, a magassag a mar renderelt radiuszbol,
            // a faziseltolas a vilag seedjebol (I3).
            float4 _MicroDetailBand;      // (alap-frekvencia ciklus/radian, sav-tort, lathatosag, -)
            float4 _MicroDetailPhase;     // seedbol szarmazo zaj-faziseltolas
            float4 _MicroDetailResponse;  // (PlainsNormalAmplitude, RockNormalAmplitude, PlainsAlbedoJitter, RockAlbedoJitter)
            float4 _MicroDetailShape;     // (SlopeReferenceSin, RockAltitudeMeters, RockFrequencyScale, SubmergenceBandMeters)
            float4 _MicroDetailSubmerged; // (SubmergedNormalScale, SubmergedAlbedoScale, -, -)
            float4 _MicroDetailGeometry;  // (rajzolt radiusz, elevationScale, relief-exaggeration, tengerszint m)
            float4x4 _MicroWorldToPlanet;
            float _MicroDetailStrength;   // globalis, elo-hangolhato erosseg (0 = kikapcsolva)
            float _MicroDetailEnable;     // ANYAG-szintu kapu: a vizfelszin 0-t kap

            uint MicroHash(uint3 p)
            {
                p *= uint3(0x9E3779B1u, 0x85EBCA77u, 0xC2B2AE3Du);
                uint h = p.x ^ p.y ^ p.z;
                h ^= h >> 15; h *= 0x2545F491u; h ^= h >> 13;
                return h;
            }

            float MicroLatticeValue(int3 cell)
            {
                return MicroHash(uint3(cell)) * (1.0 / 4294967296.0);
            }

            // Ertek-zaj kvintikus fade-gorbevel - ugyanaz a fade, mint a Core
            // FractalNoise-ban (6t^5-15t^4+10t^3), hogy a letra karaktere
            // folytonosan folytatodjon a modell sajat zaja alatt.
            float MicroValueNoise(float3 x)
            {
                float3 fi = floor(x);
                float3 f = x - fi;
                float3 w = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                int3 c = int3(fi);
                float n000 = MicroLatticeValue(c + int3(0, 0, 0));
                float n100 = MicroLatticeValue(c + int3(1, 0, 0));
                float n010 = MicroLatticeValue(c + int3(0, 1, 0));
                float n110 = MicroLatticeValue(c + int3(1, 1, 0));
                float n001 = MicroLatticeValue(c + int3(0, 0, 1));
                float n101 = MicroLatticeValue(c + int3(1, 0, 1));
                float n011 = MicroLatticeValue(c + int3(0, 1, 1));
                float n111 = MicroLatticeValue(c + int3(1, 1, 1));
                float x00 = lerp(n000, n100, w.x);
                float x10 = lerp(n010, n110, w.x);
                float x01 = lerp(n001, n101, w.x);
                float x11 = lerp(n011, n111, w.x);
                return lerp(lerp(x00, x10, w.y), lerp(x01, x11, w.y), w.z) * 2.0 - 1.0;
            }

            // Negy oktav, a sav-tort szerint atusztatva. A sulyozas
            // (1-frac, 1, 1, frac) * persistence^k, normalva - a
            // MicroDetailBand.OctaveWeights parja; a folytonossagat C#-teszt
            // bizonyitja (BandCrossing_IsContinuous).
            float MicroFbm(float3 q, float frac)
            {
                float total = 0.0;
                float norm = 0.0;
                float amplitude = 1.0;
                float3 p = q;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float fade = k == 0 ? (1.0 - frac) : (k == 3 ? frac : 1.0);
                    float weight = amplitude * fade;
                    if (weight > 0.0)
                        total += MicroValueNoise(p) * weight;
                    norm += weight;
                    amplitude *= 0.5;
                    p *= 2.0;
                }
                return norm > 1e-6 ? total / norm : 0.0;
            }

            float MicroSmoothstep01(float t)
            {
                t = saturate(t);
                return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            }

            // A felszini pont "kozetessege" es viz-alattisaga - a
            // SurfaceMicroDetail.Rockiness/Submergence parja, ugyanazokkal a
            // (uniformkent atadott) konstansokkal.
            void MicroDetailResponseAt(
                float slopeSin, float elevation, float seaLevel,
                out float normalAmplitude, out float albedoJitter, out float frequencyScale)
            {
                float bySlope = MicroSmoothstep01(slopeSin / max(_MicroDetailShape.x, 1e-6));
                float byAltitude = MicroSmoothstep01((elevation - seaLevel) / max(_MicroDetailShape.y, 1e-6));
                float rock = max(bySlope, byAltitude);
                float wet = MicroSmoothstep01((seaLevel - elevation) / max(_MicroDetailShape.w, 1e-6));
                normalAmplitude = lerp(_MicroDetailResponse.x, _MicroDetailResponse.y, rock)
                    * lerp(1.0, _MicroDetailSubmerged.x, wet);
                albedoJitter = lerp(_MicroDetailResponse.z, _MicroDetailResponse.w, rock)
                    * lerp(1.0, _MicroDetailSubmerged.y, wet);
                frequencyScale = lerp(1.0, _MicroDetailShape.z, rock);
            }

            // Stabil tangens-bazis a radialis iranyhoz (a legkisebb komponens
            // tengelyet valasztva - igy soha nem degeneralodik).
            void MicroTangentBasis(float3 up, out float3 t, out float3 b)
            {
                float3 a = abs(up);
                float3 axis = (a.x <= a.y && a.x <= a.z) ? float3(1, 0, 0)
                    : ((a.y <= a.z) ? float3(0, 1, 0) : float3(0, 0, 1));
                t = SafeNormalize(cross(up, axis), float3(1, 0, 0));
                b = cross(up, t);
            }

            // A normal perturbalasa + az albedo modulalasa. A visszaadott ertek
            // a szin-multiplikator; a normalt helyben modositja.
            float ApplyMicroDetail(float3 positionWS, inout float3 N)
            {
                float gate = _MicroDetailEnable * _MicroDetailStrength * _MicroDetailBand.z;
                if (gate <= 0.0)
                    return 1.0;

                float3 planetPos = mul(_MicroWorldToPlanet, float4(positionWS, 1.0)).xyz;
                float r = length(planetPos);
                if (r <= 1e-6)
                    return 1.0;
                float3 up = planetPos / r;

                // Elevacio-visszanyeres: a PlanetGridMesh.
                // WorldElevationFromDisplacedRadius inverze, uj kiertekeles nelkul.
                float displayElevation = (r - _MicroDetailGeometry.x) / max(_MicroDetailGeometry.y, 1e-20);
                float k = _MicroDetailGeometry.z == 0.0 ? 1.0 : _MicroDetailGeometry.z;
                float seaLevel = _MicroDetailGeometry.w;
                float elevation = seaLevel + (displayElevation - seaLevel) / k;

                // Lejto: a felszini normal es a radialis irany kozti szog szinusza.
                float slopeSin = sqrt(saturate(1.0 - dot(N, up) * dot(N, up)));

                float normalAmplitude, albedoJitter, frequencyScale;
                MicroDetailResponseAt(slopeSin, elevation, seaLevel, normalAmplitude, albedoJitter, frequencyScale);

                float frequency = _MicroDetailBand.x * frequencyScale;
                float3 q = up * frequency + _MicroDetailPhase.xyz;
                float frac = _MicroDetailBand.y;

                float3 t, b;
                MicroTangentBasis(up, t, b);
                // A ket tangencialis minta fel zaj-cellaval tavolabb: a veges
                // differencia igy a zaj SAJAT lepteken meri a meredekseget,
                // frekvenciatol fuggetlenul.
                // (A valtozo NEVE szandekosan nem "step": az HLSL-intrinsic.)
                const float sampleStep = 0.5;
                float h0 = MicroFbm(q, frac);
                float ht = MicroFbm(q + t * sampleStep, frac);
                float hb = MicroFbm(q + b * sampleStep, frac);
                float dt = (ht - h0) / sampleStep;
                float db = (hb - h0) / sampleStep;

                // SUROLO NEZET-KORREKCIO. A sav (_MicroDetailBand) a
                // LEGKOZELEBBI felszinpont pixel-labnyomabol szamol; a limb
                // fele nezve ugyanaz a felszin 1/cos-szor akkora szoget fed
                // egy pixelen, tehat ott a reszlet a Nyquist ala csuszna es
                // szemcses villogast adna (elso elo menetben pontosan ez
                // latszott a limbnel). A halvanyitas ugyanabbol a
                // labnyom-kriteriumbol kovetkezik, ami a savot is adja.
                float ndotv = saturate(dot(N, SafeNormalize(_WorldSpaceCameraPos - positionWS, N)));
                float grazing = MicroSmoothstep01((ndotv - 0.05) / 0.25);
                float gain = gate * grazing;
                if (gain <= 0.0)
                    return 1.0;

                N = SafeNormalize(N - normalAmplitude * gain * (dt * t + db * b), N);
                return 1.0 + h0 * albedoJitter * gain;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                if (_ThermalMode > 0.5 && dot(i.positionPlanet, i.positionPlanet) > 1e-10)
                {
                    float2 uv = ThermalAtlasUv(i.positionPlanet);
                    float normalized = _ThermalMode < 1.5 ? tex2D(_ThermalSurfaceTex, uv).r : tex2D(_ThermalAirTex, uv).r;
                    float kelvin = normalized * 655.35 + 150.0;
                    float3 thermal = ThermalPalette(kelvin);
                    // 0 °C kontúr: képernyőtérben állandó vastagságú sötét vonal.
                    float band = max(fwidth(kelvin) * 0.75, 1e-4);
                    if (abs(kelvin - 273.15) < band)
                        thermal *= 0.35;
                    return fixed4(thermal, i.color.a);
                }

                float3 N = SafeNormalize(i.normalWS, float3(0, 1, 0));
                // ND-151: a mikro-reszlet a VILAGITAS ELOTT perturbalja a
                // normalt (igy a Lambert ES a spekularis is latja), es visszaad
                // egy albedo-multiplikatort. Kikapcsolva (vagy nem beallitott
                // uniformokkal) egzaktul 1.0-t ad es a normalt nem valtoztatja,
                // tehat a korabbi kep BITRE valtozatlan.
                float microAlbedo = ApplyMicroDetail(i.positionWS, N);
                float3 L = SafeNormalize(_SunDir.xyz, float3(0, 1, 0));
                float ndotl = saturate(dot(N, L));

                // Diffuz: ambiens padlo + iranyfeny (sose teljesen fekete az
                // ejszakai oldal, hogy a domborzat/szin ott is kivehet
                float3 diffuse = _Ambient + (1.0 - _Ambient) * ndotl * _SunColor.rgb;

                // Blinn-Phong spekularis a valos kamera- es Nap-irannyal.
                // 2026-09-09: ha a Nap- (L) es kamera-irany (V) KOZEL
                // ELLENTETES (L+V kozel nulla - ez egy gomb limb-jenel/
                // sarkanal, bizonyos kamera-Nap szogeknel PONTOSAN elofordulhat),
                // a fel-vektor (H) korabban NaN-t adott (normalize(0,0,0)),
                // ami a GPU-n MINDENT megfertozott es EGYETLEN PIXELKENT
                // (nem geometriai hibakent, hanem kamera-/Nap-szog-fuggo
                // per-pixel esetkent) jelent meg vakito foltkent - ELTERO
                // gyokerok, mint a korabbi (mar javitott) mesh-normal NaN.
                // Degeneralt esetben a spekularis egyszeruen NULLA (fizikailag
                // ertelmetlen konfiguracio, a hatasa amugy is elhanyagolhato).
                float3 V = SafeNormalize(_WorldSpaceCameraPos - i.positionWS, N);
                float3 LV = L + V;
                float lvLenSq = dot(LV, LV);
                float spec = 0;
                if (lvLenSq > 1e-10)
                {
                    float3 H = LV * rsqrt(lvLenSq);
                    spec = pow(saturate(dot(N, H)), _Shininess) * _SpecStrength * ndotl;
                }

                float3 rgb = i.color.rgb * microAlbedo * diffuse + spec * _SunColor.rgb;

                // 2026-09-09 DIAGNOSZTIKA: ha a fenti szamitas (pl. normalize()
                // egy majdnem-nulla normalon, vagy pow() egy negativ/NaN
                // alapon) NaN/Infinity-t eredmenyez a GPU-n, az MINDEN korabbi
                // feny-/post-processing beallitastol fuggetlenul FEHERKENT
                // jelenik meg. Ha ide jutunk, CIAN-ra valtunk (a C#-oldali
                // NaN-vertex-szin-vedelem MAGENTA-jatol megkulonboztetheto
                // szinnel), hogy a screenshot egyertelmuen mutassa: a hiba
                // forrasa a GPU-oldali szamitas, nem a bemeno vertex-adat.
                if (any(isnan(rgb)) || any(isinf(rgb)))
                    return fixed4(0, 1, 1, i.color.a);

                return fixed4(rgb, i.color.a);
            }
            ENDCG
        }
    }
}
