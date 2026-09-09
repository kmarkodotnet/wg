// Csillagos hatter (felhasznaloi keres, 2026-09-06) + a lathato Nap-korong
// (SunController) KOZOS anyaga - mindketto egyszeru, onviligito ("unlit")
// pontszeru/korong-geometria, nincs szuksege arnyekolasra (a fenyforras
// MAGA a geometria). A vertex-szin hordozza a fenyesseget (ld.
// StarField.Build), additiv keveressel (Blend One One), hogy attfedo
// csillagok/a nap-korong termeszetesen "izzon", ne takarjak ki egymast.
//
// UGYANAZ a "best-effort HDRP CG" mintazat, mint a CloudUnlit/
// VertexColorUnlit-nal (ld. ott a fejlecet) - EZ SEM ELO UNITY-TESZTELT.
//
// _Color: FUGGETLEN szorzo a vertex-szintol - a StarField.Build() SAJAT
// vertex-szint ad minden csillagnak (ezert ott _Color alapertek feher =
// nincs hatasa), DE a Nap-korong (SunController.sunVisual) tipikusan egy
// Unity beepitett primitiv Quad, aminek NINCS garantaltan vertex-szine -
// enelkul a _Color fallback nelkul a korong FEKETEN jelenhetne meg. A
// Material Inspectoron allithato "Color" mezovel igy FUGGETLENUL a
// mesh-tol be lehet allitani a Nap szinet/fenyesseget.
//
// KOR ALAKU LAGYITAS (felhasznaloi visszajelzes, 2026-09-06: "a nap egy
// negyszog"): az UV koordinatakbol (StarField.Build() MOST MAR ad UV-t
// minden kvadnak) szamolt kozeppont-tavolsag alapjan a kvad SZELEI fele
// lagyan elhalványul (smoothstep) - igy egy éles szélű négyzet helyett
// egy puha szélű, kör/korong-szerű fényfolt jon ki, mind a csillagoknak,
// mind a Nap-korongnak.
Shader "WorldGen/StarUnlit"
{
    Properties
    {
        // [HDR]: a Material Inspector szinvalaszto 1.0 folott is enged
        // erteket adni (pl. 3-5) - HDRP alatt ez Bloom post-processzel
        // valodi "izzast" ad a Nap-korongnak, tulmenve azon, amit a sima
        // _Brightness szorzo onmagaban tudna (annak also van egy felso
        // hatara, ld. Range).
        [HDR] _Color ("Color (vertex-szintol FÜGGETLEN szorzó, HDR)", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness Multiplier", Range(0, 20)) = 1.5
        _SoftEdge ("Soft Edge Start (0..1, kozeptol)", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "StarForward"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Brightness;
            float _SoftEdge;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                // Kozeppont-tavolsag [0,1] (0 = kvad kozepe, 1 = sarok) -
                // a _SoftEdge-tol 1-ig lagyan lecseng a szel fele, hogy a
                // kvad kor/korong-szerunek lasson, ne éles szélű négyzetnek.
                float dist = length(i.uv - 0.5) * 2.0;
                float falloff = 1.0 - smoothstep(_SoftEdge, 1.0, dist);
                return fixed4(i.color.rgb * _Color.rgb * _Brightness * falloff, 1.0);
            }
            ENDCG
        }
    }
}
