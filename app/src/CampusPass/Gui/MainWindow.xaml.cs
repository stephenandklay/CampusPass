using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CampusPass.Core;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;

namespace CampusPass.Gui
{
    /// <summary>
    /// Pages own their controls but not the settings. The single draft lives here so
    /// that saving from 基础设置 still persists whatever 高级设置 is showing, exactly
    /// as the old single-form WinForms layout did.
    /// </summary>
    internal interface ISettingsPage
    {
        void LoadFrom(MainWindow window);
        void WriteTo(PortalSettings settings);
    }

    internal interface ICredentialPage : ISettingsPage
    {
        string UsernameText { get; }
        string PasswordText { get; }
    }

    public partial class MainWindow : FluentWindow
    {
        internal PortalSettings Draft { get; private set; }

        readonly Dictionary<Type, ISettingsPage> created = new Dictionary<Type, ISettingsPage>();

        public MainWindow()
        {
            InitializeComponent();
            Draft = AppData.LoadSettings();

            Loaded += delegate
            {
                // NavigationView resolves its internal frame from the template, which
                // does not exist during construction.
                if (!navigated) { navigated = true; RootNavigation.Navigate(typeof(Pages.BasicPage), null); }
                RefreshStatus();
            };
            Activated += delegate { RefreshStatus(); };
        }

        bool navigated;

        internal static MainWindow Of(DependencyObject child)
        {
            return (MainWindow)Window.GetWindow(child);
        }

        ICredentialPage Credentials
        {
            get { return created.Values.OfType<ICredentialPage>().FirstOrDefault(); }
        }

        void OnPageNavigated(NavigationView sender, NavigatedEventArgs args)
        {
            var page = args.Page as ISettingsPage;
            if (page == null) return;
            // Only seed a page the first time it appears; re-seeding a cached page
            // would wipe edits the user is in the middle of.
            if (created.ContainsKey(page.GetType())) return;
            created[page.GetType()] = page;
            page.LoadFrom(this);
        }

        void Collect()
        {
            foreach (ISettingsPage page in created.Values) page.WriteTo(Draft);
        }

        internal void Broadcast()
        {
            foreach (ISettingsPage page in created.Values) page.LoadFrom(this);
        }

        // ------------------------------------------------------------------ status

        void RefreshStatus()
        {
            bool enabled = StartupManager.IsEnabled;
            StatusDot.Fill = new SolidColorBrush(enabled
                ? Color.FromRgb(0x86, 0xEF, 0xAC)
                : Color.FromRgb(0xA9, 0xB6, 0xC6));
            StatusPill.Background = new SolidColorBrush(enabled
                ? Color.FromArgb(0x1A, 0x86, 0xEF, 0xAC)
                : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            StatusText.Text = enabled ? "后台已启用" : "后台未启用";
        }

        // --------------------------------------------------------------- validation

        bool ValidateValues(bool requirePassword)
        {
            Collect();
            bool missingManualFields = !Draft.AutoDetect && (
                string.IsNullOrWhiteSpace(Draft.SubmitUrl) ||
                string.IsNullOrWhiteSpace(Draft.UsernameField) ||
                string.IsNullOrWhiteSpace(Draft.PasswordField));

            if (string.IsNullOrWhiteSpace(Draft.PortalUrl) || missingManualFields ||
                string.IsNullOrWhiteSpace(Draft.ConnectivityUrl))
            {
                MessageBox.Show(this,
                    Draft.AutoDetect
                        ? "登录页地址和联网检测地址不能为空。"
                        : "登录地址、提交地址、字段名和联网检测地址不能为空。",
                    "CampusPass");
                return false;
            }

            ICredentialPage credentials = Credentials;
            string username = credentials == null ? "" : credentials.UsernameText;
            string password = credentials == null ? "" : credentials.PasswordText;
            if (requirePassword && (string.IsNullOrWhiteSpace(username) ||
                (password.Length == 0 && AppData.LoadCredential() == null)))
            {
                MessageBox.Show(this, "请输入校园网账号和密码。", "CampusPass");
                return false;
            }
            return true;
        }

        void PersistCredentialsIfProvided()
        {
            ICredentialPage credentials = Credentials;
            if (credentials == null) return;
            if (credentials.PasswordText.Length > 0)
                AppData.SaveCredential(credentials.UsernameText.Trim(), credentials.PasswordText);
        }

        // ------------------------------------------------------------------ actions

        internal void SaveAndEnable()
        {
            if (!ValidateValues(true)) return;
            AppData.SaveSettings(Draft);
            PersistCredentialsIfProvided();
            StartupManager.Enable();
            RefreshStatus();
            MessageBox.Show(this, "已保存并启用。连接校园网后会在后台自动认证。", "CampusPass");
        }

        internal void SaveSettingsOnly()
        {
            if (!ValidateValues(false)) return;
            AppData.SaveSettings(Draft);
            StartupManager.Restart();
            RefreshStatus();
            MessageBox.Show(this, "设置已保存。", "CampusPass");
        }

        internal void StopBackground()
        {
            StartupManager.Disable();
            RefreshStatus();
            MessageBox.Show(this, "后台运行和开机启动已停止。", "CampusPass");
        }

        /// <summary>
        /// The button labels this "恢复默认", which is only accurate because
        /// ChinaMobileTemplate is also what AppData.LoadSettings falls back to when
        /// there is no settings.xml. Change one without the other and the label lies.
        /// </summary>
        internal void RestoreTemplate()
        {
            Draft = PortalSettings.ChinaMobileTemplate();
            Broadcast();
        }

        internal async Task TestLogin(Action<bool> busy)
        {
            if (!ValidateValues(true)) return;
            AppData.SaveSettings(Draft);
            PersistCredentialsIfProvided();
            busy(true);
            string result;
            try
            {
                result = await Task.Run(() => PortalClient.TryLogin(AppData.LoadSettings()));
            }
            finally
            {
                busy(false);
            }
            RefreshStatus();
            MessageBox.Show(this, result, "CampusPass");
        }

        internal async Task DetectForm(Action<bool> busy)
        {
            Collect();
            if (string.IsNullOrWhiteSpace(Draft.PortalUrl))
            {
                MessageBox.Show(this, "请先填写登录页地址。", "CampusPass");
                return;
            }
            busy(true);
            PortalClient.DetectedForm detected = null;
            string error = null;
            try
            {
                string url = Draft.PortalUrl;
                detected = await Task.Run(() => PortalClient.DetectForm(url));
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                busy(false);
            }

            if (error != null)
            {
                MessageBox.Show(this, "识别失败：" + error, "CampusPass");
                return;
            }
            if (detected == null)
            {
                MessageBox.Show(this, "没有识别到普通登录表单。请打开高级设置手动填写，或确认该页面不是验证码/统一认证页面。", "CampusPass");
                return;
            }
            Draft.SubmitUrl = detected.SubmitUrl;
            Draft.Method = detected.Method;
            Draft.UsernameField = detected.UsernameField;
            Draft.PasswordField = detected.PasswordField;
            Draft.ExtraFields = detected.ExtraFields;
            Draft.AutoDetect = true;
            Broadcast();
            MessageBox.Show(this, "已识别登录表单。点击“保存并启用”即可。", "CampusPass");
        }
    }
}
