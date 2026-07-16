using System;
using System.IO;
using Newtonsoft.Json;

namespace SimpleWall.Model
{
    public class ConfigStore
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public ConfigStore(string path) { _path = path; }

        public WallConfig Load()
        {
            if (!File.Exists(_path)) return new WallConfig();

            string text;
            try
            {
                text = File.ReadAllText(_path);
            }
            catch (IOException)
            {
                // Locked by AV/backup software or similar at the moment we tried to read it -
                // that is not corruption. Return defaults for this run but leave the file
                // alone: quarantining it here would destroy a perfectly good config.
                return new WallConfig();
            }
            catch (UnauthorizedAccessException)
            {
                return new WallConfig();
            }

            try
            {
                // Explicit NullValueHandling.Ignore: a JSON property present but set to null
                // (e.g. "Clips": null) must not overwrite the collection initializer with null -
                // that would NRE the whole app on next startup. Missing/null fields keep their
                // WallConfig() defaults instead.
                var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                var config = JsonConvert.DeserializeObject<WallConfig>(text, settings);
                if (config == null)
                {
                    // Newtonsoft returns null (no exception) for empty/whitespace-only content.
                    // That is corruption too, and the one shape most likely to follow a power
                    // cut, so it must go through the same quarantine path as malformed JSON.
                    Quarantine();
                    return new WallConfig();
                }
                return config;
            }
            catch (Exception)
            {
                // Malformed JSON (or anything else unexpected at parse time, now that IO
                // failures have already been handled above) is corruption: never refuse to
                // start, but keep the bad file around for diagnosis.
                Quarantine();
                return new WallConfig();
            }
        }

        private void Quarantine()
        {
            try
            {
                // Timestamped so a second corruption doesn't clobber the evidence of the first.
                var bad = _path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                File.Move(_path, bad);
            }
            catch (Exception) { /* refusing to start is worse than losing the layout */ }
        }

        /// <summary>
        /// Persists <paramref name="config"/> durably: the new content is written to a temp
        /// file and explicitly flushed to disk (FlushFileBuffers) before an atomic rename
        /// replaces the real config file. This throws on failure by design - a wall PC that
        /// silently failed to persist a config change is worse than one that surfaces the
        /// error to its caller, so callers must catch and decide what to do (warn, retry,
        /// keep running on the in-memory config) rather than have this method paper over it.
        /// </summary>
        public void Save(WallConfig config)
        {
            lock (_gate)
            {
                var tmp = _path + ".tmp";

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(JsonConvert.SerializeObject(config, Formatting.Indented));
                    sw.Flush();
                    // FlushFileBuffers, called before the rename below. This is the entire
                    // reason write-tmp-then-rename is durable: without it, a completed Save()
                    // can still leave a zero-length/garbage config.json after a power cut,
                    // because WriteAllText/StreamWriter returning only means the bytes reached
                    // the OS cache, not the disk, and NTFS journals metadata, not file data.
                    // Looks removable. It is not.
                    fs.Flush(true);
                }

                // A single atomic ReplaceFile call, not delete-then-move: the latter has a
                // window where neither file exists, and NTFS journals those two ops
                // independently, so a cut could commit the delete without the move.
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
        }
    }
}
