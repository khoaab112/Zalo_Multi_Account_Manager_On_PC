using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZaloManagerPoc
{
    public static class WindowActivation
    {
        private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

        // Process.MainWindowHandle excludes hidden tray windows. EnumWindows includes them.
        public static List<IntPtr> Find(IEnumerable<int> processIds)
        {
            var ids = new HashSet<int>(processIds);
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint pid; GetWindowThreadProcessId(window, out pid);
                if (!ids.Contains((int)pid)) return true;
                var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                if (String.Equals(title.ToString(), "Shared Worker", StringComparison.OrdinalIgnoreCase)) return true;
                NativeRect rect;
                if (name.ToString() == "Chrome_WidgetWin_1" && GetWindowTextLength(window) > 0 && GetWindowRect(window, out rect) &&
                    (IsIconic(window) || (rect.Right - rect.Left >= 200 && rect.Bottom - rect.Top >= 100))) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows.OrderByDescending(IsWindowVisible).ToList();
        }

        public static bool Restore(Profile profile)
        {
            var processes = Instance.Processes(profile);
            try
            {
                // Restrict activation to Zalo's own UI, excluding its call/capture helpers.
                var windows = Find(processes.Where(process => process.ProcessName == "Zalo").Select(process => process.Id));
                foreach (IntPtr window in windows)
                {
                    // Never show/restore Electron windows through Win32: their compositor
                    // can remain hidden while the HWND is visible and still accepts clicks.
                    if (IsWindowVisible(window) && !IsIconic(window)) { SetForegroundWindow(window); return true; }
                }
                return false;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        public static void HideForReopen(Profile profile)
        {
            var processes = Instance.Processes(profile);
            try
            {
                foreach (var window in Find(processes.Where(p => p.ProcessName == "Zalo").Select(p => p.Id)))
                    if (IsWindowVisible(window))
                    {
                        ShowWindowAsync(window, 0);
                        for (int attempt = 0; attempt < 10 && IsWindowVisible(window); attempt++)
                            System.Threading.Thread.Sleep(25);
                        if (IsWindowVisible(window)) throw new InvalidOperationException("Cửa sổ Zalo chưa sẵn sàng để mở lại. Thử lại sau.");
                    }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
