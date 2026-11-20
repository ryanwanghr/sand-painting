using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    public sealed class MandalaProgress
    {
        internal const float RequiredCoverage = .98f;
        internal const int RequiredLayers = 3;

        public sealed class Section
        {
            public readonly List<int> Cells = new List<int>();
            public bool Complete, Enclosed = true;
            public float FinishedAt = -100;
            public int LabelCell;
        }
        public readonly int[] RegionAt = new int[SandTray.CellCount];
        public readonly List<Section> Sections = new List<Section>();
        readonly List<Section> gaps = new List<Section>();
        readonly int[] colorTargets = new int[SandTray.ColorCount];
        readonly int[] colorCredited = new int[SandTray.ColorCount];
        public float Coverage { get; private set; }
        public float PouringProgress { get; private set; }
        public float CleanupProgress { get; private set; }
        public bool Clean { get; private set; }
        public bool AllComplete { get; private set; }
        public bool PaintingComplete => AllComplete && allCovered;
        public int CompletedCount { get; private set; }
        int revision = -1;
        float eligibleSince = -1;
        bool allCovered;
        public MandalaProgress(SandTray tray)
        {
            for (int i = 0; i < RegionAt.Length; i++) RegionAt[i] = -1;
            var visited = new bool[SandTray.CellCount];
            for (int start = 0; start < RegionAt.Length; start++)
            {
                if (tray.Guide[start] < 0 || RegionAt[start] >= 0) continue;
                var section = new Section();
                CollectSection(tray.Guide, SandTray.Size, RegionAt, start, Sections.Count, section.Cells);
                colorTargets[tray.Guide[start]] += section.Cells.Count;
                Vector2 sum = Vector2.zero;
                foreach (int i in section.Cells) sum += new Vector2(i % SandTray.Size, i / SandTray.Size);
                Vector2 centroid = sum / section.Cells.Count; float best = float.MaxValue;
                foreach (int i in section.Cells)
                {
                    float distance = (new Vector2(i % SandTray.Size, i / SandTray.Size) - centroid).sqrMagnitude;
                    if (distance < best) { best = distance; section.LabelCell = i; }
                }
                Sections.Add(section);
            }
            int[] dxs = { -1, 1, 0, 0 }, dys = { 0, 0, -1, 1 };
            for (int start = 0; start < RegionAt.Length; start++)
            {
                if (visited[start] || tray.Guide[start] >= 0 || !SandTray.Playable(start % SandTray.Size, start / SandTray.Size)) continue;
                var gap = new Section(); gap.Cells.Add(start); visited[start] = true;
                for (int n = 0; n < gap.Cells.Count; n++)
                {
                    int i = gap.Cells[n];
                    for (int d = 0; d < 4; d++)
                    {
                        int x = i % SandTray.Size + dxs[d], y = i / SandTray.Size + dys[d];
                        if (!SandTray.Playable(x, y)) { gap.Enclosed = false; continue; }
                        int next = y * SandTray.Size + x;
                        if (visited[next] || tray.Guide[next] >= 0) continue;
                        visited[next] = true; gap.Cells.Add(next);
                    }
                }
                gaps.Add(gap);
            }
        }

        public bool UsesColor(int color) => colorTargets[color] > 0;
        public float ColorProgress(int color) => UsesColor(color) ? (float)colorCredited[color] / colorTargets[color] : 0;

        internal static void CollectSection(sbyte[] guide, int size, int[] regions, int start, int id, List<int> cells)
        {
            cells.Add(start);
            regions[start] = id;
            for (int n = 0; n < cells.Count; n++)
            {
                int i = cells[n], x = i % size, y = i / size;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    int next = ny * size + nx;
                    if (regions[next] >= 0 || guide[next] != guide[start]) continue;
                    regions[next] = id;
                    cells.Add(next);
                }
            }
        }

        public int Update(SandTray tray, float now, bool animate = true)
        {
            if (revision != tray.Revision)
            {
                revision = tray.Revision; eligibleSince = -1;
                foreach (var section in Sections)
                {
                    section.Complete = tray.Completed[section.Cells[0]]; section.FinishedAt = -100;
                }
            }
            Array.Clear(colorCredited, 0, colorCredited.Length);
            int finished = 0, correctTotal = 0, creditedTotal = 0;
            CompletedCount = 0; allCovered = true;
            foreach (var section in Sections)
            {
                int correct = 0;
                foreach (int i in section.Cells)
                {
                    if (tray.Heights[i] < RequiredLayers) continue;
                    if (tray.DisplayTop(i) == tray.Guide[i]) correct++;
                }
                bool covered = (float)correct / section.Cells.Count >= RequiredCoverage;
                if (!section.Complete && tray.Protection && covered)
                {
                    section.Complete = true; section.FinishedAt = animate ? now : -100;
                    tray.CompleteSection(section.Cells); finished++;
                    correct = section.Cells.Count;
                }
                int credited = section.Complete && covered ? section.Cells.Count : correct;
                colorCredited[tray.Guide[section.Cells[0]]] += credited;
                correctTotal += correct; creditedTotal += credited; allCovered &= covered;
                if (section.Complete) CompletedCount++;
            }
            Coverage = (float)correctTotal / tray.TargetCount; AllComplete = CompletedCount == Sections.Count;
            PouringProgress = (float)creditedTotal / tray.TargetCount;
            int total = 0, occupied = 0;
            CleanupProgress = 1;
            foreach (var gap in gaps)
            {
                int filled = 0; foreach (int i in gap.Cells) if (tray.Heights[i] > 0) filled++;
                occupied += filled; total += gap.Cells.Count;
                if (gap.Enclosed && gap.Cells.Count >= 20 * SandTray.AreaScale)
                    CleanupProgress = Mathf.Min(CleanupProgress, CleanFraction(filled, gap.Cells.Count / 20));
            }
            CleanupProgress = Mathf.Min(CleanupProgress, CleanFraction(occupied, total / 50));
            Clean = CleanupProgress >= 1;
            return finished;
        }

        // The strictest cleanup requirement drives the bar, including small enclosed gaps.
        static float CleanFraction(int occupied, int allowed) => occupied <= allowed ? 1 : (float)allowed / occupied;

        public bool CheckWin(SandTray tray, float now, bool busy)
        {
            if (tray.HasWon || !tray.Protection || busy || tray.Grains.Count > 0 || tray.RollingGrains.Count > 0 || !PaintingComplete || !Clean) { eligibleSince = -1; return false; }
            if (eligibleSince < 0) eligibleSince = now;
            if (now - eligibleSince < 1) return false;
            tray.HasWon = true; eligibleSince = -1; return true;
        }
    }
}
