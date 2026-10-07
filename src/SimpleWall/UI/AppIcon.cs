using System.Drawing;

namespace SimpleWall.UI
{
    /// <summary>
    /// The window icon. ApplicationIcon in the csproj only stamps the exe -- WinForms windows
    /// still show the stock icon in the title bar and taskbar unless told otherwise. Loaded from
    /// the embedded .ico rather than Icon.ExtractAssociatedIcon, which hands back the 32px image
    /// alone and leaves Windows to blur it down to 16 for the title bar.
    /// </summary>
    public static class AppIcon
    {
        public static Icon Load()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SimpleWall.ico"))
                return stream == null ? null : new Icon(stream);
        }
    }
}
