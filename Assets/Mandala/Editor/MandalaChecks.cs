using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Mandala.Game.Editor
{
    public static class MandalaChecks
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        [MenuItem("Tools/Mandala/Run Pour Checks")]
        public static string RunPour()
        {
            var center = new Vector2(220, 220);
            SandTray Pour(int frequency, float seconds, Vector2 from, Vector2 to)
            {
                var sand = new SandTray(42) { Protection = false };
                int frames = Mathf.RoundToInt(seconds * frequency);
                for (int frame = 0; frame < frames; frame++)
                {
                    sand.Apply(SandTool.Pour, Vector2.Lerp(from, to, (float)frame / frames),
                        Vector2.Lerp(from, to, (float)(frame + 1) / frames), 16, 0, 1f / frequency);
                    sand.Step(1f / frequency);
                }
                return sand;
            }
            int Total(SandTray sand) => sand.Mass() + sand.Grains.Count;
            var tray = Pour(60, 1, center, center);
            var saved = tray.Capture();
            float core = 0, fringe = 0; int coreCells = 0, fringeCells = 0;
            for (int y = 204; y <= 236; y++) for (int x = 204; x <= 236; x++)
            {
                float distance = (new Vector2(x + .5f, y + .5f) - center).sqrMagnitude;
                int height = saved.Heights[y * SandTray.Size + x];
                if (distance < 64) { core += height; coreCells++; }
                if (distance > 144 && distance < 256) { fringe += height; fringeCells++; }
            }
            core /= coreCells; fringe /= fringeCells;
            Require(core >= 3 && core > fringe * 1.8f && fringe > 0,
                "A one-second pour must build a substantial core with a lighter, nonempty fringe.");

            int footprint = 0;
            for (int y = 204; y <= 236; y++) for (int x = 204; x <= 236; x++)
                if ((new Vector2(x + .5f, y + .5f) - center).sqrMagnitude <= 256) footprint++;
            var longer = Pour(60, 2, center, center);
            float flowGain = (float)Total(longer) / (footprint * 2 * 2);
            Require(flowGain > 1.4f && flowGain < 1.6f, "The average pour flow must be about 1.5 times its previous two layers per second.");

            int savedMass = 0;
            foreach (byte height in saved.Heights) savedMass += height;
            Require(savedMass == Total(tray), "A mid-pour snapshot must retain all landed and falling sand.");
            tray.EndStroke();
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            Require(tray.Grains.Count == 0 && tray.Mass() == savedMass, "The final pour tail must land without adding or losing material.");
            tray.Clear(); tray.Restore(saved);
            Require(tray.Mass() == savedMass && tray.Grains.Count == 0, "Undo must restore the complete deposited pile.");

            Vector2 start = new Vector2(120, 220), end = new Vector2(320, 220);
            var coarse = Pour(30, 2, start, end); var fine = Pour(120, 2, start, end);
            Require(Mathf.Abs(Total(coarse) - Total(fine)) < Total(fine) * .05f,
                "Moving pour volume must stay consistent across stroke sampling rates.");
            int covered = 0;
            var moving = fine.Capture();
            for (int x = 140; x < 300; x++)
            {
                bool found = false;
                for (int y = 217; y <= 223; y++) found |= moving.Heights[y * SandTray.Size + x] > 0;
                if (found) covered++;
            }
            Require(covered >= 155, "Moving pours must leave a continuous central trail.");

            tray.Clear();
            for (int i = 0; i < SandTray.CellCount; i++) tray.Locked[i] = true;
            Require(tray.Apply(SandTool.Pour, center, center, 16, 4, 1) == 0 && tray.Grains.Count == 0,
                "Protected cells must reject a wrong-color concentrated pour.");
            Require(tray.Apply(SandTool.Pour, center, center, 16, 0, 1) > 0,
                "Protected cells must still accept their matching pour color.");

            tray.Clear(); tray.Protection = false;
            var emitted = new int[5];
            for (int frame = 0; frame < 90; frame++)
            {
                int color = frame / 18;
                emitted[color] += tray.Apply(SandTool.Pour, center, center, 16, color, 1f / 60);
                tray.Step(1f / 60);
            }
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            var actual = new int[5];
            for (int i = 0; i < SandTray.CellCount; i++)
                for (int h = 0; h < tray.Heights[i]; h++) actual[tray.Layers[i * SandTray.Levels + h]]++;
            for (int color = 0; color < 5; color++) Require(emitted[color] > 0 && actual[color] == emitted[color],
                "Switching pour colors with grains still falling must preserve every emitted color.");

            tray.Clear(); tray.Protection = false;
            tray.Apply(SandTool.Pour, center, center, 16, 0, 10);
            var saturated = tray.Capture();
            foreach (byte height in saturated.Heights) Require(height <= SandTray.Levels, "Concentrated pours must respect stack capacity including pending grains.");
            int saturatedMass = Total(tray);
            for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
            Require(tray.Grains.Count == 0 && tray.Mass() == saturatedMass, "A saturated pour must retain all accepted grains on landing.");
            return $"Pour checks passed: {flowGain:F2}x average flow, {core:F1}-layer core / {fringe:F1}-layer fringe after one second, moving trail, sampling consistency, landing, undo, protection, five-color conservation, stack capacity.";
        }

        [MenuItem("Tools/Mandala/Run Vacuum Animation Checks")]
        public static string RunVacuum()
        {
            var tray = new SandTray(42) { Protection = false };
            var center = new Vector2(220, 220);
            for (int y = 195; y < 245; y++) for (int x = 195; x < 245; x++)
                tray.Add(y * SandTray.Size + x, x % 5);
            var before = tray.Capture();
            int mass = tray.Mass();
            int removed = tray.Apply(SandTool.Vacuum, center, center, 32, 0, 1f / 60);
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
                var point = new Vector2(210 + i % 20, 220);
                tray.Apply(SandTool.Vacuum, point - Vector2.right * 12, point, 32, 0, 1f / 60);
                tray.Step(1f / 60);
                Require(tray.SuctionGrains.Count <= 3072, "Sustained suction must keep its particle budget.");
                foreach (var grain in tray.SuctionGrains)
                    Require(float.IsFinite(grain.Position.x) && float.IsFinite(grain.Position.y), "Moving suction must remain finite.");
            }
            tray.Restore(before);
            Require(tray.SuctionGrains.Count == 0 && tray.Mass() == mass, "Undo must restore sand and discard suction visuals.");
            tray.Apply(SandTool.Vacuum, center, center, 32, 0, 1f / 60);
            tray.Clear();
            Require(tray.SuctionGrains.Count == 0, "Clear must discard suction visuals.");

            int cell = 220 * SandTray.Size + 240;
            tray.Guide[cell] = 1; tray.Locked[cell] = true; tray.Protection = true;
            tray.Add(cell, 1); tray.Add(cell, 2);
            tray.Apply(SandTool.Vacuum, center, center, 32, 0, 1);
            Require(tray.Heights[cell] == 1 && tray.Top(cell) == 1, "Vacuum must stop at correct protected sand.");
            Require(tray.SuctionGrains.Count == 1 && tray.SuctionGrains[0].Color == 2, "Only stray sand may animate out of a protected section.");
            string result = "Vacuum animation checks passed: first-frame motion, curl, attraction, original colors, finite moving paths, bounded particles, release tail, saves, undo, clear, protected layers.";
            Debug.Log(result);
            return result;
        }
        [MenuItem("Tools/Mandala/Run Gameplay Checks")]
        public static string Run()
        {
            var tray = new SandTray(42); var regions = new MandalaProgress(tray);
            var center = new Vector2(220, 220);
            for (int i = 0; i < 30; i++) { tray.Apply(SandTool.Pour, center, center, 16, 0, 1f / 60); tray.Step(1f / 60); }
            Require(tray.Grains.Count > 0, "Pour must have airborne grains.");
            var snapshot = tray.Capture(); int captured = 0; foreach (byte h in snapshot.Heights) captured += h;
            Require(captured == tray.Mass() + tray.Grains.Count, "Snapshot must retain airborne grains.");
            for (int i = 0; i < 30; i++) tray.Step(1f / 60);
            Require(tray.Mass() == captured, "Landing/settling must conserve mass.");
            int mass = tray.Mass(); tray.Protection = false;
            int swept = tray.Apply(SandTool.Sweep, new Vector2(200,220), new Vector2(240,220), 24, 0, 1f / 60);
            Require(swept > 0 && tray.Mass() == mass, "Sweep must move and conserve grains.");
            tray.Apply(SandTool.Vacuum, center, center, 64, 0, 1); Require(tray.Mass() < mass, "Vacuum must remove sand.");
            tray.Restore(snapshot); Require(tray.Mass() == captured && tray.Grains.Count == 0, "Restore must land saved grains.");
            tray.Clear();
            for (int i = 0; i < SandTray.CellCount; i++) if (tray.Guide[i] >= 0) tray.Add(i, tray.Guide[i]);
            regions.Update(tray, 0, false); Require(regions.AllComplete && regions.Clean, "Completed pattern should be clean and locked.");
            int protectedMass = tray.Mass(); tray.Apply(SandTool.Vacuum, center, center, 30, 0, 1);
            Require(tray.Mass() == protectedMass, "Vacuum must preserve correct protected sand.");
            tray.Apply(SandTool.Pour, center, center, 16, 4, 1); Require(tray.Grains.Count == 0, "Locked section must reject wrong colors.");
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
            tray.Protection = false; tray.Apply(SandTool.Vacuum, center, center, 30, 0, 1);
            Require(tray.Mass() < protectedMass, "Let go must remove protection.");
            string path = Path.Combine(Path.GetTempPath(), "mandala-check-" + Guid.NewGuid() + ".sand");
            try
            {
                var saved = tray.Capture(); MandalaSave.Write(path, saved); var loaded = MandalaSave.Read(path);
                Require(loaded.HasWon && !loaded.Protection, "Save must retain game phase.");
                for (int i = 0; i < SandTray.CellCount; i++) Require(saved.Heights[i] == loaded.Heights[i], "Save heights mismatch.");
                MandalaSave.Write(path, snapshot); Require(File.Exists(path + ".bak"), "Save must preserve backup.");
                File.WriteAllBytes(path, new byte[] { 0, 1, 2 }); bool rejected = false;
                try { MandalaSave.Read(path); } catch { rejected = true; } Require(rejected, "Corrupt saves must be rejected.");
            }
            finally { foreach (string suffix in new[] { "", ".tmp", ".bak" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
            Debug.Log("Mandala gameplay checks passed: pour, landing, sweep, vacuum, undo, protection, win timing, persistence, corruption.");
            return "All gameplay checks passed; " + regions.Sections.Count + " pattern sections.";
        }
    }
}
