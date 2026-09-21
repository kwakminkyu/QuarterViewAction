Shader "Effect/Slash"
{
    // Sweeps a lit band along a UV-mapped slash mesh: U runs along the cut,
    // V from the inner edge to the outer. SlashEffect drives the band's head
    // and tail over the effect's life.
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 2.2, 1, 1)
        [HDR] _EdgeColor ("Leading Edge Color", Color) = (8, 3, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        [Header(Sweep)]
        // Head and tail of the lit band, in 0..1 along the cut. Driven from
        // SlashEffect so the crescent draws on and erases like a trail rather
        // than appearing all at once.
        _Head ("Head", Float) = 1
        _Tail ("Tail", Float) = 0
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.12

        [Header(Shape)]
        _OuterBias ("Outer Edge Bias", Range(0.1, 6)) = 1.6

        // Bounds of the mesh's unwrap as (min U, min V, size U, size V),
        // stretched to 0..1 so the unwrap need not fill the UV square exactly.
        // Set per mesh by SlashEffect.
        _MeshUVRect ("Mesh UV Rect", Vector) = (0, 0, 1, 1)
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
            Name "SlashUnlit"

            // Additive so the slash reads as light, but depth-tested so the
            // character it is swung around occludes it instead of being drawn
            // through. Effects therefore have to be placed above the ground:
            // anything below it is cut away.
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EdgeColor;
                float _Alpha;

                float _Head;
                float _Tail;
                float _EdgeSoftness;

                float _OuterBias;
                float4 _MeshUVRect;
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
                float2 uv = saturate(
                    (input.uv - _MeshUVRect.xy) / max(_MeshUVRect.zw, 1e-4));
                float u = uv.x;
                float v = uv.y;

                // The lit band runs from tail to head. Both march along the
                // cut over the effect's lifetime, so it is drawn on and then
                // wiped away from behind.
                float soft = max(_EdgeSoftness, 1e-3);
                float headMask = 1.0 - smoothstep(_Head, _Head + soft, u);
                float tailMask = smoothstep(_Tail - soft, _Tail, u);
                float band = headMask * tailMask;

                // Brightest right at the head, which is where the blade is.
                float lead = saturate(1.0 - (_Head - u) / max(soft * 2.0, 1e-3));
                lead *= step(u, _Head) * tailMask;

                float outerMask = pow(v, _OuterBias);

                float3 color = _Color.rgb + _EdgeColor.rgb * lead;
                float alpha = band * outerMask * _Color.a * _Alpha;

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
