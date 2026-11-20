using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Mandala.Game
{
    internal sealed class SandWork
    {
        public readonly SandDesign Design;
        public readonly DateTime? Date;
        public readonly string SavePath, ImagePath;

        public SandWork(SandDesign design, DateTime? date, string savePath, string imagePath)
        {
            Design = design;
            Date = date;
            SavePath = savePath;
            ImagePath = imagePath;
        }

        string ExistingSave => File.Exists(SavePath) ? SavePath : SavePath + ".bak";
        public bool HasSave => File.Exists(ExistingSave);
        public bool HasImage => File.Exists(ImagePath);
        public bool InProgress => HasSave && (!HasImage || !MandalaSave.IsFinished(ExistingSave));
        public DateTime Changed => File.GetLastWriteTimeUtc(ExistingSave);
        public DateTime Finished => File.GetLastWriteTimeUtc(ImagePath);

        public SandTray.Snapshot Load()
        {
            string path = ExistingSave;
            if (!File.Exists(path)) return null;
            try { return MandalaSave.Read(path); }
            catch (IOException) when (path == SavePath && File.Exists(SavePath + ".bak"))
            {
                return MandalaSave.Read(SavePath + ".bak");
            }
            catch (InvalidDataException) when (path == SavePath && File.Exists(SavePath + ".bak"))
            {
                return MandalaSave.Read(SavePath + ".bak");
            }
        }

        public void SaveImage(byte[] png)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ImagePath));
            string temporary = ImagePath + ".tmp";
            File.WriteAllBytes(temporary, png);
            if (HasImage) File.Replace(temporary, ImagePath, null);
            else File.Move(temporary, ImagePath);
        }
    }

    internal sealed class SandWorks
    {
        const string DateFormat = "yyyy-MM-dd";
        readonly string rootPath, dailyPath, libraryPath;
        readonly SandDesign[] designs;
        readonly Dictionary<string, SandDesign> byId;

        public SandWorks(string rootPath, IEnumerable<SandDesign> designs)
        {
            this.rootPath = rootPath;
            libraryPath = Path.Combine(rootPath, "sand-works", "library");
            dailyPath = Path.Combine(rootPath, "sand-works", "daily");
            this.designs = designs.ToArray();
            byId = this.designs.ToDictionary(design => design.Id, StringComparer.Ordinal);
        }

        public SandWork For(SandDesign design, DateTime? date = null)
        {
            string directory = date.HasValue
                ? Path.Combine(dailyPath, date.Value.ToString(DateFormat, CultureInfo.InvariantCulture), design.Id)
                : Path.Combine(libraryPath, design.Id);
            // Keep the original painting in its established location.
            string savePath = !date.HasValue && design.Id == SandDesign.OriginalId
                ? Path.Combine(rootPath, "mandala-v1.sand")
                : Path.Combine(directory, "painting.sand");
            return new SandWork(design, date, savePath, Path.Combine(directory, "completed.png"));
        }

        public SandWork Daily(DateTime day)
        {
            if (designs.Length == 0) return null;
            int start = (int)(day.Date.Ticks / TimeSpan.TicksPerDay % designs.Length);
            var choice = For(designs[start], day.Date);
            if (choice.HasSave || choice.HasImage) return choice;
            // Keep an existing daily assignment if the library changed since it was started.
            return designs.Select(design => For(design, day.Date))
                .FirstOrDefault(work => work.HasSave || work.HasImage) ?? choice;
        }

        public IEnumerable<SandWork> All()
        {
            foreach (var design in designs)
            {
                var work = For(design);
                if (work.HasSave || work.HasImage) yield return work;
            }
            if (!Directory.Exists(dailyPath)) yield break;
            foreach (string directory in Directory.EnumerateDirectories(dailyPath))
            {
                if (!DateTime.TryParseExact(Path.GetFileName(directory), DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime date)) continue;
                foreach (string designDirectory in Directory.EnumerateDirectories(directory))
                {
                    if (!byId.TryGetValue(Path.GetFileName(designDirectory), out SandDesign design)) continue;
                    var work = For(design, date);
                    if (work.HasSave || work.HasImage) yield return work;
                }
            }
        }
    }
}
