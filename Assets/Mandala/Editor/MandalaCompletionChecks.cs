using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    public static class MandalaCompletionChecks
    {
        const int Center = SandTray.Size / 2;
        const int Scale = SandTray.GridScale;

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void SameMaterial(SandTray.Snapshot expected, SandTray.Snapshot actual)
        {
            for (int i = 0; i < SandTray.CellCount; i++)
            {
                Require(expected.Heights[i] == actual.Heights[i], "Completion must not change physical sand heights.");
                for (int h = 0; h < expected.Heights[i]; h++)
                    Require(expected.Layers[i * SandTray.Levels + h] == actual.Layers[i * SandTray.Levels + h],
                        "Painting assistance must preserve original pigments.");
            }
        }

        static SandTray Painting(out MandalaProgress progress, bool updateProgress = true)
        {
            var tray = new SandTray(42); progress = new MandalaProgress(tray);
            for (int i = 0; i < SandTray.CellCount; i++)
                if (tray.Guide[i] >= 0)
                    for (int h = 0; h < 3; h++) tray.Add(i, tray.Guide[i]);
            if (updateProgress) progress.Update(tray, 0, false);
            return tray;
        }

        [MenuItem("Tools/Mandala/Run Completion Color Checks")]
        public static string Run()
        {
            const int center = Center * SandTray.Size + Center;
            var tray = Painting(out var progress, updateProgress: false);
            var section = progress.Sections[progress.RegionAt[center]];
            int allowedShallow = section.Cells.Count / 50;
            Require(allowedShallow > 0, "The center section must be large enough to tolerate shallow cells.");
            int shallow = 0;
            foreach (int cell in section.Cells)
            {
                if (cell == center) continue;
                tray.Remove(cell);
                if (++shallow == allowedShallow) break;
            }
            tray.Remove(center);
            progress.Update(tray, 0, false);
            Require(!tray.Completed[center] && !progress.AllComplete
                && progress.ColorProgress(tray.Guide[center]) < 1 && progress.PouringProgress < 1,
                "A section below 98% three-layer coverage must stay incomplete.");
            tray.Add(center, tray.Guide[center]);
            Require(progress.Update(tray, 0, false) == 1 && tray.Completed[center] && progress.PaintingComplete,
                "The section must complete when 98% of its cells reach three layers with the guide color on top.");
            int earned = progress.CompletedCount;
            tray.Add(center, 4);
            Require(tray.Top(center) == 4 && tray.DisplayTop(center) == tray.Guide[center],
                "Wrong sand must look correct in a completed section without changing its pigment.");
            int other = progress.Sections.Find(candidate => tray.Guide[candidate.Cells[0]] == 2).Cells[0];
            int gap = center;
            while (tray.Guide[gap] >= 0) gap++;
            Require(tray.DisplayColor(other, 4) == 2 && tray.DisplayColor(gap, 4) == 4,
                "The same grain must follow each completed area's color and recover its own color outside them.");
            var original = tray.Capture();
            while (tray.Heights[center] > 2) tray.Remove(center);
            Require(progress.Update(tray, 1) == 0 && progress.AllComplete && !progress.PaintingComplete,
                "A thinned section keeps its earned completion but must regain 98% three-layer coverage before release.");
            foreach (int cell in section.Cells)
                while (tray.Heights[cell] > 0) tray.Remove(cell);
            Require(progress.Update(tray, 1) == 0 && progress.CompletedCount == earned && tray.DisplayTop(center) == -1,
                "Emptying a section must keep its earned completion without creating sand or celebrating again.");
            Require(!progress.CheckWin(tray, 1, false) && !progress.CheckWin(tray, 3, false),
                "Earned but empty sections must not finish the painting.");
            var empty = tray.Capture();
            string path = Path.Combine(Path.GetTempPath(), "mandala-colors-" + Guid.NewGuid() + ".sand");
            try
            {
                MandalaSave.Write(path, empty); tray.Clear(); tray.Restore(MandalaSave.Read(path));
                progress.Update(tray, 4, false);
                Require(progress.CompletedCount == earned && tray.Completed[center] && tray.DisplayTop(center) == -1,
                    "Saving and loading must retain earned completion even with no sand left in that section.");
                tray.Restore(original); progress.Update(tray, 5, false);
                tray.Add(gap, 3);
                int mass = tray.Mass(), appearance = tray.DisplayTop(center);
                var beforeRelease = tray.Capture();
                tray.ReleaseColors();
                Require(!tray.Protection && tray.Top(center) == appearance && tray.Mass() == mass && tray.Top(gap) == 3,
                    "Release must bake the current picture without moving sand or recoloring gaps.");
                for (int h = 0; h < tray.Heights[center]; h++)
                    Require(tray.Layers[center * SandTray.Levels + h] == appearance,
                        "Every buried layer must receive the completed section's final color.");
                int moved = tray.Remove(center); tray.Add(other, moved);
                Require(tray.DisplayTop(other) == appearance,
                    "After release, grains must retain their baked colors across section boundaries.");
                var released = tray.Capture();
                tray.ReleaseColors(); SameMaterial(released, tray.Capture());
                MandalaSave.Write(path, released); tray.Restore(MandalaSave.Read(path));
                SameMaterial(released, tray.Capture());
                Require(!tray.Protection, "Reloading must retain the sweeping phase.");
                tray.Restore(beforeRelease); progress.Update(tray, 6, false);
                Require(tray.Protection && tray.Top(center) == 4 && tray.DisplayTop(center) == appearance,
                    "Undoing release must restore original pigments and temporary assistance together.");
                tray.Clear(); progress.Update(tray, 7, false);
                Require(progress.CompletedCount == 0 && !tray.Completed[center], "Clear must reset earned sections.");

                // Leave room for the wrong surface color within the v1 save's coverage allowance.
                tray.Restore(original);
                int shallowCell = section.Cells.Find(cell => tray.Heights[cell] == 2);
                tray.Add(shallowCell, tray.Guide[shallowCell]);
                var legacy = tray.Capture();
                using (var file = File.Create(path))
                using (var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
                using (var writer = new BinaryWriter(zip))
                {
                    writer.Write(0x4D414E44); writer.Write(1); writer.Write(SandTray.Size); writer.Write(SandTray.Levels);
                    writer.Write(legacy.Protection); writer.Write(legacy.HasWon);
                    for (int i = 0; i < SandTray.CellCount; i++)
                    {
                        writer.Write(legacy.Heights[i]); writer.Write(legacy.Layers, i * SandTray.Levels, legacy.Heights[i]);
                    }
                }
                tray.Restore(MandalaSave.Read(path)); progress.Update(tray, 8, false);
                Require(progress.AllComplete && tray.Top(center) == 4, "Existing v1 saves must load without losing pigments.");
            }
            finally
            {
                foreach (string suffix in new[] { "", ".tmp", ".bak" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }

            // Completion leaves movement within colored areas unchanged.
            foreach (SandTool tool in new[] { SandTool.Pour, SandTool.Sweep, SandTool.Rake })
            {
                var assisted = Painting(out _); var free = new SandTray(42);
                free.Restore(assisted.Capture()); free.Protection = false;
                Vector2 from = new Vector2(212, 220) * Scale, to = new Vector2(227, 220) * Scale;
                assisted.Apply(tool, from, to, 4 * Scale, 4, .1f); free.Apply(tool, from, to, 4 * Scale, 4, .1f);
                for (int frame = 0; frame < 30; frame++) { assisted.Step(1f / 60); free.Step(1f / 60); }
                SameMaterial(free.Capture(), assisted.Capture());
            }
            var moving = new SandTray(42);
            for (int i = 0; i < SandTray.CellCount; i++) moving.Completed[i] = true;
            var tip = Vector2.one * Center;
            moving.Apply(SandTool.Pour, tip, tip, SandTray.PourRadius, 4, .5f);
            moving.Step(.1f); moving.Step(.1f); moving.Step(.005f);
            moving.Apply(SandTool.Pour, tip, tip, SandTray.PourRadius, 4, .1f);
            Require(moving.Grains.Count > 0 && moving.RollingGrains.Count > 0,
                "The release fixture must include both airborne and rolling material.");
            int total = moving.Mass() + moving.Grains.Count;
            var fallingColors = moving.Grains.ConvertAll(grain => moving.DisplayColor(
                new Vector2(grain.Cell % SandTray.Size + .5f, grain.Cell / SandTray.Size + .5f - grain.Lift), grain.Color));
            var rollingColors = moving.RollingGrains.ConvertAll(grain => moving.DisplayColor(grain.Position, grain.Color));
            moving.ReleaseColors();
            for (int n = 0; n < fallingColors.Count; n++) Require(moving.Grains[n].Color == fallingColors[n],
                "Release must freeze the currently displayed color of airborne grains.");
            for (int n = 0; n < rollingColors.Count; n++) Require(moving.RollingGrains[n].Color == rollingColors[n],
                "Release must freeze rolling colors at their current positions, not their future destinations.");
            for (int frame = 0; frame < 60; frame++) moving.Step(1f / 60);
            Require(moving.Mass() + moving.Grains.Count == total,
                "Releasing in flight must preserve all material and its reserved landing slots.");
            return "Completion checks passed: 98% three-layer coverage, temporary location colors, original pigments, empty sections, permanent credit, coverage gate, all-layer release, cross-boundary sweeping, undo, clear, save round trips, v1 migration, unchanged movement inside colored areas.";
        }

        static Color32[] Read(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }

        [MenuItem("Tools/Mandala/Run Section Lip Checks")]
        public static string RunSectionLips()
        {
            const int offset = Center - 220, edgeX = 230 + offset;
            int Cell(int x, int y) => (y + offset) * SandTray.Size + x;
            var tray = new SandTray(42);
            Require(Array.Exists(tray.Capture().Lips, edges => edges != 0), "A new empty tray must start with section lips.");
            Array.Fill(tray.Guide, (sbyte)-1);
            var section = new List<int>();
            for (int y = 200; y < 240; y++) for (int x = 210 + offset; x < edgeX; x++)
            {
                int cell = Cell(x, y);
                tray.Guide[cell] = 0; section.Add(cell);
            }
            int Boundary(int y) => Cell(edgeX - 1, y);
            int Gap(int y) => Cell(edgeX, y);
            bool EdgeAt(SandTray.Snapshot snapshot, int y) => (snapshot.Lips[Boundary(y)] & 1) != 0;
            void Settle()
            {
                for (int frame = 0; frame < 60; frame++) tray.Step(1f / 60);
            }

            tray.Clear();
            Require(EdgeAt(tray.Capture(), 205) && EdgeAt(tray.Capture(), 230), "Clearing must restore the lips around an empty section.");
            foreach (int cell in section) for (int layer = 0; layer < 3; layer++) tray.Add(cell, 0);
            var unfinished = tray.Capture();
            Settle(); SameMaterial(unfinished, tray.Capture());

            tray.CompleteSection(section);
            var low = tray.Capture();
            tray.Add(Gap(208), 4);
            var tip = new Vector2(edgeX, 208.5f + offset);
            Require(tray.Apply(SandTool.Vacuum, tip, tip, 1.2f, 0, .1f) == 1, "Painting vacuum must still clean against a lip.");
            Settle(); SameMaterial(low, tray.Capture());

            tray.Add(Boundary(205), 0); tray.Add(Boundary(205), 0);
            tray.Step(.005f);
            Require(tray.RollingGrains.Exists(grain => grain.Source == Boundary(205) && grain.Target % SandTray.Size >= edgeX),
                "Sand above the lip must still be able to overflow into a gap.");

            tray.Restore(low); tray.ReleaseColors();
            Settle(); SameMaterial(low, tray.Capture());
            Require(EdgeAt(tray.Capture(), 205), "Let go must leave the lips in place until a tool touches them.");
            var intact = tray.Capture();
            Vector2 from = new Vector2(edgeX, 205.5f + offset), to = new Vector2(edgeX, 216.5f + offset);
            tray.Apply(SandTool.Sweep, from, from, .2f, 0, 1f / 60);
            Require(EdgeAt(tray.Capture(), 205), "A sweep without movement must leave the edge intact.");
            tray.Apply(SandTool.Sweep, from, to, .2f, 0, 1f / 60);
            var swept = tray.Capture();
            for (int y = 200; y < 240; y++)
                Require(EdgeAt(swept, y) == (y < 205 || y > 216), "A fast sweep must open exactly its continuous path.");
            Settle();
            Require(tray.Heights[Gap(210)] > 0 && tray.Heights[Gap(230)] == 0 && tray.Mass() == section.Count * 3,
                "A swept opening must let sand spill locally and conserve material while the distant edge holds.");

            tray.Restore(intact);
            Require(EdgeAt(tray.Capture(), 210), "Undo must restore an opened edge.");
            tip = new Vector2(edgeX, 230.5f + offset);
            tray.Apply(SandTool.Vacuum, tip, tip, .2f, 0, 1f / 60);
            var vacuumed = tray.Capture();
            Require(!EdgeAt(vacuumed, 230) && EdgeAt(vacuumed, 229) && EdgeAt(vacuumed, 231),
                "A vacuum held over one segment must open that segment alone.");
            Settle();
            Require(tray.Heights[Gap(230)] > 0 && tray.Heights[Gap(210)] == 0, "A vacuum opening must release sand into its gap.");

            string path = Path.Combine(Path.GetTempPath(), "mandala-lips-" + Guid.NewGuid() + ".sand");
            try
            {
                MandalaSave.Write(path, vacuumed); tray.Restore(MandalaSave.Read(path));
                var reloaded = tray.Capture();
                Require(!reloaded.Protection && !EdgeAt(reloaded, 230) && EdgeAt(reloaded, 229),
                    "A released tray must preserve both broken and intact edges across saving and reloading.");
                low.Lips = null;
                MandalaSave.Write(path, low); tray.Restore(MandalaSave.Read(path));
                Require(EdgeAt(tray.Capture(), 230), "A legacy painting must rebuild its guide edges.");
                low.Protection = false;
                MandalaSave.Write(path, low); tray.Restore(MandalaSave.Read(path));
                Require(!EdgeAt(tray.Capture(), 230), "A legacy released painting must stay open.");
            }
            finally
            {
                foreach (string suffix in new[] { "", ".tmp", ".bak" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }

            var adjacent = new List<int>();
            for (int y = 200; y < 240; y++)
            {
                int cell = Gap(y);
                tray.Guide[cell] = 1; adjacent.Add(cell);
            }
            tray.Clear();
            foreach (int cell in section) for (int layer = 0; layer < 3; layer++) tray.Add(cell, 0);
            Settle();
            var firstColor = tray.Capture();
            Require(EdgeAt(firstColor, 210) && tray.Heights[Gap(210)] == 0,
                "The shared lip must hold low sand before either section completes.");
            Require(firstColor.Lips[Cell(edgeX - 2, 210)] == 0,
                "Adjacent cells inside the same section must stay open to one another.");

            firstColor.Lips[Boundary(210)] = 0;
            tray.Restore(firstColor);
            Require(EdgeAt(tray.Capture(), 210) && !tray.Completed[Boundary(210)] && !tray.Completed[Gap(210)],
                "Older painting saves must gain lips between unfinished sections.");

            tray.CompleteSection(section);
            tray.CompleteSection(adjacent);
            tray.ReleaseColors();
            tip = new Vector2(edgeX, 210.5f + offset);
            tray.Apply(SandTool.Vacuum, tip, tip, .2f, 0, 1f / 60);
            var opened = tray.Capture();
            Require(!EdgeAt(opened, 210) && EdgeAt(opened, 211),
                "A shared edge must open in one pass even when both sections are completed.");
            tray.Restore(opened); Settle();
            Require(tray.Heights[Gap(210)] > 0,
                "Opening the shared edge must allow settling into the neighboring color after reload.");
            return "Section lip checks passed: walls on empty trays, shared edges before completion, movement within a section, painting cleanup, tall-pile overflow, intact Let go, local sweep and vacuum openings, settling and mass, undo, saves and legacy migration.";
        }

        [MenuItem("Tools/Mandala/Run Completion Color Rendering Checks")]
        public static string RunRendering()
        {
            var tray = Painting(out var progress);
            // Buried and exposed wrong pigment throughout the completed picture.
            for (int i = 0; i < SandTray.CellCount; i++) if (tray.Guide[i] >= 0)
                for (int h = 0; h < 5; h++) tray.Add(i, (tray.Guide[i] + 2) % 5);
            var original = tray.Capture();
            using (var surface = new SandTexture(progress.Sections.Count))
            {
                surface.SetViewport(512, new Rect(0, 0, 1, 1));
                surface.Render(tray, progress, false, 10); var assisted = Read(surface.Texture);
                SameMaterial(original, tray.Capture());
                tray.ReleaseColors(); surface.Render(tray, progress, false, 10); var baked = Read(surface.Texture);
                int different = 0;
                for (int i = 0; i < assisted.Length; i++) if (!assisted[i].Equals(baked[i])) different++;
                Require(different == 0, "The GPU-rendered picture must remain identical when its colors are baked for sweeping.");
                tray.Restore(original); tray.Protection = false;
                surface.Render(tray, progress, false, 10); var unassisted = Read(surface.Texture);
                different = 0;
                for (int i = 0; i < assisted.Length; i++) if (!assisted[i].Equals(unassisted[i])) different++;
                Require(different > 10000, "Completed-section correction must be visible in actual GPU output.");
            }
            return "Completion GPU checks passed: wrong original pigments visibly corrected, rendering leaves material unchanged, and the entire picture is pixel-identical across release.";
        }
    }
}
