using System;
using System.IO;
using System.Windows.Forms;
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
        public static Form Create()
        {
            var clip = FindFixtureClip();
            var config = new WallConfig();
            var library = new ClipLibrary(config.Clips);
            library.Add(clip);
            library.Add(clip);

            return new TaskEditDialog(null, library);
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
