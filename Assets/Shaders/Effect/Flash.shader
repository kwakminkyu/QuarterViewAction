Shader "Effect/Flash"
{
    // A soft radial glow for particle flashes. The falloff is worked out from
    // the particle's UVs, so it needs no texture, and the particle system's
    // colour (Start Color, Color over Lifetime) tints and fades it.
    Properties
    {
        [HDR] _Color ("Color", Color) = (6, 4, 2.4, 1)

        // How quickly the glow falls away from the centre. Low values give a
        // broad haze; high values a tight hot core.
        _Falloff ("Falloff", Range(0.5, 8)) = 2.5

        // Extra brightness right at the centre, on top of the falloff.
        _Core ("Core", Range(0, 4)) = 1.5
        _CoreSize ("Core Size", Range(0.01, 0.5)) = 0.12

        // Always by default: a flash is light, and a billboard centred on the
        // ground would otherwise lose its lower half into the floor.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 8
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
            Name "FlashUnlit"

            Blend SrcAlpha One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Falloff;
                float _Core;
                float _CoreSize;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 0 at the centre of the quad, 1 at the edge of the inscribed
                // circle, so the glow is round whatever the quad's corners do.
                float distance = length(input.uv * 2.0 - 1.0);
                float glow = pow(saturate(1.0 - distance), _Falloff);
                float core = _Core * (1.0 - smoothstep(0.0, _CoreSize, distance));

                float3 color = _Color.rgb * input.color.rgb;
                float alpha = saturate((glow + core) * _Color.a * input.color.a);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
