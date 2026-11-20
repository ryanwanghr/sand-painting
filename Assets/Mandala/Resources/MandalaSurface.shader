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
                float _Guides, _GridSize;
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
                float2 grid = uv * _GridSize - .5;
                float2 cell = floor(grid), t = frac(grid), u = 1 - t;
                float2 w0 = u * u * u / 6;
                float2 w1 = (3 * t * t * t - 6 * t * t + 4) / 6;
                float2 w2 = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
                float2 w3 = t * t * t / 6;
                float2 a = w0 + w1, b = w2 + w3;
                float2 p0 = (cell - .5 + w1 / a) / _GridSize;
                float2 p1 = (cell + 1.5 + w3 / b) / _GridSize;
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
                float2 grid = center * _GridSize - .5;
                float2 cell = floor(grid), t = frac(grid), u = 1 - t;
                float2 w0 = u * u * u / 6;
                float2 w1 = (3 * t * t * t - 6 * t * t + 4) / 6;
                float2 w2 = (-3 * t * t * t + 3 * t * t + 3 * t + 1) / 6;
                float2 pick = float2(grainHash(seed + 137.1), grainHash(seed + 263.7));
                float2 offset = -1 + step(w0, pick) + step(w0 + w1, pick) + step(w0 + w1 + w2, pick);
                float2 materialUV = (cell + offset + .5) / _GridSize;
                float4 source = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, materialUV, 0);
                // Empty neighbors shape coverage separately; never introduce black grains
                // or holes into an opaque pile at its exposed contour.
                return source.a > 0 ? source.rgb : average;
            }

            float grainOccupancy(float2 uv)
            {
                // Average across the cell grid so its steps do not repeat along the rim.
                float2 a = float2(.94, .36) / _GridSize;
                float2 b = float2(-a.y, a.x);
                return .2 * (SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, uv, 0).b
                    + SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, uv + a, 0).b
                    + SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, uv - a, 0).b
                    + SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, uv + b, 0).b
                    + SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, uv - b, 0).b);
            }

            float3 denseGrains(float2 uv, float3 pigment, float contour, inout float edgeCoverage)
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
                float2 centerUV = grainCenter / 1320;
                if (contour < .96)
                {
                    // Resolve coverage at the grain center so no filled outline runs between the dots.
                    float occupancy = grainOccupancy(centerUV);
                    float chance = smoothstep(.13, .67, occupancy);
                    float pick = lerp(.08, .92, grainHash(grainSeed + 172.3));
                    float present = smoothstep(pick - .07, pick + .07, chance);
                    float sizeVariation = grainHash(grainSeed + 221.9);
                    float packed = smoothstep(.40, .83, occupancy + (sizeVariation - .5) * .10);
                    float radius = lerp(lerp(.20, .39, sizeVariation), 1.45, packed);
                    float grainAA = footprint * .5;
                    float grain = present * (1 - smoothstep(radius - grainAA, radius + grainAA, sqrt(nearest)));
                    float fringe = 1 - smoothstep(.82, .96, contour);
                    edgeCoverage = lerp(edgeCoverage, grain, detail * fringe);
                }
                float3 material = grainPigment(centerUV, grainSeed, pigment);
                // Unresolved grains converge to the filtered surface, limiting shimmer.
                return lerp(pigment, material, detail) * (1 + light * detail);
            }

            float4 sandGrains(float2 uv, float height, float3 pigment, float contour, inout float edgeCoverage)
            {
                float density = height + max(0, height - 1) * .5;
                float2 grainGrid = uv * 880;
                float2 grainCell = floor(grainGrid);
                float width = max(max(fwidth(grainGrid.x), fwidth(grainGrid.y)) * .5, .001);
                float detail = saturate(.5 / width - .6);
                float h = min(density, 7);
                float coverage = saturate(.05 + .27 * h - .018 * h * h);
                coverage *= saturate(.55 + density * .2) * saturate(height);
                // Densities of 2.5 and 4 correspond to two and three sand layers.
                float solid = smoothstep(2.5, 4, density);
                if (solid > 0)
                {
                    float denseEdge = edgeCoverage;
                    float3 densePigment = denseGrains(uv, pigment, contour, denseEdge);
                    pigment = lerp(pigment, densePigment, solid);
                    edgeCoverage = lerp(edgeCoverage, denseEdge, solid);
                }
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
                    float2 materialUV = (floor(center * (_GridSize / 880)) + .5) / _GridSize;
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

            void guide(inout float3 color, float2 uv, float distance, float empty, float colorIndex)
            {
                float2 gradient = float2(ddx(distance), ddy(distance));
                float2 uvGradient = ddx(uv) * gradient.x + ddy(uv) * gradient.y;
                float aa = max(abs(gradient.x) + abs(gradient.y), 0.00001);
                float fill = inside(distance);
                // The stroke remains one display pixel wide at every zoom level.
                float stroke = 1 - smoothstep(aa * .35, aa * 1.3, abs(distance));
                float highlight = abs(_HighlightColor - colorIndex) < .5 ? _Highlight : 0;
                if (stroke <= 0 && fill * highlight <= 0) return;

                // Read the section on the inside of this contour, including for
                // stroke pixels that lie in a gap or a different color's section.
                float2 probe = uv;
                if (stroke > 0)
                {
                    float2 inward = uvGradient / max(dot(gradient, gradient), 1e-12);
                    float inset = 1.25 / (_GridSize * max(length(inward), 1e-6));
                    probe -= inward * max(distance + inset, 0);
                }
                float2 cellUV = (floor(probe * _GridSize) + .5) / _GridSize;
                float owner = SAMPLE_TEXTURE2D_LOD(_FeedbackTex, sampler_FeedbackTex, cellUV, 0).r * 255;
                if (abs(owner - (colorIndex + 1)) > .5) return;
                color = lerp(color, ink(float3(.5, .5, .5)), fill * .32 * empty * highlight);
                color = lerp(color, ink(float3(.60, .56, .48)), stroke * .48);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = _View.xy + input.uv * _View.zw;
                float2 sandUV = (floor(uv * _GridSize) + .5) / _GridSize;
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
                float4 granular = sandGrains(uv, height, pigment, contour, edgeCoverage);
                granular.a *= edgeCoverage;
                float3 color = lerp(paper, granular.rgb, granular.a);

                float inner = max(.20 - r, r - (.49 + .13 * petal));
                float gold = min(r - .12, max(inner, (petal - .25) * .04));
                float terracotta = min(max(.12 - r, r - .17), max(inner, (.25 - petal) * .04));
                float lagoon = max(.65 - r, r - (.76 - .12 * petal));
                float ivory = max(.88 - r, r - .92);
                float indigo = max(max(.95 - r, r - 1), (-.25 - cos(angle * 24)) * .015);
                if (_Guides > .5)
                {
                    guide(color, uv, gold, 1 - occupied, 0);
                    guide(color, uv, terracotta, 1 - occupied, 1);
                    guide(color, uv, lagoon, 1 - occupied, 2);
                    guide(color, uv, ivory, 1 - occupied, 3);
                    guide(color, uv, indigo, 1 - occupied, 4);
                    color = lerp(color, ink(float3(1,.9725,.8667)), feedback.g * .23);
                }

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
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vertRolling
            #pragma fragment fragRolling
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct RollingAttributes { float4 position : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct RollingVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            RollingVaryings vertRolling(RollingAttributes input)
            {
                RollingVaryings output;
                output.positionCS = float4(input.position.xy, 0, 1);
                output.uv = input.uv; output.color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    output.color.rgb = FastSRGBToLinear(output.color.rgb);
                #endif
                return output;
            }
            half4 fragRolling(RollingVaryings input) : SV_Target
            {
                float distance = length(input.uv * float2(1, 1.12));
                float aa = max(fwidth(distance), .02);
                float alpha = 1 - smoothstep(.76 - aa * .5, .76 + aa * .5, distance);
                float light = 1 + dot(input.uv, float2(-.10, .10));
                return half4(input.color.rgb * light, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
