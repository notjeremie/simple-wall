using System;
using System.IO;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.UI;

namespace RenderShot
{
    /// <summary>
    /// The new-task dialog as an operator first meets it. The spec's requirement is that the editor
    /// ADAPTS to the command -- clip picker for "play clip", value box for brightness/contrast --
    /// and that is a claim about what is on screen, so it gets looked at.
    ///
    /// Usage: RenderShot.exe RenderShot.TaskEditFixture artifacts\render\task-edit.png
    /// </summary>
    public static class TaskEditFixture
    {
        public static Form Create() => Build(CommandKind.PlayClip);

        internal static Form Build(CommandKind kind)
        {
            var clip = FindFixtureClip();
            var config = new WallConfig();
            var library = new ClipLibrary(config.Clips);
            library.Add(clip);
            library.Add(clip);

            var dialog = new TaskEditDialog(null, library);
            SelectCommand(dialog, kind);
            return dialog;
        }

        /// <summary>
        /// Picks the command so the adaptive branch is actually on screen. The first version of
        /// this fixture only ever rendered the default, where the value box is hidden -- so it
        /// "proved" the editor adapts while never showing the half that was broken.
        /// </summary>
        private static void SelectCommand(Control parent, CommandKind kind)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ComboBox combo && combo.Items.Count > 0 && combo.Items[0] is CommandKind)
                {
                    combo.SelectedItem = kind;
                    return;
                }
                SelectCommand(child, kind);
            }
        }

        private static string FindFixtureClip()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "red-then-blue-1964x256.mp4");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new FileNotFoundException("tests/fixtures/red-then-blue-1964x256.mp4 is missing.");
        }
    }
}
