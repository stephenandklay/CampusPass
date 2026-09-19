using System;
using System.Windows;
using System.Windows.Controls;
using CampusPass.Core;

namespace CampusPass.Gui.Pages
{
    public partial class BasicPage : Page, ICredentialPage
    {
        bool seeded;

        public BasicPage()
        {
            InitializeComponent();
        }

        public void LoadFrom(MainWindow window)
        {
            PortalSettings settings = window.Draft;

            // Credentials are not part of settings.xml, so a later re-seed (triggered
            // by 识别表单 or 恢复模板 refreshing this cached page) must not overwrite
            // what the user is typing into them.
            if (!seeded)
            {
                string[] credential = AppData.LoadCredential();
                UsernameBox.Text = credential != null ? credential[0] : "";
                PasswordBox.Password = "";
                seeded = true;
            }

            PortalUrlBox.Text = settings.PortalUrl;
            SubmitUrlBox.Text = settings.SubmitUrl;
            AutoDetectSwitch.IsChecked = settings.AutoDetect;
        }

        public void WriteTo(PortalSettings settings)
        {
            settings.PortalUrl = PortalUrlBox.Text.Trim();
            settings.SubmitUrl = SubmitUrlBox.Text.Trim();
            settings.AutoDetect = AutoDetectSwitch.IsChecked == true;
        }

        public string UsernameText { get { return UsernameBox.Text; } }
        public string PasswordText { get { return PasswordBox.Password ?? ""; } }

        void SetBusy(bool busy)
        {
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            DetectButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy;
            TestButton.IsEnabled = !busy;
            StopButton.IsEnabled = !busy;
        }

        async void OnDetectClick(object sender, RoutedEventArgs e)
        {
            await MainWindow.Of(this).DetectForm(SetBusy);
        }

        async void OnTestClick(object sender, RoutedEventArgs e)
        {
            await MainWindow.Of(this).TestLogin(SetBusy);
        }

        void OnSaveClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Of(this).SaveAndEnable();
        }

        void OnStopClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Of(this).StopBackground();
        }
    }
}
