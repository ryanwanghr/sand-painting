using System;
using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    public static class MandalaRakeChecks
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static int[] Colors(SandTray tray)
        {
            var count = new int[5];
            for (int i = 0; i < SandTray.CellCount; i++)
            for (int h = 0; h < tray.Heights[i]; h++) count[tray.Layers[i * SandTray.Levels + h]]++;
            return count;
        }

        static void SameColors(int[] expected, SandTray tray)
        {
            int[] actual = Colors(tray);
            for (int c = 0; c < 5; c++) Require(expected[c] == actual[c], "Raking must conserve every sand color.");
        }

        static float SandPosition(SandTray tray, Vector2 direction)
        {
            float position = 0;
            for (int i = 0; i < SandTray.CellCount; i++)
                position += tray.Heights[i] * Vector2.Dot(new Vector2(i % SandTray.Size + .5f, i / SandTray.Size + .5f) - Vector2.one * 220, direction);
            return position;
        }

        static void CheckForwardPush()
        {
            var tray = new SandTray(42) { Protection = false };
            foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down, new Vector2(1, 1).normalized })
            {
                tray.Clear(); tray.Protection = false;
                for (int y = 170; y < 270; y++) for (int x = 170; x < 270; x++)
                    tray.Add(y * SandTray.Size + x, (x + y) % 5);
                var before = tray.Capture();
                int[] colors = Colors(tray);
                float original = SandPosition(tray, direction);
                Vector2 from = Vector2.one * 220 - direction * 24, to = Vector2.one * 220 + direction * 24;
                tray.Apply(SandTool.Rake, from, to, 24, 0, 1f / 60);
                Require(SandPosition(tray, direction) > original + 100,
                    "Raking must transport sand forward along every stroke direction.");
                int leadingPile = 0;
                for (int i = 0; i < SandTray.CellCount; i++)
                {
                    Vector2 offset = new Vector2(i % SandTray.Size + .5f, i / SandTray.Size + .5f) - to;
                    float ahead = Vector2.Dot(offset, direction);
                    if (ahead > 1 && ahead < 7) leadingPile += Mathf.Max(0, tray.Heights[i] - before.Heights[i]);
                }
                Require(leadingPile > 10, "The rake must leave a bank of sand ahead of its teeth.");
                SameColors(colors, tray);
                for (int frame = 0; frame < 30; frame++) tray.Step(1f / 60);
                Require(SandPosition(tray, direction) > original + 50, "Forward sand transport must survive settling.");
                SameColors(colors, tray);
            }

            tray.Clear(); tray.Protection = true;
            for (int y = 180; y <= 260; y++) for (int x = 180; x <= 263; x++)
            {
                int i = y * SandTray.Size + x;
                tray.Add(i, x % 5);
                if (x >= 261) tray.Locked[i] = true;
            }
            var protectedBefore = tray.Capture();
            int[] protectedColors = Colors(tray);
            tray.Apply(SandTool.Rake, new Vector2(200, 220), new Vector2(260, 220), 24, 0, 1f / 60);
            for (int y = 180; y <= 260; y++) for (int x = 261; x <= 263; x++)
            {
                int i = y * SandTray.Size + x;
                Require(tray.Heights[i] == protectedBefore.Heights[i] && tray.Top(i) == x % 5,
                    "Forward piles must not overwrite protected sand.");
            }
            SameColors(protectedColors, tray);
        }

        [MenuItem("Tools/Mandala/Run Rake Checks")]
        public static string Run()
        {
            CheckForwardPush();
            var tray = new SandTray(42) { Protection = false };
            for (int y = 150; y < 285; y++) for (int x = 90; x < 350; x++)
                tray.Add(y * SandTray.Size + x, x / 40 % 5);
            var before = tray.Capture();
            int[] colors = Colors(tray);
            Vector2 from = new Vector2(120, 220), to = new Vector2(320, 220);
            int revision = tray.Changes;
            Require(tray.Apply(SandTool.Rake, from, from, 24, 0, 1f / 60) == 0 && tray.Changes == revision,
                "A stationary rake must leave sand alone.");
            Require(tray.Apply(SandTool.Rake, from, to, 24, 0, 1f / 60) > 0, "The rake must move real sand.");
            Require(Mathf.Abs(Mathf.DeltaAngle(tray.RakeAngle, -90)) < .01f, "The rake must face across a horizontal stroke.");
            SameColors(colors, tray);
            for (int frame = 0; frame < 90; frame++) tray.Step(1f / 60);
            for (int x = 150; x <= 290; x += 20)
            {
                int grooves = 0; bool inGroove = false;
                for (int y = 188; y <= 252; y++)
                {
                    bool empty = tray.Heights[y * SandTray.Size + x] == 0;
                    if (empty && !inGroove) grooves++;
                    inGroove = empty;
                }
                Require(grooves == SandTray.RakeTeeth, "All seven continuous grooves must survive settling, including fast strokes.");
            }
            float oldAngle = tray.RakeAngle;
            tray.Apply(SandTool.Rake, to, to + Vector2.up, 24, 0, 1f / 60);
            Require(Mathf.Abs(Mathf.DeltaAngle(oldAngle, tray.RakeAngle)) > 0 && Mathf.Abs(Mathf.DeltaAngle(oldAngle, tray.RakeAngle)) <= 9.01f,
                "A moving rake must turn gradually instead of snapping on a corner.");
            SameColors(colors, tray);

            tray.Restore(before);
            for (int i = 0; i < SandTray.CellCount; i++)
            {
                Require(tray.Heights[i] == before.Heights[i], "Undo must restore all pre-rake heights.");
                for (int h = 0; h < tray.Heights[i]; h++)
                    Require(tray.Layers[i * SandTray.Levels + h] == before.Layers[i * SandTray.Levels + h], "Undo must restore original layers.");
            }
            tray.Protection = true;
            for (int y = 185; y < 255; y++) for (int x = 180; x < 230; x++) tray.Locked[y * SandTray.Size + x] = true;
            tray.Apply(SandTool.Rake, from, to, 24, 0, 1f / 60);
            for (int y = 185; y < 255; y++) for (int x = 180; x < 230; x++)
            {
                int i = y * SandTray.Size + x;
                Require(tray.Heights[i] == before.Heights[i] && tray.Top(i) == before.Layers[i * SandTray.Levels],
                    "Rake teeth and shoulders must leave protected cells untouched.");
            }
            SameColors(colors, tray);
            tray.Protection = false;
            tray.EndStroke();
            tray.Apply(SandTool.Rake, new Vector2(-20, 220), new Vector2(460, 220), 24, 0, 1f / 60);
            tray.EndStroke();
            tray.Apply(SandTool.Rake, new Vector2(220, -20), new Vector2(220, 460), 24, 0, 1f / 60);
            tray.EndStroke();
            tray.Apply(SandTool.Rake, new Vector2(100, 100), new Vector2(340, 340), 24, 0, 1f / 60);
            SameColors(colors, tray);
            for (int i = 0; i < SandTray.CellCount; i++)
                if (!SandTray.Playable(i % SandTray.Size, i / SandTray.Size)) Require(tray.Heights[i] == 0, "Raking must never move sand off the tray.");

            foreach (RakePatternKind kind in Enum.GetValues(typeof(RakePatternKind)))
            {
                var path = new RakePattern(kind);
                Vector2 first = path.Position, previous = first;
                int frames = 0;
                while (!path.Complete && frames++ < 3000)
                {
                    Vector2 point = path.Advance(1.5f);
                    Require(Vector2.Distance(previous, point) <= 1.501f, "Animated paths must advance at a bounded speed.");
                    Require((point - Vector2.one * 220).magnitude + 28 < 218, "All animated teeth must stay inside the tray.");
                    previous = point;
                }
                Require(path.Complete && frames > 100, "Each pattern must animate over time and finish.");
                Require(path.Advance(10) == previous, "Completed paths must stay at their final point.");
                var coarse = new RakePattern(kind); var fine = new RakePattern(kind);
                coarse.Advance(100);
                for (int i = 0; i < 100; i++) fine.Advance(1);
                Require(Vector2.Distance(coarse.Position, fine.Position) < .01f, "Pattern speed must not depend on frame rate.");

                tray.Restore(before); tray.EndStroke(); path = new RakePattern(kind); previous = path.Position;
                while (!path.Complete)
                {
                    Vector2 point = path.Advance(1.5f);
                    tray.Apply(SandTool.Rake, previous, point, 24, 0, 1f / 60);
                    previous = point;
                }
                SameColors(colors, tray);
            }
            return "Rake checks passed: forward transport and leading piles in five directions, settling, seven lasting grooves, continuous fast strokes, stationary contact, gradual turns, exact color conservation, protection, boundaries, undo, all three complete paths, speed independent of frame rate.";
        }
    }
}
