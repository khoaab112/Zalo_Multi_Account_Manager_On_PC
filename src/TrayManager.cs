using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace ZaloManagerPoc
{
    public static class TrayManager
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public static readonly int WM_SHOW_MANAGER = RegisterWindowMessage("ZaloManager_ShowMainWindow_V2");
        public const int HWND_BROADCAST = 0xffff;
        public const string ShowEventName = "Local\\ZaloManager_ShowEvent";

        private static NotifyIcon notifyIcon;
        private static Window mainWindow;
        private static System.Threading.EventWaitHandle showEvent;
        private static bool listenerStarted;

        public static void Initialize(Window window)
        {
            mainWindow = window;
            StartShowEventListener();
            if (notifyIcon != null) return;

            notifyIcon = new NotifyIcon();
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZaloManager.Icon.ico"))
                {
                    if (stream != null) notifyIcon.Icon = new Icon(stream);
                    else notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                notifyIcon.Icon = SystemIcons.Application;
            }

            notifyIcon.Text = "Zalo Manager";
            notifyIcon.Visible = true;

            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Mở Zalo Manager", null, (s, e) => RestoreMainWindow());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Thoát hoàn toàn", null, (s, e) => RequestExit());
            contextMenu.Opening += delegate { exitItem.Enabled = CanRequestExit(); };
            contextMenu.Items.Add(exitItem);

            notifyIcon.ContextMenuStrip = contextMenu;
            notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
        }

        public static void StartShowEventListener()
        {
            if (listenerStarted) return;
            listenerStarted = true;
            try
            {
                bool created;
                showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowEventName, out created);
                var thread = new System.Threading.Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            showEvent.WaitOne();
                            var app = System.Windows.Application.Current;
                            if (app != null && app.Dispatcher != null)
                            {
                                app.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    RestoreMainWindow();
                                }));
                            }
                        }
                        catch (System.Threading.ThreadAbortException) { break; }
                        catch { }
                    }
                }) { IsBackground = true };
                thread.Start();
            }
            catch { }
        }

        public static void SignalShowManager()
        {
            try
            {
                using (var ev = System.Threading.EventWaitHandle.OpenExisting(ShowEventName))
                {
                    ev.Set();
                }
            }
            catch { }
            try
            {
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOW_MANAGER, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        public static void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (notifyIcon != null && notifyIcon.Visible)
            {
                notifyIcon.ShowBalloonTip(3000, title, message, icon);
            }
        }

        public static void HideToTray()
        {
            if (mainWindow != null)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                mainWindow.Hide();
            }
        }

        public static void RestoreMainWindow()
        {
            if (mainWindow != null)
            {
                mainWindow.Show();
                mainWindow.ShowInTaskbar = true;
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                mainWindow.Focus();

                var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
                if (helper.Handle != IntPtr.Zero)
                {
                    ShowWindowAsync(helper.Handle, 9);
                    SetForegroundWindow(helper.Handle);
                }

            }
        }

        internal static bool IsExiting { get; private set; }
        private static bool confirmingExit;

        private static bool CanRequestExit()
        {
            var manager = mainWindow as ManagerWindow;
            return !IsExiting && !confirmingExit && (manager == null || manager.CanRequestExit);
        }

        internal static void RequestExit()
        {
            if (!CanRequestExit()) return;
            bool confirmed;
            confirmingExit = true;
            try { confirmed = ConfirmDialog.CreateManagerExit(mainWindow).ShowDialog() == true; }
            finally { confirmingExit = false; }
            if (confirmed) ExitApplication();
        }

        private static async void ExitApplication()
        {
            if (IsExiting) return;
            IsExiting = true;
            var manager = mainWindow as ManagerWindow;
            if (manager != null) manager.UpdateShutdownState();
            try
            {
                if (manager != null) await manager.StopStartupLaunchAsync();
            }
            catch (Exception error)
            {
                Store.Log(error);
                IsExiting = false;
                if (manager != null) manager.UpdateShutdownState();
                RestoreMainWindow();
                NoticeDialog.Show(mainWindow, "Chưa thể thoát", "Không hoàn tất được việc lưu dữ liệu. Có thể thử thoát lại.");
                return;
            }
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }
            System.Windows.Application.Current.Shutdown();
        }
    }
}
