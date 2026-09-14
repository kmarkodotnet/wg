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

                float3 rgb = i.color.rgb * diffuse + spec * _SunColor.rgb;

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
