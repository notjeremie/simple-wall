using System.Windows.Forms;
using SimpleWall.Engine;

namespace RenderShot
{
    /// <summary>
    /// The editor with Brightness picked: the clip row collapses and the value box appears. This is
    /// the branch where "Value:" floated 54px below its own spinner, and no fixture rendered it --
    /// nothing had collapsed, so the layout dump and the exit code were both perfectly happy.
    ///
    /// Usage: RenderShot.exe RenderShot.TaskEditBrightnessFixture artifacts\render\task-edit-value.png
    /// </summary>
    public static class TaskEditBrightnessFixture
    {
        public static Form Create() => TaskEditFixture.Build(CommandKind.Brightness);
    }
}
