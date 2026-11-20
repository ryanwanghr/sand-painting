using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    sealed class CalmWaterBackground : IDisposable
    {
        const int MaxTextureEdge = 1024;
        const int FramesPerSecond = 30;
        static readonly int WaterScale = Shader.PropertyToID("_WaterScale");
        static readonly int TrayView = Shader.PropertyToID("_TrayView");
        static readonly int WaterTime = Shader.PropertyToID("_WaterTime");

        readonly Image image;
        readonly Material material;
        RenderTexture texture;
        Rect lastView;
        float lastZoom, lastTime, animationTime;
        int lastFrame = -1;

        public bool Animate { get; set; } = true;

        public CalmWaterBackground(Image image)
        {
            this.image = image;
            image.scaleMode = ScaleMode.StretchToFill;
            var shader = Resources.Load<Shader>("MandalaCalmWater");
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { name = "Mandala calm water", hideFlags = HideFlags.HideAndDontSave };
                material.SetFloat(WaterTime, 0);
            }
        }

        public void Render(float time, Rect trayView, float zoom)
        {
            if (material == null || Screen.width < 1 || Screen.height < 1) return;
            if (Animate) animationTime += time - lastTime;
            lastTime = time;
            int frame = Mathf.FloorToInt(animationTime * FramesPerSecond);
            float scale = Mathf.Min(1, (float)MaxTextureEdge / Mathf.Max(Screen.width, Screen.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));
            if (texture == null || texture.width != width || texture.height != height || !texture.IsCreated())
            {
                ReleaseTexture();
                texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Mandala water backdrop", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                image.image = texture;
                lastFrame = -1;
            }
            if (frame == lastFrame && trayView == lastView && zoom == lastZoom) return;

            // Keep the wave spacing at the fitted view's scale when the tray zooms.
            material.SetFloat(WaterScale, 1f / (Mathf.Min(trayView.width, trayView.height) * zoom));
            material.SetVector(TrayView, new Vector4(trayView.x, trayView.y, trayView.width, trayView.height));
            if (Animate) material.SetFloat(WaterTime, animationTime);
            var previous = RenderTexture.active;
            try { Graphics.Blit(null, texture, material, 0); }
            finally { RenderTexture.active = previous; }
            lastFrame = frame;
            lastView = trayView;
            lastZoom = zoom;
        }

        public void Dispose()
        {
            ReleaseTexture();
            UnityEngine.Object.Destroy(material);
        }

        void ReleaseTexture()
        {
            if (texture == null) return;
            image.image = null;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
