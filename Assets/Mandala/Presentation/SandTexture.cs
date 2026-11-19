using System;
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
        readonly Color32[] colorPixels = new Color32[SandTray.CellCount];
        readonly Color32[] feedbackPixels = new Color32[SandTray.CellCount];
        readonly Color32[] grainPixels = new Color32[SandTray.CellCount];
        readonly float[] fades, glows;
        int highlightColor = -1;
        float highlightUntil = -100;
        bool disposed, surfaceReady;

        public SandTexture(int sectionCount)
        {
            var shader = Resources.Load<Shader>("MandalaSurface");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The Mandala surface shader is unavailable on this graphics device.");
            material = new Material(shader) { name = "Mandala surface renderer", hideFlags = HideFlags.HideAndDontSave };
            material.SetVector("_TrayColor", (Vector4)(Color)TrayColor);
            colors = CreateField("Sand color and height", false, FilterMode.Bilinear);
            feedback = CreateField("Section feedback", true, FilterMode.Bilinear);
            grains = CreateField("Airborne sand", false, FilterMode.Point);
            material.SetTexture("_FeedbackTex", feedback);
            material.SetTexture("_GrainsTex", grains);
            fades = new float[sectionCount]; glows = new float[sectionCount];
        }

        static Texture2D CreateField(string name, bool linear, FilterMode filter) =>
            new Texture2D(SandTray.Size, SandTray.Size, TextureFormat.RGBA32, false, linear)
            { name = name, filterMode = filter, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };

        public bool SetViewport(float physicalPixels, Rect view)
        {
            // Bound memory on unusually large displays; zoom never multiplies this allocation.
            int resolution = Mathf.Clamp(Mathf.CeilToInt(physicalPixels), 64, Mathf.Min(2048, SystemInfo.maxTextureSize));
            bool resized = Texture == null || Texture.width != resolution || !Texture.IsCreated();
            if (resized)
            {
                if (Texture != null) { Texture.Release(); DestroyResource(Texture); }
                Texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32)
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
                fades[s] = section.Complete ? Mathf.Clamp01(1 - (time - section.FinishedAt) / .55f) : 1;
                float elapsed = time - section.FinishedAt;
                glows[s] = elapsed >= 0 && elapsed < .7f ? Mathf.Sin(elapsed / .7f * Mathf.PI) : 0;
            }
            for (int i = 0; i < SandTray.CellCount; i++)
            {
                int output = (SandTray.Size - 1 - i / SandTray.Size) * SandTray.Size + i % SandTray.Size;
                int top = tray.Top(i), target = tray.Guide[i], region = progress.RegionAt[i];
                // Empty RGB stays zero so filtered pigment can be normalized by occupancy in the shader.
                Color color = top >= 0 ? Palette[top] : Color.black;
                int height = tray.Heights[i];
                int x = i % SandTray.Size, y = i / SandTray.Size;
                int left = x > 0 ? tray.Heights[i - 1] : height, right = x < SandTray.Size - 1 ? tray.Heights[i + 1] : height;
                int above = y > 0 ? tray.Heights[i - SandTray.Size] : height, below = y < SandTray.Size - 1 ? tray.Heights[i + SandTray.Size] : height;
                // A cleared neighbor is an exposed contour, not a slope across the sand surface.
                // Keep relief between occupied cells without outlining piles with a heavy bevel.
                if (left == 0) left = height;
                if (right == 0) right = height;
                if (above == 0) above = height;
                if (below == 0) below = height;
                // Upper-left light: slopes facing the source brighten; raised edges shade their lower neighbors.
                float horizon = Mathf.Max(above, left) - height - 1;
                if (x > 0 && y > 0) horizon = Mathf.Max(horizon, tray.Heights[i - SandTray.Size - 1] - height - 1);
                if (x > 1 && y > 1) horizon = Mathf.Max(horizon, tray.Heights[i - 2 * SandTray.Size - 2] - height - 4);
                if (x > 2 && y > 2) horizon = Mathf.Max(horizon, tray.Heights[i - 3 * SandTray.Size - 3] - height - 7);
                float shadow = Mathf.Clamp(horizon * .018f, 0, .28f);
                float lighting = 1 + Mathf.Clamp((right - left + below - above) * .020f, -.10f, .07f) - shadow * .55f;
                color.r *= lighting; color.g *= lighting; color.b *= lighting;
                if (tray.Protection && top >= 0 && (target < 0 || top != target))
                    color = Color.Lerp(color, new Color32(124,82,111,255), .22f);
                Color32 pixel = color;
                pixel.a = (byte)Mathf.RoundToInt(tray.Heights[i] * (255f / SandTray.Levels));
                colorPixels[output] = pixel;
                feedbackPixels[output] = new Color32(
                    (byte)Mathf.RoundToInt((region >= 0 ? fades[region] : 1) * 255),
                    (byte)Mathf.RoundToInt((region >= 0 ? glows[region] : 0) * 255),
                    (byte)(top >= 0 ? 255 : 0), (byte)Mathf.RoundToInt(shadow / .28f * 255));
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
                float fade = Mathf.Clamp01(grain.Age / .03f) * Mathf.Clamp01(grain.Lift / 1.5f);
                Color32 tint = Palette[grain.Color]; tint.a = (byte)Mathf.RoundToInt(153 * fade);
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
                Color32 tint = Palette[grain.Color];
                tint.a = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(remaining / .18f));
                Vector2 trail = Vector2.ClampMagnitude(grain.Previous - grain.Position, 2.5f);
                for (int tap = 2; tap >= 0; tap--)
                {
                    Vector2 point = grain.Position + trail * (tap * .5f);
                    int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
                    if (!SandTray.Playable(x, y)) continue;
                    Color32 pixel = tint;
                    pixel.a = (byte)(pixel.a * (1 - tap * .28f));
                    grainPixels[(SandTray.Size - 1 - y) * SandTray.Size + x] = pixel;
                }
            }
            grains.SetPixels32(grainPixels); grains.Apply(false, false);
            material.SetVector("_View", new Vector4(View.x, View.y, View.width, View.height));
            material.SetFloat("_Guides", guides ? 1 : 0);
            float highlight = Mathf.Clamp01((highlightUntil - time) / .6f);
            material.SetFloat("_HighlightColor", highlightColor);
            material.SetFloat("_Highlight", highlight);
            var previous = RenderTexture.active;
            try { Graphics.Blit(colors, Texture, material); }
            finally { RenderTexture.active = previous; }
            NeedsRedraw = false;
            HighlightActive = highlight > 0;
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
