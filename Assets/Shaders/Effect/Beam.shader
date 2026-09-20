Shader "Effect/Beam"
{
    // A thin pillar of light rising from the point of impact. The quad it is
    // drawn on is rebuilt in the vertex shader so it always stands upright and
    // turns only about the vertical to face the camera - the object's own
    // rotation is ignored, so the beam never goes edge-on whichever way the
    // attack faced. Width and height are in metres, multiplied by the object's
    // scale.
    //
    // Expects a quad with UV x 0..1 across and y 0..1 from base to top.
    // _Progress (0..1 over the effect's life) is set by SlashEffect.
    Properties
    {
        [HDR] _Color ("Glow Color", Color) = (4, 1.6, 0.8, 1)
        [HDR] _CoreColor ("Core Color", Color) = (8, 7, 6, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Progress ("Progress", Range(0, 1)) = 0

        [Header(Size)]
        _Width ("Width (m)", Range(0.01, 2)) = 0.22
        _Height ("Height (m)", Range(0.1, 20)) = 7
        // Width at the top as a share of the base, and how the narrowing is
        // spread up the beam (1 even, higher keeps it wide longer).
        _TopWidth ("Top Width", Range(0, 1)) = 0.25
        _TaperCurve ("Taper Curve", Range(0.2, 4)) = 1

        [Header(Look)]
        // Share of the width taken by the white-hot core.
        _CoreWidth ("Core Width", Range(0.01, 1)) = 0.3
        // How fast the glow falls off towards the edges.
        _GlowFalloff ("Glow Falloff", Range(0.3, 6)) = 1.6
        // Height (0..1) where the beam starts fading out towards its top.
        _FadeStart ("Fade Start", Range(0, 1)) = 0.35
        // Extra brightness at the base, where the blade struck.
        _BaseGlow ("Base Glow", Range(0, 6)) = 2

        [Header(Over Time)]
        // Share of the life spent widening in from a sliver.
        _GrowTime ("Grow Time", Range(0.001, 0.5)) = 0.08
        // Progress where it starts thinning, and how: 1 even, below 1 drops
        // most of the width at once, above 1 holds on until the end.
        _ThinStart ("Thin Start", Range(0, 0.99)) = 0.2
        _ThinCurve ("Thin Curve", Range(0.2, 4)) = 0.7
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
            Name "BeamUnlit"

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CoreColor;
                float _Alpha;
                float _Progress;
                float _Width;
                float _Height;
                float _TopWidth;
                float _TaperCurve;
                float _CoreWidth;
                float _GlowFalloff;
                float _FadeStart;
                float _BaseGlow;
                float _GrowTime;
                float _ThinStart;
                float _ThinCurve;
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
                float3 origin = TransformObjectToWorld(float3(0.0, 0.0, 0.0));

                // Object scale, read off the matrix so rotation does not
                // matter: X widens the beam, Y lengthens it.
                float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));

                // Camera right, flattened, so the quad turns about the vertical
                // only and the beam stays upright on screen.
                float3 right = float3(UNITY_MATRIX_V._m00, UNITY_MATRIX_V._m01, UNITY_MATRIX_V._m02);
                right.y = 0.0;
                right = dot(right, right) > 1e-6 ? normalize(right) : float3(1.0, 0.0, 0.0);

                float3 world = origin +
                    right * (input.uv.x - 0.5) * _Width * scaleX +
                    float3(0.0, 1.0, 0.0) * input.uv.y * _Height * scaleY;

                Varyings output;
                output.positionHCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float height = saturate(input.uv.y);
                float across = abs(input.uv.x * 2.0 - 1.0);

                // Width over time: widens in from a sliver, then thins away.
                float grow = saturate(_Progress / _GrowTime);
                float thin = saturate(1.0 - (_Progress - _ThinStart) / max(1.0 - _ThinStart, 1e-3));
                float timeWidth = lerp(0.3, 1.0, grow) * pow(thin, _ThinCurve);

                // Width up the beam: full at the base, narrowing to the top.
                float taper = lerp(1.0, _TopWidth, pow(height, _TaperCurve));

                // 0 on the centre line, 1 at the current edge.
                float d = across / max(taper * timeWidth, 1e-4);

                float glow = pow(saturate(1.0 - d), _GlowFalloff);
                float core = 1.0 - smoothstep(_CoreWidth * 0.5, _CoreWidth, d);

                float fade = 1.0 - smoothstep(_FadeStart, 1.0, height);
                float base = 1.0 + _BaseGlow * pow(1.0 - height, 6.0);

                float3 color = lerp(_Color.rgb, _CoreColor.rgb, core) * base;
                float alpha = saturate(max(glow, core) * fade) * _Color.a * _Alpha;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
