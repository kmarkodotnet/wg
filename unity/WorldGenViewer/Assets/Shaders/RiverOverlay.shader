// ND-185: modellből származó térképi folyójelölés, HDRP-ben külön anyagon.
Shader "WorldGen/RiverOverlay"
{
    Properties { _Color ("River Color", Color) = (0.2, 0.55, 0.9, 1) }
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent" "DisableBatching"="True" }
        Pass
        {
            Name "RiverOverlay"
            Tags { "LightMode"="ForwardOnly" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float4 centerOS : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                float4 centerCS = TransformObjectToHClip(v.centerOS.xyz);
                if (o.positionCS.w > 0 && centerCS.w > 0)
                {
                    float2 offset = o.positionCS.xy / o.positionCS.w - centerCS.xy / centerCS.w;
                    float pixels = length(offset * _ScreenSize.xy * 0.5);
                    float scale = max(1.0, 2.0 / max(pixels, 1e-5));
                    o.positionCS.xy = (centerCS.xy / centerCS.w + offset * scale) * o.positionCS.w;
                }
                return o;
            }
            float4 Frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
