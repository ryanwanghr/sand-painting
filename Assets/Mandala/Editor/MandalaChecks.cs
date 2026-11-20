using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;

namespace Mandala.Game.Editor
{
    public static class MandalaChecks
    {
        const int Scale = SandTray.GridScale, Center = SandTray.Size / 2;
        static Vector2 Point(float x, float y) => new Vector2(x, y) * Scale;

        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        [MenuItem("Tools/Mandala/Run Round Pile Checks")]
        public static string RunPileShape()
        {
            float worstRatio = 0;
            foreach (int seed in new[] { 17, 42 })
            {
                var tray = new SandTray(seed) { Protection = false };
                Vector2 center = seed == 42 ? Point(220, 220) : Point(220.25f, 219.75f);
                int emitted = 0;
                float previousRadius = 0;
                int previousFootprint = 0;
                for (int frame = 1; frame <= 3600; frame++)
                {
                    emitted += tray.Apply(SandTool.Pour, center, center, SandTray.PourRadius, 0, 1f / 60);
                    tray.Step(1f / 60);
                    if (frame % 60 == 0) Require(tray.Mass() + tray.Grains.Count == emitted,
                        "Spreading a held pour must conserve every accepted grain.");
                    if (frame != 120 && frame != 360 && frame != 720 && frame != 1800 && frame != 3600) continue;
                    var radii = new float[16];
                    Vector2 moment = Vector2.zero;
                    int mass = tray.Mass(), footprint = 0;
                    for (int i = 0; i < SandTray.CellCount; i++)
                    {
                        int height = tray.Heights[i]; if (height == 0) continue;
                        footprint++;
                        Vector2 delta = new Vector2(i % SandTray.Size + .5f, i / SandTray.Size + .5f) - center;
                        int sector = Mathf.FloorToInt((Mathf.Atan2(delta.y, delta.x) + Mathf.PI) * 8 / Mathf.PI) % 16;
                        radii[sector] = Mathf.Max(radii[sector], delta.magnitude);
                        moment += delta * height;
                    }
                    float min = float.PositiveInfinity, max = 0, average = 0;
                    foreach (float radius in radii) { min = Mathf.Min(min, radius); max = Mathf.Max(max, radius); average += radius / 16; }
                    float ratio = max / min;
                    worstRatio = Mathf.Max(worstRatio, ratio);
                    Require(ratio < 1.15f, "Held pours must grow roughly circularly rather than along grid axes or diagonals.");
                    Require((moment / mass).magnitude < .5f * Scale, "A stationary pour must remain centered beneath its tip.");
                    if (previousRadius > 0 && frame <= 720) Require(average > previousRadius * 1.05f && footprint > previousFootprint * 1.10f,
                        "Holding a pour longer must spread its footprint outward.");
                    previousRadius = average; previousFootprint = footprint;
                    if (frame < 720) continue;
                    // Sparse outliers can hide a square dense body; check the opaque contour separately.
                    var denseRadii = new float[32];
                    for (int i = 0; i < SandTray.CellCount; i++)
                    {
                        if (tray.Heights[i] < 5) continue;
                        Vector2 delta = new Vector2(i % SandTray.Size + .5f, i / SandTray.Size + .5f) - center;
                        int sector = Mathf.FloorToInt((Mathf.Atan2(delta.y, delta.x) + Mathf.PI) * 16 / Mathf.PI) % 32;
                        denseRadii[sector] = Mathf.Max(denseRadii[sector], delta.magnitude);
                    }
                    float denseMin = float.PositiveInfinity, denseMax = 0;
                    foreach (float radius in denseRadii) { denseMin = Mathf.Min(denseMin, radius); denseMax = Mathf.Max(denseMax, radius); }
                    Require(denseMax / denseMin < 1.10f,
                        $"The opaque pile contour must remain round during a {frame / 60}-second hold (seed {seed}, ratio {denseMax / denseMin:F3}).");
                }
                tray.EndStroke();
                for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
                Require(tray.Grains.Count == 0 && tray.Mass() == emitted, "Releasing a round pile must land its tail without losing sand.");
                foreach (var grain in tray.Layers) Require(grain == 0, "Settling must retain the poured color.");
            }
            var blocked = new SandTray(42);
            int source = Center * SandTray.Size + Center;
            for (int y = Center - 1; y <= Center + 1; y++) for (int x = Center - 1; x <= Center + 1; x++)
            {
                int i = y * SandTray.Size + x;
                for (int h = 0; h < SandTray.Levels; h++) blocked.Add(i, 0);
                if (i != source) blocked.Completed[i] = true; // Assistance must not alter physical barriers.
            }
            int before = blocked.Mass();
            blocked.Step(1f / 60);
            Require(blocked.Heights[source] == SandTray.Levels && blocked.Mass() == before,
                "Short rolling paths must not leap over a full ring of raised sand.");
            blocked.Clear(); blocked.Protection = false;
            for (int y = Center - 1; y <= Center + 1; y++) for (int x = Center - 1; x <= Center + 1; x++)
            {
                int i = y * SandTray.Size + x;
                for (int h = 0; h < (i == source ? 12 : SandTray.Levels); h++) blocked.Add(i, i == source ? 1 : 0);
            }
            blocked.Step(1f / 60);
            Require(blocked.Heights[source] == 12 && blocked.Top(source) == 1,
                "Sand must not roll uphill over an intervening raised barrier.");
            return $"Round pile checks passed: 2-, 6-, 12-, 30-, and 60-second held pours, two seeds and tip alignments, round opaque contours, worst outer sector radius ratio {worstRatio:F2}, centered growth, exact mass and color conservation, release tail, raised barriers with and without color assistance.";
        }

