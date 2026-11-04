Shader "Effect/Afterimage"
{
    // A copy of the character left behind by a dash: a faint fill with a
    // bright rim, so the silhouette reads as light rather than as a second
    // body. AfterimageEmitter fades each copy through _Alpha.
    Properties
    {
        [HDR] _Color ("Fill Color", Color) = (0.6, 1.2, 2.4, 1)
        [HDR] _RimColor ("Rim Color", Color) = (2, 3.5, 6, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        // How much of the body is filled in away from the rim.
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.2
        // How tight the rim hugs the silhouette; higher is thinner.
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
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
            Name "AfterimageUnlit"

            // Additive like the other effects. Back faces are culled so a
            // copy shows one layer of its body, not its inside as well.
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RimColor;
                float _Alpha;
                float _FillAlpha;
                float _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(1.0 - saturate(dot(normal, view)), _RimPower);

                float3 color = lerp(_Color.rgb, _RimColor.rgb, rim);
                float alpha = lerp(_FillAlpha, 1.0, rim) * _Color.a * _Alpha;

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
