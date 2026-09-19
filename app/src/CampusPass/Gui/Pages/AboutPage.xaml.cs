using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CampusPass.Core;

namespace CampusPass.Gui.Pages
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();

            string version = InformationalVersion();
            VersionLine.Text = "Version " + version + "  ·  Windows 11";
            DiagVersion.Text = version;
            DiagRuntime.Text = RuntimeInformation.FrameworkDescription;
            DiagDataPath.Text = AppData.DirectoryPath;
            DiagLogPath.Text = AppData.LogPath;
        }

        static string InformationalVersion()
        {
            string version = typeof(AboutPage).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(version)) return "未知";
            int sourceRevision = version.IndexOf('+');
            return sourceRevision > 0 ? version.Substring(0, sourceRevision) : version;
        }

        void OnCopyDiagnostics(object sender, RoutedEventArgs e)
        {
            var text = new StringBuilder();
            text.AppendLine("CampusPass " + DiagVersion.Text);
            text.AppendLine("Runtime   : " + DiagRuntime.Text);
            text.AppendLine("OS        : " + RuntimeInformation.OSDescription);
            text.AppendLine("Data dir  : " + AppData.DirectoryPath);
            text.AppendLine("Log file  : " + AppData.LogPath);
            try
            {
                Clipboard.SetText(text.ToString());
                CopyButton.Content = "已复制";
                var revert = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromSeconds(2)
                };
                revert.Tick += delegate
                {
                    revert.Stop();
                    CopyButton.Content = "复制诊断信息";
                };
                revert.Start();
            }
            catch
            {
                MessageBox.Show(Window.GetWindow(this), "复制失败，请手动记录上面的路径。", "CampusPass");
            }
        }
    }
}
