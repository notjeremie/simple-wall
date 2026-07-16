using System;
using System.Windows.Forms;

namespace SimpleWall
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MessageBox.Show("scaffold ok");
        }
    }
}
