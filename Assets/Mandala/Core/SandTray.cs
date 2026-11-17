using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    public enum SandTool { Pour, Vacuum, Sweep, Rake }

    /// <summary>Layered horizontal sand, independent of scene objects and frame rate.</summary>
    public sealed class SandTray
    {
        public const int Size = 440, Levels = 24, CellCount = Size * Size;
        public readonly byte[] Heights = new byte[CellCount];
        public readonly byte[] Layers = new byte[CellCount * Levels];
        public readonly sbyte[] Guide = new sbyte[CellCount];
        public readonly bool[] Locked = new bool[CellCount];
        public readonly List<Grain> Grains = new List<Grain>(4096);
        // Removed sand is visual only: it must never land again or enter a save.
        public readonly List<SuctionGrain> SuctionGrains = new List<SuctionGrain>(3072);
        public bool Protection = true, HasWon;
        public int Revision { get; private set; }
        public int Changes { get; private set; }
        public int TargetCount { get; private set; }
        readonly byte[] pending = new byte[CellCount];
        readonly sbyte[] exposureColor = new sbyte[CellCount];
        readonly float[] exposure = new float[CellCount], nextEmission = new float[CellCount];
        readonly List<int> candidates = new List<int>(8192);
        readonly List<SweepCell> sweepCells = new List<SweepCell>(8192);
        readonly System.Random random;
        int tick;
        float sweepCredit;
        bool rakeHeading;
        public const int RakeTeeth = 7;
        public float RakeAngle { get; private set; } = 22;
        bool activePour;
        bool activeVacuum;
        Vector2 vacuumPoint;
        Vector2 pourFrom, pourTo;
        float pourRadius;
        static readonly int[] Forward = { 1, -Size, -1, Size }, Reverse = { -1, Size, 1, -Size };

        public struct Grain { public int Cell, Color; public float Lift, Velocity; }
        public struct SuctionGrain
        {
            public Vector2 Start, Target, Position, Previous;
            public int Color;
            public float Age, Duration, Curl;
        }
        struct SweepCell { public int Cell, Leave; public float Side, Order; }
        public sealed class Snapshot
        {
            public byte[] Heights, Layers;
            public bool Protection, HasWon;
        }

        public SandTray(int seed = 1)
        {
            random = new System.Random(seed);
            for (int i = 0; i < CellCount; i++)
            {
                float dx = (i % Size + .5f - Size / 2f) / (Size * .43f);
                float dy = (i / Size + .5f - Size / 2f) / (Size * .43f);
                float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx), petal = Mathf.Cos(a * 8);
                int color = -1;
                if (r < .12f) color = 0;
                else if (r < .17f) color = 1;
                else if (r > .20f && r < .49f + .13f * petal) color = petal > .25f ? 1 : 0;
                else if (r > .65f && r < .76f + .12f * Mathf.Cos(a * 8 + Mathf.PI)) color = 2;
                else if (r > .88f && r < .92f) color = 3;
                else if (r > .95f && r < 1 && Mathf.Cos(a * 24) > -.25f) color = 4;
                Guide[i] = (sbyte)color;
                exposureColor[i] = -1;
                if (color >= 0) TargetCount++;
            }
        }
        public static bool Playable(int x, int y, float inset = 2)
        {
            float dx = x + .5f - Size / 2f, dy = y + .5f - Size / 2f;
            return x >= 2 && y >= 2 && x < Size - 2 && y < Size - 2 && dx * dx + dy * dy <= (Size / 2f - inset) * (Size / 2f - inset);
        }
        public int Top(int i) => Heights[i] == 0 ? -1 : Layers[i * Levels + Heights[i] - 1];
        public bool Correct(int i) => Guide[i] >= 0 && Top(i) == Guide[i];
        public bool Protected(int i) => Protection && Locked[i];
        public bool Accepts(int i, int color) => !Protected(i) || Guide[i] == color;
        public bool Add(int i, int color)
        {
            if (i < 0 || i >= CellCount || color < 0 || color > 4 || Heights[i] + pending[i] >= Levels) return false;
            Layers[i * Levels + Heights[i]++] = (byte)color; Changes++; return true;
        }
        public int Remove(int i)
        {
            if (Heights[i] == 0) return -1;
            Changes++; return Layers[i * Levels + --Heights[i]];
        }
        public int Mass() { int n = 0; foreach (byte h in Heights) n += h; return n; }
        public void EndStroke() { sweepCredit = 0; rakeHeading = false; activePour = activeVacuum = false; }
        public int Apply(SandTool tool, Vector2 from, Vector2 to, float radius, int color, float dt)
        {
            if (dt <= 0 || radius <= 0) return 0;
            activePour = tool == SandTool.Pour;
            activeVacuum = tool == SandTool.Vacuum;
            vacuumPoint = to;
            pourFrom = from; pourTo = to; pourRadius = radius;
            if (tool != SandTool.Rake) rakeHeading = false;
            if (tool != SandTool.Sweep) sweepCredit = 0;
            if (tool == SandTool.Sweep) return Sweep(from, to, radius);
            if (tool == SandTool.Rake) return Rake(from, to, radius, dt);
            candidates.Clear();
            Vector2 movement = to - from;
            float lengthSq = movement.sqrMagnitude;
            int minX = Mathf.Max(2, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - radius));
            int maxX = Mathf.Min(Size - 3, Mathf.FloorToInt(Mathf.Max(from.x, to.x) + radius));
            int minY = Mathf.Max(2, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - radius));
            int maxY = Mathf.Min(Size - 3, Mathf.FloorToInt(Mathf.Max(from.y, to.y) + radius));
            int activity = 0;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                if (!Playable(x, y)) continue;
                Vector2 delta = new Vector2(x + .5f, y + .5f) - from;
                float t = lengthSq > 0 ? Vector2.Dot(delta, movement) / lengthSq : 0;
                if ((delta - movement * Mathf.Clamp01(t)).sqrMagnitude > radius * radius) continue;
                int i = y * Size + x;
                if (tool == SandTool.Vacuum)
                {
                    if (Heights[i] > 0 && !(Protected(i) && Correct(i))) candidates.Add(i);
                    continue;
                }
                if (!Accepts(i, color)) continue;
                float contact = 1, meanDistanceSq = delta.sqrMagnitude;
                if (lengthSq > 0)
                {
                    float cross = delta.x * movement.y - delta.y * movement.x;
                    float half = Mathf.Sqrt(Mathf.Max(0, (radius * radius - cross * cross / lengthSq) / lengthSq));
                    float enter = Mathf.Max(0, t - half), exit = Mathf.Min(1, t + half);
                    contact = Mathf.Max(0, exit - enter);
                    float a = enter - t, b = exit - t;
                    meanDistanceSq = cross * cross / lengthSq + lengthSq * (a * a + a * b + b * b) / 3;
                }
                if (exposureColor[i] != color)
                {
                    exposure[i] = 0; nextEmission[i] = .05f + (float)random.NextDouble() * .9f; exposureColor[i] = (sbyte)color;
                }
                // The radial profile averages one over the disk: a fuller center with a feathered fringe.
                float density = .2f + 1.6f * Mathf.Clamp01(1 - meanDistanceSq / (radius * radius));
                exposure[i] += dt * contact * 3 * density; // 1.5 times the previous average flow.
                while (exposure[i] >= nextEmission[i])
                {
                    nextEmission[i] = Mathf.Floor(nextEmission[i]) + 1.05f + (float)random.NextDouble() * .9f;
                    if (Heights[i] + pending[i] >= Levels) continue;
                    pending[i]++;
                    Grains.Add(new Grain { Cell = i, Color = color, Lift = 10, Velocity = 40 }); activity++;
                }
            }
            if (tool == SandTool.Vacuum)
            {
                int budget = Mathf.CeilToInt(dt * 7200);
                while (activity < budget && candidates.Count > 0)
                {
                    int pick = random.Next(candidates.Count);
                    float DistanceToIntake(int cell) => (new Vector2(cell % Size + .5f, cell / Size + .5f) - to).sqrMagnitude;
                    // A clear center forms first; the outer grains follow in a softer fringe.
                    for (int sample = 0; sample < 3; sample++)
                    {
                        int other = random.Next(candidates.Count);
                        if (DistanceToIntake(candidates[other]) < DistanceToIntake(candidates[pick])) pick = other;
                    }
                    int i = candidates[pick];
                    int removed = Remove(i); activity++;
                    if (SuctionGrains.Count < 3072)
                    {
                        Vector2 start = new Vector2(i % Size + .5f, i / Size + .5f);
                        float variation = ((i * 73 + activity * 31 + tick * 17) & 255) / 255f;
                        SuctionGrains.Add(new SuctionGrain {
                            Start = start, Position = start, Previous = start, Target = to, Color = removed,
                            Duration = .22f + Mathf.Min(1, Vector2.Distance(start, to) / radius) * .14f + variation * .06f,
                            Curl = .8f + variation * .8f
                        });
                    }
                    if (Heights[i] == 0 || (Protected(i) && Correct(i))) SwapRemove(candidates, pick);
                }
                return activity;
            }
            return activity;
        }
        static void SwapRemove(List<int> list, int index) { list[index] = list[list.Count - 1]; list.RemoveAt(list.Count - 1); }
        int Rake(Vector2 from, Vector2 to, float radius, float dt)
        {
            Vector2 move = to - from;
            float length = move.magnitude;
            if (length < .0001f) return 0;
            Vector2 direction = move / length;
            float targetAngle = Mathf.Atan2(-move.x, -move.y) * Mathf.Rad2Deg;
            float startAngle = rakeHeading ? RakeAngle : targetAngle;
            RakeAngle = Mathf.MoveTowardsAngle(startAngle, targetAngle, 540 * dt);
            rakeHeading = true;
            float turn = Mathf.DeltaAngle(startAngle, RakeAngle);
            float spacing = radius * 2 / (RakeTeeth - 1);
            float toothRadius = Mathf.Clamp(spacing * .18f, .65f, 1.65f);
            // Sample both travel and rotation so the outer teeth cannot skip sand on a turn.
            int steps = Mathf.CeilToInt((length + Mathf.Abs(turn) * Mathf.Deg2Rad * radius) / .7f);
            int activity = 0;
            for (int step = 1; step <= steps; step++)
            {
                float t = (float)step / steps, angle = (startAngle + turn * t) * Mathf.Deg2Rad;
                Vector2 across = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
                Vector2 center = Vector2.Lerp(from, to, t);
                for (int tooth = 0; tooth < RakeTeeth; tooth++)
                {
                    Vector2 tip = center + across * (-radius + tooth * spacing);
                    for (int y = Mathf.Max(2, Mathf.FloorToInt(tip.y - toothRadius)); y <= Mathf.Min(Size - 3, Mathf.FloorToInt(tip.y + toothRadius)); y++)
                    for (int x = Mathf.Max(2, Mathf.FloorToInt(tip.x - toothRadius)); x <= Mathf.Min(Size - 3, Mathf.FloorToInt(tip.x + toothRadius)); x++)
                    {
                        int source = y * Size + x;
                        Vector2 cell = new Vector2(x + .5f, y + .5f);
                        if (!Playable(x, y) || Protected(source) || (cell - tip).sqrMagnitude > toothRadius * toothRadius) continue;
                        while (Heights[source] > 0)
                        {
                            int target = -1;
                            float best = float.PositiveInfinity;
                            // Carry a small bank ahead of the teeth; growing piles spill to the shoulders.
                            for (int ahead = 0; ahead < 3; ahead++)
                            {
                                Vector2 point = tip + direction * (toothRadius + 1.5f + ahead * .8f);
                                int tx = Mathf.FloorToInt(point.x), ty = Mathf.FloorToInt(point.y);
                                if (!Playable(tx, ty)) continue;
                                int i = ty * Size + tx, height = Heights[i] + pending[i];
                                if (i == source || Protected(i) || height >= Levels) continue;
                                float score = height * .65f - 2 + ahead * .4f;
                                if (score < best) { target = i; best = score; }
                            }
                            for (int side = -1; side <= 1; side += 2)
                            for (int shoulder = 0; shoulder < 2; shoulder++)
                            {
                                Vector2 point = cell + across * (side * (toothRadius + 1.1f + shoulder * .7f) - Vector2.Dot(cell - tip, across));
                                int tx = Mathf.FloorToInt(point.x), ty = Mathf.FloorToInt(point.y);
                                if (!Playable(tx, ty)) continue;
                                int i = ty * Size + tx, height = Heights[i] + pending[i];
                                if (i == source || Protected(i) || height >= Levels) continue;
                                float score = height + shoulder * .35f;
                                if (score < best) { target = i; best = score; }
                            }
                            if (target < 0) break;
                            Add(target, Remove(source)); activity++;
                        }
                    }
                }
            }
            return activity;
        }
        int Sweep(Vector2 from, Vector2 to, float radius)
        {
            Vector2 move = to - from; float length = move.magnitude;
            if (length < .0001f) return 0;
            Vector2 direction = move / length, perpendicular = new Vector2(-direction.y, direction.x);
            int steps = Mathf.CeilToInt(length / Mathf.Max(1, Mathf.Min(6, radius * .2f)));
            float earned = sweepCredit + length * radius * 2.5f;
            int budget = Mathf.FloorToInt(earned); sweepCredit = earned - budget;
            int activity = 0;
            float spread = Mathf.Max(1, Mathf.Min(6, radius * .12f));
            for (int step = 1; step <= steps; step++)
            {
                Vector2 center = from + move * ((float)step / steps); sweepCells.Clear();
                for (int y = Mathf.Max(2, (int)(center.y - radius)); y <= Mathf.Min(Size - 3, (int)(center.y + radius)); y++)
                for (int x = Mathf.Max(2, (int)(center.x - radius)); x <= Mathf.Min(Size - 3, (int)(center.x + radius)); x++)
                {
                    int i = y * Size + x; if (Heights[i] == 0 || Protected(i)) continue;
                    Vector2 d = new Vector2(x + .5f, y + .5f) - center; if (d.sqrMagnitude > radius * radius) continue;
                    float side = Vector2.Dot(d, perpendicular), front = Vector2.Dot(d, direction);
                    int leave = Mathf.FloorToInt(Mathf.Max(0, (Mathf.Abs(side) / radius - .7f) / .3f) * (1 + ((x * 73 + y * 151) % 101) / 50f));
                    if (Heights[i] <= leave) continue;
                    uint hash = (uint)i; hash = (hash ^ (hash >> 16)) * 0x45d9f3b; hash = (hash ^ (hash >> 16)) * 0x45d9f3b;
                    sweepCells.Add(new SweepCell { Cell = i, Side = side, Leave = leave, Order = ((hash ^ (hash >> 16)) / 4294967296f) * .5f + side * side / (radius * radius) * .35f - front / radius * .75f });
                }
                sweepCells.Sort((a, b) => a.Order.CompareTo(b.Order));
                int limit = budget * step / steps;
                bool moved = true;
                while (activity < limit && moved)
                {
                    moved = false;
                    foreach (var source in sweepCells)
                    {
                        if (activity >= limit) break;
                        if (Heights[source.Cell] <= source.Leave) continue;
                        int color = Top(source.Cell), target = -1; float best = float.PositiveInfinity;
                        for (int lane = -2; lane <= 2; lane++) for (int outward = 0; outward <= 2; outward++)
                        {
                            float side = Mathf.Clamp(source.Side + lane * spread, -radius, radius);
                            float ahead = Mathf.Sqrt(Mathf.Max(0, radius * radius - side * side)) + .8f + outward;
                            Vector2 point = center + perpendicular * side + direction * ahead;
                            int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
                            if (!Playable(x, y, 3) || (new Vector2(x + .5f, y + .5f) - center).sqrMagnitude <= radius * radius) continue;
                            int i = y * Size + x, height = Heights[i] + pending[i];
                            if (height >= Levels || !Accepts(i, color)) continue;
                            float score = height * 1.2f + Mathf.Abs(side - source.Side) * .25f + outward * .9f;
                            if (score < best) { target = i; best = score; }
                        }
                        if (target < 0) continue;
                        Add(target, Remove(source.Cell)); activity++; moved = true;
                    }
                }
            }
            return activity;
        }
        public void Step(float dt)
        {
            for (int n = SuctionGrains.Count - 1; n >= 0; n--)
            {
                var grain = SuctionGrains[n];
                grain.Age += dt;
                if (grain.Age >= grain.Duration)
                {
                    SuctionGrains[n] = SuctionGrains[SuctionGrains.Count - 1];
                    SuctionGrains.RemoveAt(SuctionGrains.Count - 1);
                    continue;
                }
                if (activeVacuum) grain.Target = Vector2.Lerp(grain.Target, vacuumPoint, 1 - Mathf.Exp(-24 * dt));
                float t = grain.Age / grain.Duration;
                float angle = grain.Curl * t, sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
                Vector2 offset = grain.Start - grain.Target;
                grain.Previous = grain.Position;
                // Ease into the pull, curl, then accelerate into the intake.
                grain.Position = grain.Target + new Vector2(offset.x * cosine - offset.y * sine,
                    offset.x * sine + offset.y * cosine) * (1 - t * t);
                grain.Position.y -= Mathf.Sin(t * Mathf.PI) * 2;
                SuctionGrains[n] = grain;
            }
            activeVacuum = false;
            bool reverse = tick++ % 2 == 0; int[] directions = reverse ? Reverse : Forward;
            for (int k = 0; k < CellCount; k++)
            {
                int i = reverse ? CellCount - 1 - k : k;
                if (Heights[i] < 4 || Protected(i) || !Playable(i % Size, i / Size)) continue;
                Relax(i, directions, 3);
            }
            if (activePour)
            {
                float reach = pourRadius * 1.75f; Vector2 move = pourTo - pourFrom;
                int minX = Mathf.Max(2, (int)(Mathf.Min(pourFrom.x, pourTo.x) - reach)), maxX = Mathf.Min(Size - 3, Mathf.CeilToInt(Mathf.Max(pourFrom.x, pourTo.x) + reach));
                int minY = Mathf.Max(2, (int)(Mathf.Min(pourFrom.y, pourTo.y) - reach)), maxY = Mathf.Min(Size - 3, Mathf.CeilToInt(Mathf.Max(pourFrom.y, pourTo.y) + reach));
                for (int pass = 0; pass < Mathf.Min(4, Mathf.CeilToInt(pourRadius / 4)); pass++)
                for (int row = minY; row <= maxY; row++) for (int col = minX; col <= maxX; col++)
                {
                    bool backwards = reverse != (pass % 2 == 0);
                    int x = backwards ? maxX + minX - col : col, y = backwards ? maxY + minY - row : row, i = y * Size + x;
                    if (Heights[i] < 3 || Protected(i)) continue;
                    Vector2 delta = new Vector2(x + .5f, y + .5f) - pourFrom;
                    float t = move.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(delta, move) / move.sqrMagnitude) : 0;
                    if ((delta - move * t).sqrMagnitude <= reach * reach) Relax(i, backwards ? Reverse : Forward, pourRadius >= 12 ? 1 : 2);
                }
            }
            activePour = false;
            for (int n = Grains.Count - 1; n >= 0; n--)
            {
                Grain grain = Grains[n]; grain.Velocity += 80 * dt; grain.Lift -= grain.Velocity * dt;
                if (grain.Lift > 0) { Grains[n] = grain; continue; }
                pending[grain.Cell]--;
                if (Accepts(grain.Cell, grain.Color)) Add(grain.Cell, grain.Color);
                Grains[n] = Grains[Grains.Count - 1]; Grains.RemoveAt(Grains.Count - 1);
            }
        }
        void Relax(int i, int[] directions, int slope)
        {
            foreach (int d in directions)
            {
                int target = i + d;
                if (!Playable(target % Size, target / Size) || Heights[i] - Heights[target] <= slope || Heights[target] + pending[target] >= Levels || !Accepts(target, Top(i))) continue;
                Add(target, Remove(i)); break;
            }
        }
        public Snapshot Capture()
        {
            var state = new Snapshot { Heights = (byte[])Heights.Clone(), Layers = (byte[])Layers.Clone(), Protection = Protection, HasWon = HasWon };
            foreach (var grain in Grains) if (Accepts(grain.Cell, grain.Color) && state.Heights[grain.Cell] < Levels)
                state.Layers[grain.Cell * Levels + state.Heights[grain.Cell]++] = (byte)grain.Color;
            return state;
        }
        public void Restore(Snapshot state)
        {
            Array.Copy(state.Heights, Heights, CellCount); Array.Copy(state.Layers, Layers, Layers.Length);
            Array.Clear(pending, 0, pending.Length); Array.Clear(exposure, 0, exposure.Length); Array.Clear(Locked, 0, Locked.Length);
            for (int i = 0; i < CellCount; i++) exposureColor[i] = -1;
            Grains.Clear(); SuctionGrains.Clear(); EndStroke(); Protection = state.Protection; HasWon = state.HasWon; Revision++; Changes++;
        }
        public void Clear() => Restore(new Snapshot { Heights = new byte[CellCount], Layers = new byte[CellCount * Levels], Protection = true });
    }
}
