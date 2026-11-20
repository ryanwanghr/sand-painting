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
                float _WaterAspect;
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

            float reflectedRipple(float2 samplePosition, float2 rimNormal, float2 direction, float frequency, float phase)
            {
                float incidence = dot(direction, rimNormal);
                float crest = sin(dot(samplePosition, direction) * frequency - phase);
                return crest * smoothstep(.20, .88, -incidence);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 p = (uv - .5) * float2(max(1, _WaterAspect), max(1, rcp(_WaterAspect)));
                // Low-contrast bands drift across the water.
                float bend = .066 * sin(5.5 * p.x + 3.8 * p.y) + .027 * sin(12 * p.x - 6 * p.y);
                float band = sin(19 * (p.y + .15 * p.x + bend) + _WaterTime * .13);
                band += .40 * sin(dot(p, float2(-7, 33)) + sin(7.4 * p.x + 5 * p.y) - _WaterTime * .15);
                float3 color = lerp(float3(.420, .628, .641), float3(.447, .663, .671),
                    smoothstep(-.24, -.18, band));
                color = lerp(color, float3(.512, .700, .705), smoothstep(.38, .46, band));

                // Reflect waves locally where they meet the rim, using the tray's pan and zoom.
                float2 tray = _TrayView.xy + uv * _TrayView.zw - .5;
                float radius = length(tray);
                float rimDistance = (radius - .5) / min(_TrayView.z, _TrayView.w);
                if (rimDistance >= 0 && rimDistance < .16)
                {
                    float2 rimNormal = tray / radius;
                    float2 reflectedPosition = p - 2 * rimDistance * rimNormal;
                    float warp = .75 * sin(dot(p, float2(19, 13)) - _WaterTime * .32);
                    float ripple = .65 * reflectedRipple(reflectedPosition, rimNormal, float2(.32, .95),
                        72, _WaterTime * 1.65 + warp);
                    ripple += .46 * reflectedRipple(reflectedPosition, rimNormal, float2(-.72, -.69),
                        106, _WaterTime * 2.19 - warp);
                    float glints = .5 + .5 * sin(dot(p, float2(24, -12)) + warp + _WaterTime * .27);
                    float fade = (1 - smoothstep(0, .16, rimDistance)) * lerp(.30, 1, glints);
                    // The bright side of each crest catches more light than the trough loses.
                    float light = ripple * lerp(.48, 1, smoothstep(-.10, .45, ripple));
                    color += light * fade * float3(.073, .093, .089);
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
