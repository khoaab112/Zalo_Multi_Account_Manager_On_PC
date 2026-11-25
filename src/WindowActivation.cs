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
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
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
                string className = name.ToString();
                if (className != "Chrome_WidgetWin_1" &&
                    !(className.StartsWith("Sandbox:", StringComparison.OrdinalIgnoreCase) &&
                      className.EndsWith(":Chrome_WidgetWin_1", StringComparison.Ordinal))) return true;
                string title = Title(window);
                if (title.Length == 0 || String.Equals(title, "Shared Worker", StringComparison.OrdinalIgnoreCase)) return true;
                NativeRect rect;
                if (GetWindowRect(window, out rect) &&
                    (IsIconic(window) || (rect.Right - rect.Left >= 200 && rect.Bottom - rect.Top >= 100))) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows.OrderByDescending(IsWindowVisible).ToList();
        }

        internal static string Title(IntPtr window)
        {
            var title = new StringBuilder(Math.Max(2, GetWindowTextLength(window) + 1));
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

        internal static int OwnerProcessId(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return (int)pid;
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
                    if (IsIconic(window))
                    {
                        ShowWindowAsync(window, 9);
                        SetForegroundWindow(window);
                        return true;
                    }
                    if (IsWindowVisible(window))
                    {
                        SetForegroundWindow(window);
                        return true;
                    }
                }
                return false;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        public static bool ActivateWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            ShowWindowAsync(window, 9);
            return SetForegroundWindow(window);
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
