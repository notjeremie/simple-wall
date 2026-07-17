using System.Windows.Forms;

namespace RenderShot
{
    /// <summary>
    /// The same schedule with the master switch OFF. The banner is the whole point of this state:
    /// a silently disabled scheduler is a Sunday-afternoon discovery, by which time whatever should
    /// have gone on the wall didn't. Rendered separately because "unmissable" is a claim about
    /// pixels, and it has to be looked at.
    ///
    /// Usage: RenderShot.exe RenderShot.SchedulerOffFixture artifacts\render\schedule-off.png
    /// </summary>
    public static class SchedulerOffFixture
    {
        public static Form Create() => SchedulerFixture.Build(scheduleEnabled: false);
    }
}
