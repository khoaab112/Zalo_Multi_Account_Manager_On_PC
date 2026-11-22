using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;

namespace ZaloManagerPoc
{
    public static class SourceTarget
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ScreenPoint { public int X; public int Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out ScreenPoint point);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        public static bool IsOwnWindowAt(ScreenPoint point)
        {
            uint pid; GetWindowThreadProcessId(GetAncestor(WindowFromPoint(point), 2), out pid);
            return pid == Process.GetCurrentProcess().Id;
        }

        public static string ProcessDisplayName(int processId)
        {
            using (var process = Process.GetProcessById(processId))
            {
                string name = process.ProcessName;
                try
                {
                    string description = FileVersionInfo.GetVersionInfo(process.MainModule.FileName).FileDescription;
                    if (!String.IsNullOrWhiteSpace(description)) name = description.Trim();
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                return name;
            }
        }

        public static string ApplicationNameAt(ScreenPoint point)
        {
            IntPtr window = GetAncestor(WindowFromPoint(point), 2);
            var className = new StringBuilder(256); GetClassName(window, className, className.Capacity);
            if (className.ToString() == "Shell_TrayWnd" || className.ToString() == "Shell_SecondaryTrayWnd")
            {
                var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
                for (int depth = 0; element != null && depth < 12; depth++, element = TreeWalker.RawViewWalker.GetParent(element))
                    if ((element.Current.AutomationId ?? "").StartsWith("Appid:", StringComparison.OrdinalIgnoreCase))
                        return String.IsNullOrWhiteSpace(element.Current.Name) ? element.Current.AutomationId.Substring(6) : element.Current.Name;
            }
            uint pid; GetWindowThreadProcessId(window, out pid);
            return ProcessDisplayName((int)pid);
        }

        public static Rect BoundsAt(ScreenPoint point)
        {
            IntPtr window = GetAncestor(WindowFromPoint(point), 2);
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid == Process.GetCurrentProcess().Id) return Rect.Empty;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd")
            {
                var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
                for (int depth = 0; element != null && depth < 12; depth++, element = TreeWalker.RawViewWalker.GetParent(element))
                    if ((element.Current.AutomationId ?? "").StartsWith("Appid:", StringComparison.OrdinalIgnoreCase))
                        return element.Current.BoundingRectangle;
                return Rect.Empty;
            }
            NativeRect rect;
            if (!GetWindowRect(window, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return Rect.Empty;
            return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public static string ResolveExecutable(string executable)
        {
            if (!String.Equals(Path.GetFileName(executable), "Zalo.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Hãy trỏ vào ứng dụng Zalo PC.");
            string directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            if (directory.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                directory.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloSpace") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Đây là bản clone. Hãy trỏ vào Zalo gốc.");
            if (File.Exists(Path.Combine(directory, "resources", "app.asar"))) return Path.Combine(directory, "Zalo.exe");
            // A pinned shortcut commonly points at the launcher in Programs/Zalo.
            var versions = Directory.GetDirectories(directory, "Zalo-*")
                .Where(path => File.Exists(Path.Combine(path, "Zalo.exe")) && File.Exists(Path.Combine(path, "resources", "app.asar")))
                .OrderByDescending(path => new Version(FileVersionInfo.GetVersionInfo(Path.Combine(path, "Zalo.exe")).FileVersion)).ToList();
            if (versions.Count == 0) throw new InvalidOperationException("Chưa tìm thấy thư mục phiên bản của Zalo này.");
            return Path.Combine(versions[0], "Zalo.exe");
        }

        public static string ResolveAt(ScreenPoint point)
        {
            IntPtr window = GetAncestor(WindowFromPoint(point), 2);
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            using (var process = Process.GetProcessById((int)pid))
            {
                if (String.Equals(process.ProcessName, "Zalo", StringComparison.OrdinalIgnoreCase))
                    return ResolveExecutable(process.MainModule.FileName);
            }
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() != "Shell_TrayWnd" && className.ToString() != "Shell_SecondaryTrayWnd")
                throw new InvalidOperationException("Rê chuột lên cửa sổ Zalo hoặc biểu tượng Zalo trên taskbar.");

            var element = AutomationElement.FromPoint(new Point(point.X, point.Y));
            for (int depth = 0; element != null && depth < 12; depth++, element = TreeWalker.RawViewWalker.GetParent(element))
            {
                string id = element.Current.AutomationId;
                if (String.IsNullOrEmpty(id)) continue;
                const string prefix = "Appid:";
                if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                return ResolveTaskbarIdentity(id.Substring(prefix.Length));
            }
            throw new InvalidOperationException("Taskbar chưa cung cấp định danh ứng dụng. Mở cửa sổ Zalo rồi trỏ vào cửa sổ đó.");
        }

        public static string ResolveTaskbarIdentity(string identity)
        {
            string candidate = identity.Trim('"');
            if (Path.IsPathRooted(candidate) && File.Exists(candidate)) return ResolveExecutable(candidate);
            var matches = FindShortcuts(identity)
                .GroupBy(path => {
                    string directory = Path.GetDirectoryName(path);
                    return Path.GetFileName(directory).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetDirectoryName(directory) : directory;
                }, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(path => new Version(FileVersionInfo.GetVersionInfo(path).FileVersion)).First()).ToList();
            if (matches.Count == 1) return ResolveExecutable(matches[0]);
            if (matches.Count > 1) throw new InvalidOperationException("Có nhiều nguồn Zalo trùng định danh. Trỏ vào cửa sổ Zalo cần chọn.");
            throw new InvalidOperationException("Không tìm thấy shortcut tương ứng. Mở cửa sổ Zalo rồi trỏ vào cửa sổ đó.");
        }

        private static List<string> FindShortcuts(string identity)
        {
            var results = new List<string>();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string[] roots = { Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) };
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            try
            {
                foreach (string root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string link in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        object folder = null, item = null;
                        try
                        {
                            folder = Call(shell, "NameSpace", Path.GetDirectoryName(link));
                            item = Call(folder, "ParseName", Path.GetFileName(link));
                            string appId = Convert.ToString(Call(item, "ExtendedProperty", "System.AppUserModel.ID"));
                            if (!String.Equals(appId, identity, StringComparison.OrdinalIgnoreCase)) continue;
                            string target = Convert.ToString(Call(item, "ExtendedProperty", "System.Link.TargetParsingPath"));
                            if (!String.IsNullOrEmpty(target)) results.Add(ResolveExecutable(target));
                        }
                        catch (COMException) { }
                        catch (InvalidOperationException) { }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        finally
                        {
                            if (item != null) Marshal.ReleaseComObject(item);
                            if (folder != null) Marshal.ReleaseComObject(folder);
                        }
                    }
                }
            }
            finally { Marshal.ReleaseComObject(shell); }
            return results;
        }

        private static object Call(object target, string name, params object[] arguments)
        {
            return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, target, arguments);
        }
    }

    public class TargetOverlay : Window
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        public TargetOverlay()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
            Content = new Border { BorderBrush = Brushes.Red, BorderThickness = new Thickness(3),
                Background = new SolidColorBrush(Color.FromArgb(55, 255, 0, 0)) };
            SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000 | 0x80);
            };
        }
        public void Cover(Rect bounds)
        {
            if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) { Hide(); return; }
            if (!IsVisible) Show();
            // Native coordinates preserve alignment on monitors with different DPI settings.
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), (int)bounds.X, (int)bounds.Y,
                (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 0x10);
        }
    }

    public partial class SourcePicker : Window
    {
        private TextBlock preview;
        private readonly TextBlock clickedApplication = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private bool probing;
        private bool stopped;
        private SourceTarget.ScreenPoint lastPoint;
        private bool hasPoint;
        private readonly TargetOverlay overlay = new TargetOverlay();
        private Rect targetBounds = Rect.Empty;
        private bool selecting;
        private bool swallowedDown;
        private IntPtr mouseHook;
        private MouseCallback callback;
        private SourceTarget.ScreenPoint clickPoint;
        private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, MouseCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        public string Executable { get; private set; }
        public string SelectedApplicationName { get; private set; }

        public SourcePicker()
        {
            Title = "Trỏ chọn Zalo gốc"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            InitializePickerDesign();
            timer.Tick += Probe;
            Loaded += delegate
            {
                callback = CaptureClick;
                mouseHook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                if (mouseHook == IntPtr.Zero)
                { SetHoverStatus("Không bắt được click. Bạn có thể nhấn F8 để chọn.", false, true); }
                timer.Start();
            };
            Closed += delegate
            {
                stopped = true; timer.Stop(); overlay.Close();
                if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            };
        }

        private IntPtr CaptureClick(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int kind = message.ToInt32();
                if (kind == 0x201 && !selecting && !targetBounds.IsEmpty)
                {
                    var point = (SourceTarget.ScreenPoint)Marshal.PtrToStructure(data, typeof(SourceTarget.ScreenPoint));
                    if (targetBounds.Contains(new Point(point.X, point.Y)) && !SourceTarget.IsOwnWindowAt(point))
                    { swallowedDown = true; clickPoint = point; return new IntPtr(1); }
                }
                if (kind == 0x202 && swallowedDown)
                {
                    swallowedDown = false; selecting = true;
                    Dispatcher.BeginInvoke(new Action(async delegate { await SelectPoint(clickPoint); }));
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(mouseHook, code, message, data);
        }

        private async Task SelectPoint(SourceTarget.ScreenPoint point)
        {
            selecting = true; overlay.Hide(); targetBounds = Rect.Empty;
            SetHoverStatus("Đang tìm nguồn Zalo…", false);
            try
            {
                SelectedApplicationName = await Task.Run(() => SourceTarget.ApplicationNameAt(point));
                if (stopped) return;
                clickedApplication.Text = "Đã click · " + SelectedApplicationName;
                string executable = await Task.Run(() => SourceTarget.ResolveAt(point));
                if (stopped) return;
                Executable = executable; DialogResult = true;
            }
            catch (Exception exception)
            {
                if (!stopped) SetHoverStatus("✕ " + exception.Message, false, true);
            }
            finally { selecting = false; hasPoint = false; }
        }

        private async void Probe(object sender, EventArgs args)
        {
            if ((SourceTarget.GetAsyncKeyState(0x1B) & 0x8000) != 0) { DialogResult = false; return; }
            bool confirm = (SourceTarget.GetAsyncKeyState(0x77) & 0x8000) != 0;
            if (probing || selecting) return;
            SourceTarget.ScreenPoint point;
            if (!SourceTarget.GetCursorPos(out point)) return;
            if (confirm) { await SelectPoint(point); return; }
            if (!confirm && hasPoint && Math.Abs(point.X - lastPoint.X) < 3 && Math.Abs(point.Y - lastPoint.Y) < 3) return;
            lastPoint = point; hasPoint = true; probing = true;
            try
            {
                Rect bounds = await Task.Run(() => SourceTarget.BoundsAt(point));
                if (stopped) return;
                if (selecting) return;
                SourceTarget.ScreenPoint current; SourceTarget.GetCursorPos(out current);
                if (current.X != point.X || current.Y != point.Y) { hasPoint = false; return; }
                targetBounds = bounds; overlay.Cover(bounds);
                if (bounds.IsEmpty) SetHoverStatus("Rê chuột ra ngoài bảng chọn, đến ứng dụng Zalo.", false);
                else
                {
                    string executable = null;
                    try { executable = await Task.Run(() => SourceTarget.ResolveAt(point)); }
                    catch (InvalidOperationException) { }
                    if (stopped || selecting) return;
                    SourceTarget.ScreenPoint latest; SourceTarget.GetCursorPos(out latest);
                    if (latest.X != point.X || latest.Y != point.Y) { hasPoint = false; return; }
                    SetHoverStatus(executable == null ? "Vùng đang trỏ đã phủ đỏ. Chọn cửa sổ hoặc nút Zalo trên taskbar." : "Đã nhận diện Zalo · Click trái để chọn nguồn này.", executable != null);
                }
            }
            catch (Exception exception)
            {
                if (stopped) return;
                overlay.Hide(); targetBounds = Rect.Empty;
                Executable = null; SetHoverStatus(exception.Message, false, true);
            }
            finally { probing = false; }
        }
    }
}
