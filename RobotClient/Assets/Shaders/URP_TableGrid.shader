Shader "Custom/URP_TableGrid"
{
    Properties
    {
        [HDR] _GridColor("Grid Color", Color) = (0.23, 0.51, 0.96, 1)
        _GridSpacing("Grid Spacing", Float) = 0.5
        _GridThickness("Grid Thickness", Float) = 0.05
        _GlobalFadeRadius("Fade Radius", Float) = 3.0
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float _GridSpacing;
                float _GridThickness;
                float _GlobalFadeRadius;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // World space grid on XZ plane
                float2 coord = input.positionWS.xz / _GridSpacing;
                
                // Pure math grid (fwidth ensures anti-aliasing based on camera distance!)
                float2 grid = abs(frac(coord - 0.5) - 0.5) / fwidth(coord);
                float lineVal = min(grid.x, grid.y);
                
                // Anti-aliased line thickness filtering
                float intensity = 1.0 - smoothstep(0.0, _GridThickness * 0.5, lineVal);
                
                // Cool Sci-Fi Circular fade starting from World Center (0,0,0)
                float dist = length(input.positionWS.xz);
                float alphaFade = smoothstep(_GlobalFadeRadius, 0.0, dist); 

                half4 finalColor = _GridColor;
                // Add a very subtle base floor glow along with the grid lines
                finalColor.a = (intensity + 0.05) * _GridColor.a * alphaFade;
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}
