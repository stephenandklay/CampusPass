using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace CampusPass.Core
{
    static class BackgroundHost
    {
        static Timer timer;
        static readonly object StateGate = new object();
        static bool connected;
        static bool checkQueued;

        internal static void Run()
        {
            bool created;
            using (var mutex = new Mutex(true, AppData.MutexName, out created))
            {
                if (!created) return;
                bool stopCreated;
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, AppData.StopEventName, out stopCreated))
                {
                    stop.Reset();
                    NetworkChange.NetworkAvailabilityChanged += delegate { SetConnected(false); QueueCheck(3000); };
                    NetworkChange.NetworkAddressChanged += delegate { SetConnected(false); QueueCheck(3000); };
                    PortalSettings settings = AppData.LoadSettings();
                    int interval = IntervalSeconds(settings);
                    timer = new Timer(delegate { QueueCheck(0); }, null, 500, interval * 1000);
                    AppData.Log("后台服务已启动，未联网时检查间隔 " + interval + " 秒。");
                    stop.WaitOne();
                    timer.Dispose();
                    AppData.Log("后台服务已停止。");
                }
            }
        }

        static int IntervalSeconds(PortalSettings settings)
        {
            return Math.Max(20, Math.Min(3600, settings.CheckIntervalSeconds));
        }

        static void QueueCheck(int delay)
        {
            lock (StateGate)
            {
                // 网络已确认可用时不再轮询，交给网络变化事件在断网后唤醒。
                if (connected) return;
                if (checkQueued) return;
                checkQueued = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (delay > 0) Thread.Sleep(delay);
                    PortalSettings settings = AppData.LoadSettings();
                    bool online = PortalClient.HasInternet(settings);
                    if (online)
                    {
                        SetConnected(true);
                    }
                    else
                    {
                        SetConnected(false);
                        PortalClient.TryLogin(settings);
                    }
                }
                finally
                {
                    bool parked;
                    lock (StateGate)
                    {
                        checkQueued = false;
                        parked = connected;
                    }
                    // Only once the poll timer is suspended, so the pages are not
                    // needed again until a network-change event wakes us.
                    if (parked) TrimWorkingSet();
                }
            });
        }

        /// <summary>
        /// Hands the clean file-mapped runtime pages back to the OS while the service
        /// is parked. Committed memory is unaffected; this only lowers the resident
        /// figure, at the cost of a few soft page faults on the next check.
        /// </summary>
        static void TrimWorkingSet()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        static void SetConnected(bool value)
        {
            lock (StateGate)
            {
                connected = value;
                if (timer != null)
                {
                    int intervalMs = IntervalSeconds(AppData.LoadSettings()) * 1000;
                    timer.Change(value ? Timeout.Infinite : intervalMs, value ? Timeout.Infinite : intervalMs);
                }
            }
        }
    }
}
