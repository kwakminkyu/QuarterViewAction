Shader "Effect/BladeRibbon"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (3.2, 2.4, 1.6, 1)
        [HDR] _EdgeColor ("Dissolve Edge Color", Color) = (8, 3, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        [Header(Shape)]
        _TipBias ("Tip Bias", Range(0.1, 6)) = 1.8
        _TailFade ("Tail Fade", Range(0.001, 1)) = 0.55

        [Header(Dissolve)]
        _DissolveTex ("Dissolve Noise", 2D) = "white" {}
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _EdgeWidth ("Dissolve Edge Width", Range(0.001, 0.5)) = 0.12
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.45
        _NoiseScale ("Noise Scale", Range(0.1, 8)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BladeRibbonUnlit"

            // Must match Effect/Slash exactly. If only one of the two layers
            // takes part in depth testing they disagree wherever something
            // occludes them, which is the trail-versus-slash mismatch this
            // work set out to remove.
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DissolveTex);
            SAMPLER(sampler_DissolveTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EdgeColor;
                float _Alpha;

                float _TipBias;
                float _TailFade;

                float4 _DissolveTex_ST;
                float _Dissolve;
                float _EdgeWidth;
                float _NoiseAmount;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // u runs along the sweep (0 = oldest, 1 = at the blade),
                // v across the blade (0 = inner edge, 1 = tip).
                float u = input.uv.x;
                float v = input.uv.y;

                // Concentrating brightness at the tip is what keeps a swept
                // quad reading as an edge rather than a flat band.
                float tipMask = pow(saturate(v), _TipBias);
                float tailMask = smoothstep(0.0, _TailFade, u);

                float noise = SAMPLE_TEXTURE2D(
                    _DissolveTex,
                    sampler_DissolveTex,
                    input.uv * _NoiseScale + _DissolveTex_ST.zw).r;

                // Erodes from the tail, so the whole-ribbon fade driven by
                // _Alpha burns away instead of dimming uniformly.
                float mask = lerp(u, noise, _NoiseAmount);

                float d = mask - _Dissolve;
                float edgeWidth = max(_EdgeWidth, 1e-4);
                float body = smoothstep(0.0, edgeWidth, d);
                float edge = saturate(1.0 - abs(d) / edgeWidth) * step(0.0, d);

                float3 color = _Color.rgb + _EdgeColor.rgb * edge;
                float alpha = tipMask * tailMask * body * _Color.a * _Alpha;

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
