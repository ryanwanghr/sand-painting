using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    /// <summary>Uploads simulation fields and shades the visible tray.</summary>
    public sealed class SandTexture : IDisposable
    {
        public static readonly Color32[] Palette = { new Color32(216,167,64,255), new Color32(188,103,81,255), new Color32(85,142,136,255), new Color32(240,227,189,255), new Color32(105,110,152,255) };
        public static readonly Color32 TrayColor = new Color32(230, 220, 198, 255);
        public RenderTexture Texture { get; private set; }
        public int Resolution => Texture != null ? Texture.width : 0;
        public Rect View { get; private set; } = new Rect(0, 0, 1, 1);
        public bool NeedsRedraw { get; private set; } = true;
        public bool HighlightActive { get; private set; }
        readonly Texture2D colors, feedback, grains, grainDetail;
        readonly Material material;
        readonly bool mobileSurface;
        const int MaxMovingGrains = SandTray.MaxRollingGrains + SandTray.MaxSuctionGrains;
        const float MaxSuctionTrail = 1.4f * SandTray.GridScale;
        readonly Mesh movingMesh;
        readonly List<Vector3> movingVertices = new List<Vector3>(MaxMovingGrains * 4);
        readonly List<Vector2> movingUVs = new List<Vector2>(MaxMovingGrains * 4);
        readonly List<Color32> movingColors = new List<Color32>(MaxMovingGrains * 4);
        readonly List<int> movingTriangles = new List<int>(MaxMovingGrains * 6);
        readonly Color32[] colorPixels = new Color32[SandTray.CellCount];
        readonly Color32[] feedbackPixels = new Color32[SandTray.CellCount];
        readonly Color32[] grainPixels = new Color32[SandTray.CellCount];
        readonly float[] glows;
        int highlightColor = -1;
        float highlightUntil = -100;
        bool disposed, surfaceReady, hadTrayGrains;

        public SandTexture(int sectionCount, bool mobileSurface = false)
        {
            this.mobileSurface = mobileSurface;
            var shader = Resources.Load<Shader>("MandalaSurface");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The Mandala surface shader is unavailable on this graphics device.");
            material = new Material(shader) { name = "Mandala surface renderer", hideFlags = HideFlags.HideAndDontSave };
            movingMesh = new Mesh { name = "Moving sand", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            movingMesh.MarkDynamic();
            material.SetVector("_TrayColor", (Vector4)(Color)TrayColor);
            colors = CreateField("Sand color and height", false, FilterMode.Bilinear);
            feedback = CreateField("Section feedback", true, FilterMode.Bilinear);
            grains = CreateField("Airborne sand", false, FilterMode.Point);
            grains.SetPixels32(grainPixels); grains.Apply(false, false);
            material.SetTexture("_FeedbackTex", feedback);
            material.SetTexture("_GrainsTex", grains);
            if (mobileSurface)
            {
                grainDetail = CreateGrainDetail();
                material.SetTexture("_GrainDetail", grainDetail);
                material.EnableKeyword("MANDALA_MOBILE_SURFACE");
            }
            glows = new float[sectionCount];
        }

        static Texture2D CreateField(string name, bool linear, FilterMode filter) =>
            new Texture2D(SandTray.Size, SandTray.Size, TextureFormat.RGBA32, false, linear)
            { name = name, filterMode = filter, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        static Texture2D CreateGrainDetail()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Mandala mobile grain detail", filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(41);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte tone = (byte)random.Next(256);
                pixels[i] = new Color32(tone, tone, tone, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        public bool SetViewport(float physicalWidth, Rect view, float physicalHeight = 0)
        {
            if (physicalHeight <= 0) physicalHeight = physicalWidth;
            // Cap both axes with the same scale so the artwork keeps its aspect.
            int limit = Mathf.Min(2048, SystemInfo.maxTextureSize);
            float scale = Mathf.Min(1, limit / Mathf.Max(1, Mathf.Max(physicalWidth, physicalHeight)));
            int minimum = 64;
            if (mobileSurface)
            {
                scale *= .5f;
                minimum /= 2;
            }
            int width = Mathf.Clamp(Mathf.CeilToInt(physicalWidth * scale), minimum, limit);
            int height = Mathf.Clamp(Mathf.CeilToInt(physicalHeight * scale), minimum, limit);
            bool resized = Texture == null || Texture.width != width || Texture.height != height || !Texture.IsCreated();
            if (resized)
            {
                if (Texture != null) { Texture.Release(); DestroyResource(Texture); }
                Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Mandala visible surface", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
                Texture.Create();
            }
            NeedsRedraw |= resized || View != view;
            View = view;
            return resized;
        }

        public void Highlight(int color, float time)
        {
            highlightColor = color;
            highlightUntil = time + 1.2f;
            HighlightActive = true;
            NeedsRedraw = true;
        }

        void UploadSurface(SandTray tray, MandalaProgress progress, float time)
        {
            for (int s = 0; s < progress.Sections.Count; s++)
            {
                var section = progress.Sections[s];
                float elapsed = time - section.FinishedAt;
                glows[s] = elapsed >= 0 && elapsed < .7f ? Mathf.Sin(elapsed / .7f * Mathf.PI) : 0;
            }
            const int spacing = SandTray.GridScale, row = spacing * SandTray.Size, diagonal = row + spacing;
            const int slopeSpan = 4, slopeSpacing = slopeSpan * spacing, slopeOffset = slopeSpan * diagonal;
            for (int y = 0; y < SandTray.Size; y++)
            for (int x = 0; x < SandTray.Size; x++)
            {
                int i = y * SandTray.Size + x, output = (SandTray.Size - 1 - y) * SandTray.Size + x;
                int region = progress.RegionAt[i];
                byte guide = region >= 0 && !progress.Sections[region].Complete ? (byte)(tray.Guide[i] + 1) : (byte)0;
                byte glow = region >= 0 ? (byte)Mathf.RoundToInt(glows[region] * 255) : (byte)0;
                int height = tray.Heights[i];
                if (height == 0)
                {
                    // Empty RGB stays zero so filtered pigment can be normalized by occupancy.
                    colorPixels[output] = default;
                    feedbackPixels[output] = new Color32(guide, glow, 0, 0);
                    continue;
                }
                int top = tray.DisplayTop(i), target = tray.Guide[i];
                Color color = Palette[top];
                int left = x >= spacing ? tray.Heights[i - spacing] : height;
                int right = x < SandTray.Size - spacing ? tray.Heights[i + spacing] : height;
                int above = y >= spacing ? tray.Heights[i - row] : height;
                int below = y < SandTray.Size - spacing ? tray.Heights[i + row] : height;
                // A cleared neighbor is an exposed contour, not a slope across the sand surface.
                // Keep relief between occupied cells without outlining piles with a heavy bevel.
                if (left == 0) left = height;
                if (right == 0) right = height;
                if (above == 0) above = height;
                if (below == 0) below = height;
                // Upper-left light: slopes facing the source brighten; raised edges shade their lower neighbors.
                float horizon = Mathf.Max(above, left) - height - 1;
                if (x >= spacing && y >= spacing) horizon = Mathf.Max(horizon, tray.Heights[i - diagonal] - height - 1);
                if (x >= 2 * spacing && y >= 2 * spacing) horizon = Mathf.Max(horizon, tray.Heights[i - 2 * diagonal] - height - 4);
                if (x >= 3 * spacing && y >= 3 * spacing) horizon = Mathf.Max(horizon, tray.Heights[i - 3 * diagonal] - height - 7);
                float shadow = Mathf.Clamp(horizon * .018f, 0, .28f);

                // Measure slope along the light over a wider span to reveal the sides of a mound.
                int towardLight = x >= slopeSpacing && y >= slopeSpacing ? tray.Heights[i - slopeOffset] : height;
                int awayFromLight = x < SandTray.Size - slopeSpacing && y < SandTray.Size - slopeSpacing
                    ? tray.Heights[i + slopeOffset] : height;
                if (towardLight == 0) towardLight = height;
                if (awayFromLight == 0) awayFromLight = height;
                float broadSlope = (awayFromLight - towardLight) / (float)slopeSpan;
                float fineSlope = right - left + below - above;
                float lighting = 1 + Mathf.Clamp(broadSlope * .047f + fineSlope * .020f, -.24f, .18f) - shadow * .60f;
                // Preserve the hue of light pigments by scaling all three channels together.
                lighting = Mathf.Min(lighting, 1f / Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
                color.r *= lighting; color.g *= lighting; color.b *= lighting;
                if (tray.Protection && top != target)
                    color = Color.Lerp(color, new Color32(124,82,111,255), .22f);
                Color32 pixel = color;
                pixel.a = (byte)Mathf.RoundToInt(height * (255f / SandTray.Levels));
                colorPixels[output] = pixel;
                feedbackPixels[output] = new Color32(guide, glow, 255, (byte)Mathf.RoundToInt(shadow / .28f * 255));
            }
            colors.SetPixels32(colorPixels); colors.Apply(false, false);
            feedback.SetPixels32(feedbackPixels); feedback.Apply(false, false);
            surfaceReady = true;
        }

        void UploadPourGrains(SandTray tray)
        {
            bool hasTrayGrains = false;
            foreach (var grain in tray.Grains)
            {
                if (grain.Outlet.HasValue) continue; // The tray overlay draws grains from the handheld pourer.
                byte opacity = PourGrainOpacity(grain);
                if (opacity == 0) continue;
                if (!hasTrayGrains)
                {
                    Array.Clear(grainPixels, 0, grainPixels.Length);
                    hasTrayGrains = true;
                }
                Color32 tint = Palette[tray.DisplayColor(grain.Position, grain.Color)];
                tint.a = opacity;
                int x = grain.Cell % SandTray.Size;
                float y = grain.Cell / SandTray.Size - grain.Lift;
                int row = Mathf.FloorToInt(y);
                float blend = y - row;
                PlotPourGrain(x, row, tint, 1 - blend);
                PlotPourGrain(x, row + 1, tint, blend);
            }
            if (!hasTrayGrains)
            {
                if (!hadTrayGrains) return;
                Array.Clear(grainPixels, 0, grainPixels.Length);
            }
            grains.SetPixels32(grainPixels); grains.Apply(false, false);
            hadTrayGrains = hasTrayGrains;
        }

        public void Render(SandTray tray, MandalaProgress progress, bool guides, float time,
            bool updateSurface = true, float suctionInterpolation = 1)
        {
            if (Texture == null) return;
            if (updateSurface || !surfaceReady) UploadSurface(tray, progress, time);
            UploadPourGrains(tray);
            material.SetFloat("_GridSize", SandTray.Size);
            material.SetVector("_View", new Vector4(View.x, View.y, View.width, View.height));
            material.SetFloat("_Guides", guides ? 1 : 0);
            float highlight = Mathf.Clamp01((highlightUntil - time) / .6f);
            material.SetFloat("_HighlightColor", highlightColor);
            material.SetFloat("_Highlight", highlight);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(colors, Texture, material, 0);
                DrawMovingGrains(tray, suctionInterpolation);
            }
            finally { RenderTexture.active = previous; }
            NeedsRedraw = false;
            HighlightActive = highlight > 0;
        }

        internal static byte PourGrainOpacity(SandTray.Grain grain)
        {
            // Choose one grain per ID pair so a landing cannot change which grains are visible.
            uint hash = (grain.Id >> 1) * 0x9e3779b9u;
            hash = (hash ^ (hash >> 16)) * 0x7feb352du;
            if ((grain.Id & 1) != (hash & 1)) return 0;
            float fade = Mathf.Clamp01(grain.Age / .03f) * Mathf.Clamp01(grain.Lift / (1.5f * SandTray.GridScale));
            return (byte)Mathf.RoundToInt(153 * fade);
        }

        void DrawMovingGrains(SandTray tray, float suctionInterpolation)
        {
            if (tray.RollingGrains.Count == 0 && tray.SuctionGrains.Count == 0) return;
            movingVertices.Clear(); movingUVs.Clear(); movingColors.Clear(); movingTriangles.Clear();
            float pixelSize = Mathf.Max(View.width / Texture.width, View.height / Texture.height);
            foreach (var grain in tray.RollingGrains)
                AddMovingGrain(grain.Position, Vector2.zero, .28f, pixelSize, Palette[tray.DisplayColor(grain.Position, grain.Color)]);
            foreach (var grain in tray.SuctionGrains)
            {
                Vector2 position = Vector2.Lerp(grain.Previous, grain.Position, suctionInterpolation);
                float remaining = (1 - grain.Age / grain.Duration) / .22f;
                float fade = Mathf.SmoothStep(0, 1, remaining);
                Vector2 trail = Vector2.ClampMagnitude(grain.Previous - grain.Position, MaxSuctionTrail) * fade;
                Color32 tint = Palette[tray.DisplayColor(position, grain.Color)];
                tint.a = (byte)Mathf.RoundToInt(218 * fade);
                AddMovingGrain(position, trail, Mathf.Lerp(.16f, .38f, fade), pixelSize, tint);
            }
            if (movingVertices.Count == 0) return;
            movingMesh.Clear();
            movingMesh.SetVertices(movingVertices); movingMesh.SetUVs(0, movingUVs);
            movingMesh.SetColors(movingColors); movingMesh.SetTriangles(movingTriangles, 0);
            Graphics.SetRenderTarget(Texture);
            if (material.SetPass(1)) Graphics.DrawMeshNow(movingMesh, Matrix4x4.identity);
        }

        void AddMovingGrain(Vector2 position, Vector2 trail, float grainRadius, float pixelSize, Color32 tint)
        {
            Vector2 tail = new Vector2(trail.x, -trail.y) / SandTray.Size;
            Vector2 center = new Vector2(position.x, SandTray.Size - position.y) / SandTray.Size + tail * .5f;
            float trailLength = tail.magnitude;
            Vector2 direction = trailLength > 0 ? tail / trailLength : Vector2.up;
            grainRadius /= SandTray.Size;
            // Give subpixel grains enough coverage to rasterize, retaining their apparent area.
            float radius = Mathf.Max(grainRadius, pixelSize);
            float stretch = trailLength * .65f;
            Vector2 along = direction * (radius + stretch);
            Vector2 across = new Vector2(direction.y, -direction.x) * radius;
            Vector2 extent = new Vector2(Mathf.Abs(across.x) + Mathf.Abs(along.x), Mathf.Abs(across.y) + Mathf.Abs(along.y));
            if (center.x + extent.x < View.xMin || center.x - extent.x > View.xMax ||
                center.y + extent.y < View.yMin || center.y - extent.y > View.yMax) return;
            tint.a = (byte)Mathf.RoundToInt(tint.a * (grainRadius / radius) * ((grainRadius + stretch) / (radius + stretch)));
            if (tint.a == 0) return;
            Color32 tailTint = tint;
            tailTint.a = (byte)Mathf.RoundToInt(tint.a * (1 - .55f * trailLength * SandTray.Size / MaxSuctionTrail));
            int first = movingVertices.Count;
            Corner(-1, -1, tint); Corner(1, -1, tint); Corner(1, 1, tailTint); Corner(-1, 1, tailTint);
            movingTriangles.Add(first); movingTriangles.Add(first + 1); movingTriangles.Add(first + 2);
            movingTriangles.Add(first); movingTriangles.Add(first + 2); movingTriangles.Add(first + 3);

            void Corner(float x, float y, Color32 color)
            {
                Vector2 corner = center + across * x + along * y;
                movingVertices.Add(new Vector3((corner.x - View.x) / View.width * 2 - 1,
                    (corner.y - View.y) / View.height * 2 - 1, 0));
                movingUVs.Add(new Vector2(x, y)); movingColors.Add(color);
            }
        }

        void PlotPourGrain(int x, int y, Color32 tint, float weight)
        {
            if (y < 0 || y >= SandTray.Size) return;
            int output = (SandTray.Size - 1 - y) * SandTray.Size + x;
            tint.a = (byte)Mathf.RoundToInt(tint.a * weight);
            // Overlap stays translucent, and a fading grain cannot erase a stronger one.
            if (tint.a > grainPixels[output].a) grainPixels[output] = tint;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Texture != null) { Texture.Release(); DestroyResource(Texture); }
            DestroyResource(material);
            DestroyResource(movingMesh);
            DestroyResource(colors);
            DestroyResource(feedback);
            DestroyResource(grains);
            DestroyResource(grainDetail);
        }

        static void DestroyResource(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
