using System;
using UnityEngine;

namespace Mandala.Game
{
    internal static class SandArtPreview
    {
        const int Size = 320;

        public static Texture2D Original() => Create((x, y) => SandTray.GuideColor(x, y, SandTray.Size));

        public static byte[] Capture(SandTray tray)
        {
            var texture = Create((x, y) => tray.DisplayTop(y * SandTray.Size + x));
            try { return texture.EncodeToPNG(); }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        static Texture2D Create(Func<int, int, int> sample)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int r = 0, g = 0, b = 0, covered = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    int sampleX = (4 * x + 2 * sx + 1) * SandTray.Size / (4 * Size);
                    int sampleY = (4 * y + 2 * sy + 1) * SandTray.Size / (4 * Size);
                    int color = sample(sampleX, sampleY);
                    if (color < 0) continue;
                    var pigment = SandTexture.Palette[color];
                    r += pigment.r;
                    g += pigment.g;
                    b += pigment.b;
                    covered++;
                }
                if (covered == 0) continue;
                uint grain = (uint)(y * Size + x) * 0x9e3779b9u;
                int shade = (int)((grain ^ (grain >> 15)) % 21) - 10;
                byte Channel(int sum) => (byte)Mathf.Clamp(sum / covered + shade, 0, 255);
                pixels[(Size - 1 - y) * Size + x] = new Color32(Channel(r), Channel(g), Channel(b), (byte)(255 * covered / 4));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
