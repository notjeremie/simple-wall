using System;
using System.IO;
using Newtonsoft.Json;

namespace SimpleWall.Model
{
    public class ConfigStore
    {
        private readonly string _path;

        public ConfigStore(string path) { _path = path; }

        public WallConfig Load()
        {
            if (!File.Exists(_path)) return new WallConfig();

            try
            {
                return JsonConvert.DeserializeObject<WallConfig>(File.ReadAllText(_path))
                       ?? new WallConfig();
            }
            catch (Exception)
            {
                Quarantine();
                return new WallConfig();
            }
        }

        private void Quarantine()
        {
            try
            {
                var bad = _path + ".bad";
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(_path, bad);
            }
            catch (Exception) { /* refusing to start is worse than losing the layout */ }
        }

        public void Save(WallConfig config)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(config, Formatting.Indented));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }
    }
}