        [MenuItem("Tools/Mandala/Run Pour Checks")]
        public static string RunPour()
        {
            var center = Point(220, 220);
            SandTray Pour(int frequency, float seconds, Vector2 from, Vector2 to, float radius = SandTray.PourRadius)
            {
                var sand = new SandTray(42) { Protection = false };
                int frames = Mathf.RoundToInt(seconds * frequency);
                for (int frame = 0; frame < frames; frame++)
                {
                    sand.Apply(SandTool.Pour, Vector2.Lerp(from, to, (float)frame / frames),
                        Vector2.Lerp(from, to, (float)(frame + 1) / frames), radius, 0, 1f / frequency);
                    sand.Step(1f / frequency);
                }
                return sand;
            }
            int Total(SandTray sand) => sand.Mass() + sand.Grains.Count;
            var tray = Pour(60, 1, center, center);
            var saved = tray.Capture();
            float core = 0, fringe = 0; int coreCells = 0, fringeCells = 0;
            float radiusSq = SandTray.PourRadius * SandTray.PourRadius;
            int extent = Mathf.CeilToInt(SandTray.PourRadius);
            for (int y = Center - extent; y <= Center + extent; y++) for (int x = Center - extent; x <= Center + extent; x++)
            {
                float distance = (new Vector2(x + .5f, y + .5f) - center).sqrMagnitude;
                int height = saved.Heights[y * SandTray.Size + x];
                if (distance < radiusSq * .25f) { core += height; coreCells++; }
                if (distance > radiusSq * .5625f && distance < radiusSq) { fringe += height; fringeCells++; }
            }
            core /= coreCells; fringe /= fringeCells;
            Require(core >= 3 && core > fringe * 1.8f && fringe > 0,
                "A one-second pour must build a substantial core with a lighter, nonempty fringe.");

            var longer = Pour(60, 2, center, center);
            float flowGain = Total(longer) / (Mathf.PI * 16 * 16 * 3 * 2 * SandTray.AreaScale);
            Require(flowGain > 1.4f && flowGain < 1.6f, "The smaller nozzle must pour about 1.5 times as much sand as the previous stream.");
            var wider = Pour(60, 2, center, center, 16 * Scale);
            Require(Mathf.Abs(Total(wider) - Total(longer)) < Total(wider) * .05f,
                "Changing nozzle area must preserve the stream's total flow.");

            int savedMass = 0;
            foreach (byte height in saved.Heights) savedMass += height;
            Require(savedMass == Total(tray), "A mid-pour snapshot must retain all landed and falling sand.");
            tray.EndStroke();
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            Require(tray.Grains.Count == 0 && tray.Mass() == savedMass, "The final pour tail must land without adding or losing material.");
            tray.Clear(); tray.Restore(saved);
            Require(tray.Mass() == savedMass && tray.Grains.Count == 0, "Undo must restore the complete deposited pile.");

            Vector2 start = Point(120, 220), end = Point(320, 220);
            var coarse = Pour(30, 2, start, end); var fine = Pour(120, 2, start, end);
            Require(Mathf.Abs(Total(coarse) - Total(fine)) < Total(fine) * .05f,
                "Moving pour volume must stay consistent across stroke sampling rates.");
            int covered = 0;
            var moving = fine.Capture();
            for (int x = 140 * Scale; x < 300 * Scale; x++)
            {
                bool found = false;
                for (int y = 217 * Scale; y <= 223 * Scale; y++) found |= moving.Heights[y * SandTray.Size + x] > 0;
                if (found) covered++;
            }
            Require(covered >= 155 * Scale, "Moving pours must leave a continuous central trail.");

            tray.Clear();
            for (int i = 0; i < SandTray.CellCount; i++) tray.Completed[i] = true;
            Require(tray.Apply(SandTool.Pour, center, center, SandTray.PourRadius, 4, 1) > 0 && tray.Grains.Count > 0,
                "Completed cells must accept wrong-color sand without changing its stored pigment.");
            Require(tray.Apply(SandTool.Pour, center, center, SandTray.PourRadius, 0, 1) > 0,
                "Completed cells must also accept their matching pour color.");

            tray.Clear(); tray.Protection = false;
            var emitted = new int[5];
            for (int frame = 0; frame < 90; frame++)
            {
                int color = frame / 18;
                emitted[color] += tray.Apply(SandTool.Pour, center, center, SandTray.PourRadius, color, 1f / 60);
                tray.Step(1f / 60);
            }
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            var actual = new int[5];
            for (int i = 0; i < SandTray.CellCount; i++)
                for (int h = 0; h < tray.Heights[i]; h++) actual[tray.Layers[i * SandTray.Levels + h]]++;
            foreach (var rolling in tray.RollingGrains) actual[rolling.Color]++;
            for (int color = 0; color < 5; color++) Require(emitted[color] > 0 && actual[color] == emitted[color],
                "Switching pour colors with grains still falling must preserve every emitted color.");

            tray.Clear(); tray.Protection = false;
            tray.Apply(SandTool.Pour, center, center, SandTray.PourRadius, 0, 10);
            var saturated = tray.Capture();
            foreach (byte height in saturated.Heights) Require(height <= SandTray.Levels, "Concentrated pours must respect stack capacity including pending grains.");
            int saturatedMass = Total(tray);
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            Require(tray.Grains.Count == 0 && tray.Mass() == saturatedMass, "A saturated pour must retain all accepted grains on landing.");
            CheckFullColumnCorrection();
            return $"Pour checks passed: {flowGain:F2}x average flow, {core:F1}-layer core / {fringe:F1}-layer fringe after one second, moving trail, sampling consistency, landing, undo, completion assistance, five-color conservation, stack capacity, full-column color correction.";
        }

