using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    /// <summary>Uploads simulation fields, then shades only the visible tray at display resolution.</summary>
    public sealed class SandTexture : IDisposable
    {
        public static readonly Color32[] Palette = { new Color32(216,167,64,255), new Color32(188,103,81,255), new Color32(85,142,136,255), new Color32(240,227,189,255), new Color32(105,110,152,255) };
        public static readonly Color32 TrayColor = new Color32(230, 220, 198, 255);
        public RenderTexture Texture { get; private set; }
        public int Resolution => Texture != null ? Texture.width : 0;
        public Rect View { get; private set; } = new Rect(0, 0, 1, 1);
        public bool NeedsRedraw { get; private set; } = true;
        public bool HighlightActive { get; private set; }
        readonly Texture2D colors, feedback, grains;
        readonly Material material;
        readonly Mesh rollingMesh;
        readonly List<Vector3> rollingVertices = new List<Vector3>(SandTray.MaxRollingGrains * 4);
        readonly List<Vector2> rollingUVs = new List<Vector2>(SandTray.MaxRollingGrains * 4);
        readonly List<Color32> rollingColors = new List<Color32>(SandTray.MaxRollingGrains * 4);
        readonly List<int> rollingTriangles = new List<int>(SandTray.MaxRollingGrains * 6);
        readonly Color32[] colorPixels = new Color32[SandTray.CellCount];
        readonly Color32[] feedbackPixels = new Color32[SandTray.CellCount];
        readonly Color32[] grainPixels = new Color32[SandTray.CellCount];
        readonly float[] glows;
        int highlightColor = -1;
        float highlightUntil = -100;
        bool disposed, surfaceReady;

        public SandTexture(int sectionCount)
        {
            var shader = Resources.Load<Shader>("MandalaSurface");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The Mandala surface shader is unavailable on this graphics device.");
            material = new Material(shader) { name = "Mandala surface renderer", hideFlags = HideFlags.HideAndDontSave };
            rollingMesh = new Mesh { name = "Rolling sand", hideFlags = HideFlags.HideAndDontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            rollingMesh.MarkDynamic();
            material.SetVector("_TrayColor", (Vector4)(Color)TrayColor);
            colors = CreateField("Sand color and height", false, FilterMode.Bilinear);
            feedback = CreateField("Section feedback", true, FilterMode.Bilinear);
            grains = CreateField("Airborne sand", false, FilterMode.Point);
            material.SetTexture("_FeedbackTex", feedback);
            material.SetTexture("_GrainsTex", grains);
            glows = new float[sectionCount];
        }

        static Texture2D CreateField(string name, bool linear, FilterMode filter) =>
            new Texture2D(SandTray.Size, SandTray.Size, TextureFormat.RGBA32, false, linear)
            { name = name, filterMode = filter, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        public bool SetViewport(float physicalWidth, Rect view, float physicalHeight = 0)
        {
            if (physicalHeight <= 0) physicalHeight = physicalWidth;
            // Cap both axes with the same scale so the artwork keeps its aspect.
            int limit = Mathf.Min(2048, SystemInfo.maxTextureSize);
            float scale = Mathf.Min(1, limit / Mathf.Max(1, Mathf.Max(physicalWidth, physicalHeight)));
            int width = Mathf.Clamp(Mathf.CeilToInt(physicalWidth * scale), 64, limit);
            int height = Mathf.Clamp(Mathf.CeilToInt(physicalHeight * scale), 64, limit);
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
                float lighting = 1 + Mathf.Clamp((right - left + below - above) * .020f, -.10f, .07f) - shadow * .55f;
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

        public void Render(SandTray tray, MandalaProgress progress, bool guides, float time, bool updateSurface = true)
        {
            if (Texture == null) return;
            if (updateSurface || !surfaceReady) UploadSurface(tray, progress, time);
            Array.Clear(grainPixels, 0, grainPixels.Length);
            foreach (var grain in tray.Grains)
            {
                // Choose one grain per ID pair; list compaction cannot change its visibility in flight.
                uint hash = (grain.Id >> 1) * 0x9e3779b9u;
                hash = (hash ^ (hash >> 16)) * 0x7feb352du;
                if ((grain.Id & 1) != (hash & 1)) continue;
                float fade = Mathf.Clamp01(grain.Age / .03f) * Mathf.Clamp01(grain.Lift / (1.5f * SandTray.GridScale));
                Color32 tint = Palette[tray.DisplayColor(new Vector2(grain.Cell % SandTray.Size + .5f, grain.Cell / SandTray.Size + .5f - grain.Lift), grain.Color)]; tint.a = (byte)Mathf.RoundToInt(153 * fade);
                int x = grain.Cell % SandTray.Size;
                float y = grain.Cell / SandTray.Size - grain.Lift;
                int row = Mathf.FloorToInt(y);
                float blend = y - row;
                PlotPourGrain(x, row, tint, 1 - blend);
                PlotPourGrain(x, row + 1, tint, blend);
            }
            foreach (var grain in tray.SuctionGrains)
            {
                float remaining = 1 - grain.Age / grain.Duration;
                Color32 tint = Palette[tray.DisplayColor(grain.Position, grain.Color)];
                tint.a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(remaining / .18f));
                Vector2 trail = Vector2.ClampMagnitude(grain.Previous - grain.Position, 2.5f * SandTray.GridScale);
                const int trailSteps = 2 * SandTray.GridScale;
                for (int tap = trailSteps; tap >= 0; tap--)
                {
                    float along = (float)tap / trailSteps;
                    Vector2 point = grain.Position + trail * along;
                    int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
                    if (!SandTray.Playable(x, y)) continue;
                    Color32 pixel = tint;
                    pixel.a = (byte)(pixel.a * (1 - along * .56f));
                    grainPixels[(SandTray.Size - 1 - y) * SandTray.Size + x] = pixel;
                }
            }
            grains.SetPixels32(grainPixels); grains.Apply(false, false);
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
                DrawRollingGrains(tray);
            }
            finally { RenderTexture.active = previous; }
            NeedsRedraw = false;
            HighlightActive = highlight > 0;
        }

        void DrawRollingGrains(SandTray tray)
        {
            if (tray.RollingGrains.Count == 0) return;
            rollingVertices.Clear(); rollingUVs.Clear(); rollingColors.Clear(); rollingTriangles.Clear();
            foreach (var grain in tray.RollingGrains)
            {
                // A small quad follows the actual fractional position. It is drawn
                // directly at display resolution, so the simulation grid cannot snap it.
                Vector2 uv = new Vector2(grain.Position.x, SandTray.Size - grain.Position.y) / SandTray.Size;
                float grainRadius = .28f / SandTray.Size;
                // Keep a subpixel grain rasterized between pixel centers, reducing
                // its opacity to preserve coverage instead of making it look larger.
                float radius = Mathf.Max(grainRadius, View.width / Texture.width);
                if (uv.x + radius < View.xMin || uv.x - radius > View.xMax || uv.y + radius < View.yMin || uv.y - radius > View.yMax) continue;
                int first = rollingVertices.Count;
                Color32 tint = Palette[tray.DisplayColor(grain.Position, grain.Color)];
                tint.a = (byte)Mathf.RoundToInt(255 * grainRadius * grainRadius / (radius * radius));
                Corner(-1, -1); Corner(1, -1); Corner(1, 1); Corner(-1, 1);
                rollingTriangles.Add(first); rollingTriangles.Add(first + 1); rollingTriangles.Add(first + 2);
                rollingTriangles.Add(first); rollingTriangles.Add(first + 2); rollingTriangles.Add(first + 3);

                void Corner(float x, float y)
                {
                    Vector2 position = uv + new Vector2(x, y) * radius;
                    rollingVertices.Add(new Vector3((position.x - View.x) / View.width * 2 - 1,
                        (position.y - View.y) / View.height * 2 - 1, 0));
                    rollingUVs.Add(new Vector2(x, y)); rollingColors.Add(tint);
                }
            }
            if (rollingVertices.Count == 0) return;
            rollingMesh.Clear();
            rollingMesh.SetVertices(rollingVertices); rollingMesh.SetUVs(0, rollingUVs);
            rollingMesh.SetColors(rollingColors); rollingMesh.SetTriangles(rollingTriangles, 0);
            Graphics.SetRenderTarget(Texture);
            if (material.SetPass(1)) Graphics.DrawMeshNow(rollingMesh, Matrix4x4.identity);
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
            DestroyResource(rollingMesh);
            DestroyResource(colors);
            DestroyResource(feedback);
            DestroyResource(grains);
        }

        static void DestroyResource(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
