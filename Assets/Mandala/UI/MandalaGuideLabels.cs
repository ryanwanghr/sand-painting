using UnityEngine;
using UnityEngine.UIElements;

namespace Mandala.Game
{
    /// <summary>Display-resolution text, positioned with the same view transform as input and shading.</summary>
    public sealed class MandalaGuideLabels
    {
        readonly Label[] labels;
        readonly MandalaProgress progress;
        readonly VisualElement layer;
        readonly int[] labelCells, clearance;
        readonly bool[] fits;
        const float NormalBoxSize = 16, NormalFontSize = 10, MinimumFontSize = 8, Padding = 1;
        Rect lastView;
        Vector2 lastSize;

        public MandalaGuideLabels(Image image, SandTray tray, MandalaProgress progress)
        {
            this.progress = progress;
            clearance = BuildClearance(progress.RegionAt);
            labelCells = new int[progress.Sections.Count];
            fits = new bool[labelCells.Length];
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("guide-labels");
            image.Add(layer);
            labels = new Label[progress.Sections.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                var section = progress.Sections[i];
                int best = section.LabelCell;
                var center = new Vector2(best % SandTray.Size, best / SandTray.Size);
                float nearest = float.MaxValue;
                foreach (int cell in section.Cells)
                {
                    float distance = (new Vector2(cell % SandTray.Size, cell / SandTray.Size) - center).sqrMagnitude;
                    if (clearance[cell] < clearance[best] || (clearance[cell] == clearance[best] && distance >= nearest)) continue;
                    best = cell; nearest = distance;
                }
                labelCells[i] = best;
                int color = tray.Guide[best];
                var label = new Label((color + 1).ToString()) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("guide-number");
                labels[i] = label;
                layer.Add(label);
            }
        }

        static int[] BuildClearance(int[] regions)
        {
            var distance = new int[regions.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = regions[i] < 0 ? 0 : SandTray.Size;
            for (int pass = 0; pass < 2; pass++)
            {
                int step = pass == 0 ? 1 : -1;
                for (int i = pass == 0 ? 0 : distance.Length - 1; i >= 0 && i < distance.Length; i += step)
                {
                    if (regions[i] < 0) continue;
                    int x = i % SandTray.Size, y = i / SandTray.Size;
                    for (int dx = -1; dx <= 1; dx++)
                        Relax(x + dx, y - step);
                    Relax(x - step, y);

                    void Relax(int nx, int ny)
                    {
                        int next = ny * SandTray.Size + nx;
                        int neighbor = nx < 0 || ny < 0 || nx >= SandTray.Size || ny >= SandTray.Size
                            || regions[next] != regions[i] ? 0 : distance[next];
                        distance[i] = Mathf.Min(distance[i], neighbor + 1);
                    }
                }
            }
            return distance;
        }

        static Vector2 UV(int cell) => new Vector2((cell % SandTray.Size + .5f) / SandTray.Size,
            1 - (cell / SandTray.Size + .5f) / SandTray.Size);

        float BoxSize(int cell, Rect view, Vector2 size)
        {
            Vector2 uv = UV(cell);
            float scaleX = size.x / view.width, scaleY = size.y / view.height;
            float edge = Mathf.Min(Mathf.Min(uv.x - view.xMin, view.xMax - uv.x) * scaleX,
                Mathf.Min(uv.y - view.yMin, view.yMax - uv.y) * scaleY);
            float available = (2 * clearance[cell] - 1) * Mathf.Min(scaleX, scaleY) / SandTray.Size;
            return Mathf.Clamp(Mathf.Min(available, edge * 2) - Padding * 2, 0, NormalBoxSize);
        }

        int VisibleCell(int section, Rect view, Vector2 size)
        {
            int best = labelCells[section];
            float bestBox = BoxSize(best, view, size);
            if (NormalFontSize * bestBox / NormalBoxSize >= MinimumFontSize) return best;
            float nearest = float.MaxValue;
            foreach (int cell in progress.Sections[section].Cells)
            {
                Vector2 uv = UV(cell);
                if (!view.Contains(uv)) continue;
                float box = BoxSize(cell, view, size);
                float distance = (uv - view.center).sqrMagnitude;
                if (box < bestBox || (box == bestBox && distance >= nearest)) continue;
                best = cell; bestBox = box; nearest = distance;
            }
            return best;
        }

        public void Update(Rect view, float size, bool guides) => Update(view, Vector2.one * size, guides);

        public void Update(Rect view, Vector2 size, bool guides)
        {
            layer.EnableInClassList("hidden", !guides);
            bool reposition = view != lastView || size != lastSize;
            for (int i = 0; i < labels.Length; i++)
            {
                var section = progress.Sections[i];
                if (reposition)
                {
                    int cell = VisibleCell(i, view, size);
                    Vector2 uv = UV(cell);
                    float box = BoxSize(cell, view, size);
                    float fontSize = NormalFontSize * box / NormalBoxSize;
                    float left = (uv.x - view.x) / view.width * size.x - box * .5f;
                    float top = (1 - (uv.y - view.y) / view.height) * size.y - box * .5f;
                    fits[i] = fontSize >= MinimumFontSize && left >= 0 && top >= 0 && left + box <= size.x && top + box <= size.y;
                    labels[i].style.width = box;
                    labels[i].style.height = box;
                    labels[i].style.fontSize = fontSize;
                    labels[i].style.left = left;
                    labels[i].style.top = top;
                }
                bool visible = !section.Complete && fits[i];
                labels[i].EnableInClassList("hidden", !visible);
            }
            lastView = view; lastSize = size;
        }
    }
}
