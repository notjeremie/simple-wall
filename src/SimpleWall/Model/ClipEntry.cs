namespace SimpleWall.Model
{
    public class ClipEntry
    {
        /// <summary>
        /// A clip's look with no adjustment. Brightness/contrast are a 0..2 float where 1.0 is
        /// "leave the picture alone", so a fresh clip and any clip in an old (pre-look) config
        /// both sit here. Named so the default and the replace-reset (ClipLibrary.Replace) share
        /// one value, and kept in Model so nothing here has to reach into the engine's
        /// AdjustValue (the dependency only runs the other way).
        /// </summary>
        public const float NeutralLook = 1.0f;

        public int Slot { get; set; }
        public string Path { get; set; }

        /// <summary>
        /// Per-clip look, applied to the wall whenever this clip plays -- from the Stream Deck,
        /// the mouse, the scheduler, or the boot default. Editing the fader/slider while the clip
        /// is on the wall writes here (debounced save); replacing the clip's file resets it. There
        /// is no global wall brightness -- the look lives on the clip. Defaults to
        /// <see cref="NeutralLook"/> so existing clips are unchanged and old configs (which have no
        /// such field) deserialize to neutral.
        /// </summary>
        public float Brightness { get; set; } = NeutralLook;
        public float Contrast { get; set; } = NeutralLook;
    }
}
