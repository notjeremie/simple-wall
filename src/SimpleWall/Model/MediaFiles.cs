using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SimpleWall.Model
{
    /// <summary>
    /// Which files can go in a slot. One list, because the add, replace and drop paths and both
    /// file dialogs each used to carry their own copy of it.
    ///
    /// A still image is a clip like any other: it loops on the wall (forever -- see
    /// VlcOptions.ImageDuration), takes the look and the Fit, and answers its Stream Deck button.
    /// </summary>
    public static class MediaFiles
    {
        public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".mkv", ".m4v" };
        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        public static bool IsVideo(string path) => HasExtension(path, VideoExtensions);
        public static bool IsImage(string path) => HasExtension(path, ImageExtensions);
        public static bool IsSupported(string path) => IsVideo(path) || IsImage(path);

        public static string DialogFilter =>
            "Videos and images|" + Patterns(VideoExtensions.Concat(ImageExtensions)) +
            "|Videos|" + Patterns(VideoExtensions) +
            "|Images|" + Patterns(ImageExtensions) +
            "|All files|*.*";

        private static bool HasExtension(string path, string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var extension = Path.GetExtension(path);
            return extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static string Patterns(IEnumerable<string> extensions) =>
            string.Join(";", extensions.Select(e => "*" + e));
    }
}
