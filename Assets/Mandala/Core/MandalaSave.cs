using System;
using System.IO;
using System.IO.Compression;

namespace Mandala.Game
{
    /// <summary>Versioned, bounded binary saves. A failed write leaves the last good file intact.</summary>
    public static class MandalaSave
    {
        const int Magic = 0x4D414E44, Version = 1;
        public static void Write(string path, SandTray.Snapshot state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary))
            using (var zip = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(SandTray.Size); writer.Write(SandTray.Levels);
                writer.Write(state.Protection); writer.Write(state.HasWon);
                for (int i = 0; i < SandTray.CellCount; i++)
                {
                    writer.Write(state.Heights[i]); writer.Write(state.Layers, i * SandTray.Levels, state.Heights[i]);
                }
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
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version || reader.ReadInt32() != SandTray.Size || reader.ReadInt32() != SandTray.Levels)
                    throw new InvalidDataException("Unsupported mandala save.");
                var state = new SandTray.Snapshot { Protection = reader.ReadBoolean(), HasWon = reader.ReadBoolean(), Heights = new byte[SandTray.CellCount], Layers = new byte[SandTray.CellCount * SandTray.Levels] };
                for (int i = 0; i < SandTray.CellCount; i++)
                {
                    byte height = reader.ReadByte(); if (height > SandTray.Levels) throw new InvalidDataException("Invalid sand height.");
                    state.Heights[i] = height;
                    for (int z = 0; z < height; z++)
                    {
                        byte color = reader.ReadByte(); if (color > 4) throw new InvalidDataException("Invalid sand color.");
                        state.Layers[i * SandTray.Levels + z] = color;
                    }
                }
                if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected save data.");
                return state;
            }
        }
    }
}
