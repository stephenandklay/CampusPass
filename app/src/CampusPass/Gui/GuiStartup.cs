using System;
using System.Windows;
using CampusPass.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CampusPass.Gui
{
    /// <summary>
    /// The only entry point allowed to touch WPF. Program.Main reaches it solely on
    /// the GUI path, which is what keeps the background service free of
    /// PresentationFramework.
    /// </summary>
    static class GuiStartup
    {
        internal static void Run()
        {
            var app = new App();
            app.InitializeComponent();

            // Light only: the app no longer offers a theme switch, and forcing it
            // keeps the palette from following whatever Windows is set to.
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, updateAccent: false);

            // A rename or an in-place move leaves the autostart value pointing at the
            // old path, which fails silently at the next logon. Repoint it here so the
            // user's enable choice survives without them doing anything.
            if (StartupManager.IsRegisteredToOtherExecutable)
                StartupManager.AdoptCurrentExecutable();

            app.Run(new MainWindow());
        }
    }
}
