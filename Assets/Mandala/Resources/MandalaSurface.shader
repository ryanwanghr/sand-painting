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

            void sampleSurface(float2 uv, out float4 sand, out float4 feedback)
            {
                // Reconstruct a smooth contour from cell centers. Cubic B-spline weights
                // stay positive, so color boundaries cannot ring or reveal pale seams.
                // Pairing weights uses four bilinear taps instead of sixteen point taps.
                float2 grid = uv * 440 - .5;
                float2 cell = floor(grid), t = frac(grid), u = 1 - t;
                float2 w0 = u * u * u / 6;
                float2 w1 = (3 * t * t * t - 6 * t * t + 4) / 6;
                float2 w2 = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
                float2 w3 = t * t * t / 6;
                float2 a = w0 + w1, b = w2 + w3;
                float2 p0 = (cell - .5 + w1 / a) / 440;
                float2 p1 = (cell + 1.5 + w3 / b) / 440;
                sand = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p0) * a.x * a.y
                     + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(p1.x, p0.y)) * b.x * a.y
                     + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(p0.x, p1.y)) * a.x * b.y
                     + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p1) * b.x * b.y;
                feedback = SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, p0) * a.x * a.y
                         + SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, float2(p1.x, p0.y)) * b.x * a.y
                         + SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, float2(p0.x, p1.y)) * a.x * b.y
                         + SAMPLE_TEXTURE2D(_FeedbackTex, sampler_FeedbackTex, p1) * b.x * b.y;
            }

            float3 grainPigment(float2 center, float2 seed, float3 average)
            {
                // Draw one cell from the same positive B-spline distribution used for
                // the smooth surface. Each grain keeps one material instead of a blend.
                float2 grid = center * 440 - .5;
                float2 cell = floor(grid), t = frac(grid), u = 1 - t;
                float2 w0 = u * u * u / 6;
                float2 w1 = (3 * t * t * t - 6 * t * t + 4) / 6;
                float2 w2 = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
                float2 pick = float2(grainHash(seed + 137.1), grainHash(seed + 263.7));
                float2 offset = -1 + step(w0, pick) + step(w0 + w1, pick) + step(w0 + w1 + w2, pick);
                float2 materialUV = (cell + offset + .5) / 440;
                float4 source = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, materialUV, 0);
                // Empty neighbors shape coverage separately; never introduce black grains
                // or holes into an opaque pile at its exposed contour.
                return source.a > 0 ? source.rgb : average;
            }

            float3 denseGrains(float2 uv, float3 pigment)
            {
                float2 grid = uv * 1320;
                float footprint = max(max(fwidth(grid.x), fwidth(grid.y)), .001);
                float detail = 1 - smoothstep(.6, 1.6, footprint);
                if (detail <= 0) return pigment;

                float2 cell = floor(grid);
                float nearest = 8;
                float light = 0;
                float2 grainCenter = grid, grainSeed = cell;
                // Irregular touching grain facets: surface texture without holes in dense sand.
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 seed = cell + float2(x, y);
                    float2 center = seed + .5 + (float2(grainHash(seed + 7.3), grainHash(seed + 29.1)) - .5) * .8;
                    float2 delta = grid - center;
                    float distance = dot(delta, delta);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        grainCenter = center;
                        grainSeed = seed;
                        light = (grainHash(seed + 91.7) - .5) * .08 + dot(delta, float2(-.025, .025));
                    }
                }
                float3 material = grainPigment(grainCenter / 1320, grainSeed, pigment);
                // Unresolved grains converge to the filtered surface, limiting shimmer.
                return lerp(pigment, material, detail) * (1 + light * detail);
            }

            float4 sandGrains(float2 uv, float height, float3 pigment)
            {
                float density = height + max(0, height - 1) * .5;
                float2 grainGrid = uv * 880;
                float2 grainCell = floor(grainGrid);
                float width = max(max(fwidth(grainGrid.x), fwidth(grainGrid.y)) * .5, .001);
                float detail = saturate(.5 / width - .6);
                float h = min(density, 7);
                float coverage = saturate(.05 + .27 * h - .018 * h * h);
                coverage *= saturate(.55 + density * .2) * saturate(height);
                float solid = smoothstep(4, 7, density);
                if (solid > 0) pigment = lerp(pigment, denseGrains(uv, pigment), solid);
                if (detail <= 0 || solid >= 1) return float4(pigment, lerp(coverage, 1, solid));

                float grains = 0;
                float3 grainColor = pigment;
                // Search neighboring cells: freely displaced grains may cross their seed-cell border.
                // Each whole grain reads its material at its own center, avoiding square cell cutouts.
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 seed = grainCell + float2(x, y);
                    float2 center = seed + .5 + (float2(grainHash(seed), grainHash(seed + 19.7)) - .5) * .96;
                    float2 materialUV = (floor(center * .5) + .5) / 440;
                    float4 source = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, materialUV, 0);
                    float sourceHeight = source.a * 24;
                    float sourceDensity = sourceHeight + max(0, sourceHeight - 1) * .5;
                    float random = grainHash(seed + 73.9);
                    float angle = grainHash(seed + 31.3) * 6.2831853;
                    float sine, cosine;
                    sincos(angle, sine, cosine);
                    float2 delta = grainGrid - center;
                    delta = float2(delta.x * cosine - delta.y * sine, delta.x * sine + delta.y * cosine);
                    float2 shape = float2(lerp(.72, 1.06, random), lerp(1.08, .80, random));
                    float distance = length(delta / shape);
                    float radius = (.26 + min(sourceDensity, 7) * .07) * lerp(.76, 1.14, grainHash(seed + 5.3));
                    float mask = 1 - smoothstep(radius - width, radius + width, distance);
                    mask *= smoothstep(random - .1, random + .1, saturate(.55 + sourceDensity * .2)) * step(.5, sourceHeight);
                    if (mask > grains) { grains = mask; grainColor = source.rgb; }
                }
                return float4(lerp(pigment, grainColor, detail * (1 - solid)),
                    lerp(lerp(coverage, grains, detail), 1, solid));
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
                float2 sandUV = (floor(uv * 440) + .5) / 440;
                float2 p = (uv - .5) / .43;
                float r = length(p), angle = atan2(p.y, p.x);
                float petal = cos(angle * 8);
                float4 sand, feedback;
                sampleSurface(uv, sand, feedback);
                float occupied = feedback.b;
                // Height describes occupied sand only. Empty neighbors reduce contour
                // coverage, never the deposit's density or its choice of grain rendering.
                float height = sand.a * 24 / max(occupied, .001);
                // Keep the reconstructed outline crisp at display resolution. A small,
                // continuous grain-scale offset breaks up the contour without cell-shaped cuts.
                float2 edgeGrid = uv * 1320, edgeCell = floor(edgeGrid), edgeT = frac(edgeGrid);
                edgeT = edgeT * edgeT * (3 - 2 * edgeT);
                float edgeNoise = lerp(lerp(grainHash(edgeCell + 11.7), grainHash(edgeCell + float2(1, 0) + 11.7), edgeT.x),
                    lerp(grainHash(edgeCell + float2(0, 1) + 11.7), grainHash(edgeCell + 1 + 11.7), edgeT.x), edgeT.y) - .5;
                float edgeDetail = 1 - smoothstep(.6, 1.6, max(fwidth(edgeGrid.x), fwidth(edgeGrid.y)));
                float contour = occupied + edgeNoise * .06 * edgeDetail;
                float edgeAA = max(fwidth(contour) * .65, .025);
                float edgeCoverage = smoothstep(.5 - edgeAA, .5 + edgeAA, contour);
                float paperNoise = (grainHash(floor(uv * 1777) + 47.1) - .5) * .009;
                // Cleared tray stays clean; height-based shading is already in the sand pigment.
                float3 paper = ink(saturate(_TrayColor.rgb + paperNoise));
                float3 pigment = sand.rgb / max(occupied, .001);
                float4 granular = sandGrains(uv, height, pigment);
                granular.a *= edgeCoverage;
                float3 color = lerp(paper, granular.rgb, granular.a);

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
