Shader "Pitch/Net"
{
    Properties
    {
        _Color ("Net Colour", Color) = (1, 1, 1, 0.35)
        _Grid ("Grid", Float) = 40
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Grid;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V Vert(A input) { V o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 Frag(V input) : SV_Target
            {
                float2 grid = abs(frac(input.uv * _Grid) - 0.5);
                float line = 1.0 - smoothstep(0.02, 0.08, min(grid.x, grid.y));
                return half4(_Color.rgb, _Color.a * line);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
