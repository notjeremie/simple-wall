using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SimpleWall.Tests
{
    /// <summary>Solid-colour stills drawn on demand, so the image fixtures are code, not binaries.</summary>
    internal static class TestImages
    {
        public static string SolidPng(string directory, Color colour, int width = 1664, int height = 256)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "still-" + colour.Name + ".png");

            using (var bitmap = new Bitmap(width, height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(colour);
                bitmap.Save(path, ImageFormat.Png);
            }

            return path;
        }
    }
}
