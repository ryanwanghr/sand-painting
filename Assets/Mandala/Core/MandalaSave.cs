using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Mandala.Game
{
    /// <summary>Versioned, bounded binary saves. A failed write leaves the last good file intact.</summary>
    public static class MandalaSave
    {
        const int Magic = 0x4D414E44, Version = 3;
        public static void Write(string path, SandTray.Snapshot state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(zip))
            {
                // A legacy snapshot without an edge mask retains its migration behavior.
                writer.Write(Magic); writer.Write(state.Lips == null ? 2 : Version); writer.Write(SandTray.Size); writer.Write(SandTray.Levels);
                writer.Write(state.Protection); writer.Write(state.HasWon);
                for (int i = 0; i < SandTray.CellCount; i++)
                {
                    writer.Write(state.Heights[i]); writer.Write(state.Layers, i * SandTray.Levels, state.Heights[i]);
                    writer.Write(state.Completed != null && state.Completed[i]);
                }
                if (state.Lips != null) writer.Write(state.Lips, 0, SandTray.CellCount);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        public static SandTray.Snapshot Read(string path)
        {
            using (var file = File.OpenRead(path))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                int magic = reader.ReadInt32(), version = reader.ReadInt32();
                int size = reader.ReadInt32(), levels = reader.ReadInt32();
                if (magic != Magic || version < 1 || version > Version ||
                    (size != SandTray.Size && size != SandTray.ReferenceSize) || levels != SandTray.Levels)
                    throw new InvalidDataException("Unsupported mandala save.");
                int count = size * size;
                var state = new SandTray.Snapshot { Protection = reader.ReadBoolean(), HasWon = reader.ReadBoolean(),
                    Heights = new byte[count], Layers = new byte[count * levels], Completed = new bool[count] };
                for (int i = 0; i < count; i++)
                {
                    byte height = reader.ReadByte(); if (height > SandTray.Levels) throw new InvalidDataException("Invalid sand height.");
                    state.Heights[i] = height;
                    for (int z = 0; z < height; z++)
                    {
                        byte color = reader.ReadByte(); if (color > 4) throw new InvalidDataException("Invalid sand color.");
                        state.Layers[i * SandTray.Levels + z] = color;
                    }
                    if (version >= 2) state.Completed[i] = reader.ReadBoolean();
                }
                if (version >= 3)
                {
                    state.Lips = reader.ReadBytes(count);
                    if (state.Lips.Length != count || Array.Exists(state.Lips, value => value > 3))
                        throw new InvalidDataException("Invalid sand edge data.");
                }
                if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected save data.");
                return size == SandTray.Size ? state : ExpandGrid(state, version == 1 && state.Protection);
            }
        }

        static SandTray.Snapshot ExpandGrid(SandTray.Snapshot old, bool inferCompletion)
        {
            const int size = SandTray.ReferenceSize, scale = SandTray.GridScale;
            const int count = SandTray.CellCount, levels = SandTray.Levels;
            var state = new SandTray.Snapshot { Protection = old.Protection, HasWon = old.HasWon,
                Heights = new byte[count], Layers = new byte[count * levels], Completed = new bool[count],
                Lips = old.Lips == null ? null : new byte[count] };
            bool hasCompleted = Array.IndexOf(old.Completed, true) >= 0;
            sbyte[] guide = null;
            if (hasCompleted || inferCompletion)
            {
                guide = new sbyte[size * size];
                for (int i = 0; i < guide.Length; i++) guide[i] = SandTray.GuideColor(i % size, i / size, size);
                if (inferCompletion)
                {
                    InferCompletion(old, guide);
                    hasCompleted = Array.IndexOf(old.Completed, true) >= 0;
                }
            }
            for (int y = 0; y < SandTray.Size; y++)
            for (int x = 0; x < SandTray.Size; x++)
            {
                int sourceX = x / scale, sourceY = y / scale;
                int source = sourceY * size + sourceX, target = y * SandTray.Size + x;
                int height = old.Heights[source];
                state.Heights[target] = (byte)height;
                if (height > 0) Array.Copy(old.Layers, source * levels, state.Layers, target * levels, height);
                if (state.Lips != null)
                {
                    byte edges = old.Lips[source];
                    if (x % scale == scale - 1) state.Lips[target] |= (byte)(edges & 1);
                    if (y % scale == scale - 1) state.Lips[target] |= (byte)(edges & 2);
                }
                if (!hasCompleted) continue;
                sbyte color = SandTray.GuideColor(x, y, SandTray.Size);
                if (color == guide[source]) state.Completed[target] = old.Completed[source];
                else if (color >= 0)
                {
                    // The finer guide resolves its boundaries again. Carry their completion
                    // from adjacent legacy cells of the same color.
                    for (int ny = Math.Max(0, sourceY - 1); ny <= Math.Min(size - 1, sourceY + 1); ny++)
                    for (int nx = Math.Max(0, sourceX - 1); nx <= Math.Min(size - 1, sourceX + 1); nx++)
                    {
                        int neighbor = ny * size + nx;
                        if (guide[neighbor] == color && old.Completed[neighbor]) state.Completed[target] = true;
                    }
                }
            }
            return state;
        }

        static void InferCompletion(SandTray.Snapshot old, sbyte[] guide)
        {
            var visited = new int[guide.Length];
            Array.Fill(visited, -1);
            var cells = new List<int>();
            for (int start = 0; start < guide.Length; start++)
            {
                if (guide[start] < 0 || visited[start] >= 0) continue;
                cells.Clear();
                MandalaProgress.CollectSection(guide, SandTray.ReferenceSize, visited, start, 0, cells);
                int correct = 0;
                foreach (int cell in cells)
                {
                    int height = old.Heights[cell];
                    if (height < MandalaProgress.RequiredLayers) continue;
                    if (old.Layers[cell * SandTray.Levels + height - 1] == guide[cell]) correct++;
                }
                if ((float)correct / cells.Count >= MandalaProgress.RequiredCoverage)
                    foreach (int cell in cells) old.Completed[cell] = true;
            }
        }
    }
}
