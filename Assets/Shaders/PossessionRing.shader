Shader "Pitch/PossessionRing"
{
    Properties
    {
        _Color ("Ring Colour", Color) = (0.16, 0.62, 1, 0.92)
        _Inner ("Inner radius", Float) = 0.42
        _Outer ("Outer radius", Float) = 0.62
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Inner;
                float _Outer;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };
            V Vert(A input)
            {
                V output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }
            half4 Frag(V input) : SV_Target
            {
                float radius = length(input.positionOS.xz);
                float band = smoothstep(_Inner - 0.03, _Inner + 0.06, radius)
                           * (1.0 - smoothstep(_Outer - 0.08, _Outer + 0.03, radius));
                float pulse = 0.85 + 0.15 * sin(_Time.y * 3.2);
                return half4(_Color.rgb * (1.2 + pulse * 0.4), _Color.a * band * pulse);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
