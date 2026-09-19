using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace CampusPass.Core
{
    static class AppData
    {
        internal const string AppName = "CampusPass";
        internal const string LegacyAppName = "CampusFlow";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string RunName = "CampusPass";
        internal const string LegacyRunName = "CampusFlow";
        internal const string MutexName = @"Local\CampusPass.SingleInstance";
        internal const string StopEventName = @"Local\CampusPass.Stop";
        // Signalled as well, so a pre-rename instance retires instead of running
        // alongside us under a mutex name it cannot see.
        internal const string LegacyStopEventName = @"Local\CampusFlow.Stop";

        internal static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        internal static readonly string LegacyDirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppName);
        internal static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.xml");
        internal static readonly string CredentialPath = Path.Combine(DirectoryPath, "credentials.dat");
        internal static readonly string LogPath = Path.Combine(DirectoryPath, "campuspass.log");

        const long LogRollThresholdBytes = 2L * 1024 * 1024;

        /// <summary>
        /// Carries a pre-rename install forward: settings, the DPAPI-protected
        /// credential and the log all move to the CampusPass folder so an existing
        /// user does not have to re-enter their account. The legacy folder is left
        /// in place as the only copy to fall back to.
        /// </summary>
        internal static void MigrateFromLegacy()
        {
            try
            {
                if (!Directory.Exists(LegacyDirectoryPath)) return;
                Directory.CreateDirectory(DirectoryPath);
                CopyIfMissing(Path.Combine(LegacyDirectoryPath, "settings.xml"), SettingsPath);
                CopyIfMissing(Path.Combine(LegacyDirectoryPath, "credentials.dat"), CredentialPath);
                CopyIfMissing(Path.Combine(LegacyDirectoryPath, "campusflow.log"), LogPath);
            }
            catch { }
        }

        static void CopyIfMissing(string source, string target)
        {
            if (!File.Exists(source) || File.Exists(target)) return;
            File.Copy(source, target, false);
        }

        // Building an XmlSerializer is expensive and the background poll loop
        // calls LoadSettings on every check.
        static readonly XmlSerializer SettingsSerializer = new XmlSerializer(typeof(PortalSettings));

        internal static PortalSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string xml = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    using (var reader = new StringReader(xml))
                    {
                        var settings = (PortalSettings)SettingsSerializer.Deserialize(reader);
                        // Settings written before AutoDetect existed must default it to
                        // true, not to the deserializer's false.
                        if (!xml.Contains("<AutoDetect>")) settings.AutoDetect = true;
                        return settings;
                    }
                }
            }
            catch (Exception ex) { Log("读取设置失败：" + ex.Message); }
            return PortalSettings.ChinaMobileTemplate();
        }

        internal static void SaveSettings(PortalSettings settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            using (var stream = File.Create(SettingsPath))
                SettingsSerializer.Serialize(stream, settings);
        }

        internal static void SaveCredential(string username, string password)
        {
            Directory.CreateDirectory(DirectoryPath);
            string plain = Convert.ToBase64String(Encoding.UTF8.GetBytes(username)) + "\n"
                         + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
            File.WriteAllBytes(CredentialPath,
                ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));
        }

        internal static string[] LoadCredential()
        {
            try
            {
                if (!File.Exists(CredentialPath)) return null;
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(CredentialPath), null, DataProtectionScope.CurrentUser);
                string[] parts = Encoding.UTF8.GetString(plain).Split('\n');
                if (parts.Length != 2) return null;
                return new[]
                {
                    Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])),
                    Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))
                };
            }
            catch (Exception ex) { Log("读取凭据失败：" + ex.Message); return null; }
        }

        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                TryRollLog();
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss  ") + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Best effort only. The GUI and the background service both append to the
        /// same file, so the rename throws whenever the other holds a handle — in
        /// which case we just keep appending. Must never surface an error.
        /// </summary>
        static void TryRollLog()
        {
            try
            {
                var log = new FileInfo(LogPath);
                if (!log.Exists || log.Length < LogRollThresholdBytes) return;
                string archived = log.FullName + ".1";
                if (File.Exists(archived)) File.Delete(archived);
                log.MoveTo(archived);
            }
            catch { }
        }
    }
}
