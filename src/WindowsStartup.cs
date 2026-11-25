using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    internal static class WindowsStartup
    {
        internal const string Argument = "--startup";
        internal const string SignalName = "Local\\ZaloManager_Startup";
        private const string LinkName = "Zalo Manager.lnk";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan LaunchGap = TimeSpan.FromSeconds(3);

        internal static string[] SelectedProfiles(IEnumerable<Profile> profiles)
        {
            return profiles.Where(profile => !profile.IsOriginal && profile.StartWithWindows).Select(profile => profile.Id).ToArray();
        }

        internal static void SetRegistration(bool enabled, string directory = null)
        {
            directory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (String.IsNullOrEmpty(directory))
                throw new IOException("Không tìm được thư mục Khởi động của người dùng Windows.");
            string link = Path.Combine(directory, LinkName);
            if (!enabled)
            {
                if (Directory.Exists(directory)) File.Delete(link);
                return;
            }

            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                ShellIdentity.SaveManagerShortcut(temporary, Argument, "Zalo Manager", null);
                if (File.Exists(link)) File.Replace(temporary, link, null);
                else File.Move(temporary, link);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static bool IsRegistered(string directory = null)
        {
            directory = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return !String.IsNullOrEmpty(directory) && File.Exists(Path.Combine(directory, LinkName));
        }

        internal static async Task LaunchSelectedAsync(string[] ids, Func<string, CancellationToken, Task<bool>> launch,
            CancellationToken token, Func<TimeSpan, CancellationToken, Task> delay)
        {
            if (ids.Length == 0) return;
            await delay(InitialDelay, token);
            bool launched = false;
            foreach (string id in ids)
            {
                if (launched) await delay(LaunchGap, token);
                token.ThrowIfCancellationRequested();
                launched = await launch(id, token);
            }
        }
    }

    public partial class ManagerWindow
    {
        private readonly CancellationTokenSource startupCancellation = new CancellationTokenSource();
        private bool deferStartupStorage;
        private Task startupLaunch;

        internal void RequestStartupLaunch()
        {
            if (startupLaunch != null || TrayManager.IsExiting) return;
            var ids = WindowsStartup.SelectedProfiles(profiles);
            startupLaunch = RunStartupLaunchAsync(ids, startupCancellation.Token);
        }

        private async Task RunStartupLaunchAsync(string[] ids, CancellationToken token)
        {
            try { await WindowsStartup.LaunchSelectedAsync(ids, StartSelectedProfileAsync, token, Task.Delay); }
            catch (OperationCanceledException) { if (!token.IsCancellationRequested) throw; }
            finally
            {
                if (deferStartupStorage)
                {
                    deferStartupStorage = false;
                    if (!TrayManager.IsExiting) RefreshStorageUsage();
                }
            }
        }

        private async Task<bool> StartSelectedProfileAsync(string id, CancellationToken token)
        {
            while (busy) await Task.Delay(500, token);
            var profile = profiles.FirstOrDefault(item => item.Id == id && item.StartWithWindows);
            if (profile == null) return false;
            SetBusy(true);
            try
            {
                return await Task.Run(() => Instance.StartAtLogon(profile, token), token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                try { Store.Log(new IOException("Không tự mở được hồ sơ " + profile.Id + ".", error)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                status.Text = "Không tự mở được hồ sơ “" + profile.Name + "”. " + error.Message;
                return true;
            }
            finally { SetBusy(false); }
        }

        internal async Task StopStartupLaunchAsync()
        {
            startupCancellation.Cancel();
            if (startupLaunch != null) await startupLaunch;
        }
    }
}
