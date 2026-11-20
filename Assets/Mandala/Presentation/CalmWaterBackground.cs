using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    sealed class CalmWaterBackground : IDisposable
    {
        const int MaxTextureEdge = 1024;
        const int FramesPerSecond = 30;
        static readonly int Aspect = Shader.PropertyToID("_WaterAspect");
        static readonly int TrayView = Shader.PropertyToID("_TrayView");
        static readonly int WaterTime = Shader.PropertyToID("_WaterTime");

        readonly Image image;
        readonly Material material;
        RenderTexture texture;
        Rect lastView;
        int lastFrame = -1;

        public CalmWaterBackground(Image image)
        {
            this.image = image;
            image.scaleMode = ScaleMode.StretchToFill;
            var shader = Resources.Load<Shader>("MandalaCalmWater");
            if (shader != null && shader.isSupported)
                material = new Material(shader) { name = "Mandala calm water", hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(float time, Rect trayView)
        {
            if (material == null || Screen.width < 1 || Screen.height < 1) return;
            int frame = Mathf.FloorToInt(time * FramesPerSecond);
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
            if (frame == lastFrame && trayView == lastView) return;

            material.SetFloat(Aspect, (float)Screen.width / Screen.height);
            material.SetVector(TrayView, new Vector4(trayView.x, trayView.y, trayView.width, trayView.height));
            material.SetFloat(WaterTime, time);
            var previous = RenderTexture.active;
            try { Graphics.Blit(null, texture, material, 0); }
            finally { RenderTexture.active = previous; }
            lastFrame = frame;
            lastView = trayView;
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
