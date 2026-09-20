Shader "Effect/ShockRing"
{
    // A shockwave ring drawn entirely in the shader on a flat quad: a thin band
    // with ragged spikes reaching in from it. The quad is spread out by the
    // effect's Scale Curve; the spikes retract into the band as the effect
    // nears its end, driven by _Progress, which SlashEffect sets every frame.
    //
    // The spikes come in two layers - a few big ones and a scatter of small
    // thorns - and within each layer spikes may be missing, bunched, much
    // longer than their neighbours or flared at the root. That irregularity is
    // what keeps the ring from reading as a machined gear.
    //
    // All sizes are in quad space: 1 is the distance from the quad's centre to
    // the middle of an edge.
    Properties
    {
        [HDR] _Color ("Color", Color) = (3, 0.7, 0.4, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        // Set by SlashEffect: 0 as the effect appears, 1 as it ends.
        _Progress ("Progress", Range(0, 1)) = 0

        [Header(Band)]
        _Radius ("Outer Radius", Range(0.1, 1)) = 0.96
        // Band thickness in metres, measured inwards from the outer edge, so
        // it reads the same on screen whatever size the ring has spread to.
        // It swells quickly to its peak, holds there while the ring spreads,
        // then collapses to a line near the end.
        _WidthStart ("Start Width (m)", Range(0, 2)) = 0.12
        _WidthPeak ("Peak Width (m)", Range(0.001, 2)) = 0.45
        _WidthEnd ("End Width (m)", Range(0, 0.5)) = 0.01
        // Progress by which the band has swelled to its peak.
        _PeakAt ("Peak At", Range(0.01, 1)) = 0.3
        // Progress at which it starts collapsing; it reaches End Width at 1.
        _CollapseStart ("Collapse Start", Range(0, 0.99)) = 0.7
        // Shape of the collapse: 1 is even; below 1 drops most of the width
        // straight away, above 1 holds on and drops at the very end.
        _CollapseCurve ("Collapse Curve", Range(0.2, 4)) = 0.6
        // Bulges the band's inner edge into lobes between the spikes.
        _Scallop ("Scallop Depth", Range(0, 0.3)) = 0.05
        _ScallopCount ("Scallop Count", Range(1, 32)) = 9

        [Header(Big Spikes)]
        _SpikeCount ("Slots", Range(1, 64)) = 14
        // Chance each slot actually has a spike; below 1 leaves gaps.
        _SpikeChance ("Chance", Range(0, 1)) = 0.85
        _SpikeLength ("Longest", Range(0, 1)) = 0.55
        // 1 spreads lengths evenly; higher makes most spikes short with the
        // odd long one, which is what reads as irregular.
        _LengthBias ("Length Bias", Range(0.3, 5)) = 2.2
        // Width of a spike as a share of its slot. Longer spikes come out a
        // little wider, as in the reference.
        _SpikeWidth ("Width", Range(0.05, 2)) = 0.5
        // How far a spike may drift from its slot's middle, in slots. Near 1
        // lets neighbours bunch up or leave wide gaps.
        _SpikePlaceJitter ("Placement Jitter", Range(0, 1)) = 0.45
        // 1 is straight-sided; higher scoops the sides into needle tips.
        _SpikeSharpness ("Sharpness", Range(0.5, 6)) = 2.8

        [Header(Spike Roots)]
        // A wide, low shoulder under each spike that webs it into the band.
        // Width is a multiple of the spike's own width; height a share of its
        // length (0 turns the roots off).
        _RootWidth ("Root Width", Range(1, 4)) = 2.2
        _RootHeight ("Root Height", Range(0, 1)) = 0.22

        [Header(Small Spikes)]
        _DetailCount ("Slots", Range(0, 96)) = 34
        _DetailChance ("Chance", Range(0, 1)) = 0.55
        _DetailLength ("Longest", Range(0, 0.5)) = 0.12

        [Header(Shape)]
        // Sweeps the tips round the ring so the spikes lean like claws.
        _SpikeSkew ("Skew", Range(-1, 1)) = 0.1
        // Fades the spikes towards their tips (0 solid to the point).
        _TipFade ("Tip Fade", Range(0, 1)) = 0.3
        _Seed ("Seed", Float) = 3

        [Header(Spike Retract)]
        // Progress over which the spikes shrink back into the band: they are
        // full length before Start and gone by End. Each spike shifts that
        // window by up to Jitter, so they do not all vanish together.
        _RetractStart ("Start", Range(0, 1)) = 0.5
        _RetractEnd ("End", Range(0, 1)) = 0.85
        _RetractJitter ("Jitter", Range(0, 0.5)) = 0.15

        [Header(Blending)]
        // Additive by default, like the slashes. SrcAlpha / OneMinusSrcAlpha
        // gives ordinary transparency, which can show dark colours.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
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
            Name "ShockRingUnlit"

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Alpha;
                float _Progress;
                float _Radius;
                float _WidthStart;
                float _WidthPeak;
                float _WidthEnd;
                float _PeakAt;
                float _CollapseStart;
                float _CollapseCurve;
                float _Scallop;
                float _ScallopCount;
                float _SpikeCount;
                float _SpikeChance;
                float _SpikeLength;
                float _LengthBias;
                float _SpikeWidth;
                float _SpikePlaceJitter;
                float _SpikeSharpness;
                float _RootWidth;
                float _RootHeight;
                float _DetailCount;
                float _DetailChance;
                float _DetailLength;
                float _SpikeSkew;
                float _TipFade;
                float _Seed;
                float _RetractStart;
                float _RetractEnd;
                float _RetractJitter;
                float _SrcBlend;
                float _DstBlend;
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
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Three stable random numbers for a spike slot.
            float3 Hash3(float slot, float salt)
            {
                float3 p = frac(float3(slot, slot + 17.13, slot + 31.71) * 0.1031 +
                    (_Seed + salt) * 0.0713);
                p += dot(p, p.yzx + 33.33);
                return frac((p.xxy + p.yzz) * p.zyx);
            }

            // How far one layer of spikes reaches past the band at a point round
            // the ring. Looks two slots either way, so a spike pushed well off
            // its slot's middle is never cut off at the slot's edge.
            float LayerReach(
                float around,
                float count,
                float chance,
                float longest,
                float salt)
            {
                count = max(round(count), 1.0);
                float cell = around * count;
                float slot = floor(cell);
                float reach = 0.0;

                [unroll]
                for (int offset = -2; offset <= 2; offset++)
                {
                    float neighbour = slot + offset;
                    // Wrap so the slots across the seam are the same spikes as
                    // the ones at the far end of the ring.
                    float wrapped = fmod(neighbour + count, count);
                    float3 a = Hash3(wrapped, salt);
                    float3 b = Hash3(wrapped, salt + 57.0);

                    // Missing spikes leave a gap in the ring's edge.
                    float present = step(a.x, chance);

                    // Most spikes short, a few long.
                    float size = max(pow(a.y, _LengthBias), 0.08);
                    float spikeLength = longest * size;

                    float centre = neighbour + 0.5 +
                        (a.z - 0.5) * 2.0 * _SpikePlaceJitter;
                    float halfWidth = 0.5 * _SpikeWidth *
                        lerp(0.6, 1.3, b.x) * lerp(0.75, 1.25, size);

                    float fromCentre = abs(cell - centre) / max(halfWidth, 1e-4);
                    float needle = pow(saturate(1.0 - fromCentre), _SpikeSharpness);
                    float root = _RootHeight *
                        pow(saturate(1.0 - fromCentre / _RootWidth), 2.0);

                    // Each spike retracts on its own schedule.
                    float start = _RetractStart + (b.y - 0.5) * 2.0 * _RetractJitter;
                    float end = start + max(_RetractEnd - _RetractStart, 1e-3);
                    float retract = 1.0 - smoothstep(start, end, _Progress);

                    reach = max(reach,
                        present * spikeLength * max(needle, root) * retract);
                }

                return reach;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);

                float outer = _Radius;
                // Swell, hold, collapse - in metres.
                float swell = smoothstep(0.0, _PeakAt, _Progress);
                float collapse = saturate(
                    (_Progress - _CollapseStart) / max(1.0 - _CollapseStart, 1e-3));
                float widthMetres = lerp(_WidthStart, _WidthPeak, swell);
                widthMetres = lerp(widthMetres, _WidthEnd, pow(collapse, _CollapseCurve));

                // The quad is scaled up as the ring spreads, so turn metres into
                // quad space by the object's current scale. A band wider than
                // the ring itself would just fill it in.
                float ringScale = max(length(float3(
                    UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)), 1e-4);
                float band = min(widthMetres / ringScale, outer);

                // Angle round the ring as 0..1.
                float around = atan2(p.y, p.x) / (2.0 * PI) + 0.5;

                // Lobes on the band's inner edge. Whole numbers of waves keep
                // them seamless where the angle wraps.
                float waves = max(round(_ScallopCount), 1.0);
                float lobes = 0.6 * sin(around * 2.0 * PI * waves + _Seed) +
                    0.4 * sin(around * 2.0 * PI * (waves * 2.0 + 1.0) + _Seed * 1.7);
                float inner = outer - band - _Scallop * (0.5 + 0.5 * lobes);

                // Swept further round the deeper into the ring a point lies,
                // which leans the spikes.
                float swept = frac(around + _SpikeSkew * max(inner - radius, 0.0));

                float reach = max(
                    LayerReach(swept, _SpikeCount, _SpikeChance, _SpikeLength, 0.0),
                    _DetailCount >= 1.0
                        ? LayerReach(swept, _DetailCount, _DetailChance, _DetailLength, 101.0)
                        : 0.0);

                // Signed distance to the shape: negative inside. The outer edge
                // is the band's rim; the inner edge dips in wherever a spike is.
                float edge = max(radius - outer, (inner - reach) - radius);
                float aa = max(fwidth(edge), 1e-4);
                float coverage = saturate(0.5 - edge / aa);

                // 0 on the band, 1 at a spike's tip.
                float intoSpike = saturate((inner - radius) / max(reach, 1e-4));
                float alpha = coverage * (1.0 - _TipFade * intoSpike);

                return half4(_Color.rgb, saturate(alpha * _Color.a * _Alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
