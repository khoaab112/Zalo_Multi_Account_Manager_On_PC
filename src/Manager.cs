using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZaloManagerPoc
{
    [DataContract]
    public class Profile : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Source { get; set; }
        [DataMember] public string SourceHash { get; set; }
        [DataMember] public string PatchedHash { get; set; }
        [DataMember] public string ActiveDirectory { get; set; }
        [DataMember] public string InstalledVersion { get; set; }
        [DataMember] public string AvatarPath { get; set; }
        [DataMember] public string ShortcutPath { get; set; }
        [DataMember] public int PatchRevision { get; set; }
        public bool IsOriginal { get { return Id == "original"; } }
        public string Status { get; set; }
        public string UpdateStatus { get; set; }
        internal StorageMeasurement MeasuredStorage { get; set; }
        public string StorageUsage { get { return MeasuredStorage == null ? null : MeasuredStorage.Text; } }
        public string StorageDetails { get { return MeasuredStorage == null ? null : MeasuredStorage.Details; } }
        public string CdpStatus { get { return CdpMessageWatcher.GetStatus(this); } }
        public int RecalledCount { get { return CdpMessageWatcher.GetRecalledCount(Id); } }
        public Visibility RecalledBadgeVisibility { get { return RecalledCount > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public string DataPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), IsOriginal ? "ZaloData" : "zm" + Id); } }
        public string DirectoryPath { get { return ActiveDirectory ?? Path.Combine(Store.Root, "Instances", Id); } }
        public string Executable { get { return Path.Combine(IsOriginal ? Source : DirectoryPath, "Zalo.exe"); } }
        public string Version { get { return InstalledVersion ?? Path.GetFileName(Source); } }
        public string SubLabel { get { return IsOriginal ? "Bản cài đặt chuẩn" : "Hồ sơ độc lập"; } }
        public string Initials
        {
            get
            {
                if (IsOriginal) return "ZG";
                var words = (Name ?? "Zalo").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return words.Length == 0 ? "Z" : (words[0].Substring(0, 1) + (words.Length > 1 ? words[words.Length - 1].Substring(0, 1) : "")).ToUpperInvariant();
            }
        }
        public Brush AvatarBackground
        {
            get
            {
                if (!String.IsNullOrEmpty(AvatarPath) && File.Exists(AvatarPath))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(AvatarPath); bitmap.EndInit(); bitmap.Freeze();
                        return new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    }
                    catch (IOException) { }
                    catch (NotSupportedException) { }
                }
                return new SolidColorBrush(IsOriginal ? Color.FromRgb(0, 104, 255) : Color.FromRgb(14, 165, 233));
            }
        }
        public Visibility InitialsVisibility { get { return !String.IsNullOrEmpty(AvatarPath) && File.Exists(AvatarPath) ? Visibility.Collapsed : Visibility.Visible; } }
        public string StatusText
        {
            get
            {
                if (Status != "Đang chạy") return "○ " + (Status ?? "Đã dừng");
                if (!CdpMessageWatcher.IsTracking(this)) return "Chưa theo dõi";
                return CdpMessageWatcher.HasOpenConversation(this) ? "● Đang theo dõi" : "Chờ mở chat";
            }
        }
        public string StatusBackground { get { return Status == "Đang chạy" ? (CdpMessageWatcher.HasOpenConversation(this) ? "#ECFDF5" : "#FFFBEB") : "#F1F5F9"; } }
        public string StatusForeground { get { return Status == "Đang chạy" ? (CdpMessageWatcher.HasOpenConversation(this) ? "#059669" : "#92400E") : "#64748B"; } }
        public string UpdateBackground { get { return IsOriginal ? "#EFF6FF" : (UpdateStatus ?? "").Contains("lỗi") ? "#FEF2F2" : (UpdateStatus ?? "").Contains("Đã đồng bộ") ? "#ECFDF5" : "#FFFBEB"; } }
        public string UpdateForeground { get { return IsOriginal ? "#1D4ED8" : (UpdateStatus ?? "").Contains("lỗi") ? "#DC2626" : (UpdateStatus ?? "").Contains("Đã đồng bộ") ? "#059669" : "#B45309"; } }
    }

    public class OriginalInstallation
    {
        public string DirectoryPath { get; set; }
        public string Version { get; set; }
        public string Fingerprint { get; set; }
        public string PackageHash { get; set; }
    }

    public static class Original
    {
        private static OriginalInstallation cached;
        public static string PreferredRoot;
        private static readonly string SettingPath = Path.Combine(Store.Root, "original-path.txt");
        public static void Invalidate() { cached = null; }

        public static void Select(string executable)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            Validate(directory);
            string root = Path.GetDirectoryName(directory);
            if (!Path.GetFileName(directory).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase)) root = directory;
            File.WriteAllText(SettingPath, root);
            PreferredRoot = root;
            cached = null;
        }

        public static OriginalInstallation Detect()
        {
            if (PreferredRoot == null && File.Exists(SettingPath)) PreferredRoot = File.ReadAllText(SettingPath).Trim();
            var roots = new List<string>();
            if (!String.IsNullOrEmpty(PreferredRoot)) roots.Add(PreferredRoot);
            else
            {
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Zalo"));
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zalo"));
                using (var uninstall = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall != null)
                        foreach (string name in uninstall.GetSubKeyNames())
                            using (var key = uninstall.OpenSubKey(name))
                            {
                                if (key == null || !String.Equals(key.GetValue("DisplayName", "").ToString().Split(' ')[0], "Zalo", StringComparison.OrdinalIgnoreCase)) continue;
                                string location = key.GetValue("InstallLocation", "").ToString();
                                if (!String.IsNullOrWhiteSpace(location)) roots.Add(location);
                            }
                }
            }
            var candidates = new List<string>();
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                candidates.Add(root);
                candidates.AddRange(Directory.GetDirectories(root, "Zalo-*"));
            }
            var ordered = candidates.Where(path => File.Exists(Path.Combine(path, "Zalo.exe")) && File.Exists(Path.Combine(path, "resources", "app.asar")))
                .OrderByDescending(path => new Version(FileVersionInfo.GetVersionInfo(Path.Combine(path, "Zalo.exe")).FileVersion)).ToList();
            if (ordered.Count == 0) throw new FileNotFoundException("Chưa tìm thấy Zalo gốc. Chọn Zalo.exe trong thư mục cài đặt.");
            string source = ordered[0];
            string fingerprint = source + File.GetLastWriteTimeUtc(Path.Combine(source, "Zalo.exe")).Ticks + ":" + File.GetLastWriteTimeUtc(Path.Combine(source, "resources", "app.asar")).Ticks;
            if (cached != null && cached.Fingerprint == fingerprint) return cached;
            Validate(source);
            cached = new OriginalInstallation { DirectoryPath = source, Version = FileVersionInfo.GetVersionInfo(Path.Combine(source, "Zalo.exe")).FileVersion, Fingerprint = fingerprint,
                PackageHash = Instance.Hash(Path.Combine(source, "resources", "app.asar")) };
            return cached;
        }

        public static void Validate(string directory)
        {
            string fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            if (fullPath.StartsWith(Store.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloSpace") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hãy chọn Zalo trong thư mục cài đặt gốc.");
            string executable = Path.Combine(fullPath, "Zalo.exe");
            if (!File.Exists(Path.Combine(fullPath, "resources", "app.asar"))) throw new FileNotFoundException("Thiếu resources/app.asar tại nguồn gốc.");
            string script = "Import-Module ($PSHOME + '\\Modules\\Microsoft.PowerShell.Security') -ErrorAction Stop; $s=Get-AuthenticodeSignature -LiteralPath '" + executable.Replace("'", "''") + "'; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match '(?i)(CN|O)=VNG'){exit 0}; exit 1";
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
                { UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(info))
            {
                if (!process.WaitForExit(30000)) { process.Kill(); throw new IOException("Quá thời gian kiểm tra chữ ký Zalo gốc."); }
                if (process.ExitCode != 0) throw new InvalidDataException("Không xác minh được chữ ký số VNG của Zalo gốc.");
            }
        }
    }

    public static class Store
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZaloManagerPoC");
        private static readonly string FilePath = Path.Combine(Root, "profiles.json");
        public static List<Profile> Load()
        {
            Directory.CreateDirectory(Root);
            if (!File.Exists(FilePath)) return new List<Profile>();
            try { return ReadProfiles(FilePath); }
            catch (Exception exception)
            {
                if (!(exception is SerializationException) && !(exception is InvalidDataException)) throw;
                string backup = FilePath + ".bak";
                if (!File.Exists(backup)) throw;
                var recovered = ReadProfiles(backup);
                File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                Save(recovered);
                Log(new InvalidDataException("Đã phục hồi danh sách hồ sơ từ bản cấu hình dự phòng.", exception));
                return recovered;
            }
        }
        private static List<Profile> ReadProfiles(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var profiles = (List<Profile>)new DataContractJsonSerializer(typeof(List<Profile>)).ReadObject(stream);
                if (profiles == null || profiles.Any(profile => profile == null)) throw new InvalidDataException("Danh sách hồ sơ không hợp lệ.");
                var ids = new HashSet<string>();
                foreach (var profile in profiles)
                {
                    int id;
                    if (!Int32.TryParse(profile.Id, out id) || id < 100000 || id > 999999 || !ids.Add(profile.Id))
                        throw new InvalidDataException("Mã hồ sơ không hợp lệ hoặc trùng lặp.");
                }
                return profiles;
            }
        }
        public static void Save(IEnumerable<Profile> profiles)
        {
            string temporary = FilePath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(List<Profile>)).WriteObject(stream, profiles.ToList());
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
        }
        public static void Log(Exception exception)
        {
            File.AppendAllText(Path.Combine(Root, "manager.log"), DateTimeOffset.Now.ToString("o") + " " + exception + Environment.NewLine);
        }
    }

    public static class Instance
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private static readonly HashSet<string> RuntimeProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Zalo", "ZaloCall", "ZaloCap", "ZaloExecutable", "ZaviMeet", "zocr-host", "sl", "Update"
        };
        public static string DetectSource()
        {
            return Original.Detect().DirectoryPath;
        }

        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        // Equal-length substitutions preserve ASAR file offsets. Only payload bytes are changed.
        public static byte[] Patch(byte[] source, string id)
        {
            if (source.Length < 16 || BitConverter.ToUInt32(source, 0) != 4)
                throw new InvalidDataException("Định dạng ASAR chưa được hỗ trợ.");
            int payload = checked(8 + (int)BitConverter.ToUInt32(source, 4));
            int headerLength = checked((int)BitConverter.ToUInt32(source, 12));
            if (payload > source.Length || payload < 16 || headerLength <= 0 || headerLength > payload - 16)
                throw new InvalidDataException("Header ASAR không hợp lệ.");
            string header = Encoding.UTF8.GetString(source, 16, headerLength);
            if (header.Contains("\"integrity\""))
                throw new InvalidDataException("Phiên bản ASAR có kiểm tra integrity. Cần đánh giá tương thích riêng.");
            byte[] result = (byte[])source.Clone();
            Replace(result, payload, "ZaloData", "zm" + id, true);
            Replace(result, payload, "com.vng.zalo", "zmgr.p" + id, true);
            Replace(result, payload, "PipeZCallSend", "ZMC_S_" + id + "_", true);
            Replace(result, payload, "PipeZCallRecv", "ZMC_R_" + id + "_", true);
            string script = Encoding.ASCII.GetString(result, payload, result.Length - payload);
            var exits = System.Text.RegularExpressions.Regex.Matches(script,
                @"\[""restart process"",""[^""]+""\]\),0===([a-zA-Z_$][a-zA-Z0-9_$]*)\?([a-zA-Z_$][a-zA-Z0-9_$]*\(\)):");
            foreach (System.Text.RegularExpressions.Match match in exits)
            {
                var call = match.Groups[2];
                string replacement = "0" + new string(' ', call.Length - 1);
                Buffer.BlockCopy(Encoding.ASCII.GetBytes(replacement), 0, result, payload + call.Index, call.Length);
            }
            return result;
        }

        private static void Replace(byte[] bytes, int start, string oldValue, string newValue, bool required)
        {
            byte[] before = Encoding.ASCII.GetBytes(oldValue);
            byte[] after = Encoding.ASCII.GetBytes(newValue);
            if (before.Length != after.Length) throw new InvalidOperationException("Độ dài chuỗi thay thế khác nhau.");
            int count = 0;
            for (int i = start; i <= bytes.Length - before.Length; i++)
            {
                if (bytes[i] != before[0]) continue;
                int j = 1;
                while (j < before.Length && bytes[i + j] == before[j]) j++;
                if (j != before.Length) continue;
                Buffer.BlockCopy(after, 0, bytes, i, after.Length);
                count++;
                i += before.Length - 1;
            }
            if (required && count == 0) throw new InvalidDataException("Không tìm thấy dấu hiệu tương thích: " + oldValue);
        }

        public static void Prepare(Profile profile)
        {
            string version = FileVersionInfo.GetVersionInfo(Path.Combine(profile.Source, "Zalo.exe")).FileVersion;
            if (IsRunning(profile)) throw new InvalidOperationException("Hồ sơ đang chạy.");
            if (Directory.Exists(profile.DirectoryPath))
                throw new IOException("Thư mục hồ sơ đã tồn tại. Không thể ghi đè: " + profile.DirectoryPath);
            string sourceAsar = Path.Combine(profile.Source, "resources", "app.asar");
            string sourceHash = Hash(sourceAsar);
            byte[] patched = Patch(File.ReadAllBytes(sourceAsar), profile.Id);
            string staging = profile.DirectoryPath + ".preparing";
            if (Directory.Exists(staging)) throw new IOException("Có lần chuẩn bị chưa hoàn tất: " + staging);
            Directory.CreateDirectory(staging);
            CopyDirectory(profile.Source, staging);
            string cloneAsar = Path.Combine(staging, "resources", "app.asar");
            if (Hash(cloneAsar) != sourceHash || Hash(sourceAsar) != sourceHash)
                throw new IOException("Zalo vừa cập nhật trong lúc sao chép. Hãy thử lại với hồ sơ mới.");
            File.WriteAllBytes(cloneAsar, patched);
            profile.SourceHash = sourceHash;
            profile.PatchedHash = Hash(cloneAsar);
            profile.InstalledVersion = version;
            profile.PatchRevision = 4;
            Directory.Move(staging, profile.DirectoryPath);
        }

        public static bool IsRunning(Profile profile)
        {
            var processes = Processes(profile);
            try { return processes.Count > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        // Publish new binaries by changing metadata only after a complete, verified copy.
        // The previous generation remains usable if preparing or saving the update fails.
        public static Profile PrepareUpdate(Profile profile, OriginalInstallation original)
        {
            if (IsRunning(profile)) throw new InvalidOperationException("Hồ sơ đang chạy, chờ thoát Zalo để cập nhật.");
            var next = new Profile { Id = profile.Id, Name = profile.Name, AvatarPath = profile.AvatarPath, ShortcutPath = profile.ShortcutPath, Source = original.DirectoryPath,
                ActiveDirectory = Path.Combine(Store.Root, "Releases", profile.Id, Guid.NewGuid().ToString("N")) };
            Prepare(next);
            if (next.SourceHash != original.PackageHash || next.InstalledVersion != original.Version)
                throw new IOException("Nguồn Zalo đã thay đổi trong lúc cập nhật. Chờ lần kiểm tra tiếp theo.");
            return next;
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string entry in Directory.GetFileSystemEntries(source))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Không sao chép liên kết thư mục: " + entry);
                string target = Path.Combine(destination, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) CopyDirectory(entry, target);
                else
                {
                    using (var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
        }

        public static List<Process> Processes(Profile profile)
        {
            var found = new List<Process>();
            var roots = new List<string> { Path.GetDirectoryName(profile.Executable).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar };
            if (!profile.IsOriginal)
            {
                roots.Add(Path.Combine(Store.Root, "Instances", profile.Id) + Path.DirectorySeparatorChar);
                roots.Add(Path.Combine(Store.Root, "Releases", profile.Id) + Path.DirectorySeparatorChar);
            }
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!RuntimeProcesses.Contains(process.ProcessName)) { process.Dispose(); continue; }
                    IntPtr handle = OpenProcess(0x1000, false, process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            var path = new StringBuilder(32768); int size = path.Capacity;
                            if (QueryFullProcessImageName(handle, 0, path, ref size) && roots.Any(root => path.ToString().StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                            { found.Add(process); continue; }
                        }
                        finally { CloseHandle(handle); }
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            return found;
        }

        public static void Open(Profile profile)
        {
            OpenAndCheckWindow(profile);
        }

        internal static bool OpenAndCheckWindow(Profile profile)
        {
            using (var launch = new Mutex(false, "Local\\ZaloManagerProfileLaunch-" + profile.Id))
            {
                try { launch.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { }
                try { return OpenProfile(profile); }
                finally { try { launch.ReleaseMutex(); } catch (ApplicationException) { } }
            }
        }

        private static bool OpenProfile(Profile profile)
        {
            bool running = IsRunning(profile);
            int runningPort = running ? CdpMessageWatcher.FindPort(profile) : 0;
            if (running && runningPort > 0 && WindowActivation.Restore(profile))
            {
                CdpMessageWatcher.Start(profile, runningPort);
                return true;
            }
            if (running && runningPort == 0)
            {
                Stop(profile);
                running = false;
            }
            if (!profile.IsOriginal && (String.IsNullOrEmpty(profile.PatchedHash) || Hash(Path.Combine(profile.DirectoryPath, "resources", "app.asar")) != profile.PatchedHash))
                throw new InvalidDataException("Gói ứng dụng đã thay đổi hoặc chuẩn bị chưa hoàn tất. Cần kiểm tra tương thích trước khi mở.");
            // Use Zalo's own single-instance handler for a tray-only session.
            // A crashed Electron main process can leave helpers behind. Pass a fresh
            // port even when those helpers made the profile appear to be running.
            int port = CdpMessageWatcher.AllocatePort();
            var startInfo = new ProcessStartInfo(profile.Executable)
            {
                WorkingDirectory = Path.GetDirectoryName(profile.Executable),
                UseShellExecute = false,
                Arguments = "--remote-debugging-address=127.0.0.1 --remote-debugging-port=" + port
            };
            startInfo.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            using (Process.Start(startInfo)) { }
            CdpMessageWatcher.Start(profile, running ? 0 : port);
            for (int attempt = 0; attempt < 40; attempt++)
            { System.Threading.Thread.Sleep(250); if (WindowActivation.Restore(profile)) return true; }
            return false;
        }

        public static void Close(Profile profile)
        {
            var running = Processes(profile);
            try { foreach (var process in running) process.CloseMainWindow(); }
            finally { foreach (var process in running) process.Dispose(); }
        }

        public static void Stop(Profile profile)
        {
            CdpMessageWatcher.Stop(profile);
            Exception failure = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var running = Processes(profile);
                if (running.Count == 0) return;
                try
                {
                    foreach (var process in running)
                    {
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception exception) { failure = exception; }
                    }
                }
                finally { foreach (var process in running) process.Dispose(); }
                System.Threading.Thread.Sleep(250);
            }
            if (IsRunning(profile)) throw new IOException("Còn tiến trình hồ sơ chưa dừng. " + (failure == null ? "" : failure.Message));
        }
    }

    public partial class ManagerWindow : Window
    {
        private readonly ObservableCollection<Profile> profiles;
        private readonly ObservableCollection<Profile> rows = new ObservableCollection<Profile>();
        private Profile originalRow;
        private readonly DataGrid grid = new DataGrid();
        private readonly TextBox name = new TextBox { Width = 210, Text = "Tài khoản mới", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        private readonly StackPanel toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 16) };
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private readonly TextBlock originalText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        private readonly StackPanel sourceToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        private readonly ProgressBar sourceProgress = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        private readonly TextBlock sourceResult = new TextBlock { Text = "Chưa kiểm tra", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock resultIcon = new TextBlock { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 20, Width = 28, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button detectButton = new Button { Content = "Kiểm tra Zalo gốc / đồng bộ", Padding = new Thickness(12, 7, 12, 7) };
        private bool busy;
        private readonly Dictionary<Profile, string> displayedCdpStatus = new Dictionary<Profile, string>();
        public ManagerWindow()
        {
            profiles = new ObservableCollection<Profile>(Store.Load());
            Title = "Zalo Manager · PoC"; Width = 1440; Height = 720; MinWidth = 1100; MinHeight = 450;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
            var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "Quản lý hồ sơ Zalo", FontSize = 26, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = "Mỗi hồ sơ có bản sao chương trình và thư mục dữ liệu riêng. Đăng nhập trực tiếp trong cửa sổ Zalo.", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            header.Children.Add(originalText);
            detectButton.Click += async delegate { await Synchronize(true); };
            sourceToolbar.Children.Add(detectButton);
            var selectButton = new Button { Content = "Trỏ chọn Zalo gốc…", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
            selectButton.Click += async delegate
            {
                SetBusy(true);
                bool selected = false;
                string selectedApplication = null;
                try
                {
                    var picker = new SourcePicker { Owner = this };
                    SetSourceResult("⌖", "Rê chuột lên Zalo rồi click vào vùng phủ đỏ để chọn.", Brushes.SteelBlue, false);
                    if (picker.ShowDialog() != true)
                    { SetSourceResult("•", "Đã hủy chọn nguồn.", Brushes.DimGray, false); return; }
                    selectedApplication = picker.SelectedApplicationName;
                    SetSourceResult("↻", "Đã chọn " + selectedApplication + " · Đang xác minh nguồn…", Brushes.SteelBlue, true);
                    await Task.Run(() => Original.Select(picker.Executable));
                    selected = true;
                }
                catch (Exception exception)
                { SetSourceResult("✕", (selectedApplication == null ? "Chọn nguồn thất bại" : "Đã chọn " + selectedApplication) + " · " + exception.Message, Brushes.Firebrick, false); Store.Log(exception); }
                finally { SetBusy(false); }
                if (selected)
                {
                    await Synchronize();
                    sourceResult.Text = "Đã chọn " + selectedApplication + " · " + sourceResult.Text;
                }
            };
            sourceToolbar.Children.Add(selectButton); header.Children.Add(sourceToolbar);
            var sourceFeedback = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            sourceResult.MaxWidth = 950;
            sourceFeedback.Children.Add(resultIcon); sourceFeedback.Children.Add(sourceResult);
            header.Children.Add(sourceFeedback); header.Children.Add(sourceProgress);
            toolbar.Children.Add(name);
            AddButton("Thêm hồ sơ", AddProfile);
            header.Children.Add(toolbar);
            DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
            grid.AutoGenerateColumns = false; grid.IsReadOnly = true; grid.CanUserAddRows = false;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.SelectionMode = DataGridSelectionMode.Single; grid.RowHeight = 60; grid.ItemsSource = rows;
            grid.CanUserSortColumns = false; grid.CanUserReorderColumns = false;
            var imageFactory = new FrameworkElementFactory(typeof(Image));
            imageFactory.SetValue(Image.WidthProperty, 36.0); imageFactory.SetValue(Image.HeightProperty, 36.0);
            imageFactory.SetBinding(Image.SourceProperty, new Binding("AvatarPath"));
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Avatar", Width = 60, CellTemplate = new DataTemplate { VisualTree = imageFactory } });
            AddColumn("Tên hồ sơ", "Name", 160); AddColumn("Trạng thái", "Status", 115);
            AddColumn("Phiên bản", "Version", 120);
            AddColumn("Cập nhật", "UpdateStatus", 170);
            AddActionsColumn();
            grid.Columns[grid.Columns.Count - 1].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            grid.Columns[grid.Columns.Count - 1].MinWidth = 570;
            panel.Children.Add(grid);
            status.Text = "PoC · Thoát ứng dụng quản lý để các cửa sổ Zalo tiếp tục chạy. Nút đóng gửi yêu cầu đóng cửa sổ, Zalo có thể thu nhỏ xuống khay hệ thống.";
            timer.Tick += delegate { if (!busy) UpdateProfileStatus(false); };
            timer.Start(); RefreshStatus();
            Loaded += async delegate { await Synchronize(false, false); };
            Loaded += delegate { TrayManager.Initialize(this); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (busy) { args.Cancel = true; status.Text = "Đang chuẩn bị phiên bản. Chờ hoàn tất để đóng ứng dụng."; return; }
                args.Cancel = true;
                TrayManager.HideToTray();
                TrayManager.ShowBalloon("Zalo Manager", "Ứng dụng đang chạy ngầm ở khay hệ thống để tiếp tục theo dõi tin nhắn thu hồi.");
            };
            Action refreshRecallBadges = delegate
            {
                Dispatcher.BeginInvoke(new Action(() => grid.Items.Refresh()));
            };
            CdpMessageWatcher.RecalledMessagesChanged += refreshRecallBadges;
            Closed += delegate
            {
                CdpMessageWatcher.RecalledMessagesChanged -= refreshRecallBadges;
                timer.Stop();
                CdpMessageWatcher.StopAll();
            };
            ApplyModernLayout();
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var source = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            if (source != null) source.AddHook(WndProc);
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == TrayManager.WM_SHOW_MANAGER)
            {
                TrayManager.RestoreMainWindow();
                handled = true;
            }
            return IntPtr.Zero;
        }
        private void SetBusy(bool value)
        {
            busy = value; toolbar.IsEnabled = !value; sourceToolbar.IsEnabled = !value; grid.IsEnabled = !value;
        }

        private void SetSourceResult(string icon, string message, Brush color, bool loading)
        {
            resultIcon.Text = icon; resultIcon.Foreground = color;
            sourceResult.Text = message; sourceResult.Foreground = color;
            sourceProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            detectButton.Content = loading ? "Đang kiểm tra…" : "↻ Kiểm tra & Đồng bộ";
            if (sourceBanner != null)
            {
                string background = loading ? "#EFF6FF" : icon == "✓" ? "#F0FDF4" : icon == "✕" ? "#FEF2F2" : "#F8FAFC";
                sourceBanner.Background = ModernBrush(background);
                sourceBanner.BorderBrush = ModernBrush(loading ? "#BFDBFE" : icon == "✓" ? "#BBF7D0" : icon == "✕" ? "#FECACA" : "#E2E8F0");
            }
        }

        private async Task Synchronize(bool force = false, bool updateClones = true)
        {
            if (busy) return;
            SetBusy(true);
            SetSourceResult("↻", "Đang kiểm tra Zalo gốc và trạng thái các bản clone…", Brushes.SteelBlue, true);
            var visibleLoading = Task.Delay(500);
            try
            {
                if (force) Original.Invalidate();
                var original = await Task.Run(() => Original.Detect());
                ShowOriginal(original);
                originalRow = new Profile { Id = "original", Name = "Zalo gốc", Source = original.DirectoryPath, InstalledVersion = original.Version, UpdateStatus = "Nguồn nhân bản" };
                int failed = 0, waiting = 0;
                foreach (var profile in profiles.ToList())
                {
                    if (profile.SourceHash == original.PackageHash &&
                        (profile.InstalledVersion ?? original.Version) == original.Version && profile.PatchRevision >= 4)
                    { profile.UpdateStatus = "Đã đồng bộ"; continue; }
                    if (!updateClones) { profile.UpdateStatus = "Có bản cập nhật · bấm Kiểm tra để đồng bộ"; continue; }
                    if (Instance.IsRunning(profile))
                    { profile.UpdateStatus = "Chờ thoát Zalo để cập nhật"; waiting++; continue; }
                    profile.UpdateStatus = "Đang chuẩn bị " + original.Version; grid.Items.Refresh();
                    try
                    {
                        var next = await Task.Run(() => Instance.PrepareUpdate(profile, original));
                        // Another launcher may have opened the old generation during the copy.
                        if (Instance.IsRunning(profile))
                            throw new IOException("Hồ sơ vừa mở lại. Lần đồng bộ sau sẽ thử lại khi Zalo đã thoát.");
                        var updated = profiles.Select(item => item == profile ? next : item).ToList();
                        Store.Save(updated);
                        next.UpdateStatus = "Đã đồng bộ";
                        int index = profiles.IndexOf(profile); profiles[index] = next;
                        if (!String.IsNullOrEmpty(next.ShortcutPath))
                        {
                            try { ProfileActions.CreateShortcut(next); }
                            catch (Exception exception) { failed++; Store.Log(exception); next.UpdateStatus = "Đã cập nhật · lỗi shortcut"; }
                        }
                    }
                    catch (Exception exception)
                    { failed++; Store.Log(exception); profile.UpdateStatus = "Cập nhật lỗi · " + exception.Message; }
                }
                await visibleLoading;
                if (failed > 0) SetSourceResult("✕", "Đã nhận diện Zalo gốc · " + failed + " hồ sơ cập nhật thất bại. Xem cột Cập nhật.", Brushes.Firebrick, false);
                else if (waiting > 0) SetSourceResult("✓", "Kiểm tra thành công · " + waiting + " hồ sơ chờ thoát Zalo để đồng bộ.", Brushes.DarkGreen, false);
                else SetSourceResult("✓", "Kiểm tra thành công · Zalo gốc " + original.Version + " · Các hồ sơ đã đồng bộ.", Brushes.DarkGreen, false);
                status.Text = "Cập nhật thủ công bằng nút Kiểm tra Zalo gốc / đồng bộ. Hồ sơ đang chạy cần thoát Zalo rồi kiểm tra lại.";
            }
            catch (Exception exception)
            {
                SetSourceResult("✕", "Kiểm tra thất bại · " + exception.Message, Brushes.Firebrick, false);
                originalText.Text = "Chưa xác minh được Zalo gốc · " + exception.Message;
                baseSignature.Text = "Chưa xác minh"; baseSignature.Foreground = ModernBrush("#B45309");
                Store.Log(exception);
            }
            finally { SetBusy(false); RefreshStatus(); }
        }
        private void AddColumn(string title, string property, double width)
        { grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width }); }
        private void AddButton(string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += action; toolbar.Children.Add(button);
        }
        private void RefreshStatus()
        {
            UpdateProfileStatus(true);
        }

        private void UpdateProfileStatus(bool refreshGrid)
        {
            var displayed = new List<Profile>();
            if (originalRow != null) displayed.Add(originalRow);
            displayed.AddRange(profiles);
            bool changed = !rows.SequenceEqual(displayed);
            if (changed) { rows.Clear(); foreach (var row in displayed) rows.Add(row); displayedCdpStatus.Clear(); }
            foreach (var profile in rows)
            {
                var running = Instance.Processes(profile);
                string nextStatus = running.Count > 0 ? "Đang chạy" : (!profile.IsOriginal && profile.PatchedHash == null ? "Chuẩn bị chưa xong" : "Đã dừng");
                changed |= profile.Status != nextStatus;
                profile.Status = nextStatus;
                if (running.Count > 0) CdpMessageWatcher.Start(profile, 0);
                else CdpMessageWatcher.Stop(profile);
                try
                {
                    if (!profile.IsOriginal && !String.IsNullOrEmpty(profile.ShortcutPath)) ProfileActions.ApplyAvatar(profile, running);
                }
                catch (Exception exception) { Store.Log(exception); }
                finally { foreach (var process in running) process.Dispose(); }
                string statusKey = profile.StatusText + "\n" + profile.CdpStatus;
                string previousStatus;
                changed |= !displayedCdpStatus.TryGetValue(profile, out previousStatus) || previousStatus != statusKey;
                displayedCdpStatus[profile] = statusKey;
            }
            if (changed || refreshGrid) grid.Items.Refresh();
            if (profileCount != null) profileCount.Text = "Tổng cộng " + rows.Count + " hồ sơ";
            RefreshStorageUsage();
        }
        private async void AddProfile(object sender, RoutedEventArgs args)
        {
            if (String.IsNullOrWhiteSpace(name.Text))
            {
                profileNameError.Visibility = Visibility.Visible;
                System.Windows.Automation.AutomationProperties.SetHelpText(name, ProfileNameRequired);
                name.Focus();
                return;
            }
            SetBusy(true); timer.Stop();
            try
            {
                string id;
                var random = new Random();
                do { id = random.Next(100000, 1000000).ToString(); }
                while (profiles.Any(p => p.Id == id) || Directory.Exists(Path.Combine(Store.Root, "Instances", id)) || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zm" + id)));
                var profile = new Profile { Id = id, Name = name.Text.Trim(), Source = Instance.DetectSource() };
                profiles.Add(profile); Store.Save(profiles);
                status.Text = "Đang sao chép và chuẩn bị hồ sơ. Có thể mất vài phút…";
                await Task.Run(() => Instance.Prepare(profile));
                Store.Save(profiles); grid.SelectedItem = profile;
                status.Text = "Đã chuẩn bị hồ sơ. Bấm Mở Zalo ở hàng hồ sơ để đăng nhập.";
                RefreshStatus();
                NoticeDialog.Success(this, "Tạo hồ sơ thành công", "Hồ sơ đã sẵn sàng. Mở Zalo để đăng nhập.", profile.Name);
            }
            catch (Exception exception) { Report(exception); }
            finally { SetBusy(false); RefreshStatus(); timer.Start(); }
        }
        private void Report(Exception exception)
        {
            Store.Log(exception); status.Text = exception.Message;
            NoticeDialog.Show(this, "Không thể hoàn tất", exception.Message);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(Store.Root);
                    File.AppendAllText(Path.Combine(Store.Root, "crash.log"),
                        DateTimeOffset.Now.ToString("o") + " [AppDomain] " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null") + "\r\n");
                }
                catch { }
            };
            try { Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null); } catch { }
            ShellIdentity.SetManagerIdentity();
            // The file CDN can close the legacy Framework negotiation before returning HTTP.
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            CdpMessageWatcher.IncomingMessageCache.Initialize();
            bool profileLaunch = args.Length == 2 && args[0] == "--open-profile";
            bool owner;
            using (var mutex = new Mutex(true, "Local\\ZaloManagerPoC", out owner))
            {
                if (!owner && !profileLaunch)
                {
                    TrayManager.SignalShowManager();
                    return;
                }
                try
                {
                    if (profileLaunch)
                    {
                        Instance.Open(ResolveShortcutProfile(args[1]));
                        if (!owner) return;
                    }
                    if (args.Length > 0 && args[0] == "--probe")
                    {
                        if (Store.Load().Count != 0) throw new IOException("Chỉ chạy probe khi chưa có hồ sơ PoC.");
                        var profile = new Profile { Id = "900001", Name = "Kiểm thử PoC", Source = Instance.DetectSource() };
                        // This mode prepares a dedicated empty profile for smoke testing, without logging in.
                        if (Directory.Exists(profile.DataPath) || Directory.Exists(profile.DirectoryPath))
                            throw new IOException("Hồ sơ kiểm thử đã tồn tại.");
                        Instance.Prepare(profile);
                        Store.Save(new List<Profile> { profile });
                        File.WriteAllText(Path.Combine(Store.Root, "probe-result.txt"), "PREPARED\n" + profile.SourceHash + "\n" + profile.PatchedHash);
                        return;
                    }
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += (s, e) =>
                    {
                        try
                        {
                            Directory.CreateDirectory(Store.Root);
                            File.AppendAllText(Path.Combine(Store.Root, "crash.log"),
                                DateTimeOffset.Now.ToString("o") + " [Dispatcher] " + (e.Exception != null ? e.Exception.ToString() : "null") + "\r\n");
                        }
                        catch { }
                    };
                    app.Run(new ManagerWindow());
                }
                catch (Exception exception)
                {
                    Directory.CreateDirectory(Store.Root); Store.Log(exception);
                    if (args.Length > 0 && !profileLaunch) Environment.ExitCode = 1;
                    else NoticeDialog.Show(null, "Zalo Manager", exception.Message);
                }
            }
        }

        private static Profile ResolveShortcutProfile(string id)
        {
            if (id == "original")
            {
                var original = Original.Detect();
                return new Profile { Id = id, Name = "Zalo gốc", Source = original.DirectoryPath, InstalledVersion = original.Version };
            }
            var profile = Store.Load().SingleOrDefault(item => item.Id == id);
            if (profile == null) throw new InvalidOperationException("Không tìm thấy hồ sơ Zalo của shortcut.");
            return profile;
        }
    }
}
