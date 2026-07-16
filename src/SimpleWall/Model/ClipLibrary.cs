using System.Collections.Generic;
using System.Linq;

namespace SimpleWall.Model
{
    public class ClipLibrary
    {
        public const int MaxClips = 50;

        private readonly List<ClipEntry> _clips;

        public ClipLibrary() : this(new List<ClipEntry>()) { }
        public ClipLibrary(List<ClipEntry> clips) { _clips = clips; }

        public IReadOnlyList<ClipEntry> Clips => _clips;

        public ClipEntry Add(string path)
        {
            if (_clips.Count >= MaxClips) return null;

            var entry = new ClipEntry { Slot = LowestFreeSlot(), Path = path };
            _clips.Add(entry);
            return entry;
        }

        private int LowestFreeSlot()
        {
            var used = new HashSet<int>(_clips.Select(c => c.Slot));
            var slot = 1;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        public void Remove(int slot) => _clips.RemoveAll(c => c.Slot == slot);

        public ClipEntry BySlot(int slot) => _clips.FirstOrDefault(c => c.Slot == slot);
    }
}
