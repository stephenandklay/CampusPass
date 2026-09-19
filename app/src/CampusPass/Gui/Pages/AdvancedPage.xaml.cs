using System;
using System.Windows;
using System.Windows.Controls;
using CampusPass.Core;

namespace CampusPass.Gui.Pages
{
    public partial class AdvancedPage : Page, ISettingsPage
    {
        public AdvancedPage()
        {
            InitializeComponent();
        }

        public void LoadFrom(MainWindow window)
        {
            PortalSettings settings = window.Draft;
            MethodBox.SelectedItem = string.IsNullOrEmpty(settings.Method) ? "POST" : settings.Method.ToUpperInvariant();
            UsernameFieldBox.Text = settings.UsernameField;
            PasswordFieldBox.Text = settings.PasswordField;
            ExtraFieldsBox.Text = settings.ExtraFields;
            ConnectivityUrlBox.Text = settings.ConnectivityUrl;
            ConnectivityExpectedBox.Text = settings.ConnectivityExpected;
            SuccessKeywordsBox.Text = settings.SuccessKeywords;
            IntervalBox.Value = Math.Max(20, Math.Min(3600, settings.CheckIntervalSeconds));
            DiscoverRedirectSwitch.IsChecked = settings.DiscoverRedirect;
        }

        public void WriteTo(PortalSettings settings)
        {
            settings.Method = MethodBox.SelectedItem == null ? "POST" : MethodBox.SelectedItem.ToString();
            settings.UsernameField = UsernameFieldBox.Text.Trim();
            settings.PasswordField = PasswordFieldBox.Text.Trim();
            // Deliberately untrimmed, as before: these two can legitimately carry
            // leading/trailing whitespace the portal expects.
            settings.ExtraFields = ExtraFieldsBox.Text;
            settings.ConnectivityUrl = ConnectivityUrlBox.Text.Trim();
            settings.ConnectivityExpected = ConnectivityExpectedBox.Text;
            settings.SuccessKeywords = SuccessKeywordsBox.Text;
            settings.CheckIntervalSeconds = Convert.ToInt32(IntervalBox.Value ?? 60d);
            settings.DiscoverRedirect = DiscoverRedirectSwitch.IsChecked == true;
        }

        void OnRestoreClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Of(this).RestoreTemplate();
        }

        void OnSaveClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Of(this).SaveSettingsOnly();
        }
    }
}
