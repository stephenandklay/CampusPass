using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace CampusPass.Core
{
    static class StartupManager
    {
        internal static bool IsEnabled
        {
            get { return RegisteredCommand != null; }
        }

        /// <summary>
        /// True when autostart is registered but launches a different binary than
        /// this one — after an in-place replacement, a move, or the rename away from
        /// CampusFlow, any of which leave a key that silently fails at logon.
        /// </summary>
        internal static bool IsRegisteredToOtherExecutable
        {
            get
            {
                string command = RegisteredCommand;
                if (string.IsNullOrEmpty(command)) return false;
                if (string.Equals(Unquote(command), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
                // Adopting would rewrite the key to point at *this* exe. When this exe
                // is a build output under bin/obj/publish, that silently moves a
                // user's autostart onto a path that a later rebuild can delete.
                if (IsBuildOutputPath(Environment.ProcessPath)) return false;
                return true;
            }
        }

        static bool IsBuildOutputPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalised = path.Replace('/', '\\');
            return normalised.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalised.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalised.IndexOf("\\publish\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The rename changed the registry *value name* too, so a pre-rename
        /// registration is filed under CampusFlow and would otherwise be invisible.
        /// </summary>
        internal static string RegisteredCommand
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppData.RunKey))
                {
                    if (key == null) return null;
                    return (key.GetValue(AppData.RunName) as string)
                        ?? (key.GetValue(AppData.LegacyRunName) as string);
                }
            }
        }

        internal static void Enable()
        {
            RemoveLegacyTask();
            Stop();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppData.RunKey))
                key.SetValue(AppData.RunName, "\"" + Environment.ProcessPath + "\" --background");
            LaunchBackground();
        }

        internal static void Disable()
        {
            RemoveLegacyTask();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppData.RunKey, true))
                if (key != null) key.DeleteValue(AppData.RunName, false);
            Stop();
        }

        internal static void Restart()
        {
            if (!IsEnabled) return;
            Stop();
            Thread.Sleep(500);
            LaunchBackground();
        }

        /// <summary>
        /// Repoints a stale Run key at this exe and restarts the service, so an
        /// in-place upgrade keeps working without the user pressing anything.
        /// </summary>
        internal static void AdoptCurrentExecutable()
        {
            RemoveLegacyTask();
            Stop();
            Thread.Sleep(500);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppData.RunKey))
                key.SetValue(AppData.RunName, "\"" + Environment.ProcessPath + "\" --background");
            LaunchBackground();
        }

        internal static bool IsBackgroundRunning()
        {
            bool created;
            using (var mutex = new Mutex(false, AppData.MutexName, out created))
            {
                try { return !mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { return true; }
            }
        }

        static void LaunchBackground()
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--background") { UseShellExecute = false });
        }

        static string Unquote(string command)
        {
            if (command.Length > 1 && command[0] == '"')
            {
                int closing = command.IndexOf('"', 1);
                if (closing > 0) return command.Substring(1, closing - 1);
            }
            // Unquoted form: strip the trailing argument.
            int space = command.IndexOf(' ');
            string head = space > 0 ? command.Substring(0, space) : command;
            return Path.GetFullPath(head);
        }

        static void Stop()
        {
            Signal(AppData.StopEventName);
            // A pre-rename instance waits on the old event name and cannot see the
            // new one; without this both would run and race against the portal.
            Signal(AppData.LegacyStopEventName);
        }

        static void Signal(string eventName)
        {
            try { EventWaitHandle.OpenExisting(eventName).Set(); }
            catch { }
        }

        static void RemoveLegacyTask()
        {
            RunHidden("schtasks.exe", "/End /TN CampusAutoLogin");
            RunHidden("schtasks.exe", "/Delete /TN CampusAutoLogin /F");
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppData.RunKey, true))
            {
                if (key == null) return;
                key.DeleteValue("CampusAutoLogin", false);
                key.DeleteValue(AppData.LegacyRunName, false);
            }
        }

        static void RunHidden(string file, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(file, arguments) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                using (Process process = Process.Start(info)) process.WaitForExit(4000);
            }
            catch { }
        }
    }
}
