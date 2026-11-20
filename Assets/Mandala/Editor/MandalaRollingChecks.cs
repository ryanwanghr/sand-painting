using System;
using UnityEditor;
using UnityEngine;

namespace Mandala.Game.Editor
{
    public static class MandalaRollingChecks
    {
        const int Scale = SandTray.GridScale, Center = SandTray.Size / 2;
        const int Source = Center * SandTray.Size + Center, Target = Source + 1;
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static SandTray Ramp(int height)
        {
            var tray = new SandTray(17);
            // Track the contrasting top grain while the pile spreads without artificial locks.
            for (int h = 0; h < height - 1; h++) tray.Add(Source, 0);
            tray.Add(Source, 1);
            return tray;
        }

        static int SnapshotMass(SandTray.Snapshot snapshot)
        {
            int mass = 0; foreach (byte height in snapshot.Heights) mass += height; return mass;
        }

        [MenuItem("Tools/Mandala/Run Rolling Sand Checks")]
        public static string Run()
        {
            var tray = Ramp(12);
            int mass = tray.Mass();
            tray.Step(0);
            Require(tray.RollingGrains.Count == 0 && tray.Mass() == mass, "Zero time must not move sand.");
            tray.Step(.005f);
            Require(tray.RollingGrains.Count == 1 && tray.Top(Source) == 0 && tray.Heights[Target] == 0,
                "The contrasting grain must leave its source and remain in transit before landing.");
            var first = tray.RollingGrains[0];
            Require(first.Color == 1 && first.Source == Source && first.Duration > .005f,
                "Rolling must preserve color and have a nonzero travel duration.");
            tray.EndStroke(); tray.Step(.005f);
            var moving = tray.RollingGrains[0];
            Require(Vector2.Distance(moving.Position, first.Start) > 0 && moving.Position != new Vector2(first.Target % SandTray.Size + .5f, first.Target / SandTray.Size + .5f)
                && Vector2.Distance(first.Position, moving.Position) <= 42 * Scale * .005f + .001f,
                "After release, rolling must advance through intermediate positions at a bounded speed.");
            var saved = tray.Capture();
            Require(tray.Mass() == mass && SnapshotMass(saved) == mass && saved.Layers[first.Target * SandTray.Levels] == 1,
                "Saving in transit must preserve all material and the rolling color at its reserved destination.");
            while (tray.Heights[first.Target] == 0) tray.Step(.005f);
            Require(tray.Top(first.Target) == 1 && tray.Mass() == mass,
                "A reserved transfer must land its original color exactly once.");

            float Speed(SandTray.RollingGrain grain) => Vector2.Distance(grain.Start,
                new Vector2(grain.Target % SandTray.Size + .5f, grain.Target / SandTray.Size + .5f)) / grain.Duration;
            var gentle = Ramp(3);
            for (int y = Center - 2 * Scale; y <= Center + 2 * Scale; y++)
            for (int x = Center - 2 * Scale; x <= Center + 2 * Scale; x++)
                if (y * SandTray.Size + x != Source) gentle.Add(y * SandTray.Size + x, 0);
            gentle.Step(.005f);
            Require(Speed(gentle.RollingGrains[0]) < Speed(first), "Gentle slopes must roll more slowly than steep slopes.");
            var incoming = Ramp(3);
            for (int y = Center - 2 * Scale; y <= Center + 2 * Scale; y++) for (int x = Center - 2 * Scale; x <= Center + 2 * Scale; x++)
            {
                int cell = y * SandTray.Size + x;
                if (cell != Source) for (int h = 0; h < 10; h++) incoming.Add(cell, 0);
            }
            incoming.Apply(SandTool.Pour,Vector2.one * (Center + .5f),Vector2.one * (Center + .5f),.1f,0,1f/60);
            incoming.Step(.005f);
            Require(incoming.Grains.Count > 0 && !incoming.RollingGrains.Exists(grain => grain.Source == Source),
                "Airborne reservations must not make a low source roll uphill before its incoming grains land.");

            var cascade = Ramp(12); cascade.Step(.005f);
            var tracer = cascade.RollingGrains[0];
            for (int h = 0; h < 6; h++) cascade.Add(tracer.Target, 0);
            cascade.Step(tracer.Duration);
            Require(cascade.Top(tracer.Target) == 1,
                "An arriving tracer must land without cascading into another hop in that tick.");
            tray.Restore(saved);
            Require(tray.RollingGrains.Count == 0 && tray.Mass() == mass, "Undo must restore moving material without stale transfers.");
            tray.Clear();
            Require(tray.RollingGrains.Count == 0 && tray.Mass() == 0, "Clear must discard all rolling material and reservations.");

            tray = Ramp(12);
            for (int i = 0; i < SandTray.CellCount; i++) tray.Completed[i] = true;
            tray.Step(.005f);
            Require(tray.RollingGrains.Count == 1 && tray.Heights[Source] == 11,
                "Completed sand must still roll downhill across section boundaries.");
            int landing = tray.RollingGrains[0].Target;
            for (int n = 0; n < 30; n++) tray.Step(.005f);
            Require(tray.Mass() == 12 && tray.Heights[landing] > 0,
                "Completion assistance must preserve every reserved landing slot.");

            tray = Ramp(12); tray.Step(.005f);
            int before = tray.Mass();
            int removed = tray.Apply(SandTool.Vacuum, Vector2.one * (Center + .5f), Vector2.one * (Center + .5f), 2, 0, .05f);
            Require(removed > 0 && tray.RollingGrains.Count == 0 && tray.Mass() == before - removed,
                "Vacuuming must remove moving material and release its reservation.");
            bool redRemoved = false; foreach (var grain in tray.SuctionGrains) redRemoved |= grain.Color == 1;
            Require(redRemoved, "Vacuuming a rolling grain must animate its original color out.");
            for (int n = 0; n < 60; n++) tray.Step(1f / 60);
            Require(tray.Mass() == before - removed, "Vacuumed rolling sand must never land again.");

            // A dense pour must conserve colors and respect stack/particle capacity.
            tray = new SandTray(42) { Protection = false };
            int emitted = 0;
            for (int frame = 0; frame < 180; frame++)
            {
                emitted += tray.Apply(SandTool.Pour, Vector2.one * Center, Vector2.one * Center, SandTray.PourRadius, frame / 36, 1f / 60);
                tray.Step(1f / 60);
                Require(tray.Mass() + tray.Grains.Count == emitted, "Every pouring/rolling step must conserve material.");
                Require(tray.RollingGrains.Count <= SandTray.MaxRollingGrains, "Rolling particles must stay within their budget.");
            }
            Require(SnapshotMass(tray.Capture()) == emitted, "Mid-pour saves must include both airborne and rolling material.");
            foreach (byte height in tray.Capture().Heights) Require(height <= SandTray.Levels, "Reservations must keep snapshots within stack capacity.");

            var crowded = new SandTray(42) { Protection = false };
            for (int y = 140 * Scale; y < 300 * Scale; y += 2) for (int x = 140 * Scale; x < 300 * Scale; x += 2)
                for (int h = 0; h < 4; h++) crowded.Add(y * SandTray.Size + x, h % 5);
            int crowdedMass = crowded.Mass(); crowded.Step(.005f);
            Require(crowded.RollingGrains.Count == SandTray.MaxRollingGrains && crowded.Mass() == crowdedMass,
                "Exhausting the moving-grain budget must leave excess material in place, without instant transfers or loss.");

            var finished = new SandTray(17); var progress = new MandalaProgress(finished);
            for (int i = 0; i < SandTray.CellCount; i++) if (finished.Guide[i] >= 0)
                for (int h = 0; h < 3; h++) finished.Add(i, finished.Guide[i]);
            progress.Update(finished,0,false);
            finished.RollingGrains.Add(first);
            Require(!progress.CheckWin(finished,0,false) && !progress.CheckWin(finished,2,false),
                "Rolling sand must block completion even when every section is filled.");
            finished.RollingGrains.Clear();
            Require(!progress.CheckWin(finished,3,false) && progress.CheckWin(finished,4.1f,false),
                "The full completion delay must begin after rolling finishes.");
            return "Rolling checks passed: real travel time, fractional motion after release, bounded slope-dependent speed, original colors, mass/reservation conservation, saves, undo, clear, completion assistance, vacuum, particle and stack limits.";
        }

