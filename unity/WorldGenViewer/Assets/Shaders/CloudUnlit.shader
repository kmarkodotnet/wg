// M6/M13 felho-MVP: a VertexColorUnlit.shader ATLATSZO valtozata - a
// felhoreteg minden geometriaja/adata a MAR MEGLEVO csapadek-mezobol jon
// (I3: nincs dekorativ, kezzel festett felhotextura), a sűrűséget a
// vertex-szin ALFA csatornaja hordozza (ld. PlanetGridMesh.BuildClouds).
//
// UGYANAZ a "best-effort HDRP CG" mintazat, mint a VertexColorUnlit-nal
// (ld. ott a fejlecet) - EZ SEM ELO UNITY-TESZTELT innen. Egyszerubb, mint
// a terep-shader (nincs spekularis - a felhonek nincs ertelme csillanjon),
// csak Lambert-diffuz + alfa-atlatszosag.
Shader "WorldGen/CloudUnlit"
{
    Properties
    {
        _SunDir ("Sun Direction (world, to-sun)", Vector) = (0.4, 0.6, 0.7, 0)
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CloudForward"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _SunDir;
            float4 _SunColor;
            float _Ambient;

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
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = UnityObjectToClipPos(v.positionOS);
                o.normalWS = UnityObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            fixed4 Frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 L = normalize(_SunDir.xyz);
                float ndotl = saturate(dot(N, L));
                float3 diffuse = _Ambient + (1.0 - _Ambient) * ndotl * _SunColor.rgb;
                return fixed4(i.color.rgb * diffuse, i.color.a);
            }
            ENDCG
        }
    }
}
