using SimpleWall.UI;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// The slot-aware save rule, extracted from SaveAdjustSoonIfChanged so it can be tested without
    /// a window (same reason as MainForm.DefaultClipToPlay). Looks belong to clips now, so switching
    /// clips changes what the sliders show without editing anything -- and saving on every Stream
    /// Deck press would rewrite config.json for no reason. Only a change to the SAME clip's look is
    /// an edit worth persisting.
    /// </summary>
    public class MainFormLookTests
    {
        [Fact]
        public void SameClipSameLookIsNothing()
        {
            Assert.Equal(MainForm.LookChange.None,
                MainForm.ClassifyLookChange(2, 0.7f, 1.0f, 2, 0.7f, 1.0f));
        }

        [Fact]
        public void SameClipChangedBrightnessIsAnEdit()
        {
            Assert.Equal(MainForm.LookChange.Edited,
                MainForm.ClassifyLookChange(2, 0.7f, 1.0f, 2, 0.5f, 1.0f));
        }

        [Fact]
        public void SameClipChangedContrastIsAnEdit()
        {
            Assert.Equal(MainForm.LookChange.Edited,
                MainForm.ClassifyLookChange(2, 0.7f, 1.0f, 2, 0.7f, 1.3f));
        }

        [Fact]
        public void ADifferentClipIsASwitchEvenWhenTheLookNumbersDiffer()
        {
            Assert.Equal(MainForm.LookChange.Switched,
                MainForm.ClassifyLookChange(2, 0.7f, 1.0f, 3, 0.2f, 1.9f));
        }

        [Fact]
        public void GoingToNoClipIsASwitch()
        {
            Assert.Equal(MainForm.LookChange.Switched,
                MainForm.ClassifyLookChange(2, 0.7f, 1.0f, null, 1.0f, 1.0f));
        }

        [Fact]
        public void ComingFromNoClipIsASwitch()
        {
            Assert.Equal(MainForm.LookChange.Switched,
                MainForm.ClassifyLookChange(null, 1.0f, 1.0f, 2, 0.7f, 1.0f));
        }

        /// <summary>
        /// NaN.Equals(NaN) is true, so a corrupted look compares equal to itself and does NOT fire a
        /// write on every event forever -- the trap this project has hit twice.
        /// </summary>
        [Fact]
        public void SameClipWithAStableNaNLookIsNothing()
        {
            Assert.Equal(MainForm.LookChange.None,
                MainForm.ClassifyLookChange(2, float.NaN, float.NaN, 2, float.NaN, float.NaN));
        }

        // --- PendingSaveAfter: the failed-save retry ---
        // These pin the exact regression a review caught: after a save fails, the config stays
        // dirty, and the NEXT event -- even a clip switch that persists nothing itself -- must keep a
        // save armed so the unsaved edit still reaches disk (SaveConfig writes the whole config).

        [Fact]
        public void AnEditArmsASave()
        {
            Assert.True(MainForm.PendingSaveAfter(MainForm.LookChange.Edited, wasDirty: false));
        }

        [Fact]
        public void ASwitchOrNothingWhileCleanArmsNoSave()
        {
            Assert.False(MainForm.PendingSaveAfter(MainForm.LookChange.Switched, wasDirty: false));
            Assert.False(MainForm.PendingSaveAfter(MainForm.LookChange.None, wasDirty: false));
        }

        [Fact]
        public void ASwitchWhileDirtyStillArmsTheSave_theFailedSaveRetry()
        {
            // Edit A -> save fails (still dirty) -> switch to B: the retry must survive the switch,
            // or A's edit is lost. This is the regression.
            Assert.True(MainForm.PendingSaveAfter(MainForm.LookChange.Switched, wasDirty: true));
        }

        [Fact]
        public void NothingWhileDirtyStillArmsTheSave()
        {
            Assert.True(MainForm.PendingSaveAfter(MainForm.LookChange.None, wasDirty: true));
        }
    }
}
