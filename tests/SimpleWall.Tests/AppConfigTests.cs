using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SimpleWall.Tests
{
    /// <summary>
    /// Guards App.config, the sibling of app.manifest and the other file that can kill the
    /// app on startup while the build stays clean and every other test passes.
    ///
    /// A bad App.config takes down the entire .NET configuration system: the first
    /// ConfigurationManager read throws, and because logging itself reads configuration the
    /// app dies reporting "Configuration system failed to initialize" with no useful detail.
    /// It shipped that way on 2026-07-20, declaring a section that .NET Framework 4.7+
    /// already provides built in.
    ///
    /// These are cheap assertions against a file nothing else validates.
    /// </summary>
    public class AppConfigTests
    {
        private const string DpiSection = "System.Windows.Forms.ApplicationConfigurationSection";

        private static string ConfigPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "simple-wall.sln")))
                dir = dir.Parent;

            Assert.True(dir != null, "Could not find the repo root (simple-wall.sln) above " + AppContext.BaseDirectory);
            var path = Path.Combine(dir.FullName, "src", "SimpleWall", "App.config");
            Assert.True(File.Exists(path), "App.config is missing at " + path);
            return path;
        }

        [Fact]
        public void App_config_is_well_formed_xml()
        {
            var ex = Record.Exception(() => XDocument.Load(ConfigPath()));
            Assert.True(ex == null,
                "App.config is not well-formed XML, so the app will die at startup with " +
                "'Configuration system failed to initialize'. Parser said: " + ex?.Message);
        }

        /// <summary>
        /// The one that actually bit. The DPI section is built into .NET Framework 4.7+;
        /// declaring it in configSections is a hard error, not a harmless redundancy.
        /// </summary>
        [Fact]
        public void Does_not_redeclare_the_built_in_dpi_section()
        {
            var declared = XDocument.Load(ConfigPath()).Root
                .Elements("configSections")
                .Elements("section")
                .Any(e => (string)e.Attribute("name") == DpiSection);

            Assert.False(declared,
                DpiSection + " is built into .NET Framework 4.7+ and must NOT be declared in " +
                "<configSections>. Declaring it throws 'this section is a built-in section, it " +
                "cannot be declared by the user' and takes the whole configuration system down.");
        }

        /// <summary>
        /// The real test: hand the file to the actual .NET configuration system and make it
        /// parse, exactly as it is parsed during startup. The XML-shape assertions above are
        /// a readable description of the known failure; THIS is what would catch the next
        /// one, whatever form it takes.
        ///
        /// Worth it because the alternative (launch the app and see) turned out to be
        /// unreliable: the headless build VM has no interactive session, the app is
        /// single-instance so a stuck process silently suppresses later launches, and its
        /// config parser proved more lenient than a real Windows desktop anyway.
        /// </summary>
        [Fact]
        public void Real_configuration_system_can_parse_it()
        {
            var map = new System.Configuration.ExeConfigurationFileMap { ExeConfigFilename = ConfigPath() };

            var ex = Record.Exception(() =>
            {
                var config = System.Configuration.ConfigurationManager.OpenMappedExeConfiguration(
                    map, System.Configuration.ConfigurationUserLevel.None);

                // Sections parse lazily, so touching them is what forces the failure.
                foreach (System.Configuration.ConfigurationSection section in config.Sections)
                {
                    var _ = section.SectionInformation.Name;
                }
            });

            Assert.True(ex == null,
                "The .NET configuration system could not parse App.config, so the app will die " +
                "at startup with 'Configuration system failed to initialize'. It said: " + ex?.Message);
        }

        /// <summary>
        /// The payload: without this, the manifest makes the process DPI-aware but the
        /// WinForms controls never respond to a DPI change, and the fix is only half done.
        /// </summary>
        [Fact]
        public void Opts_winforms_into_per_monitor_v2()
        {
            var value = XDocument.Load(ConfigPath()).Root
                .Elements(DpiSection)
                .Elements("add")
                .Where(e => (string)e.Attribute("key") == "DpiAwareness")
                .Select(e => (string)e.Attribute("value"))
                .FirstOrDefault();

            Assert.Equal("PerMonitorV2", value);
        }
    }
}
