using System;
using System.IO;
using Newtonsoft.Json;

namespace SimpleWall.Model
{
    public class ConfigStore
    {
        private readonly string _path;

        // Static, not per-instance: UI, OSC and the scheduler will each construct their own
        // ConfigStore over the same path (Task 12/13). A per-instance gate would let two
        // instances race on the same "_path + .tmp" - best case an unhandled IOException on
        // a thread-pool thread kills the whole process; worst case one save's Replace() lands
        // on top of another's half-truncated temp file and config.json ends up zero-length.
        // A single static gate serialises every Save()/Load() regardless of how many
        // ConfigStore instances exist. Saves are rare and millisecond-scale, so over-locking
        // across hypothetical multiple config paths costs nothing.
        private static readonly object _gate = new object();

        public ConfigStore(string path) { _path = path; }

        public WallConfig Load()
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return new WallConfig();

                string text;
                try
                {
                    // Eager read is load-bearing: because ReadAllText fully reads the file and
                    // closes the handle before returning, the parse step below never touches a
                    // file handle, so an IOException from a locked/in-use file is structurally
                    // impossible there. If this is ever changed to a streaming reader (e.g.
                    // JsonTextReader over File.OpenText), an IOException mid-parse would fall
                    // into the parse catch below and get quarantined as "corrupt" - silently
                    // reintroducing the bug where a merely-locked, perfectly good config gets
                    // renamed to .bad.
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
                catch (Exception)
                {
                    // Anything else unexpected reading the file (SecurityException,
                    // NotSupportedException, ...) - still not corruption, and this method's
                    // whole job is to never refuse to start. Same "leave it alone" handling
                    // as the specific IO cases above, restored here by construction rather
                    // than by an argument about which exception types are reachable.
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
