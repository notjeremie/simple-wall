using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using SimpleWall.Engine;
using SimpleWall.Model;
using SimpleWall.Scheduling;
using SimpleWall.UI;

namespace RenderShot
{
    /// <summary>
    /// The Schedule tab with a realistic schedule: a weekly task, an every-day task, a one-off, a
    /// disabled one, and one that CANNOT fire because its slot is empty (shown red). If a render
    /// only ever shows healthy rows, it isn't telling you anything.
    ///
    /// Usage: RenderShot.exe RenderShot.SchedulerFixture artifacts\render\schedule.png
    /// </summary>
    public static class SchedulerFixture
    {
        public static Form Create() => Build(scheduleEnabled: true);

        internal static Form Build(bool scheduleEnabled)
        {
            var clip = FindFixtureClip();
            var config = new WallConfig();
            var library = new ClipLibrary(config.Clips);
            library.Add(clip); // slot 1
            library.Add(clip); // slot 2

            config.Tasks.AddRange(new[]
            {
                new ScheduledTask
                {
                    Days = new List<DayOfWeek> { DayOfWeek.Sunday },
                    Time = new TimeSpan(13, 0, 0),
                    Command = WallCommand.PlayClip(1)
                },
                new ScheduledTask
                {
                    Days = new List<DayOfWeek>((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek))),
                    Time = new TimeSpan(8, 0, 0),
                    Command = WallCommand.WithValue(CommandKind.Brightness, 0.6f)
                },
                new ScheduledTask
                {
                    Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday },
                    Time = new TimeSpan(22, 30, 0),
                    Command = WallCommand.Simple(CommandKind.Stop),
                    Enabled = false
                },
                new ScheduledTask
                {
                    OneOffDate = new DateTime(2026, 8, 1),
                    Time = new TimeSpan(20, 0, 0),
                    Command = WallCommand.PlayClip(2)
                },
                new ScheduledTask
                {
                    // Points at an empty slot: must render red.
                    Days = new List<DayOfWeek> { DayOfWeek.Friday },
                    Time = new TimeSpan(18, 0, 0),
                    Command = WallCommand.PlayClip(9)
                }
            });

            var scheduler = new Scheduler(config.Tasks) { Enabled = scheduleEnabled };
            var engine = new StubEngine();
            var thumbnails = new ThumbnailCache(Path.Combine(Path.GetTempPath(), "sw-rendershot-thumbs"));

            var form = new MainForm(engine, library, scheduler, config, thumbnails);
            SelectScheduleTab(form);
            return form;
        }

        /// <summary>
        /// The Schedule tab is not the one that opens, and RenderShot renders whatever is showing,
        /// so it has to be selected before the picture is taken.
        /// </summary>
        private static void SelectScheduleTab(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TabControl tabs && tabs.TabPages.Count > 1)
                {
                    tabs.SelectedIndex = 1;
                    return;
                }
                SelectScheduleTab(child);
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

        private class StubEngine : IWallEngine
        {
            public int? CurrentSlot => 1;
            public bool IsPlaying => true;

#pragma warning disable 67 // never raised: a render is a still life
            public event EventHandler StateChanged;
            public event EventHandler<ClipUnavailableEventArgs> ClipUnavailable;
#pragma warning restore 67

            public void Execute(WallCommand command) { }
        }
    }
}
