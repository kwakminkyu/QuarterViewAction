Shader "Effect/Slash"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 2.2, 1, 1)
        [HDR] _EdgeColor ("Leading Edge Color", Color) = (8, 3, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        [Header(Arc)]
        // Describe where the authored arc sits so the shader can work out how
        // far along it each fragment is without relying on a UV unwrap.
        _ArcCenter ("Arc Center (deg)", Float) = 180
        _ArcSpan ("Arc Span (deg)", Float) = 150
        _InnerRadius ("Inner Radius", Float) = 0.78
        _OuterRadius ("Outer Radius", Float) = 1.0

        [Header(Sweep)]
        // Head and tail of the lit band, in 0..1 along the arc. Driven from
        // SlashEffect so the crescent draws on and erases like a trail rather
        // than appearing all at once.
        _Head ("Head", Float) = 1
        _Tail ("Tail", Float) = 0
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.12

        [Header(Shape)]
        _OuterBias ("Outer Edge Bias", Range(0.1, 6)) = 1.6

        [Header(Dissolve)]
        _DissolveTex ("Dissolve Noise", 2D) = "white" {}
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.4
        _NoiseScale ("Noise Scale", Range(0.1, 8)) = 2

        // Uses the mesh's own unwrap instead of deriving the arc coordinates
        // from vertex positions. Only worth turning on once the mesh actually
        // has UVs, and it allows shapes that are not arcs about the origin.
        [Toggle(_USE_MESH_UV)] _UseMeshUV ("Use Mesh UVs", Float) = 0
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

            // Must match Effect/BladeRibbon exactly. If only one of the two
            // layers takes part in depth testing they disagree wherever
            // something occludes them.
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma shader_feature_local _USE_MESH_UV

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DissolveTex);
            SAMPLER(sampler_DissolveTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EdgeColor;
                float _Alpha;

                float _ArcCenter;
                float _ArcSpan;
                float _InnerRadius;
                float _OuterRadius;

                float _Head;
                float _Tail;
                float _EdgeSoftness;

                float _OuterBias;

                float4 _DissolveTex_ST;
                float _Dissolve;
                float _NoiseAmount;
                float _NoiseScale;
                float _UseMeshUV;
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
                float3 positionOS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float u;
                float v;

#ifdef _USE_MESH_UV
                u = input.uv.x;
                v = input.uv.y;
#else
                // Rotate into the arc's own frame first. The authored arc is
                // centred on 180 degrees, so measuring the angle directly
                // would straddle the atan2 seam and tear the sweep in half.
                float centre = radians(_ArcCenter);
                float cs = cos(-centre);
                float sn = sin(-centre);
                float2 p = float2(
                    input.positionOS.x * cs - input.positionOS.y * sn,
                    input.positionOS.x * sn + input.positionOS.y * cs);

                float angle = atan2(p.y, p.x);
                float span = max(radians(_ArcSpan), 1e-3);
                u = saturate(angle / span + 0.5);

                float radius = length(input.positionOS.xy);
                v = saturate(
                    (radius - _InnerRadius) /
                    max(_OuterRadius - _InnerRadius, 1e-4));
#endif

                // The lit band runs from tail to head. Both march along the
                // arc over the effect's lifetime, so the cut is drawn on and
                // then wiped away from behind.
                float soft = max(_EdgeSoftness, 1e-3);
                float headMask = 1.0 - smoothstep(_Head, _Head + soft, u);
                float tailMask = smoothstep(_Tail - soft, _Tail, u);
                float band = headMask * tailMask;

                // Brightest right at the head, which is where the blade is.
                float lead = saturate(1.0 - (_Head - u) / max(soft * 2.0, 1e-3));
                lead *= step(u, _Head) * tailMask;

                float outerMask = pow(saturate(v), _OuterBias);

                float noise = SAMPLE_TEXTURE2D(
                    _DissolveTex,
                    sampler_DissolveTex,
                    float2(u, v) * _NoiseScale + _DissolveTex_ST.zw).r;

                float burn = step(_Dissolve, lerp(1.0, noise, _NoiseAmount));

                float3 color = _Color.rgb + _EdgeColor.rgb * lead;
                float alpha = band * outerMask * burn * _Color.a * _Alpha;

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