        static Texture2D Read(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = texture; image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); image.Apply(); return image; }
            finally { RenderTexture.active = previous; }
        }

        [MenuItem("Tools/Mandala/Run Rolling Sand Rendering Checks")]
        public static string RunRendering()
        {
            var tray = new SandTray(17) { Protection = false };
            var progress = new MandalaProgress(tray);
            using (var surface = new SandTexture(progress.Sections.Count))
            {
                surface.SetViewport(512, new Rect(212f/440,212f/440,16f/440,16f/440));
                surface.Render(tray,progress,false,0);
                var paper = Read(surface.Texture);
                var rolling = new SandTray.RollingGrain { Source = Source, Target = Target, Color = 1,
                    Start = new Vector2(218.5f,222.5f) * Scale, Position = new Vector2(218.5f,222.5f) * Scale, Duration = .1f };
                tray.RollingGrains.Add(rolling);
                surface.Render(tray,progress,false,0,false);
                var first = Read(surface.Texture);
                try
                {
                    int x = 208, y = 176; // Actual tray coordinates, including flipped simulation Y.
                    Require(Mathf.Abs(paper.GetPixel(x,y).g-first.GetPixel(x,y).g) > .05f,
                        "The rolling grain must appear in the actual GPU output at its fractional position.");
                    foreach (var pixel in first.GetPixels()) Require(pixel.a > .99f,
                        "Antialiased rolling grains must preserve opaque tray coverage beneath them.");
                    rolling.Position.x += .25f; tray.RollingGrains[0] = rolling;
                    surface.Render(tray,progress,false,.025f,false);
                    var moved = Read(surface.Texture);
                    try
                    {
                        Require(Mathf.Abs(first.GetPixel(x,y).g-moved.GetPixel(x,y).g) > .02f,
                            "A quarter-cell move must shift the visible grain before crossing a simulation cell.");
                        int movedX = x + 8 / Scale;
                        Require(Mathf.Abs(paper.GetPixel(movedX,y).g-moved.GetPixel(movedX,y).g) > .05f,
                            "Rolling motion must follow the fractional destination, without grid snapping.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(moved); }
                    tray.RollingGrains.Clear(); surface.Render(tray,progress,false,1,false);
                    var cleared = Read(surface.Texture);
                    try { Require(cleared.GetPixel(x,y) == paper.GetPixel(x,y), "The final rolling frame must clear without leaving ghosts."); }
                    finally { UnityEngine.Object.DestroyImmediate(cleared); }
                }
                finally { UnityEngine.Object.DestroyImmediate(paper); UnityEngine.Object.DestroyImmediate(first); }
            }
            return "Rolling GPU checks passed: correct position and Y orientation, quarter-cell movement, cached settled surface, clean release tail.";
        }
    }
}
