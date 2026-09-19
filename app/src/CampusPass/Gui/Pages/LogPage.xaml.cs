using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CampusPass.Core;

namespace CampusPass.Gui.Pages
{
    public partial class LogPage : Page
    {
        readonly ObservableCollection<string> lines = new ObservableCollection<string>();

        public LogPage()
        {
            InitializeComponent();
            LogList.ItemsSource = lines;
            TailBox.SelectedIndex = 1;
            // The legacy viewer refreshed on TabPage.Enter; this is the WPF equivalent.
            IsVisibleChanged += delegate { if (IsVisible) Load(); };
        }

        int Tail { get { return TailBox.SelectedItem is int value ? value : 800; } }

        void Load()
        {
            try
            {
                string[] tail = LogTail.ReadLastLines(AppData.LogPath, Tail);
                lines.Clear();
                foreach (string line in tail) lines.Add(line);
                if (lines.Count > 0) LogList.ScrollIntoView(lines[lines.Count - 1]);

                long bytes = LogTail.GetSizeBytes(AppData.LogPath);
                LogFooter.Text = "显示最后 " + lines.Count + " 行 · 文件 " + FormatSize(bytes);
                if (bytes > 5L * 1024 * 1024)
                    LogFooter.Text += "（日志较大，关闭程序后可手动删除）";
            }
            catch (Exception ex)
            {
                lines.Clear();
                lines.Add("读取日志失败：" + ex.Message);
                LogFooter.Text = "";
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            Load();
        }

        void OnTailChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsVisible) Load();
        }

        void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppData.DirectoryPath);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppData.DirectoryPath + "\"")
                {
                    // .NET Core flipped this default to false; explorer needs the shell.
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "打开日志目录失败：" + ex.Message, "CampusPass");
            }
        }
    }
}
