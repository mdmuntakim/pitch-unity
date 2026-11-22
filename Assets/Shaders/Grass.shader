Shader "Pitch/Grass"
{
    Properties
    {
        _BaseMap ("Grass Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (0.16, 0.42, 0.2, 1)
        _BumpMap ("Grass Normal", 2D) = "bump" {}
        _StripeScale ("Mowing stripe scale", Float) = 1.5
        _WearColor ("Wear colour", Color) = (0.34, 0.31, 0.2, 1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.18
        _DetailStrength ("Detail strength", Range(0,1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _WearColor;
                float _StripeScale;
                float _Smoothness;
                float _DetailStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogCoord : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                output.shadowCoord = TransformWorldToShadowCoord(positions.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 world = input.positionWS.xz;
                // Mowing stripes across the pitch.
                float stripe = step(0.5, frac(world.x * _StripeScale * 0.055));
                float3 baseColour = _BaseColor.rgb * lerp(0.82, 1.12, stripe);

                // Layered detail texture, stretched, plus a fine grain.
                float3 detail = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv * 17.0).g;
                baseColour *= lerp(1.0, detail * 1.25, _DetailStrength);
                baseColour *= lerp(1.0, 0.82 + grain * 0.36, 0.35);

                // Wear near both goals and in the goalmouths.
                float2 goalA = float2(0.0, -52.5);
                float2 goalB = float2(0.0, 52.5);
                float wear = saturate(1.0 - min(distance(world, goalA), distance(world, goalB)) / 22.0);
                wear *= wear;
                baseColour = lerp(baseColour, _WearColor.rgb, wear * 0.55);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = baseColour;
                surfaceData.metallic = 0.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;
                surfaceData.normalTS = float3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(inputData.normalWS);

                half4 colour = UniversalFragmentPBR(inputData, surfaceData);
                colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V ShadowVert(A input)
            {
                V output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = ApplyShadowClamping(
                    TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection)));
                return output;
            }
            half4 ShadowFrag(V input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
