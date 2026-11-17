Shader "Mandala/Surface"
{
    Properties
    {
        _MainTex ("Sand color and height", 2D) = "white" {}
        _FeedbackTex ("Section feedback", 2D) = "black" {}
        _GrainsTex ("Airborne grains", 2D) = "black" {}
        _TrayColor ("Tray color", Vector) = (.9019608, .8627451, .7764706, 1)
    }
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FeedbackTex); SAMPLER(sampler_FeedbackTex);
            TEXTURE2D(_GrainsTex); SAMPLER(sampler_GrainsTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _View;
                float4 _TrayColor;
                float _Guides;
                float _HighlightColor, _Highlight;
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

            // Graphics.Blit supplies a quad for this offscreen UI texture.
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 ink(float3 srgb)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return srgb;
                #else
                    return FastSRGBToLinear(srgb);
                #endif
            }

            float inside(float distance)
            {
                float aa = max(fwidth(distance), 0.00001);
                return 1 - smoothstep(-aa * .5, aa * .5, distance);
            }

            float grainHash(float2 position)
            {
                float3 p = frac(position.xyx * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float sandCoverage(float2 uv, float height)
            {
                // Grow coverage gradually from sparse residue to a continuous five-layer deposit.
                float density = height + max(0, height - 1) * .5;
                // Grain positions stay anchored to the tray through panning, zooming, and redraws.
                float2 grainGrid = uv * 880;
                float2 grainCell = floor(grainGrid);
                float2 jitter = float2(grainHash(grainCell), grainHash(grainCell + 19.7)) - .5;
                float random = grainHash(grainCell + 73.9);
                float2 shape = lerp(float2(.83, 1.06), float2(1.06, .83), random);
                float distance = length((frac(grainGrid) - .5 - jitter * .32) / shape);
                float radius = (.26 + min(density, 7) * .07) * lerp(.88, 1.08, grainHash(grainCell + 5.3));
                float width = max(max(fwidth(grainGrid.x), fwidth(grainGrid.y)) * .5, .001);
                float grains = 1 - smoothstep(radius - width, radius + width, distance);
                grains *= smoothstep(random - .1, random + .1, saturate(.55 + density * .2));
                grains = lerp(grains, 1, smoothstep(4, 7, density));

                // Unresolved grains converge to coverage instead of sparkling or aliasing at fit-to-tray scale.
                float h = min(density, 7);
                float coverage = saturate(.05 + .27 * h - .018 * h * h);
                coverage *= saturate(.55 + density * .2);
                float variation = lerp(.75, 1.25, grainHash(floor(uv * 440)));
                coverage *= lerp(variation, 1, smoothstep(1, 4, density));
                float detail = saturate(.5 / width - .6);
                return saturate(lerp(coverage, grains, detail)) * step(.5, height);
            }

            void guide(inout float3 color, float distance, float fade, float empty, float colorIndex)
            {
                float aa = max(fwidth(distance), 0.00001);
                float fill = inside(distance);
                // The stroke remains one display pixel wide at every zoom level.
                float stroke = 1 - smoothstep(aa * .35, aa * 1.3, abs(distance));
                float highlight = abs(_HighlightColor - colorIndex) < .5 ? _Highlight : 0;
                color = lerp(color, ink(float3(.5, .5, .5)), fill * .32 * empty * fade * highlight);
                color = lerp(color, ink(float3(.60, .56, .48)), stroke * .48 * fade);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = _View.xy + input.uv * _View.zw;
                // Material state stays on the simulation grid; thin coverage resolves into grains when zoomed.
                float2 sandUV = (floor(uv * 440) + .5) / 440;
                float2 p = (uv - .5) / .43;
                float r = length(p), angle = atan2(p.y, p.x);
                float petal = cos(angle * 8);
                float4 sand = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sandUV);
                float4 feedback = SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, sandUV);
                float occupied = feedback.b;
                float height = sand.a * 24;
                float paperNoise = (grainHash(floor(uv * 440) + 47.1) - .5) * .009;
                float3 paper = ink(saturate(_TrayColor.rgb + paperNoise)) * (1 - feedback.a * .28);
                float3 color = lerp(paper, sand.rgb, sandCoverage(uv, height));

                float inner = max(.20 - r, r - (.49 + .13 * petal));
                float gold = min(r - .12, max(inner, (petal - .25) * .04));
                float terracotta = min(max(.12 - r, r - .17), max(inner, (.25 - petal) * .04));
                float lagoon = max(.65 - r, r - (.76 - .12 * petal));
                float ivory = max(.88 - r, r - .92);
                float indigo = max(max(.95 - r, r - 1), (-.25 - cos(angle * 24)) * .015);
                float fade = _Guides * feedback.r;
                guide(color, gold, fade, 1 - occupied, 0);
                guide(color, terracotta, fade, 1 - occupied, 1);
                guide(color, lagoon, fade, 1 - occupied, 2);
                guide(color, ivory, fade, 1 - occupied, 3);
                guide(color, indigo, fade, 1 - occupied, 4);
                color = lerp(color, ink(float3(1,.9725,.8667)), feedback.g * .23 * _Guides);

                float4 airborne = SAMPLE_TEXTURE2D(_GrainsTex, sampler_GrainsTex, sandUV);
                color = lerp(color, airborne.rgb, airborne.a);

                float radius = length(uv - .5);
                float rim = inside(.4932 - radius);
                color = lerp(color, ink(float3(.5451,.4549,.3137)), rim);
                float alpha = inside(radius - .5);
                return float4(saturate(color), alpha);
            }
            ENDHLSL
        }
    }
}
