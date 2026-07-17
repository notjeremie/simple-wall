using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RenderShot
{
    /// <summary>
    /// Renders a Form to a PNG without a desktop session, so a layout bug can be SEEN
    /// over SSH. This exists because the spike's control window shipped with every
    /// GroupBox collapsed to a sliver: it was built and reviewed entirely over SSH,
    /// three rounds of review looked at VLC, and nobody could look at the screen.
    ///
    /// Deliberately never shows the form and never fires Load. Load is where the real
    /// windows start VLC, and a layout check that needs a video card is a layout check
    /// nobody will run. Touching Handle creates the HWND without OnCreateControl, so
    /// Form.OnLoad never runs; the children are then created explicitly, ignoring the
    /// visibility they inherit from a parent that is deliberately never shown.
    ///
    /// THE ONE RULE THIS IMPOSES ON EVERY FORM: build the control tree in the constructor.
    /// Anything added in Load is invisible here, and RenderShot cannot detect the difference
    /// -- it would report a clean render of a window that is missing half its controls, which
    /// is worse than no check at all. Load is for hardware, never for layout.
    ///
    /// Usage: RenderShot.exe &lt;Namespace.FormType&gt; &lt;out.png&gt; [width height]
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: RenderShot.exe <Namespace.FormType> <out.png> [width height]");
                return 2;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var form = Instantiate(args[0]))
                {
                    if (args.Length >= 4)
                        form.Size = new Size(int.Parse(args[2]), int.Parse(args[3]));

                    Realize(form);
                    var collapsed = DumpLayout(form, 0);

                    var bounds = new Rectangle(0, 0, form.Width, form.Height);
                    using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                    {
                        form.DrawToBitmap(bitmap, bounds);
                        var path = Path.GetFullPath(args[1]);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        bitmap.Save(path, ImageFormat.Png);
                        Console.WriteLine($"rendered {form.GetType().Name} {bounds.Width}x{bounds.Height} -> {path}");
                    }

                    if (collapsed > 0)
                    {
                        Console.Error.WriteLine($"{collapsed} control(s) collapsed to nothing -- this window is broken.");
                        return 3;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        /// <summary>
        /// A Form with a parameterless constructor, or a fixture: any type with a
        /// <c>public static Form Create()</c>. Real windows have dependencies (MainForm needs an
        /// engine), and the fakes for those belong here in the tool rather than in production
        /// code, where a render-only constructor would be one more thing to keep honest.
        /// </summary>
        private static Form Instantiate(string typeName)
        {
            var type = Type.GetType(typeName)
                       ?? Type.GetType($"{typeName}, SimpleWall")
                       ?? Type.GetType($"{typeName}, RenderShot");
            if (type == null) throw new ArgumentException($"no such type: {typeName}");

            if (typeof(Form).IsAssignableFrom(type))
            {
                if (type.GetConstructor(Type.EmptyTypes) != null)
                    return (Form)Activator.CreateInstance(type);

                throw new ArgumentException(
                    $"{typeName} has no parameterless constructor. Add a fixture with a " +
                    "'public static Form Create()' to RenderShot and pass that type instead.");
            }

            var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (create != null && typeof(Form).IsAssignableFrom(create.ReturnType))
                return (Form)create.Invoke(null, null);

            throw new ArgumentException($"{typeName} is neither a Form nor a fixture with a static Create()");
        }

        /// <summary>
        /// Gives the form and its whole tree real window handles and a settled layout,
        /// without showing it. Reading Handle creates the HWND but skips OnCreateControl,
        /// which is what would fire Form.OnLoad.
        /// </summary>
        private static void Realize(Form form)
        {
            var handle = form.Handle;
            GC.KeepAlive(handle);
            CreateChildren(form);
            form.PerformLayout();
            Application.DoEvents();
        }

        /// <summary>
        /// The measured tree next to the picture, returning the number of collapsed controls.
        /// A control can be missing from the PNG for two very different reasons -- laid out at
        /// zero size, or laid out fine and never painted -- and only the numbers tell those
        /// apart. An empty read-only TextBox, for instance, is Control-gray on a Control-gray
        /// form and its border is non-client, so it renders as nothing while being perfectly fine.
        /// </summary>
        private static int DumpLayout(Control control, int depth)
        {
            var b = control.Bounds;
            var name = string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name;
            var isCollapsed = (b.Width <= 1 || b.Height <= 1) && !IsLegitimatelyEmpty(control);
            Console.WriteLine($"{new string(' ', depth * 2)}{name} [{control.GetType().Name}] " +
                              $"{b.Width}x{b.Height} @{b.X},{b.Y} handle={control.IsHandleCreated}" +
                              (isCollapsed ? "  <-- COLLAPSED" : ""));

            var count = isCollapsed ? 1 : 0;
            foreach (Control child in control.Controls)
                count += DumpLayout(child, depth + 1);
            return count;
        }

        /// <summary>
        /// An AutoSize label with nothing to say measures zero wide, which is correct rather than
        /// broken. Flagging it would cry wolf, and a check that cries wolf gets ignored on the day
        /// it is right.
        ///
        /// Labels only, and no wider. TextBox.AutoSize is true by default and an empty clip-path
        /// field has empty Text, so a looser rule here quietly excuses a TextBox squeezed to 1px --
        /// which is precisely the bug this whole tool exists to catch.
        /// </summary>
        private static bool IsLegitimatelyEmpty(Control control) =>
            control is Label && control.AutoSize && string.IsNullOrEmpty(control.Text);

        private static readonly MethodInfo CreateControlIgnoringVisibility =
            typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null)
            ?? throw new InvalidOperationException(
                "Control.CreateControl(bool) is gone -- RenderShot cannot build a hidden control tree without it.");

        private static void CreateChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                // A child of a form that is never shown reports Visible == false and would
                // refuse to build itself, so force it past that check.
                CreateControlIgnoringVisibility.Invoke(child, new object[] { true });
                CreateChildren(child);
            }
        }
    }
}
