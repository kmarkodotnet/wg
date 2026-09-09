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
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.positionWS = mul(unity_ObjectToWorld, v.positionOS).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
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
