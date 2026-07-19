using SimpleWall.Model;

namespace SimpleWall.Engine
{
    /// <summary>
    /// One-time upgrades applied to a freshly loaded <see cref="WallConfig"/> before anything runs.
    /// Pure and static so each migration is a plain unit test.
    /// </summary>
    public static class ConfigMigration
    {
        /// <summary>
        /// Moves a pre-look config's single global brightness/contrast onto each clip, once.
        ///
        /// Before clip-looks there was one wall-wide brightness/contrast; now the look lives on the
        /// clip. On the first launch after upgrade the global is whatever the operator last set and
        /// every clip is still at its neutral default, so copying the global onto each clip
        /// reproduces the wall's exact current appearance -- nothing jumps. The global is then reset
        /// to neutral, so on every later launch this is a no-op and clips keep their own looks.
        ///
        /// The caller MUST persist a true return, or the un-reset global on disk re-seeds every clip
        /// on the next launch and wipes any per-clip looks set in between.
        ///
        /// Compared and seeded THROUGH the clamp: config.json is deliberately not range-validated,
        /// so a NaN/overflow global would otherwise read as "non-neutral" and be written onto every
        /// clip. Clamped, a NaN global is the neutral it displays as, and seeding never stores one.
        ///
        /// Imperfect only in a case that cannot occur -- a clip deliberately left neutral while the
        /// global was non-neutral -- because there was no per-clip look before this migration, so on
        /// the one launch where the global is non-neutral every clip is necessarily at the default.
        /// </summary>
        public static bool SeedClipLooks(WallConfig config)
        {
            if (config == null) return false;

            float brightness = AdjustValue.Clamp(config.Brightness);
            float contrast = AdjustValue.Clamp(config.Contrast);

            if (brightness == ClipEntry.NeutralLook && contrast == ClipEntry.NeutralLook)
            {
                // Nothing to seed, but still normalise the stored global so a NaN/overflow value
                // there doesn't ride along forever. Not persisted here -- it re-normalises on every
                // load and never touches a clip, so it is not worth a write.
                config.Brightness = ClipEntry.NeutralLook;
                config.Contrast = ClipEntry.NeutralLook;
                return false;
            }

            foreach (var clip in config.Clips)
            {
                clip.Brightness = brightness;
                clip.Contrast = contrast;
            }
            config.Brightness = ClipEntry.NeutralLook;
            config.Contrast = ClipEntry.NeutralLook;
            return true;
        }
    }
}
