using System;
using System.IO;
using Microsoft.Win32;

namespace SimpleWall.Infrastructure
{
    /// <summary>
    /// Whether Windows launches SimpleWall at logon, via HKCU\...\Run.
    ///
    /// HKCU deliberately: no admin rights, no service, and unticking removes it cleanly. A service
    /// would run in session 0 with no desktop to put an always-on-top output window on, which is
    /// the whole product.
    ///
    /// An instance rather than the static class the plan sketched, for one reason: the value name
    /// is injectable, so the tests write to a name of their own and prove the registry round-trip
    /// against the real registry instead of asserting it from memory. Nothing else here wants a
    /// static.
    ///
    /// **The registry is the only source of truth.** WallConfig used to carry an `Autostart` bool
    /// as well; it was never read, and it is gone. Two sources would disagree the first time
    /// anyone touched msconfig or Task Manager's Startup tab, and this app would then show a
    /// ticked box for a machine that never comes back after a reboot -- the exact silent lie
    /// every other part of this project is built to avoid.
    /// </summary>
    public class Autostart
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultValueName = "SimpleWall";

        private readonly string _valueName;

        public Autostart(string valueName = DefaultValueName)
        {
            if (string.IsNullOrWhiteSpace(valueName)) throw new ArgumentException("A value name is required.", nameof(valueName));
            _valueName = valueName;
        }

        /// <summary>Whether Windows will launch SOMETHING for us at logon -- see <see cref="RegisteredPath"/>.</summary>
        public virtual bool IsEnabled() => RegisteredPath() != null;

        /// <summary>
        /// The EXE Windows will actually launch at logon, unquoted, or null if autostart is off.
        ///
        /// This exists so the settings tab can say something better than yes/no. If the app has
        /// been copied or moved, the Run value still points at the OLD path: the tick box would
        /// say "on", and the machine would boot and launch nothing (or worse, a stale copy). The
        /// answer to "is autostart on" is then genuinely "yes, but not for this one", and only a
        /// path can say that.
        /// </summary>
        public virtual string RegisteredPath()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                // GetValue expands REG_EXPAND_SZ for us; anything that isn't a string was not
                // written by us and is not something we can honestly claim to understand.
                var value = key?.GetValue(_valueName) as string;
                if (string.IsNullOrWhiteSpace(value)) return null;
                return value.Trim().Trim('"');
            }
        }

        /// <summary>
        /// Throws on failure, by design. A checkbox that ticks and silently does nothing is worse
        /// than an error message: the operator walks away believing the wall will come back after
        /// the next reboot. The caller catches and says so on screen.
        ///
        /// CreateSubKey, not OpenSubKey(writable: true) as the plan sketched -- that returns NULL
        /// when the key is absent, and the sketch's `if (key == null) return;` turned the one case
        /// worth reporting into a silent no-op. Run essentially always exists, which is precisely
        /// what makes that branch untested and permanent.
        /// </summary>
        public virtual void Set(bool enabled, string exePath)
        {
            if (enabled && string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("An exe path is required to enable autostart.", nameof(exePath));

            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null)
                    throw new InvalidOperationException(@"HKCU\" + RunKey + " could not be opened for writing.");

                // Quoted: the path contains spaces on any ordinary install ("C:\Program Files\..."),
                // and an unquoted Run value is split on them -- Windows would try to launch
                // "C:\Program" with the rest as arguments.
                if (enabled) key.SetValue(_valueName, "\"" + exePath + "\"", RegistryValueKind.String);
                else key.DeleteValue(_valueName, throwOnMissingValue: false);
            }
        }

        /// <summary>
        /// Whether <paramref name="exePath"/> is the one registered. Compared as full paths and
        /// case-insensitively, because Windows paths are, and a registered
        /// "c:\wall\simplewall.exe" is the same file as "C:\Wall\SimpleWall.exe".
        /// </summary>
        public virtual bool PointsAt(string exePath)
        {
            var registered = RegisteredPath();
            if (registered == null || string.IsNullOrWhiteSpace(exePath)) return false;

            try
            {
                return string.Equals(Path.GetFullPath(registered), Path.GetFullPath(exePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // A hand-edited Run value can hold something that isn't a path at all. It is
                // certainly not ours, and asking "is autostart pointing at us" must not throw.
                return false;
            }
        }
    }
}
