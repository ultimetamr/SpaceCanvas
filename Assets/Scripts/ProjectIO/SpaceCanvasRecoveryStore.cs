using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Two-slot recovery journal. An interrupted write cannot destroy the other validated slot.</summary>
    public sealed class SpaceCanvasRecoveryStore
    {
        public sealed class Entry
        {
            public long sequence;
            public string savedUtc;
            public SpaceCanvasProject project;
        }
        private readonly string directory;
        private const long MaxBytes = 32L * 1024 * 1024;
        public SpaceCanvasRecoveryStore(string directory) { this.directory = directory; }
        private string Slot(int i) { return Path.Combine(directory, "recovery-" + i + ".json"); }

        public bool TryRead(out Entry entry)
        {
            entry = null;
            for (var i = 0; i < 2; i++)
            {
                try
                {
                    var path = Slot(i);
                    if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) continue;
                    var candidate = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(path),
                        new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 64 });
                    if (candidate == null || candidate.sequence < 1 || candidate.project == null) continue;
                    ProjectValidator.Validate(candidate.project);
                    if (entry == null || candidate.sequence > entry.sequence) entry = candidate;
                }
                catch (Exception) { /* A corrupt slot is ignored; the previous valid slot remains available. */ }
            }
            return entry != null;
        }

        public void Write(SpaceCanvasProject project)
        {
            ProjectValidator.Validate(project);
            TryRead(out var previous);
            var entry = new Entry { sequence = (previous?.sequence ?? 0) + 1,
                savedUtc = DateTime.UtcNow.ToString("O"), project = project };
            Directory.CreateDirectory(directory);
            // Always write into the slot OTHER than the newest good one, even after corruption recovery.
            var path = Slot((int)(entry.sequence % 2));
            var temp = path + ".tmp";
            var text = JsonConvert.SerializeObject(entry, Formatting.Indented);
            if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new IOException("恢复副本超过 32 MB 限制，请手动保存工程。");
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            var check = JsonConvert.DeserializeObject<Entry>(File.ReadAllText(temp));
            if (check?.project == null) throw new IOException("恢复副本校验失败。");
            ProjectValidator.Validate(check.project);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public void Clear()
        { for (var i = 0; i < 2; i++) { if (File.Exists(Slot(i))) File.Delete(Slot(i)); } }

        public static string Fingerprint(SpaceCanvasProject project)
        {
            // updateTime is capture time, not an edit. Never mutate the caller's data.
            var clone = Newtonsoft.Json.Linq.JObject.FromObject(project);
            clone["updateTime"] = 0;
            clone.Remove("savedAtUtcMilliseconds");
            clone.Remove("preview");
            var text = clone.ToString(Formatting.None);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        public static string Backup(string projectsDirectory, SpaceCanvasProject project)
        {
            Directory.CreateDirectory(projectsDirectory);
            var path = Path.Combine(projectsDirectory, "恢复备份_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".spacecanvas");
            var created = false;
            try
            {
                var json = ProjectSerializer.Serialize(project);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    var bytes = new UTF8Encoding(false).GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                ProjectSerializer.Deserialize(File.ReadAllText(path));
                return path;
            }
            catch
            {
                try { if (created && File.Exists(path)) File.Delete(path); } catch (IOException) { }
                throw;
            }
        }
    }
}
