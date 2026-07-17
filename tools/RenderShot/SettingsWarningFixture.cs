using System.Windows.Forms;

namespace RenderShot
{
    /// <summary>
    /// The Settings tab in every state that MATTERS, on one screen: OSC port edited since the
    /// socket bound (amber "restart to apply", still naming the real port), and autostart pointing
    /// at a DIFFERENT copy of the app (amber warning naming the other path).
    ///
    /// This is the render that has to be looked at. The Task 13 lesson was a fixture that only
    /// ever showed the branch where everything was fine -- it proved nothing, and a broken layout
    /// sailed through. The warning branch is where the long status sentences live, and long
    /// sentences are what a table's row sizing gets wrong.
    ///
    /// Usage: RenderShot.exe RenderShot.SettingsWarningFixture artifacts\render\settings-warning.png
    /// </summary>
    public static class SettingsWarningFixture
    {
        public static Form Create() => SettingsFixture.Build(warning: true);
    }
}