        static void CheckFullColumnCorrection()
        {
            var tray = new SandTray(42);
            int cell = Center * SandTray.Size + Center, top = cell * SandTray.Levels + SandTray.Levels - 1;
            int matching = tray.Guide[cell], wrong = (matching + 1) % 5;
            var tip = Vector2.one * (Center + .5f);
            int Pour(int color) => tray.Apply(SandTool.Pour, tip, tip, .45f, color, .001f);

            // A flat patch keeps the test column full when its neighbors start settling.
            for (int y = Center - 12; y <= Center + 12; y++)
            for (int x = Center - 12; x <= Center + 12; x++)
            for (int h = 0; h < SandTray.Levels; h++) tray.Add(y * SandTray.Size + x, wrong);

            tray.Remove(cell);
            Require(Pour(wrong) == 1 && Pour(matching) == 0 && tray.Heights[cell] == SandTray.Levels - 1,
                "A column with a reserved final slot must wait for its incoming grain to land.");
            tray.Step(.25f);
            var before = tray.Capture();
            int mass = tray.Mass(), changes = tray.Changes;
            Require(Pour(wrong) == 0 && Pour(matching) == 1 && Pour(matching) == 0 && tray.Top(cell) == wrong,
                "A full wrong-color column must emit exactly one matching grain and keep its old color until landing.");
            var inFlight = tray.Capture();
            Require(inFlight.Heights[cell] == SandTray.Levels && inFlight.Layers[top] == matching,
                "A snapshot must include the incoming replacement without increasing the column height.");
            tray.Step(.25f);
            Require(tray.Heights[cell] == SandTray.Levels && tray.Top(cell) == matching && tray.Mass() == mass && tray.Changes > changes,
                "Landing must replace the surface grain and mark the tray changed while keeping its height and mass.");
            for (int h = 1; h < SandTray.Levels; h++)
                Require(tray.Layers[top - h] == before.Layers[top - h], "Replacement must preserve the buried layers.");
            Require(Pour(matching) == 0, "A full matching column must reject redundant grains.");

            tray.Restore(before);
            Require(tray.Top(cell) == wrong && Pour(matching) == 1, "Undo must restore the original surface and clear pour reservations.");
            tray.Remove(cell);
            Require(Pour(wrong) == 0, "An airborne replacement must retain its landing slot if the surface is removed.");
            tray.Step(.25f);
            Require(tray.Heights[cell] == SandTray.Levels && tray.Top(cell) == matching,
                "A replacement must fill a slot that opens before it lands.");

            tray.Restore(before);
            tray.Completed[cell] = true;
            Require(Pour(matching) == 0, "Completed sections must keep the normal capacity limit.");
            tray.Restore(before);
            tray.ReleaseColors();
            Require(Pour(matching) == 0, "Free shaping must keep the normal capacity limit.");
        }

