using System.Collections.Generic;
using System.Linq;

namespace SimpleWall.Model
{
    /// <summary>
    /// Holds the clip roster with STABLE slot numbers: slot N is permanently the OSC
    /// address "/clip/N", and removing a clip never renumbers the others. That guarantee
    /// is the entire reason this class exists - a Stream Deck button is programmed once,
    /// against a slot number, and must keep triggering the same clip for the life of the
    /// deployment, with no silent drift.
    ///
    /// The constructor normalizes whatever list it is handed (e.g. one loaded from a
    /// hand-edited or corrupted config.json) so the rest of the class can assume a clean
    /// invariant from then on: every slot is unique and in 1..<see cref="MaxClips"/>. See
    /// the constructor's doc comment for the exact repair rules, and
    /// <see cref="WasNormalized"/> for how a caller learns whether a repair happened.
    ///
    /// Not thread-safe: all access is expected to happen on the UI thread. The OSC
    /// listener marshals every command onto it, and the scheduler ticks via a WinForms
    /// timer (also the UI thread), so no locking is added here - if that assumption ever
    /// needs to change, it should be a deliberate design change, not a patch to this class.
    /// </summary>
    public class ClipLibrary
    {
        public const int MaxClips = 50;

        private readonly List<ClipEntry> _clips;

        /// <summary>
        /// True if the constructor had to repair the list handed to it - either by
        /// dropping entries beyond the 50-slot ceiling, or by reassigning duplicate or
        /// out-of-range slot numbers. Task 10 uses this to tell the operator their config
        /// was repaired and to trigger a save, so the repair isn't silently lost on the
        /// next load.
        /// </summary>
        public bool WasNormalized { get; }

        public ClipLibrary() : this(new List<ClipEntry>()) { }

        /// <summary>
        /// Wraps <paramref name="clips"/> - the SAME list instance a WallConfig holds, so
        /// that mutations here (Add/Remove, and any repair performed below) are visible to
        /// the caller's config object and persist on its next Save().
        ///
        /// ConfigStore deliberately does not validate clip slots (Task 3) - that is this
        /// constructor's job. It normalizes in list order:
        ///   1. If there are more than <see cref="MaxClips"/> entries, only the first
        ///      <see cref="MaxClips"/> (in list order) are kept - there are only that many
        ///      slots, so the rest are dropped.
        ///   2. Walking the kept entries in order, the FIRST entry to claim a given slot
        ///      keeps it - the same one BySlot's FirstOrDefault would already have picked,
        ///      so it's the least surprising choice. Any later entry with a duplicate
        ///      slot, or a slot outside 1..<see cref="MaxClips"/>, is reassigned to the
        ///      lowest free slot - never dropped, because losing a clip silently is worse
        ///      than renumbering one.
        /// A duplicate-slot config is already ambiguous - "/clip/7" has no defined meaning
        /// once two clips claim it - so deterministic repair beats both silent ambiguity
        /// and refusing to start.
        /// </summary>
        public ClipLibrary(List<ClipEntry> clips)
        {
            _clips = clips;
            WasNormalized = Normalize();
        }

        /// <summary>
        /// Live view of the roster. The entries are the same mutable objects the library
        /// holds internally - mutating a <see cref="ClipEntry"/>'s Slot directly bypasses
        /// the uniqueness invariant this class exists to enforce (nothing re-normalizes
        /// after construction). Go through <see cref="Add"/>/<see cref="Remove"/> instead.
        /// </summary>
        public IReadOnlyList<ClipEntry> Clips => _clips;

        /// <summary>
        /// Adds a clip at the lowest free slot. Returns null ONLY when the library is
        /// already at the <see cref="MaxClips"/> ceiling, so a caller (Task 10) can
        /// explain to the operator why nothing happened.
        /// </summary>
        public ClipEntry Add(string path)
        {
            // Correct by construction: normalization guarantees every slot is unique and
            // in 1..MaxClips, so Count always equals the number of distinct occupied
            // slots - it can never be inflated by duplicates or out-of-range entries.
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

        /// <summary>Removing a slot with no clip assigned is a harmless no-op.</summary>
        public void Remove(int slot) => _clips.RemoveAll(c => c.Slot == slot);

        /// <summary>Returns null for a slot with no clip assigned.</summary>
        public ClipEntry BySlot(int slot) => _clips.FirstOrDefault(c => c.Slot == slot);

        private bool Normalize()
        {
            var normalized = false;

            if (_clips.Count > MaxClips)
            {
                _clips.RemoveRange(MaxClips, _clips.Count - MaxClips);
                normalized = true;
            }

            var used = new HashSet<int>();
            foreach (var clip in _clips)
            {
                // used.Add returns false when the slot is already taken, so this one
                // condition covers both "duplicate" and "in range" in a single check.
                if (clip.Slot >= 1 && clip.Slot <= MaxClips && used.Add(clip.Slot)) continue;

                var slot = 1;
                while (used.Contains(slot)) slot++;
                clip.Slot = slot;
                used.Add(slot);
                normalized = true;
            }

            return normalized;
        }
    }
}
