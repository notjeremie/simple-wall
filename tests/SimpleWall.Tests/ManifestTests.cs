using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Guards app.manifest, which is the one file in this project that can break the app
    /// completely while every test passes and the build reports no warnings.
    ///
    /// A malformed manifest is rejected by Windows during process creation: the app dies
    /// with "the application has failed to start because its side-by-side configuration is
    /// incorrect" before a single line of our code runs, so there is no log, no exception,
    /// and the message names no cause. It shipped exactly that way on 2026-07-20 (a double
    /// hyphen inside an XML comment, which is illegal XML) and blocked launch on Win10.
    ///
    /// The build does not validate this file, and the ARM64 build VM turned out to tolerate
    /// the bad manifest and launch anyway, so neither the compiler nor a smoke test on the
    /// VM would have caught it. These assertions are cheap and they are the only thing
    /// standing between a typo here and a dead app on a wall.
    /// </summary>
    public class ManifestTests
    {
        private const string AsmV1 = "urn:schemas-microsoft-com:asm.v1";
        private const string CompatV1 = "urn:schemas-microsoft-com:compatibility.v1";
        private const string Win10Guid = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";

        private static string ManifestPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "simple-wall.sln")))
                dir = dir.Parent;

            Assert.True(dir != null, "Could not find the repo root (simple-wall.sln) above " + AppContext.BaseDirectory);
            var path = Path.Combine(dir.FullName, "src", "SimpleWall", "app.manifest");
            Assert.True(File.Exists(path), "app.manifest is missing at " + path);
            return path;
        }

        /// <summary>
        /// The one that matters. Everything else here is a detail; this is the difference
        /// between an app that starts and an app that does not.
        /// </summary>
        [Fact]
        public void Manifest_is_well_formed_xml()
        {
            var path = ManifestPath();
            var ex = Record.Exception(() => XDocument.Load(path));
            Assert.True(ex == null,
                "app.manifest is not well-formed XML, so Windows will refuse to start the app " +
                "with a side-by-side configuration error. Most likely cause: a double hyphen " +
                "inside an XML comment. Parser said: " + ex?.Message);
        }

        /// <summary>
        /// Belt and braces: XDocument is lenient about some things a strict reader rejects,
        /// and Windows uses a strict one.
        /// </summary>
        [Fact]
        public void Manifest_parses_under_a_strict_reader()
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using (var reader = XmlReader.Create(ManifestPath(), settings))
            {
                var ex = Record.Exception(() => { while (reader.Read()) { } });
                Assert.True(ex == null, "app.manifest failed a strict XML read: " + ex?.Message);
            }
        }

        /// <summary>
        /// type="win32" is required on assemblyIdentity in a Win32 application manifest.
        /// </summary>
        [Fact]
        public void AssemblyIdentity_declares_win32()
        {
            var identity = XDocument.Load(ManifestPath()).Root
                .Element(XName.Get("assemblyIdentity", AsmV1));

            Assert.NotNull(identity);
            Assert.Equal("win32", (string)identity.Attribute("type"));
        }

        /// <summary>
        /// Without the Windows 10 supportedOS GUID, Windows silently ignores dpiAwareness
        /// and the app is DPI-unaware again, with nothing anywhere to say so. That is the
        /// original bug this manifest exists to fix, so it is worth asserting.
        /// </summary>
        [Fact]
        public void Declares_windows_10_compatibility()
        {
            var ids = XDocument.Load(ManifestPath()).Root
                .Element(XName.Get("compatibility", CompatV1))
                ?.Element(XName.Get("application", CompatV1))
                ?.Elements(XName.Get("supportedOS", CompatV1));

            Assert.NotNull(ids);
            Assert.Contains(ids, e => string.Equals((string)e.Attribute("Id"), Win10Guid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The payload. PerMonitorV2 is what keeps the output window landing on real
        /// physical pixels; dpiAware is the Win7/8 fallback and must survive alongside it.
        /// </summary>
        [Fact]
        public void Declares_per_monitor_v2_and_the_legacy_fallback()
        {
            var text = File.ReadAllText(ManifestPath());
            Assert.Contains("PerMonitorV2", text);
            Assert.Contains("<dpiAware ", text);
        }
    }
}