        [MenuItem("Tools/Mandala/Run Vacuum Animation Checks")]
        public static string RunVacuum()
        {
            var tray = new SandTray(42) { Protection = false };
            var center = Point(220, 220);
            for (int y = 195 * Scale; y < 245 * Scale; y++) for (int x = 195 * Scale; x < 245 * Scale; x++)
                tray.Add(y * SandTray.Size + x, x % 5);
            var before = tray.Capture();
            int mass = tray.Mass();
            int removed = tray.Apply(SandTool.Vacuum, center, center, 32 * Scale, 0, 1f / 60);
            Require(Mathf.Abs(removed / (float)SandTray.AreaScale - 120) <= 1, "Vacuum must remove the same tray volume per tick.");
            Require(removed > 0 && tray.SuctionGrains.Count == removed, "Removed sand must enter the suction animation.");
            Require(tray.Mass() == mass - removed, "Suction visuals must not count as settled sand.");
            int savedMass = 0;
            foreach (byte height in tray.Capture().Heights) savedMass += height;
            Require(savedMass == tray.Mass(), "Saving mid-vacuum must not restore removed grains.");
            var first = tray.SuctionGrains[0];
            Require(first.Color == ((int)first.Start.x % 5), "Suction must retain the actual removed color.");
            tray.Step(1f / 60);
            Require(tray.SuctionGrains[0].Position != first.Position, "Suction must visibly move on its first frame.");
            for (int i = 0; i < 8; i++) tray.Step(1f / 60);
            var curled = tray.SuctionGrains[0];
            Vector2 start = first.Start - center, current = curled.Position - center;
            Require(Vector2.Distance(curled.Position, center) < start.magnitude, "Grains must move inward.");
            Require(Mathf.Abs(start.x * current.y - start.y * current.x) > .01f, "The inward path must curl.");
            tray.EndStroke();
            for (int i = 0; i < 40; i++) tray.Step(1f / 60);
            Require(tray.SuctionGrains.Count == 0 && tray.Mass() == savedMass, "Release must finish the tail without re-landing sand.");

            tray.Restore(before);
            for (int i = 0; i < 90; i++)
            {
                var point = Point(210 + i % 20, 220);
                tray.Apply(SandTool.Vacuum, point - Vector2.right * (12 * Scale), point, 32 * Scale, 0, 1f / 60);
                tray.Step(1f / 60);
                Require(tray.SuctionGrains.Count <= SandTray.MaxSuctionGrains, "Sustained suction must keep its particle budget.");
                foreach (var grain in tray.SuctionGrains)
                    Require(float.IsFinite(grain.Position.x) && float.IsFinite(grain.Position.y), "Moving suction must remain finite.");
            }
            tray.Restore(before);
            Require(tray.SuctionGrains.Count == 0 && tray.Mass() == mass, "Undo must restore sand and discard suction visuals.");
            tray.Apply(SandTool.Vacuum, center, center, 32 * Scale, 0, 1f / 60);
            tray.Clear();
            Require(tray.SuctionGrains.Count == 0, "Clear must discard suction visuals.");

            int cell = Center * SandTray.Size + 240 * Scale;
            tray.Guide[cell] = 1; tray.Completed[cell] = true; tray.Protection = true;
            tray.Add(cell, 1); tray.Add(cell, 2);
            int gap = cell + 1, unfinished = cell + 2;
            tray.Guide[gap] = -1; tray.Add(gap, 3);
            tray.Guide[unfinished] = 4; tray.Add(unfinished, 0);
            tray.Apply(SandTool.Vacuum, center, center, 32 * Scale, 0, 1);
            Require(tray.Heights[cell] == 2 && tray.Top(cell) == 2 && tray.Layers[cell * SandTray.Levels] == 1,
                "Painting vacuum must preserve every layer inside a completed section, including its original wrong pigments.");
            Require(tray.Heights[gap] == 0 && tray.Heights[unfinished] == 0 && tray.SuctionGrains.Count == 2,
                "The same vacuum stroke must still clean gaps and unfinished sections beside a completed edge.");
            var painting = tray.Capture();
            tray.ReleaseColors();
            Require(tray.Apply(SandTool.Vacuum, center, center, 32 * Scale, 0, 1) == 2 && tray.Heights[cell] == 0,
                "After Let go, the vacuum must remove all formerly completed sand.");
            tray.Restore(painting);
            Require(tray.Apply(SandTool.Vacuum, center, center, 32 * Scale, 0, 1) == 0 && tray.Heights[cell] == 2,
                "Undoing release must restore completed-section vacuum protection.");

            tray.Clear();
            int source = Center * SandTray.Size + Center;
            for (int h = 0; h < 11; h++) tray.Add(source, 0);
            tray.Add(source, 1); tray.Step(.005f);
            Require(tray.RollingGrains.Count == 1, "The boundary fixture must contain a real reserved rolling transfer.");
            var rolling = tray.RollingGrains[0];
            tray.Completed[source] = true;
            int rollingMass = tray.Mass();
            Require(tray.Apply(SandTool.Vacuum, rolling.Position, rolling.Position, .2f, 0, .05f) == 0
                && tray.RollingGrains.Count == 1 && tray.Mass() == rollingMass,
                "A rolling grain currently inside a completed section must be preserved even if its destination is unfinished.");
            tray.Completed[source] = false; tray.Completed[rolling.Target] = true;
            int removedRolling = tray.Apply(SandTool.Vacuum, rolling.Position, rolling.Position, .2f, 0, .05f);
            Require(removedRolling > 0 && tray.RollingGrains.Count == 0 && tray.Mass() == rollingMass - removedRolling,
                "A rolling grain outside a completed section must remain removable even when heading into one.");
            for (int frame = 0; frame < 60; frame++) tray.Step(1f / 60);
            Require(tray.Mass() == rollingMass - removedRolling, "Vacuuming a boundary transfer must release its landing reservation.");
            string result = "Vacuum animation checks passed: motion, original colors, bounded particles, release tail, saves, undo, completed-section protection, nearby cleanup, unrestricted release, rolling-position boundaries and reservations.";
            Debug.Log(result);
            return result;
        }
        [MenuItem("Tools/Mandala/Run Gameplay Checks")]
        public static string Run()
        {
            var tray = new SandTray(42); var regions = new MandalaProgress(tray);
            var center = Point(220, 220);
            for (int i = 0; i < 30; i++) { tray.Apply(SandTool.Pour, center, center, 16 * Scale, 0, 1f / 60); tray.Step(1f / 60); }
            Require(tray.Grains.Count > 0, "Pour must have airborne grains.");
            var snapshot = tray.Capture(); int captured = 0; foreach (byte h in snapshot.Heights) captured += h;
            Require(captured == tray.Mass() + tray.Grains.Count, "Snapshot must retain airborne grains.");
            for (int i = 0; i < 30; i++) tray.Step(1f / 60);
            Require(tray.Mass() == captured, "Landing/settling must conserve mass.");
            int mass = tray.Mass(); tray.Protection = false;
            int swept = tray.Apply(SandTool.Sweep, Point(200, 220), Point(240, 220), 24 * Scale, 0, 1f / 60);
            Require(swept > 0 && tray.Mass() == mass, "Sweep must move and conserve grains.");
            tray.Apply(SandTool.Vacuum, center, center, 64 * Scale, 0, 1); Require(tray.Mass() < mass, "Vacuum must remove sand.");
            tray.Restore(snapshot); Require(tray.Mass() == captured && tray.Grains.Count == 0, "Restore must land saved grains.");
            tray.Clear();
            for (int i = 0; i < SandTray.CellCount; i++) if (tray.Guide[i] >= 0)
                for (int h = 0; h < 3; h++) tray.Add(i, tray.Guide[i]);
            regions.Update(tray, 0, false); Require(regions.AllComplete && regions.Clean, "Completed pattern should be clean and color assisted.");
            int completedMass = tray.Mass();
            Require(!regions.CheckWin(tray, 0, false) && !regions.CheckWin(tray, .9f, false) && regions.CheckWin(tray, 1.1f, false), "Victory requires a settled second.");
            Require(!regions.CheckWin(tray, 3, false), "Victory must only fire once.");
            for (int i = 0; i < SandTray.CellCount; i++)
                if (tray.Guide[i] < 0 && SandTray.Playable(i % SandTray.Size, i / SandTray.Size)) tray.Add(i, 0);
            regions.Update(tray, 4, false);
            Require(!regions.Clean, "Filled negative space must block a clean finish.");
            tray.HasWon = false;
            Require(!regions.CheckWin(tray, 6, false), "Colored sections alone cannot win with dirty gaps.");
            for (int i = 0; i < SandTray.CellCount; i++) if (tray.Guide[i] < 0) while (tray.Heights[i] > 0) tray.Remove(i);
            regions.Update(tray, 7, false);
            Require(!regions.CheckWin(tray, 7, false), "Clean tray starts a new settling interval.");
            Require(!regions.CheckWin(tray, 7.8f, true), "Active stroke interrupts settling.");
            Require(!regions.CheckWin(tray, 8, false) && regions.CheckWin(tray, 9.1f, false), "Interrupted settling must restart the full second.");
            tray.ReleaseColors(); tray.Apply(SandTool.Vacuum, center, center, 30 * Scale, 0, 1);
            Require(tray.Mass() < completedMass, "Sweeping-stage sand must remain removable.");
            string path = Path.Combine(Path.GetTempPath(), "mandala-check-" + Guid.NewGuid() + ".sand");
            try
            {
                var saved = tray.Capture(); MandalaSave.Write(path, saved); var loaded = MandalaSave.Read(path);
                Require(loaded.HasWon && !loaded.Protection, "Save must retain game phase.");
                for (int i = 0; i < SandTray.CellCount; i++) Require(saved.Heights[i] == loaded.Heights[i], "Save heights mismatch.");
                MandalaSave.Write(path, snapshot); Require(File.Exists(path + ".bak"), "Save must preserve backup.");
                CheckLegacyGrid(path);
                File.WriteAllBytes(path, new byte[] { 0, 1, 2 }); bool rejected = false;
                try { MandalaSave.Read(path); } catch { rejected = true; } Require(rejected, "Corrupt saves must be rejected.");
            }
            finally { foreach (string suffix in new[] { "", ".tmp", ".bak" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
            Debug.Log("Mandala gameplay checks passed: pour, landing, sweep, vacuum, undo, color assistance, win timing, persistence, corruption.");
            return "All gameplay checks passed; " + regions.Sections.Count + " pattern sections.";
        }

        static void CheckLegacyGrid(string path)
        {
            const int oldSize = SandTray.ReferenceSize, oldCount = oldSize * oldSize;
            const int source = oldSize / 2 * (oldSize + 1);
            foreach (var (version, depth) in new[] { (1, 2), (1, 3), (2, 2), (3, 2) })
            {
                // Version 1 earns completion from the old guide and pigment, without a mask.
                bool painting = version == 1;
                int oldMass = 0;
                using (var file = File.Create(path))
                using (var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
                using (var writer = new BinaryWriter(zip))
                {
                    writer.Write(0x4D414E44); writer.Write(version); writer.Write(oldSize); writer.Write(SandTray.Levels);
                    writer.Write(painting); writer.Write(true);
                    for (int i = 0; i < oldCount; i++)
                    {
                        bool filled = i == source;
                        float dx = i % oldSize + .5f - oldSize / 2f, dy = i / oldSize + .5f - oldSize / 2f;
                        const float goldRadius = oldSize * .43f * .12f;
                        bool gold = painting && dx * dx + dy * dy < goldRadius * goldRadius;
                        int height = filled || gold ? depth : 0;
                        if (gold && i == source - 1) height = 2;
                        oldMass += height;
                        writer.Write((byte)height);
                        for (int h = 0; h < height; h++)
                            writer.Write((byte)(filled ? (h == height - 1 ? 4 : 1) : 0));
                        if (version >= 2) writer.Write(filled);
                    }
                    if (version == 3)
                        for (int i = 0; i < oldCount; i++) writer.Write((byte)(i == source ? 1 : 0));
                }
                var state = MandalaSave.Read(path);
                Require(state.HasWon && state.Protection == painting, "Grid migration must preserve the saved game phase.");
                int mass = 0; foreach (byte height in state.Heights) mass += height;
                Require(mass == oldMass * SandTray.AreaScale, "Grid migration must conserve sand volume.");
                for (int dy = 0; dy < Scale; dy++) for (int dx = 0; dx < Scale; dx++)
                {
                    int cell = (Center + dy) * SandTray.Size + Center + dx;
                    Require(state.Heights[cell] == depth, "Grid migration must preserve depth.");
                    for (int h = 0; h < depth; h++)
                        Require(state.Layers[cell * SandTray.Levels + h] == (h == depth - 1 ? 4 : 1),
                            "Grid migration must preserve every pigment in order.");
                    Require(state.Completed[cell] == (version >= 2 || depth >= 3),
                        "Grid migration must preserve saved completion and tolerate a shallow cell within the 98% threshold.");
                    Require(version == 3 ? state.Lips[cell] == (dx == Scale - 1 ? 1 : 0) : state.Lips == null,
                        "Grid migration must expand intact edges and leave broken edges open.");
                }
            }
        }
    }
}
