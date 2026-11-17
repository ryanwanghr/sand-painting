using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    public sealed class MandalaProgress
    {
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
        public float Coverage { get; private set; }
        public bool Clean { get; private set; }
        public bool AllComplete { get; private set; }
        public int CompletedCount { get; private set; }
        int revision = -1;
        float eligibleSince = -1;
        public MandalaProgress(SandTray tray)
        {
            for (int i = 0; i < RegionAt.Length; i++) RegionAt[i] = -1;
            var visited = new bool[SandTray.CellCount];
            for (int start = 0; start < RegionAt.Length; start++)
            {
                if (tray.Guide[start] < 0 || RegionAt[start] >= 0) continue;
                var section = new Section(); int id = Sections.Count; section.Cells.Add(start); RegionAt[start] = id;
                Vector2 sum = Vector2.zero;
                for (int n = 0; n < section.Cells.Count; n++)
                {
                    int i = section.Cells[n], x = i % SandTray.Size, y = i / SandTray.Size; sum += new Vector2(x, y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= SandTray.Size || ny >= SandTray.Size) continue;
                        int next = ny * SandTray.Size + nx;
                        if (RegionAt[next] >= 0 || tray.Guide[next] != tray.Guide[start]) continue;
                        RegionAt[next] = id; section.Cells.Add(next);
                    }
                }
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
        public int Update(SandTray tray, float now, bool animate = true)
        {
            if (revision != tray.Revision)
            {
                revision = tray.Revision; eligibleSince = -1;
                foreach (var section in Sections) { section.Complete = false; section.FinishedAt = -100; }
                Array.Clear(tray.Locked, 0, tray.Locked.Length);
            }
            int finished = 0, correctTotal = 0; CompletedCount = 0;
            foreach (var section in Sections)
            {
                int correct = 0; foreach (int i in section.Cells) if (tray.Correct(i)) correct++;
                correctTotal += correct; float coverage = (float)correct / section.Cells.Count;
                bool complete = section.Complete ? coverage >= .80f : coverage >= .98f;
                if (complete != section.Complete)
                {
                    section.Complete = complete; section.FinishedAt = complete && animate ? now : -100;
                    foreach (int i in section.Cells) tray.Locked[i] = complete;
                    if (complete) finished++;
                }
                if (complete) CompletedCount++;
            }
            Coverage = (float)correctTotal / tray.TargetCount; AllComplete = CompletedCount == Sections.Count;
            int total = 0, occupied = 0; Clean = true;
            foreach (var gap in gaps)
            {
                int filled = 0; foreach (int i in gap.Cells) if (tray.Heights[i] > 0) filled++;
                occupied += filled; total += gap.Cells.Count;
                if (gap.Enclosed && gap.Cells.Count >= 20 && filled > Math.Floor(gap.Cells.Count * .05 + 1e-9)) Clean = false;
            }
            Clean &= occupied <= Math.Floor(total * .02 + 1e-9);
            return finished;
        }
        public bool CheckWin(SandTray tray, float now, bool busy)
        {
            if (tray.HasWon || !tray.Protection || busy || tray.Grains.Count > 0 || !AllComplete || !Clean) { eligibleSince = -1; return false; }
            if (eligibleSince < 0) eligibleSince = now;
            if (now - eligibleSince < 1) return false;
            tray.HasWon = true; eligibleSince = -1; return true;
        }
    }
}
