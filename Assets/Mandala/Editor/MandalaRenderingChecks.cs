using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game.Editor
{
    public static class MandalaRenderingChecks
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static Texture2D Read(RenderTexture source)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                image.Apply();
                return image;
            }
            finally { RenderTexture.active = previous; }
        }

        [MenuItem("Tools/Mandala/Run Color Highlight Checks")]
        public static string RunHighlights()
        {
            var tray = new SandTray(17);
            var progress = new MandalaProgress(tray);
            var samples = new[] { new Vector2(.5f, .5f), new Vector2(.56f, .5f),
                new Vector2(.798f, .623f), new Vector2(.887f, .5f), new Vector2(.91925f, .5f) };
            using (var surface = new SandTexture(progress.Sections.Count))
            {
                surface.SetViewport(1024, new Rect(0, 0, 1, 1));
                Color[] Snapshot()
                {
                    var image = Read(surface.Texture);
                    try
                    {
                        var colors = new Color[samples.Length];
                        for (int i = 0; i < samples.Length; i++)
                            colors[i] = image.GetPixel((int)(samples[i].x * image.width), (int)(samples[i].y * image.height));
                        return colors;
                    }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                surface.Render(tray, progress, false, 0);
                var paper = Snapshot();
                surface.Render(tray, progress, true, 0);
                var idle = Snapshot();
                for (int i = 0; i < samples.Length; i++)
                    Require(Difference(idle[i], paper[i]) < .005f, "Area interiors must have no color highlight by default.");
                Require(!surface.HighlightActive, "No highlight may start without a color click.");
                for (int color = 0; color < samples.Length; color++)
                {
                    surface.Highlight(color, 10);
                    Require(surface.NeedsRedraw && surface.HighlightActive, "Every color click must request a fresh highlight.");
                    surface.Render(tray, progress, true, 10);
                    var peak = Snapshot();
                    float strength = Difference(peak[color], idle[color]);
                    Require(strength > .006f, "The clicked color's areas must visibly highlight.");
                    for (int other = 0; other < samples.Length; other++)
                        if (other != color) Require(Difference(peak[other], idle[other]) < .005f, "Only the clicked color may highlight.");
                    surface.Render(tray, progress, true, 10.9f);
                    var fading = Snapshot();
                    float faded = Difference(fading[color], idle[color]);
                    Require(faded > 0 && faded < strength, "The highlight must fade before disappearing.");
                    surface.Highlight(color, 10.9f);
                    surface.Render(tray, progress, true, 10.9f);
                    Require(Difference(Snapshot()[color], peak[color]) < .005f, "Clicking the same color during a fade must restart its highlight.");
                    surface.Render(tray, progress, true, 12.2f);
                    Require(Difference(Snapshot()[color], idle[color]) < .005f && !surface.HighlightActive,
                        "The last animation frame must remove the highlight and stop redrawing.");
                    surface.Highlight(color, 13);
                    surface.Render(tray, progress, true, 13);
                    Require(Difference(Snapshot()[color], peak[color]) < .005f, "Clicking the same color after expiry must replay the highlight.");
                    int next = (color + 1) % samples.Length;
                    surface.Highlight(next, 13.1f);
                    surface.Render(tray, progress, true, 13.1f);
                    var switched = Snapshot();
                    Require(Difference(switched[color], idle[color]) < .005f && Difference(switched[next], idle[next]) > .006f,
                        "Switching colors must replace the previous highlight immediately.");
                    surface.Render(tray, progress, false, 13.1f);
                    var hidden = Snapshot();
                    for (int i = 0; i < samples.Length; i++)
                        Require(Difference(hidden[i], paper[i]) < .005f, "Guides off must hide color highlights.");
                }
            }
            return "Color highlight checks passed: neutral startup, all 5 colors, matching areas only, fade, expiry, repeated clicks during and after fading, color switching, guides off.";
        }

        [MenuItem("Tools/Mandala/Run Guide Label Checks")]
        public static string RunGuideLabels()
        {
            var tray = new SandTray(17);
            var progress = new MandalaProgress(tray);
            var image = new Image();
            var guides = new MandalaGuideLabels(image, tray, progress);
            var layer = image[0];
            int hidden = 0, revealed = 0, shrunk = 0, checkedLabels = 0;
            foreach (float size in new[] { 120f, 360f, 720f, 1080f })
            {
                var full = new Rect(0, 0, 1, 1);
                guides.Update(full, size, true);
                var wasHidden = new bool[progress.Sections.Count];
                var anchors = new Vector2[progress.Sections.Count];
                for (int i = 0; i < layer.childCount; i++)
                {
                    var label = (Label)layer[i];
                    wasHidden[i] = label.ClassListContains("hidden");
                    if (wasHidden[i]) hidden++;
                    float box = label.style.width.value.value;
                    anchors[i] = new Vector2((label.style.left.value.value + box * .5f) / size,
                        1 - (label.style.top.value.value + box * .5f) / size);
                }
                foreach (float zoom in new[] { 1f, 2f, 4f })
                for (int focus = 0; focus < anchors.Length; focus++)
                {
                    float extent = 1 / zoom;
                    var view = new Rect(Mathf.Clamp(anchors[focus].x - extent * .5f, 0, 1 - extent),
                        Mathf.Clamp(anchors[focus].y - extent * .5f, 0, 1 - extent), extent, extent);
                    guides.Update(view, size, true);
                    for (int i = 0; i < layer.childCount; i++)
                    {
                        var label = (Label)layer[i];
                        if (label.ClassListContains("hidden")) continue;
                        float font = label.style.fontSize.value.value;
                        Require(font >= 8 && font <= 10, "Visible numbers must remain readable and bounded.");
                        if (font < 10) shrunk++;
                        if (i == focus && wasHidden[i]) revealed++;
                        float left = label.style.left.value.value, top = label.style.top.value.value;
                        float box = label.style.width.value.value;
                        Require(left >= 0 && top >= 0 && left + box <= size && top + box <= size,
                            "Numbers must fit inside the visible viewport.");
                        int xMin = Mathf.FloorToInt((view.x + left / size * extent) * SandTray.Size);
                        int xMax = Mathf.FloorToInt((view.x + (left + box) / size * extent) * SandTray.Size);
                        int yMin = Mathf.FloorToInt((1 - view.yMax + top / size * extent) * SandTray.Size);
                        int yMax = Mathf.FloorToInt((1 - view.yMax + (top + box) / size * extent) * SandTray.Size);
                        for (int y = yMin; y <= yMax; y++)
                        for (int x = xMin; x <= xMax; x++)
                            Require(x >= 0 && y >= 0 && x < SandTray.Size && y < SandTray.Size
                                && progress.RegionAt[y * SandTray.Size + x] == i,
                                "The entire number box must stay inside its own section, including curved areas.");
                        checkedLabels++;
                    }
                }
            }
            Require(hidden > 0 && revealed > 0 && shrunk > 0, "Exercise hiding, shrinking, and revealing on zoom.");
            int ring = progress.Sections.FindIndex(section => tray.Guide[section.LabelCell] == 3);
            Require(ring >= 0, "The circular ivory section must exist.");
            foreach (float size in new[] { 360f, 720f })
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = direction * Mathf.PI / 4;
                    Vector2 center = new Vector2(.5f + .387f * Mathf.Cos(angle), .5f + .387f * Mathf.Sin(angle));
                    var view = new Rect(Mathf.Clamp(center.x - .125f, 0, .75f), Mathf.Clamp(center.y - .125f, 0, .75f), .25f, .25f);
                    guides.Update(view, size, true);
                    var label = (Label)layer[ring];
                    Require(!label.ClassListContains("hidden"), "The circle's number must be visible when zoomed onto any side, including the bottom.");
                    float box = label.style.width.value.value;
                    float left = label.style.left.value.value, top = label.style.top.value.value;
                    Require(left >= 0 && top >= 0 && left + box <= size && top + box <= size, "Relocated numbers must fit in the viewport.");
                    int xMin = Mathf.FloorToInt((view.x + left / size * view.width) * SandTray.Size);
                    int xMax = Mathf.FloorToInt((view.x + (left + box) / size * view.width) * SandTray.Size);
                    int yMin = Mathf.FloorToInt((1 - view.yMax + top / size * view.height) * SandTray.Size);
                    int yMax = Mathf.FloorToInt((1 - view.yMax + (top + box) / size * view.height) * SandTray.Size);
                    for (int y = yMin; y <= yMax; y++)
                    for (int x = xMin; x <= xMax; x++)
                        Require(progress.RegionAt[y * SandTray.Size + x] == ring, "Relocated numbers must remain inside the circle's band.");
                }
                guides.Update(new Rect(.375f, .375f, .25f, .25f), size, true);
                Require(layer[ring].ClassListContains("hidden"), "The circle's number must hide when the circle is outside the viewport.");
            }
            guides.Update(new Rect(0, 0, 1, 1), 1080, false);
            Require(layer.ClassListContains("hidden"), "Guides off must hide the number layer.");
            foreach (var section in progress.Sections) section.Complete = true;
            guides.Update(new Rect(0, 0, 1, 1), 1080, true);
            foreach (var label in layer.Children()) Require(label.ClassListContains("hidden"), "Completed sections must hide numbers without a view change.");
            return $"Guide label checks passed: {checkedLabels} boxes inside their sections, {hidden} hidden, {revealed} revealed, {shrunk} shrunk; circle visible in all 8 directions at 2 sizes; viewport, guides, completion.";
        }

        [MenuItem("Tools/Mandala/Run Rendering Checks")]
        public static string Run()
        {
            var tray = new SandTray(17);
            var progress = new MandalaProgress(tray);
            var previous = RenderTexture.active;
            using (var surface = new SandTexture(progress.Sections.Count))
            {
                surface.SetViewport(1024, new Rect(0, 0, 1, 1));
                surface.Render(tray, progress, false, 0);
                Require(surface.Resolution == 1024, "Surface must follow display resolution, not grid resolution.");
                Require(RenderTexture.active == previous, "Rendering must restore the previous render target.");
                var image = Read(surface.Texture);
                try
                {
                    Require(image.GetPixel(0, 0).a < .01f, "Tray corners must remain transparent.");
                    Require(image.GetPixel(512, 512).a > .99f, "Tray center must remain opaque.");
                    float min = 1, max = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        Color pixel = image.GetPixel(400 + i, 850);
                        min = Mathf.Min(min, pixel.r); max = Mathf.Max(max, pixel.r);
                        Require(pixel.r > pixel.b, "Paper must retain its warm palette.");
                    }
                    Require(max - min < .02f, "Empty tray must not contain a checkerboard from unbound textures.");
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }

                // Place a colored patch away from the center, then zoom onto it. Simulation Y points down.
                for (int y = 320; y < 340; y++) for (int x = 100; x < 120; x++)
                    for (int h = 0; h < 8; h++) tray.Add(y * SandTray.Size + x, 0);
                tray.Protection = false;
                var originalTexture = surface.Texture;
                surface.SetViewport(1024, new Rect(.125f, .125f, .25f, .25f));
                Require(surface.Texture == originalTexture, "Zoom must reuse the viewport-sized render target.");
                surface.Render(tray, progress, false, 0);
                image = Read(surface.Texture);
                try
                {
                    Color patch = image.GetPixel(512, 512);
                    Require(patch.r - patch.b > .35f, "Zoom must sample the correct colored patch, including Y orientation.");
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
                // A deep, continuous layer still follows the simulation cells when zoomed.
                surface.SetViewport(1024, new Rect(.125f, .125f, .25f, .25f));
                surface.Render(tray, progress, false, 0);
                image = Read(surface.Texture);
                try
                {
                    Color a = image.GetPixel(513, 513), b = image.GetPixel(514, 514);
                    Require(a == b, "Continuous sand must retain the simulation's cell texture when zoomed.");
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
                Require(tray.Heights.Length == 440 * 440, "Rendering must not resize simulation or saves.");
                surface.SetViewport(5000, new Rect(0, 0, 1, 1));
                Require(surface.Resolution <= 2048, "Surface allocation must remain bounded.");
            }
            return "Rendering checks passed: native resolution, alpha, texture bindings, viewport zoom, allocation cap, unchanged grid.";
        }

        [MenuItem("Tools/Mandala/Run Sand Depth Rendering Checks")]
        public static string RunDepth()
        {
            var tray = new SandTray(17) { Protection = false };
            var progress = new MandalaProgress(tray);
            var view = new Rect(196f / 440, 196f / 440, 48f / 440, 48f / 440);
            int resolution = 528;
            using (var surface = new SandTexture(progress.Sections.Count))
            {
                surface.SetViewport(resolution, view);
                Color[] Pixels(float time = 0)
                {
                    surface.Render(tray, progress, false, time);
                    var image = Read(surface.Texture);
                    try { return image.GetPixels(); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                void Fill(int height, int color = 0)
                {
                    tray.Clear(); tray.Protection = false;
                    for (int y = 200; y < 240; y++) for (int x = 200; x < 240; x++)
                        for (int h = 0; h < height; h++) tray.Add(y * SandTray.Size + x, color);
                }
                int Pixel(int x, int y, float offsetX = .5f, float offsetY = .5f)
                {
                    int px = Mathf.FloorToInt(((x + offsetX) / 440 - view.x) / view.width * resolution);
                    int py = Mathf.FloorToInt((1 - (y + offsetY) / 440 - view.y) / view.height * resolution);
                    return py * resolution + px;
                }
                float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                Color[] paper = Pixels();
                float PaperFraction(Color[] pixels)
                {
                    int empty = 0, total = 0;
                    for (int y = 208; y < 232; y++) for (int x = 208; x < 232; x++)
                    for (int sy = 0; sy < 6; sy++) for (int sx = 0; sx < 6; sx++)
                    {
                        int i = Pixel(x, y, (sx + .5f) / 6, (sy + .5f) / 6);
                        if (Difference(pixels[i], paper[i]) < .02f) empty++;
                        total++;
                    }
                    return (float)empty / total;
                }

                Fill(1);
                int changes = tray.Changes, mass = tray.Mass();
                Color[] thin = Pixels(), later = Pixels(123);
                Require(tray.Changes == changes && tray.Mass() == mass, "Rendering depth must not change simulation material.");
                for (int i = 0; i < thin.Length; i++) Require(thin[i] == later[i], "Residue grains must not move or flicker with time.");
                float thinPaper = PaperFraction(thin);
                Require(thinPaper > .25f && thinPaper < .85f, "Thin residue must show both colored grains and exposed tray.");
                Fill(8); Color[] deep = Pixels(); float deepPaper = PaperFraction(deep);
                float Pigment(Color[] pixels)
                {
                    float total = 0;
                    for (int y = 208; y < 232; y++) for (int x = 208; x < 232; x++)
                    for (int sy = 0; sy < 6; sy++) for (int sx = 0; sx < 6; sx++)
                    {
                        int i = Pixel(x, y, (sx + .5f) / 6, (sy + .5f) / 6);
                        total += Difference(pixels[i], paper[i]);
                    }
                    return total;
                }
                float deepPigment = Pigment(deep), previousStrength = 0;
                var strengths = new string[5];
                for (int height = 1; height <= 5; height++)
                {
                    Fill(height); Color[] layer = Pixels();
                    float strength = Pigment(layer) / deepPigment;
                    strengths[height - 1] = strength.ToString("P0");
                    Require(strength > previousStrength + .025f,
                        "Each layer through five must visibly increase sand coverage.");
                    if (height < 5) Require(strength < .99f,
                        "The coverage ramp must retain a visible difference before five layers.");
                    else Require(strength > .99f && PaperFraction(layer) < .01f && deepPaper < .01f,
                        "Five layers and deeper deposits must look continuous.");
                    previousStrength = strength;
                }

                // Sampling the same tray point after panning must retain the same grain.
                Fill(1); thin = Pixels();
                Color grain = thin[Pixel(220, 220, .32f, .32f)];
                view.x += 2f / 440;
                surface.SetViewport(resolution, view);
                Color[] panned = Pixels();
                Require(Difference(grain, panned[Pixel(220, 220, .32f, .32f)]) < .005f,
                    "Panning must keep residue anchored to the sand rather than the viewport.");
                view = new Rect(208f / 440, 208f / 440, 24f / 440, 24f / 440); resolution = 264;
                surface.SetViewport(resolution, view);
                Color[] zoomed = Pixels();
                Require(Difference(grain, zoomed[Pixel(220, 220, .32f, .32f)]) < .005f,
                    "Zooming at the same sampling density must retain the same tray-anchored grains.");
                view = new Rect(196f / 440, 196f / 440, 48f / 440, 48f / 440); resolution = 528;
                surface.SetViewport(resolution, view);

                Fill(4); Color[] lowPile = Pixels();
                Fill(16); Color[] highPile = Pixels();
                int shadowPixel = Pixel(241, 241);
                Require(lowPile[shadowPixel].grayscale - highPile[shadowPixel].grayscale > .035f,
                    "Taller piles must cast stronger short shadows onto the exposed tray.");
                float lit = 0, shaded = 0;
                for (int y = 208; y < 232; y++)
                {
                    lit += highPile[Pixel(200, y)].grayscale;
                    shaded += highPile[Pixel(239, y)].grayscale;
                }
                Require((lit - shaded) / 24 > .05f, "Raised edges facing the upper-left light must be brighter than the opposite slope.");

                for (int color = 0; color < 5; color++)
                {
                    Fill(8, color); Color[] colored = Pixels();
                    Color.RGBToHSV(SandTexture.Palette[color], out float expectedHue, out _, out _);
                    Color.RGBToHSV(colored[Pixel(220, 220)], out float actualHue, out _, out _);
                    Require(Mathf.Abs(Mathf.DeltaAngle(expectedHue * 360, actualHue * 360)) < 4,
                        "Depth lighting must preserve each palette color's hue.");
                }
                tray.Clear(); Color[] cleared = Pixels();
                for (int i = 0; i < paper.Length; i++) Require(Difference(paper[i], cleared[i]) < .005f,
                    "Removing the sand must remove all residue and pile shadows.");
                return $"Sand depth checks passed: sparse residue ({thinPaper:P0} tray visible), gradual layers 1–5 ({string.Join(", ", strengths)} pigment strength), time stability, anchored pan and zoom, pile shadows, lit slopes, all five hues, unchanged material, clean removal.";
            }
        }
    }
}
