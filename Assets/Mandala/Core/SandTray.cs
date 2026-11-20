using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mandala.Game
{
    public enum SandTool { Pour, Vacuum, Sweep, Rake }

    /// <summary>Layered horizontal sand, independent of scene objects and frame rate.</summary>
    public sealed class SandTray
    {
        public const int ReferenceSize = 440, Size = 880, Levels = 24, CellCount = Size * Size, ColorCount = 5;
        // Distances are in cells; each layer occupies 1 / AreaScale of its former area.
        public const int GridScale = Size / ReferenceSize, AreaScale = GridScale * GridScale;
        public const float PourRadius = 12 * GridScale;
        const int TrayInset = 2 * GridScale;
        const int SettleTileSize = 16 * GridScale, SettleTiles = (Size + SettleTileSize - 1) / SettleTileSize;
        const int LipHeight = 3;
        const byte EastLip = 1, SouthLip = 2;
        public readonly byte[] Heights = new byte[CellCount];
        public readonly byte[] Layers = new byte[CellCount * Levels];
        public readonly sbyte[] Guide = new sbyte[CellCount];
        public readonly bool[] Completed = new bool[CellCount];
        public readonly List<Grain> Grains = new List<Grain>(4096 * AreaScale);
        public const int MaxRollingGrains = 4096 * AreaScale;
        public readonly List<RollingGrain> RollingGrains = new List<RollingGrain>(MaxRollingGrains);
        // Removed sand is visual only: it must never land again or enter a save.
        public const int MaxSuctionGrains = 3072 * AreaScale;
        public readonly List<SuctionGrain> SuctionGrains = new List<SuctionGrain>(MaxSuctionGrains);
        // Painting controls color assistance and vacuum protection. Section lips have their own lifetime.
        public bool Protection = true, HasWon;
        public int Revision { get; private set; }
        public int Changes { get; private set; }
        public int TargetCount { get; private set; }
        readonly byte[] pending = new byte[CellCount];
        // Each cell owns the shared edges to its east and south, so a lip has one bit.
        readonly byte[] lips = new byte[CellCount];
        int lipCount;
        readonly byte[] settlingHeights = new byte[CellCount], settlingSurface = new byte[CellCount];
        bool[] settlingTiles = new bool[SettleTiles * SettleTiles], dirtyTiles = new bool[SettleTiles * SettleTiles];
        readonly bool[] rollingFrom = new bool[CellCount];
        readonly int[] arrivedAt = new int[CellCount];
        readonly sbyte[] exposureColor = new sbyte[CellCount];
        readonly float[] exposure = new float[CellCount], nextEmission = new float[CellCount];
        readonly List<int> candidates = new List<int>(8192);
        readonly List<SweepCell> sweepCells = new List<SweepCell>(8192);
        readonly System.Random random;
        uint nextGrainId;
        int tick;
        float sweepCredit;
        bool rakeHeading;
        public const int RakeTeeth = 7;
        public float RakeAngle { get; private set; } = 22;
        bool activeVacuum;
        Vector2 vacuumPoint;
        static readonly Vector2Int[] RollDirections = {
            new Vector2Int(1, 0), new Vector2Int(2, -1), new Vector2Int(1, -1), new Vector2Int(1, -2),
            new Vector2Int(0, -1), new Vector2Int(-1, -2), new Vector2Int(-1, -1), new Vector2Int(-2, -1),
            new Vector2Int(-1, 0), new Vector2Int(-2, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 2),
            new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(1, 1), new Vector2Int(2, 1)
        };

        public struct Grain { public int Cell, Color; public float Lift, Velocity, Age; public uint Id; }
        public struct RollingGrain
        {
            public int Source, Target, Color;
            public Vector2 Start, Position;
            public float Age, Duration;
        }
        public struct SuctionGrain
        {
            public Vector2 Start, Target, Position, Previous;
            public int Color;
            public float Age, Duration, Curl;
        }
        struct SweepCell { public int Cell, Leave; public float Side, Order; }
        public sealed class Snapshot
        {
            public byte[] Heights, Layers, Lips;
            public bool[] Completed;
            public bool Protection, HasWon;
        }

        public SandTray(int seed = 1)
        {
            random = new System.Random(seed);
            for (int i = 0; i < CellCount; i++)
            {
                Guide[i] = GuideColor(i % Size, i / Size, Size);
                exposureColor[i] = -1;
                if (Guide[i] >= 0) TargetCount++;
            }
        }
        internal static sbyte GuideColor(int x, int y, int size)
        {
            float dx = (x + .5f - size / 2f) / (size * .43f);
            float dy = (y + .5f - size / 2f) / (size * .43f);
            float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx), petal = Mathf.Cos(a * 8);
            if (r < .12f) return 0;
            if (r < .17f) return 1;
            if (r > .20f && r < .49f + .13f * petal) return (sbyte)(petal > .25f ? 1 : 0);
            if (r > .65f && r < .76f + .12f * Mathf.Cos(a * 8 + Mathf.PI)) return 2;
            if (r > .88f && r < .92f) return 3;
            if (r > .95f && r < 1 && Mathf.Cos(a * 24) > -.25f) return 4;
            return -1;
        }
        public static bool Playable(int x, int y, float inset = TrayInset)
        {
            float dx = x + .5f - Size / 2f, dy = y + .5f - Size / 2f;
            return x >= TrayInset && y >= TrayInset && x < Size - TrayInset && y < Size - TrayInset
                && dx * dx + dy * dy <= (Size / 2f - inset) * (Size / 2f - inset);
        }
        public int Top(int i) => Heights[i] == 0 ? -1 : Layers[i * Levels + Heights[i] - 1];
        public bool Correct(int i) => Guide[i] >= 0 && Top(i) == Guide[i];
        // Display assistance never changes a grain's stored pigment or an empty cell.
        public int DisplayColor(int cell, int color) => color >= 0 && Protection && cell >= 0 && cell < CellCount
            && Completed[cell] && Guide[cell] >= 0 ? Guide[cell] : color;
        public int DisplayColor(Vector2 position, int color)
        {
            int x = Mathf.FloorToInt(position.x), y = Mathf.FloorToInt(position.y);
            return x >= 0 && y >= 0 && x < Size && y < Size ? DisplayColor(y * Size + x, color) : color;
        }
        public int DisplayTop(int i) => DisplayColor(i, Top(i));
        bool VacuumProtected(int cell) => Protection && Completed[cell] && Guide[cell] >= 0;
        bool VacuumProtected(Vector2 position)
        {
            int x = Mathf.FloorToInt(position.x), y = Mathf.FloorToInt(position.y);
            return x >= 0 && y >= 0 && x < Size && y < Size && VacuumProtected(y * Size + x);
        }
        public void CompleteSection(List<int> cells)
        {
            foreach (int cell in cells)
            {
                Completed[cell] = true;
                AddSectionLips(cell);
            }
            Changes++;
        }

        void AddSectionLips(int cell)
        {
            int color = Guide[cell];
            if (color < 0) return;
            int x = cell % Size, y = cell / Size;
            Add(x + 1, y, cell, EastLip);
            Add(x - 1, y, cell - 1, EastLip);
            Add(x, y + 1, cell, SouthLip);
            Add(x, y - 1, cell - Size, SouthLip);

            void Add(int neighborX, int neighborY, int owner, byte edge)
            {
                if (!Playable(neighborX, neighborY) || Guide[neighborY * Size + neighborX] == color || (lips[owner] & edge) != 0) return;
                lips[owner] |= edge;
                lipCount++;
            }
        }

        bool BlocksTransfer(int source, int target, bool rolling = false)
        {
            int sourceHeight = Heights[source];
            bool checkLips = lipCount > 0 && sourceHeight <= LipHeight;
            if (!checkLips && !rolling) return false;
            int dx = target % Size - source % Size, dy = target / Size - source / Size;
            int width = Math.Abs(dx), height = Math.Abs(dy);
            int stepX = Math.Sign(dx), stepY = Math.Sign(dy) * Size;
            int x = 0, y = 0, cell = source;
            while (x < width || y < height)
            {
                // Compare the next grid crossings along a line from cell center to cell center.
                int vertical = x < width ? (2 * x + 1) * height : int.MaxValue;
                int horizontal = y < height ? (2 * y + 1) * width : int.MaxValue;
                if (vertical == horizontal)
                {
                    // At a corner, check both routes so diagonal hops cannot slip around a lip.
                    if (checkLips && (HasEastEdge(cell, stepX) || HasSouthEdge(cell + stepX, stepY)
                        || HasSouthEdge(cell, stepY) || HasEastEdge(cell + stepY, stepX))) return true;
                    if (rolling && BlocksRollAt(cell + stepX) && BlocksRollAt(cell + stepY)) return true;
                    cell += stepX + stepY; x++; y++;
                }
                else if (vertical < horizontal)
                {
                    if (checkLips && HasEastEdge(cell, stepX)) return true;
                    cell += stepX; x++;
                }
                else
                {
                    if (checkLips && HasSouthEdge(cell, stepY)) return true;
                    cell += stepY; y++;
                }
                if (rolling && cell != target && BlocksRollAt(cell)) return true;
            }
            return false;

            bool BlocksRollAt(int i) => settlingSurface[i] > sourceHeight || settlingSurface[i] == Levels;
        }

        bool HasEastEdge(int cell, int step) => (lips[step > 0 ? cell : cell - 1] & EastLip) != 0;
        bool HasSouthEdge(int cell, int step) => (lips[step > 0 ? cell : cell - Size] & SouthLip) != 0;

        void BreakLips(Vector2 from, Vector2 to, float radius)
        {
            if (lipCount == 0) return;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - radius) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - radius) - 1);
            int maxX = Mathf.Min(Size - 2, Mathf.FloorToInt(Mathf.Max(from.x, to.x) + radius));
            int maxY = Mathf.Min(Size - 2, Mathf.FloorToInt(Mathf.Max(from.y, to.y) + radius));
            int before = lipCount;
            Vector2 travel = to - from;
            float radiusSq = radius * radius;
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                int cell = y * Size + x;
                if ((lips[cell] & EastLip) != 0 && TouchesEdge(new Vector2(x + 1, y), Vector2.up))
                {
                    lips[cell] &= unchecked((byte)~EastLip);
                    lipCount--;
                    WakeSettling(cell);
                }
                if ((lips[cell] & SouthLip) != 0 && TouchesEdge(new Vector2(x, y + 1), Vector2.right))
                {
                    lips[cell] &= unchecked((byte)~SouthLip);
                    lipCount--;
                    WakeSettling(cell);
                }
            }
            if (lipCount != before) Changes++;

            bool TouchesEdge(Vector2 start, Vector2 edge)
            {
                // A swept disk touches a segment if either endpoint is close, or the segments cross.
                if (DistanceToSegmentSq(start, from, travel) <= radiusSq
                    || DistanceToSegmentSq(start + edge, from, travel) <= radiusSq
                    || DistanceToSegmentSq(from, start, edge) <= radiusSq
                    || DistanceToSegmentSq(to, start, edge) <= radiusSq) return true;
                bool vertical = edge.y != 0;
                float across = vertical ? travel.x : travel.y;
                if (across == 0) return false;
                float t = (vertical ? start.x - from.x : start.y - from.y) / across;
                if (t < 0 || t > 1) return false;
                float along = vertical ? from.y + travel.y * t - start.y : from.x + travel.x * t - start.x;
                return along >= 0 && along <= 1;
            }
        }

        static float DistanceToSegmentSq(Vector2 point, Vector2 start, Vector2 direction)
        {
            Vector2 offset = point - start;
            float lengthSq = direction.sqrMagnitude;
            if (lengthSq == 0) return offset.sqrMagnitude;
            return (offset - direction * Mathf.Clamp01(Vector2.Dot(offset, direction) / lengthSq)).sqrMagnitude;
        }
        public void ReleaseColors()
        {
            if (!Protection) return;
            // Bake the entire stack, so sweeping cannot uncover old buried mistakes.
            for (int i = 0; i < CellCount; i++)
                for (int h = 0; h < Heights[i]; h++)
                    Layers[i * Levels + h] = (byte)DisplayColor(i, Layers[i * Levels + h]);
            // Preserve current appearances even when Let go is chosen during a transfer.
            for (int n = 0; n < Grains.Count; n++)
            {
                var grain = Grains[n];
                grain.Color = DisplayColor(new Vector2(grain.Cell % Size + .5f, grain.Cell / Size + .5f - grain.Lift), grain.Color);
                Grains[n] = grain;
            }
            for (int n = 0; n < RollingGrains.Count; n++)
            {
                var grain = RollingGrains[n]; grain.Color = DisplayColor(grain.Position, grain.Color); RollingGrains[n] = grain;
            }
            for (int n = 0; n < SuctionGrains.Count; n++)
            {
                var grain = SuctionGrains[n]; grain.Color = DisplayColor(grain.Position, grain.Color); SuctionGrains[n] = grain;
            }
            Protection = false; Changes++;
        }
        public bool Add(int i, int color)
        {
            if (i < 0 || i >= CellCount || color < 0 || color > 4 || Heights[i] + pending[i] >= Levels) return false;
            Layers[i * Levels + Heights[i]++] = (byte)color;
            WakeSettling(i);
            Changes++; return true;
        }
        bool CanReplaceFullTop(int cell, int color, byte[] heights, byte[] layers) =>
            Protection && color >= 0 && color == Guide[cell] && !Completed[cell] && heights[cell] == Levels &&
            layers[cell * Levels + Levels - 1] != color;

        void WakeSettling(int cell)
        {
            // A height, reservation or edge can affect sources within one long hop.
            int x = cell % Size, y = cell / Size;
            const int reach = TrayInset;
            int left = Mathf.Max(0, x - reach) / SettleTileSize;
            int right = Mathf.Min(Size - 1, x + reach) / SettleTileSize;
            int top = Mathf.Max(0, y - reach) / SettleTileSize;
            int bottom = Mathf.Min(Size - 1, y + reach) / SettleTileSize;
            for (int row = top; row <= bottom; row++)
            for (int col = left; col <= right; col++)
                dirtyTiles[row * SettleTiles + col] = true;
        }
        public int Remove(int i)
        {
            if (Heights[i] == 0) return -1;
            WakeSettling(i);
            Changes++; return Layers[i * Levels + --Heights[i]];
        }
        // Rolling grains are material in transit; airborne pour grains are counted separately.
        public int Mass() { int n = RollingGrains.Count; foreach (byte h in Heights) n += h; return n; }
        public void EndStroke() { sweepCredit = 0; rakeHeading = false; activeVacuum = false; }
        public int Apply(SandTool tool, Vector2 from, Vector2 to, float radius, int color, float dt)
        {
            if (dt <= 0 || radius <= 0) return 0;
            activeVacuum = tool == SandTool.Vacuum;
            vacuumPoint = to;
            if (tool != SandTool.Rake) rakeHeading = false;
            if (tool != SandTool.Sweep) sweepCredit = 0;
            if (!Protection && (tool == SandTool.Vacuum || (tool == SandTool.Sweep && (to - from).sqrMagnitude >= 1e-8f)))
                BreakLips(from, to, radius);
            if (tool == SandTool.Sweep) return Sweep(from, to, radius);
            if (tool == SandTool.Rake) return Rake(from, to, radius, dt);
            candidates.Clear();
            Vector2 movement = to - from;
            float lengthSq = movement.sqrMagnitude;
            float pourRate = 4.5f * (16 * 16 * AreaScale) / (radius * radius);
            int minX = Mathf.Max(TrayInset, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - radius));
            int maxX = Mathf.Min(Size - TrayInset - 1, Mathf.FloorToInt(Mathf.Max(from.x, to.x) + radius));
            int minY = Mathf.Max(TrayInset, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - radius));
            int maxY = Mathf.Min(Size - TrayInset - 1, Mathf.FloorToInt(Mathf.Max(from.y, to.y) + radius));
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
                    if (Heights[i] > 0 && !VacuumProtected(i)) candidates.Add(i);
                    continue;
                }
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
                exposure[i] += dt * contact * pourRate * density; // Fixed stream volume, independent of footprint area.
                while (exposure[i] >= nextEmission[i])
                {
                    nextEmission[i] = Mathf.Floor(nextEmission[i]) + 1.05f + (float)random.NextDouble() * .9f;
                    // A full column can reserve one matching pour to replace its surface grain.
                    if (Heights[i] + pending[i] >= Levels &&
                        (pending[i] != 0 || !CanReplaceFullTop(i, color, Heights, Layers))) continue;
                    pending[i]++;
                    WakeSettling(i);
                    Grains.Add(new Grain { Cell = i, Color = color, Lift = 10 * GridScale, Velocity = 40 * GridScale, Id = nextGrainId++ }); activity++;
                }
            }
            if (tool == SandTool.Vacuum)
            {
                int budget = Mathf.CeilToInt(dt * (7200 * AreaScale));
                // Moving surface grains belong to the material being vacuumed too.
                for (int n = RollingGrains.Count - 1; n >= 0 && activity < budget; n--)
                {
                    var rolling = RollingGrains[n];
                    Vector2 offset = rolling.Position - from;
                    float along = lengthSq > 0 ? Mathf.Clamp01(Vector2.Dot(offset, movement) / lengthSq) : 0;
                    if ((offset - movement * along).sqrMagnitude > radius * radius || VacuumProtected(rolling.Position)) continue;
                    pending[rolling.Target]--; rollingFrom[rolling.Source] = false;
                    WakeSettling(rolling.Target); WakeSettling(rolling.Source);
                    RollingGrains[n] = RollingGrains[RollingGrains.Count - 1]; RollingGrains.RemoveAt(RollingGrains.Count - 1);
                    activity++; Changes++;
                    if (SuctionGrains.Count < MaxSuctionGrains)
                        SuctionGrains.Add(new SuctionGrain { Start = rolling.Position, Position = rolling.Position,
                            Previous = rolling.Position, Target = to, Color = rolling.Color, Duration = .3f, Curl = 1 });
                }
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
                    if (SuctionGrains.Count < MaxSuctionGrains)
                    {
                        Vector2 start = new Vector2(i % Size + .5f, i / Size + .5f);
                        float variation = ((i * 73 + activity * 31 + tick * 17) & 255) / 255f;
                        SuctionGrains.Add(new SuctionGrain {
                            Start = start, Position = start, Previous = start, Target = to, Color = removed,
                            Duration = .22f + Mathf.Min(1, Vector2.Distance(start, to) / radius) * .14f + variation * .06f,
                            Curl = .8f + variation * .8f
                        });
                    }
                    if (Heights[i] == 0) SwapRemove(candidates, pick);
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
            float toothRadius = Mathf.Clamp(spacing * .18f, .65f * GridScale, 1.65f * GridScale);
            // Sample both travel and rotation so the outer teeth cannot skip sand on a turn.
            int steps = Mathf.CeilToInt((length + Mathf.Abs(turn) * Mathf.Deg2Rad * radius) / (.7f * GridScale));
            int activity = 0;
            for (int step = 1; step <= steps; step++)
            {
                float t = (float)step / steps, angle = (startAngle + turn * t) * Mathf.Deg2Rad;
                Vector2 across = new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle));
                Vector2 center = Vector2.Lerp(from, to, t);
                for (int tooth = 0; tooth < RakeTeeth; tooth++)
                {
                    Vector2 tip = center + across * (-radius + tooth * spacing);
                    for (int y = Mathf.Max(TrayInset, Mathf.FloorToInt(tip.y - toothRadius)); y <= Mathf.Min(Size - TrayInset - 1, Mathf.FloorToInt(tip.y + toothRadius)); y++)
                    for (int x = Mathf.Max(TrayInset, Mathf.FloorToInt(tip.x - toothRadius)); x <= Mathf.Min(Size - TrayInset - 1, Mathf.FloorToInt(tip.x + toothRadius)); x++)
                    {
                        int source = y * Size + x;
                        Vector2 cell = new Vector2(x + .5f, y + .5f);
                        if (!Playable(x, y) || (cell - tip).sqrMagnitude > toothRadius * toothRadius) continue;
                        while (Heights[source] > 0)
                        {
                            int target = -1;
                            float best = float.PositiveInfinity;
                            // Carry a small bank ahead of the teeth; growing piles spill to the shoulders.
                            for (int ahead = 0; ahead < 3; ahead++)
                            {
                                Vector2 point = tip + direction * (toothRadius + (1.5f + ahead * .8f) * GridScale);
                                int tx = Mathf.FloorToInt(point.x), ty = Mathf.FloorToInt(point.y);
                                if (!Playable(tx, ty)) continue;
                                int i = ty * Size + tx, height = Heights[i] + pending[i];
                                if (i == source || height >= Levels) continue;
                                float score = height * .65f - 2.75f + ahead * .4f;
                                if (score < best && !BlocksTransfer(source, i)) { target = i; best = score; }
                            }
                            for (int side = -1; side <= 1; side += 2)
                            for (int shoulder = 0; shoulder < 2; shoulder++)
                            {
                                Vector2 point = cell + across * (side * (toothRadius + (1.1f + shoulder * .7f) * GridScale) - Vector2.Dot(cell - tip, across));
                                int tx = Mathf.FloorToInt(point.x), ty = Mathf.FloorToInt(point.y);
                                if (!Playable(tx, ty)) continue;
                                int i = ty * Size + tx, height = Heights[i] + pending[i];
                                if (i == source || height >= Levels) continue;
                                float score = height + shoulder * .35f;
                                if (score < best && !BlocksTransfer(source, i)) { target = i; best = score; }
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
            int steps = Mathf.CeilToInt(length / Mathf.Clamp(radius * .2f, GridScale, 6 * GridScale));
            float earned = sweepCredit + length * radius * 2.5f;
            int budget = Mathf.FloorToInt(earned); sweepCredit = earned - budget;
            int activity = 0;
            float spread = Mathf.Clamp(radius * .12f, GridScale, 6 * GridScale);
            for (int step = 1; step <= steps; step++)
            {
                Vector2 center = from + move * ((float)step / steps); sweepCells.Clear();
                for (int y = Mathf.Max(TrayInset, (int)(center.y - radius)); y <= Mathf.Min(Size - TrayInset - 1, (int)(center.y + radius)); y++)
                for (int x = Mathf.Max(TrayInset, (int)(center.x - radius)); x <= Mathf.Min(Size - TrayInset - 1, (int)(center.x + radius)); x++)
                {
                    int i = y * Size + x; if (Heights[i] == 0) continue;
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
                        for (int lane = -2; lane <= 2; lane++) for (int outward = 0; outward <= 2 * GridScale; outward++)
                        {
                            float side = Mathf.Clamp(source.Side + lane * spread, -radius, radius);
                            float ahead = Mathf.Sqrt(Mathf.Max(0, radius * radius - side * side)) + .8f * GridScale + outward;
                            Vector2 point = center + perpendicular * side + direction * ahead;
                            int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
                            if (!Playable(x, y, 3 * GridScale) || (new Vector2(x + .5f, y + .5f) - center).sqrMagnitude <= radius * radius) continue;
                            int i = y * Size + x, height = Heights[i] + pending[i];
                            if (height >= Levels) continue;
                            float score = height * 1.2f + (Mathf.Abs(side - source.Side) * .25f + outward * .9f) / GridScale;
                            if (score < best && !BlocksTransfer(source.Cell, i)) { target = i; best = score; }
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
            if (dt <= 0) return;
            tick++;
            for (int n = RollingGrains.Count - 1; n >= 0; n--)
            {
                var grain = RollingGrains[n];
                grain.Age += dt;
                Vector2 end = new Vector2(grain.Target % Size + .5f, grain.Target / Size + .5f);
                grain.Position = Vector2.Lerp(grain.Start, end, grain.Age / grain.Duration);
                if (grain.Age < grain.Duration) { RollingGrains[n] = grain; continue; }
                pending[grain.Target]--;
                // The reserved landing slot keeps every accepted grain within stack capacity.
                Add(grain.Target, grain.Color);
                arrivedAt[grain.Target] = tick;
                rollingFrom[grain.Source] = false;
                WakeSettling(grain.Source);
                RollingGrains[n] = RollingGrains[RollingGrains.Count - 1];
                RollingGrains.RemoveAt(RollingGrains.Count - 1);
            }
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
                grain.Position.y -= Mathf.Sin(t * Mathf.PI) * (2 * GridScale);
                SuctionGrains[n] = grain;
            }
            activeVacuum = false;
            Settle();
            for (int n = Grains.Count - 1; n >= 0; n--)
            {
                Grain grain = Grains[n]; grain.Age += dt; grain.Velocity += 80 * GridScale * dt; grain.Lift -= grain.Velocity * dt;
                if (grain.Lift > 0) { Grains[n] = grain; continue; }
                int cell = grain.Cell;
                pending[cell]--;
                if (!Add(cell, grain.Color) && CanReplaceFullTop(cell, grain.Color, Heights, Layers))
                {
                    Layers[cell * Levels + Levels - 1] = (byte)grain.Color;
                    Changes++;
                }
                Grains[n] = Grains[Grains.Count - 1]; Grains.RemoveAt(Grains.Count - 1);
            }
        }
        void Settle()
        {
            int firstTile = Array.IndexOf(dirtyTiles, true);
            if (firstTile < 0 || RollingGrains.Count >= MaxRollingGrains) return;
            int lastTile = Array.LastIndexOf(dirtyTiles, true);
            (settlingTiles, dirtyTiles) = (dirtyTiles, settlingTiles);
            Array.Clear(dirtyTiles, 0, dirtyTiles.Length);
            // Downhill choices see the pile and reserved arrivals as they stood before this scan.
            Array.Copy(Heights, settlingHeights, CellCount);
            Array.Copy(Heights, settlingSurface, CellCount);
            foreach (var grain in Grains)
                if (settlingHeights[grain.Cell] < Levels) settlingHeights[grain.Cell]++;
            foreach (var grain in RollingGrains) settlingHeights[grain.Target]++;
            int direction = tick % 2 == 0 ? -1 : 1;
            int minY = firstTile / SettleTiles * SettleTileSize;
            int maxY = Mathf.Min(Size, (lastTile / SettleTiles + 1) * SettleTileSize) - 1;
            int firstY = direction > 0 ? minY : maxY, lastY = direction > 0 ? maxY : minY;
            int firstColumn = direction > 0 ? 0 : SettleTiles - 1, lastColumn = direction > 0 ? SettleTiles - 1 : 0;
            for (int y = firstY; y != lastY + direction; y += direction)
            for (int column = firstColumn; column != lastColumn + direction; column += direction)
            {
                if (!settlingTiles[y / SettleTileSize * SettleTiles + column]) continue;
                int minX = column * SettleTileSize, maxX = Mathf.Min(Size, minX + SettleTileSize) - 1;
                int firstX = direction > 0 ? minX : maxX, lastX = direction > 0 ? maxX : minX;
                for (int x = firstX; x != lastX + direction; x += direction)
                {
                    int i = y * Size + x;
                    if (Heights[i] < 3 || rollingFrom[i] || !Playable(x, y)) continue;
                    if (arrivedAt[i] == tick) { WakeSettling(i); continue; }
                    Relax(i, x, y, direction);
                    if (RollingGrains.Count < MaxRollingGrains) continue;
                    for (int tile = firstTile; tile <= lastTile; tile++) dirtyTiles[tile] |= settlingTiles[tile];
                    return;
                }
            }
        }

        void Relax(int i, int x, int y, int direction)
        {
            uint hash = (uint)(i + tick * 19391);
            hash = (hash ^ (hash >> 16)) * 0x7feb352d;
            int start = (int)(hash ^ (hash >> 15)) & 15;
            int height = Heights[i], target = -1;
            float surface = height + RestOffset(i), steepest = 1.5f;
            // Compare slopes at the original spacing. Nearby cells can still receive sand
            // when the finer grid exposes a pocket or a short path around a blocked hop.
            FindTarget(GridScale, 16);
            if (target < 0 && GridScale > 1) FindTarget(1, 8);
            if (target < 0) return;
            Vector2 from = new Vector2(x + .5f, y + .5f);
            Vector2 to = new Vector2(target % Size + .5f, target / Size + .5f);
            float speed = Mathf.Clamp(14 + steepest * 8, 20, 42) * GridScale;
            int color = Remove(i); pending[target]++; rollingFrom[i] = true;
            WakeSettling(target);
            RollingGrains.Add(new RollingGrain { Source = i, Target = target, Color = color,
                Start = from, Position = from, Duration = Vector2.Distance(from, to) / speed });

            void FindTarget(int spacing, int count)
            {
                int stride = 16 / count, first = start - start % stride;
                for (int n = 0; n < count; n++)
                {
                    int index = (first + n * stride) & 15;
                    var offset = RollDirections[index];
                    int nx = x + offset.x * spacing * direction, ny = y + offset.y * spacing * direction;
                    // Playable sources already have an inset at least as wide as any hop.
                    int neighbor = ny * Size + nx;
                    int drop = height - settlingHeights[neighbor];
                    if (drop < 2 || !Playable(nx, ny) || Heights[neighbor] + pending[neighbor] >= Levels) continue;
                    float distance = (index & 1) != 0 ? 2.23606798f : (index & 3) == 2 ? 1.41421356f : 1;
                    float gradient = (surface - settlingHeights[neighbor] - RestOffset(neighbor)) / distance;
                    if (gradient <= steepest || BlocksTransfer(i, neighbor, true)) continue;
                    steepest = gradient; target = neighbor;
                }
            }
        }
        static float RestOffset(int cell)
        {
            uint hash = (uint)cell * 0x9e3779b9u;
            hash = (hash ^ (hash >> 16)) * 0x7feb352du;
            hash = (hash ^ (hash >> 15)) * 0x846ca68bu;
            return ((hash ^ (hash >> 16)) & 65535) / 65536f - .5f;
        }
        public Snapshot Capture()
        {
            var state = new Snapshot { Heights = (byte[])Heights.Clone(), Layers = (byte[])Layers.Clone(), Completed = (bool[])Completed.Clone(), Lips = (byte[])lips.Clone(), Protection = Protection, HasWon = HasWon };
            foreach (var grain in Grains)
            {
                int cell = grain.Cell;
                if (state.Heights[cell] < Levels)
                    state.Layers[cell * Levels + state.Heights[cell]++] = (byte)grain.Color;
                else if (CanReplaceFullTop(cell, grain.Color, state.Heights, state.Layers))
                    state.Layers[cell * Levels + Levels - 1] = (byte)grain.Color;
            }
            foreach (var grain in RollingGrains)
                state.Layers[grain.Target * Levels + state.Heights[grain.Target]++] = (byte)grain.Color;
            return state;
        }
        public void Restore(Snapshot state)
        {
            Array.Copy(state.Heights, Heights, CellCount); Array.Copy(state.Layers, Layers, Layers.Length);
            Array.Clear(dirtyTiles, 0, dirtyTiles.Length);
            for (int i = 0; i < CellCount; i++) if (Heights[i] >= 3) WakeSettling(i);
            Array.Clear(pending, 0, pending.Length); Array.Clear(exposure, 0, exposure.Length); Array.Clear(Completed, 0, Completed.Length);
            if (state.Completed != null) Array.Copy(state.Completed, Completed, CellCount);
            Array.Clear(lips, 0, lips.Length); lipCount = 0;
            if (state.Protection)
            {
                // Painting lips are always intact. Rebuild them so older paintings gain shared-color lips.
                for (int i = 0; i < CellCount; i++) if (Completed[i]) AddSectionLips(i);
            }
            else if (state.Lips != null)
            {
                Array.Copy(state.Lips, lips, CellCount);
                foreach (byte edges in lips)
                {
                    if ((edges & EastLip) != 0) lipCount++;
                    if ((edges & SouthLip) != 0) lipCount++;
                }
            }
            Array.Clear(rollingFrom, 0, rollingFrom.Length); Array.Clear(arrivedAt, 0, arrivedAt.Length);
            for (int i = 0; i < CellCount; i++) exposureColor[i] = -1;
            Grains.Clear(); RollingGrains.Clear(); SuctionGrains.Clear(); nextGrainId = 0; EndStroke(); Protection = state.Protection; HasWon = state.HasWon; Revision++; Changes++;
        }
        public void Clear() => Restore(new Snapshot { Heights = new byte[CellCount], Layers = new byte[CellCount * Levels], Protection = true });
    }
}
