Shader "Mandala/Calm Water"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TrayView;
                float _WaterScale;
                float _WaterTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 tray = _TrayView.xy + input.uv * _TrayView.zw - .5;
                float2 p = tray * _WaterScale;
                // Low-contrast bands drift across the water.
                float bend = .066 * sin(5.5 * p.x + 3.8 * p.y) + .027 * sin(12 * p.x - 6 * p.y);
                float band = sin(19 * (p.y + .15 * p.x + bend) + _WaterTime * .13);
                band += .40 * sin(dot(p, float2(-7, 33)) + sin(7.4 * p.x + 5 * p.y) - _WaterTime * .15);
                float3 color = lerp(float3(.420, .628, .641), float3(.447, .663, .671),
                    smoothstep(-.24, -.18, band));
                color = lerp(color, float3(.512, .700, .705), smoothstep(.38, .46, band));

                // A faint ripple follows the rim and dissolves into the water.
                float radius = length(tray);
                float rimDistance = (radius - .5) * _WaterScale;
                if (rimDistance >= 0 && rimDistance < .10)
                {
                    float2 rimNormal = tray / radius;
                    float phase = 75 * rimDistance + dot(rimNormal, float2(.9, -.6)) - _WaterTime * 1.1;
                    float fade = 1 - smoothstep(0, .10, rimDistance);
                    color += sin(phase) * fade * float3(.012, .020, .020);
                }

                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color = SRGBToLinear(color);
                #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
