Shader "Pitch/Crowd"
{
    Properties
    {
        _Ambient ("Ambient", Range(0,1)) = 0.55
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Ambient;
            CBUFFER_END

            // Per-instance shirt colour. The name must not collide with the
            // material property above, otherwise HLSL sees two globals called
            // _Color and the shader fails to compile.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float fogCoord : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V Vert(A input)
            {
                V output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return output;
            }
            half4 Frag(V input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 colour = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceColor) * _Color;
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normalize(input.normalWS), mainLight.direction)) * 0.55 + _Ambient;
                colour.rgb *= ndl * mainLight.color;
                colour.rgb = MixFog(colour.rgb, input.fogCoord);
                return colour;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
