using System;
using System.IO;
using UnityEngine;

namespace Mandala.Game
{
    public sealed class SandDesign : ScriptableObject
    {
        public const string OriginalId = "eightfold-mandala";
        public const string ResourceFolder = "SandDesigns";

        [SerializeField, HideInInspector] string id;
        [SerializeField] string title;
        [SerializeField] string collection;
        [SerializeField, HideInInspector] Texture2D mask;

        public string Id => id;
        public string Title => title;
        public string Collection => string.IsNullOrWhiteSpace(collection) ? "Unsorted" : collection.Trim();
        public Texture2D Mask => mask;

#if UNITY_EDITOR
        public void Initialize(string designId, string designTitle, string designCollection, Texture2D designMask)
        {
            id = designId;
            title = designTitle;
            collection = designCollection;
            mask = designMask;
        }
#endif

        internal sbyte[] BuildGuide() => id == OriginalId ? null : ReadMask(mask);

        public static sbyte[] ReadMask(Texture2D source)
        {
            int size = SandTray.Size;
            if (source == null || source.width != size || source.height != size || !source.isReadable)
                throw new InvalidDataException($"The mask must be a readable {size} × {size} PNG.");

            var pixels = source.GetPixels32();
            var guide = new sbyte[pixels.Length];
            Array.Fill(guide, (sbyte)-1);
            int marked = 0;
            // Texture2D pixel rows run bottom-up. The tray uses top-down rows.
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color32 pixel = pixels[(size - 1 - y) * size + x];
                if (pixel.a == 0) continue;
                int color = Array.IndexOf(SandTexture.Palette, pixel);
                if (color < 0)
                    throw new InvalidDataException($"Pixel ({x + 1}, {y + 1}) must be transparent or use one of the five exact palette colors.");
                if (!SandTray.Playable(x, y))
                    throw new InvalidDataException($"Pixel ({x + 1}, {y + 1}) lies outside the circular tray.");
                guide[y * size + x] = (sbyte)color;
                marked++;
            }
            if (marked == 0) throw new InvalidDataException("The mask contains no paintable areas.");
            return guide;
        }
    }
}
