using System.Windows.Forms;

namespace RenderShot
{
    /// <summary>
    /// The MainForm with NOTHING on the wall. There is no global brightness anymore -- a look
    /// belongs to a clip -- so with no clip playing the brightness/contrast sliders and their Reset
    /// buttons have nothing to adjust and must render DISABLED (greyed), alongside Play/Stop which
    /// already do. This is the state the clip-looks change introduced, so it is the one to look at.
    ///
    /// Usage: RenderShot.exe RenderShot.MainFormNoClipFixture artifacts\render\mainform-noclip.png
    /// </summary>
    public static class MainFormNoClipFixture
    {
        public static Form Create() => MainFormFixture.Build(currentSlot: null);
    }
}
