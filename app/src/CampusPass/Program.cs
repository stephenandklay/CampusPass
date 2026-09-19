using System;
using CampusPass.Core;

namespace CampusPass
{
    static class Program
    {
        /// <summary>
        /// The background service is the long-lived half of this process, so it must
        /// never drag WPF into its working set. This works because JIT is per-method:
        /// on the --background path GuiStartup.Run is never compiled, so the type is
        /// never resolved and PresentationFramework/Wpf.Ui are never loaded.
        /// Keep every WPF and Wpf.Ui reference behind GuiStartup.Run.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            AppData.MigrateFromLegacy();

            if (args != null && args.Length > 0 && args[0] == "--background")
            {
                BackgroundHost.Run();
                return;
            }
            Gui.GuiStartup.Run();
        }
    }
}
