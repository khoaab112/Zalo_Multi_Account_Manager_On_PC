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
        [DataMember(EmitDefaultValue = false)] public bool StartWithWindows { get; set; }
        public bool IsOriginal { get { return Id == "original"; } }
        public string Status { get; set; }
        public bool IsLoggedIn { get; set; }
        public string UpdateStatus { get; set; }
        internal StorageMeasurement MeasuredStorage { get; set; }
        public string StorageUsage { get { return MeasuredStorage == null ? null : MeasuredStorage.Text; } }
        public string StorageDetails { get { return MeasuredStorage == null ? null : MeasuredStorage.Details; } }
        [DataMember(EmitDefaultValue = false)] public bool UsesSandboxie { get; set; }
        internal string LegacyDataPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zm" + Id); } }
        public string DataPath { get { return IsOriginal ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZaloData") :
            UsesSandboxie ? SandboxieClone.DataPath(this) : LegacyDataPath; } }
        public string DirectoryPath { get { return ActiveDirectory ?? Path.Combine(Store.Root, "Instances", Id); } }
        public string Executable { get { return Path.Combine(IsOriginal || UsesSandboxie ? Source : DirectoryPath, "Zalo.exe"); } }
        public string Version { get { return InstalledVersion ?? Path.GetFileName(Source); } }
        public string SubLabel { get { return IsOriginal ? "Bản cài đặt chuẩn" : UsesSandboxie ? "Sandboxie-Plus" : "Hồ sơ phiên bản cũ"; } }
        public string Initials
        {
            get
            {
                if (IsOriginal) return "ZG";
                var words = (Name ?? "Zalo").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return words.Length == 0 ? "Z" : (words[0].Substring(0, 1) + (words.Length > 1 ? words[words.Length - 1].Substring(0, 1) : "")).ToUpperInvariant();
            }
        }
        private string cachedAvatarPath;
        private Brush cachedAvatarBrush;
        public Brush AvatarBackground
        {
            get
            {
                if (!String.IsNullOrEmpty(AvatarPath) && File.Exists(AvatarPath))
                {
                    if (cachedAvatarBrush != null && String.Equals(cachedAvatarPath, AvatarPath, StringComparison.OrdinalIgnoreCase))
                        return cachedAvatarBrush;
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(AvatarPath); bitmap.EndInit(); bitmap.Freeze();
                        var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                        brush.Freeze();
                        cachedAvatarPath = AvatarPath;
                        cachedAvatarBrush = brush;
                        return brush;
                    }
                    catch (IOException) { }
                    catch (NotSupportedException) { }
                }
                cachedAvatarPath = null;
                cachedAvatarBrush = null;
                return new SolidColorBrush(IsOriginal ? Color.FromRgb(0, 104, 255) : Color.FromRgb(14, 165, 233));
            }
        }
        public Visibility InitialsVisibility { get { return !String.IsNullOrEmpty(AvatarPath) && File.Exists(AvatarPath) ? Visibility.Collapsed : Visibility.Visible; } }
        public string StatusText { get { return (Status == "Đang chạy" ? "● " : "○ ") + (Status ?? "Đã dừng"); } }
        public string StatusBackground { get { return Status == "Đang chạy" ? "#ECFDF5" : "#F1F5F9"; } }
        public string StatusForeground { get { return Status == "Đang chạy" ? "#059669" : "#64748B"; } }
        public string LoginStatusText { get { return IsLoggedIn ? "Đã đăng nhập" : "Chưa đăng nhập"; } }
        public string LoginStatusBackground { get { return IsLoggedIn ? "#EFF6FF" : "#FFFBEB"; } }
        public string LoginStatusForeground { get { return IsLoggedIn ? "#1D4ED8" : "#B45309"; } }
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
            VerifySignature(executable);
        }

        internal static void VerifySignature(string executable)
        {
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
        private static readonly string OriginalFilePath = Path.Combine(Root, "original-profile.json");
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
        public static Profile ApplyOriginal(Profile original)
        {
            if (original == null || !File.Exists(OriginalFilePath)) return original;
            try
            {
                using (var stream = File.OpenRead(OriginalFilePath))
                {
                    var saved = (Profile)new DataContractJsonSerializer(typeof(Profile)).ReadObject(stream);
                    if (saved != null)
                    {
                        if (!String.IsNullOrWhiteSpace(saved.Name)) original.Name = saved.Name;
                        if (!String.IsNullOrEmpty(saved.AvatarPath)) original.AvatarPath = saved.AvatarPath;
                        if (!String.IsNullOrEmpty(saved.ShortcutPath)) original.ShortcutPath = saved.ShortcutPath;
                    }
                }
            }
            catch (Exception error) { Log(error); }
            return original;
        }
        public static void SaveOriginal(Profile original)
        {
            if (original == null) return;
            Directory.CreateDirectory(Root);
            string temporary = OriginalFilePath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(Profile)).WriteObject(stream, new Profile
                {
                    Id = "original",
                    Name = original.Name,
                    AvatarPath = original.AvatarPath,
                    ShortcutPath = original.ShortcutPath
                });
                stream.Flush(true);
            }
            if (File.Exists(OriginalFilePath)) File.Replace(temporary, OriginalFilePath, null);
            else File.Move(temporary, OriginalFilePath);
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

        public static void Prepare(Profile profile)
        {
            Original.Validate(profile.Source);
            profile.SourceHash = Hash(Path.Combine(profile.Source, "resources", "app.asar"));
            profile.InstalledVersion = FileVersionInfo.GetVersionInfo(Path.Combine(profile.Source, "Zalo.exe")).FileVersion;
            SandboxieClone.Configure(profile);
            profile.UsesSandboxie = true;
        }

        public static bool IsLoggedIn(Profile profile)
        {
            if (profile == null) return false;
            string dataPath = profile.DataPath;
            if (String.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath)) return false;
            try
            {
                return Directory.EnumerateFiles(dataPath, "ftsm_*.db")
                    .Select(path => Path.GetFileNameWithoutExtension(path).Substring(5))
                    .Any(id => id.Length > 0 && id.All(Char.IsDigit));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static bool IsRunning(Profile profile)
        {
            var processes = Processes(profile);
            try { return processes.Count > 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        public static Profile PrepareUpdate(Profile profile, OriginalInstallation original)
        {
            if (IsRunning(profile)) throw new InvalidOperationException("Hồ sơ đang chạy, chờ thoát Zalo để cập nhật.");
            var next = new Profile
            {
                Id = profile.Id, Name = profile.Name, AvatarPath = profile.AvatarPath, ShortcutPath = profile.ShortcutPath,
                Source = original.DirectoryPath, StartWithWindows = profile.StartWithWindows
            };
            Prepare(next);
            if (next.SourceHash != original.PackageHash || next.InstalledVersion != original.Version)
                throw new IOException("Nguồn Zalo đã thay đổi trong lúc cập nhật.");
            if (!profile.UsesSandboxie && Directory.Exists(profile.DataPath) && !Directory.Exists(next.DataPath))
            {
                string staging = next.DataPath + ".migration-" + Guid.NewGuid().ToString("N");
                try
                {
                    CopyDirectory(profile.DataPath, staging);
                    if (IsRunning(profile)) throw new IOException("Hồ sơ cũ vừa mở lại. Hãy dừng trước khi sao chép dữ liệu.");
                    Directory.Move(staging, next.DataPath);
                }
                catch
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    throw;
                }
            }
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

        private static List<string> ProcessRoots(Profile profile)
        {
            var roots = new List<string> { Path.GetDirectoryName(profile.Executable).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar };
            if (!profile.IsOriginal)
            {
                roots.Add(Path.Combine(Store.Root, "Instances", profile.Id) + Path.DirectorySeparatorChar);
                roots.Add(Path.Combine(Store.Root, "Releases", profile.Id) + Path.DirectorySeparatorChar);
            }
            return roots;
        }

        private static bool OwnsProcess(Profile profile, string path, string box)
        {
            if (profile.UsesSandboxie)
                return String.Equals(box, SandboxieClone.BoxName(profile), StringComparison.OrdinalIgnoreCase);
            return String.IsNullOrEmpty(box) && ProcessRoots(profile).Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        public static List<Process> Processes(Profile profile)
        {
            var found = new List<Process>();
            try
            {
                foreach (int id in ScanProfileProcesses(new[] { profile })[profile])
                {
                    try { found.Add(Process.GetProcessById(id)); }
                    catch (ArgumentException) { }
                }
                return found;
            }
            catch
            {
                foreach (var process in found) process.Dispose();
                throw;
            }
        }

        private static string GetRuntimeProcessPath(Process process)
        {
            if (!RuntimeProcesses.Contains(process.ProcessName)) return null;
            IntPtr handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(32768);
                int size = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }

        internal static Dictionary<Profile, int[]> ScanProfileProcesses(Profile[] profiles)
        {
            var paths = new List<Tuple<int, string, string>>();
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    var path = GetRuntimeProcessPath(process);
                    if (path != null) paths.Add(Tuple.Create(process.Id, path, SandboxieClone.ProcessBox(process.Id)));
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            return profiles.ToDictionary(profile => profile, profile => paths
                .Where(entry => OwnsProcess(profile, entry.Item2, entry.Item3)).Select(entry => entry.Item1).ToArray());
        }

        public static void Open(Profile profile)
        {
            OpenAndCheckWindow(profile);
        }

        internal static bool OpenAndCheckWindow(Profile profile)
        {
            return WithLaunchLock(profile, () => OpenProfile(profile, CancellationToken.None, true), CancellationToken.None);
        }

        private static bool WithLaunchLock(Profile profile, Func<bool> open, CancellationToken token)
        {
            using (var launch = new Mutex(false, "Local\\ZaloManagerProfileLaunch-" + profile.Id))
            {
                int result = 0;
                try
                {
                    if (token.CanBeCanceled)
                        result = WaitHandle.WaitAny(new WaitHandle[] { launch, token.WaitHandle }, TimeSpan.FromSeconds(15));
                    else if (!launch.WaitOne(TimeSpan.FromSeconds(15))) result = WaitHandle.WaitTimeout;
                }
                catch (AbandonedMutexException) { }
                if (result == 1) token.ThrowIfCancellationRequested();
                if (result != 0) throw new IOException("Hồ sơ đang được mở từ một thao tác khác. Hãy thử lại.");
                try
                {
                    token.ThrowIfCancellationRequested();
                    return open();
                }
                finally { launch.ReleaseMutex(); }
            }
        }

        internal static bool StartAtLogon(Profile profile, CancellationToken token)
        {
            return WithLaunchLock(profile, delegate
            {
                if (profile.IsOriginal || IsRunning(profile)) return false;
                OpenProfile(profile, token, false);
                return true;
            }, token);
        }

        private static bool OpenProfile(Profile profile, CancellationToken token, bool activate)
        {
            if (IsRunning(profile) && (!activate || WindowActivation.Restore(profile))) return true;
            token.ThrowIfCancellationRequested();
            if (profile.UsesSandboxie) SandboxieClone.Start(profile);
            else if (profile.IsOriginal) StartNativeOriginal(profile);
            else
            {
                if (String.IsNullOrEmpty(profile.PatchedHash) || Hash(Path.Combine(profile.DirectoryPath, "resources", "app.asar")) != profile.PatchedHash)
                    throw new InvalidDataException("Hãy chọn Kiểm tra & Đồng bộ để đưa hồ sơ sang Sandboxie.");
                using (Process.Start(new ProcessStartInfo(profile.Executable)
                    { WorkingDirectory = profile.DirectoryPath, UseShellExecute = false })) { }
            }
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (token.WaitHandle.WaitOne(250)) token.ThrowIfCancellationRequested();
                if (activate ? WindowActivation.Restore(profile) : IsRunning(profile)) return true;
            }
            return false;
        }

        private static void StartNativeOriginal(Profile profile)
        {
            string launcher = profile.Executable;
            string directory = Path.GetDirectoryName(launcher);
            if (Path.GetFileName(directory).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(directory);
                string installedLauncher = Path.Combine(parent, "Zalo.exe");
                if (File.Exists(installedLauncher)) launcher = installedLauncher;
            }
            Original.VerifySignature(launcher);
            var startInfo = new ProcessStartInfo(launcher)
            {
                WorkingDirectory = Path.GetDirectoryName(launcher),
                UseShellExecute = false
            };
            startInfo.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            using (Process.Start(startInfo)) { }
        }

        public static void Stop(Profile profile)
        {
            if (profile.UsesSandboxie)
            {
                SandboxieClone.Stop(profile);
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (!IsRunning(profile)) return;
                    Thread.Sleep(250);
                }
                throw new IOException("Box Sandboxie vẫn còn tiến trình.");
            }
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
        private bool loadingOnStartup = true;
        public ManagerWindow() : this(false) { }

        internal ManagerWindow(bool windowsLogon)
        {
            deferStartupStorage = windowsLogon;
            profiles = new ObservableCollection<Profile>(Store.Load());
            Title = "Zalo Manager · PoC"; Width = 1440; Height = 720; MinWidth = 1100; MinHeight = 450;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));
            var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "Quản lý hồ sơ Zalo", FontSize = 26, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = "Các hồ sơ dùng chung bản Zalo gốc và có dữ liệu riêng. Đăng nhập trực tiếp trong cửa sổ Zalo.", Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
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
                if (TrayManager.IsExiting) return;
                if (busy) { args.Cancel = true; status.Text = "Đang chuẩn bị phiên bản. Chờ hoàn tất để đóng ứng dụng."; return; }
                args.Cancel = true;
                TrayManager.HideToTray();
                TrayManager.ShowBalloon("Zalo Manager", "Ứng dụng đang chạy ở khay hệ thống.");
            };
            Closed += delegate { CloseStorageMonitoring(); timer.Stop(); };
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
            busy = value || TrayManager.IsExiting;
            toolbar.IsEnabled = !busy;
            sourceToolbar.IsEnabled = !busy;
            grid.IsEnabled = !busy;
            if (managerHeaderActions != null) managerHeaderActions.IsEnabled = !busy;
            if (profileTableLoading != null)
            {
                profileLoadingCaption.Text = TrayManager.IsExiting ? "Đang lưu và thoát…" :
                    loadingOnStartup ? "Đang tải hồ sơ…" : "Đang xử lý…";
                profileTableLoading.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        internal bool CanRequestExit { get { return !busy; } }
        internal void UpdateShutdownState() { SetBusy(false); }

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
                originalRow = Store.ApplyOriginal(new Profile { Id = "original", Name = "Zalo gốc", Source = original.DirectoryPath, InstalledVersion = original.Version, UpdateStatus = "Nguồn nhân bản" });
                int failed = 0, waiting = 0;
                foreach (var profile in profiles.ToList())
                {
                    if (profile.UsesSandboxie && profile.SourceHash == original.PackageHash &&
                        profile.InstalledVersion == original.Version && String.Equals(profile.Source, original.DirectoryPath, StringComparison.OrdinalIgnoreCase))
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
            finally
            {
                if (!updateClones) loadingOnStartup = false;
                SetBusy(false);
                RefreshStatus();
            }
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

        private bool processStatusScanning;
        private bool processStatusRefreshRequested;

        private async void UpdateProfileStatus(bool refreshGrid)
        {
            var displayed = new List<Profile>();
            if (originalRow != null) displayed.Add(originalRow);
            displayed.AddRange(profiles);
            bool changed = !rows.SequenceEqual(displayed);
            if (changed)
            {
                rows.Clear();
                foreach (var row in displayed) rows.Add(row);
                profileStorageRefreshRequested = true;
            }
            if (loadingOnStartup) return;
            if (!deferStartupStorage) RefreshStorageUsage();
            if (processStatusScanning)
            {
                processStatusRefreshRequested |= changed || refreshGrid;
                return;
            }
            var snapshotRows = rows.ToArray();
            processStatusScanning = true;
            Dictionary<Profile, int[]> snapshot;
            Dictionary<Profile, bool> loginStates = null;
            try
            {
                snapshot = await Task.Run(() =>
                {
                    var procs = Instance.ScanProfileProcesses(snapshotRows);
                    loginStates = snapshotRows.ToDictionary(p => p, p =>
                        (p.IsLoggedIn && procs[p].Length == 0 && !refreshGrid) ? p.IsLoggedIn : Instance.IsLoggedIn(p));
                    return procs;
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Store.Log(error);
                snapshot = null;
            }
            try
            {
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    processStatusScanning = false;
                    if (snapshot != null) ApplyProcessStatus(snapshotRows, snapshot, loginStates, changed || refreshGrid);
                })).Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
        }

        private void ApplyProcessStatus(Profile[] snapshotRows, Dictionary<Profile, int[]> snapshot, Dictionary<Profile, bool> loginStates, bool changed)
        {
            if (TrayManager.IsExiting || Dispatcher.HasShutdownStarted) return;
            if (processStatusRefreshRequested || !rows.SequenceEqual(snapshotRows))
            {
                processStatusRefreshRequested = false;
                UpdateProfileStatus(true);
                return;
            }
            foreach (var profile in snapshotRows)
            {
                var running = new List<Process>();
                foreach (int id in snapshot[profile])
                {
                    try { running.Add(Process.GetProcessById(id)); }
                    catch (ArgumentException) { }
                }
                string nextStatus = running.Count > 0 ? "Đang chạy" : (!profile.IsOriginal && !profile.UsesSandboxie && profile.PatchedHash == null ? "Chuẩn bị chưa xong" : "Đã dừng");
                changed |= profile.Status != nextStatus;
                profile.Status = nextStatus;
                bool nextLoggedIn = loginStates != null && loginStates.ContainsKey(profile) && loginStates[profile];
                changed |= profile.IsLoggedIn != nextLoggedIn;
                profile.IsLoggedIn = nextLoggedIn;
                try
                {
                    if (!profile.IsOriginal || !String.IsNullOrEmpty(profile.ShortcutPath) || !String.IsNullOrEmpty(profile.AvatarPath) || profile.Name != "Zalo gốc")
                        ProfileActions.ApplyAvatar(profile, running);
                }
                catch (Exception exception) { Store.Log(exception); }
                finally { foreach (var process in running) process.Dispose(); }
            }
            if (changed) grid.Items.Refresh();
            if (profileCount != null) profileCount.Text = "Tổng cộng " + rows.Count + " hồ sơ";
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
                var profile = new Profile { Id = id, Name = name.Text.Trim() };
                status.Text = "Đang tạo box Sandboxie…";
                await Task.Run(() =>
                {
                    var original = Original.Detect();
                    profile.Source = original.DirectoryPath;
                    Instance.Prepare(profile);
                });
                Store.Save(profiles.Concat(new[] { profile }));
                profiles.Add(profile);
                name.Clear();
                status.Text = "Đã chuẩn bị hồ sơ. Bấm Mở Zalo ở hàng hồ sơ để đăng nhập.";
                RefreshStatus();
                grid.SelectedItem = profile;
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
            if (args.Length == 1 && args[0] == OriginalWindowsStartup.Argument)
            {
                try
                {
                    if (OriginalWindowsStartup.IsEnabled())
                        Instance.Open(ResolveShortcutProfile("original"));
                }
                catch (Exception error)
                {
                    try { Directory.CreateDirectory(Store.Root); Store.Log(error); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    Environment.ExitCode = 1;
                }
                return;
            }
            ShellIdentity.SetManagerIdentity();
            // The file CDN can close the legacy Framework negotiation before returning HTTP.
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            bool profileLaunch = args.Length == 2 && args[0] == "--open-profile";
            bool windowsLogon = args.Length == 1 && args[0] == WindowsStartup.Argument;
            if (windowsLogon && !WindowsStartup.IsRegistered()) return;
            bool owner;
            using (var startupSignal = new EventWaitHandle(false, EventResetMode.AutoReset, WindowsStartup.SignalName))
            using (var mutex = new Mutex(true, "Local\\ZaloManagerPoC", out owner))
            {
                if (!owner && !profileLaunch)
                {
                    if (windowsLogon) startupSignal.Set();
                    else TrayManager.SignalShowManager();
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
                        File.WriteAllText(Path.Combine(Store.Root, "probe-result.txt"), "PREPARED\n" + profile.SourceHash + "\n" + profile.InstalledVersion);
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
                    var window = new ManagerWindow(windowsLogon);
                    if (windowsLogon)
                    {
                        window.ShowActivated = false;
                        window.ShowInTaskbar = false;
                        window.Loaded += delegate { TrayManager.HideToTray(); };
                        startupSignal.Set();
                    }
                    var startupWait = ThreadPool.RegisterWaitForSingleObject(startupSignal,
                        delegate { window.Dispatcher.BeginInvoke(new Action(window.RequestStartupLaunch)); }, null, Timeout.Infinite, false);
                    try { app.Run(window); }
                    finally { startupWait.Unregister(null); }
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
                return Store.ApplyOriginal(new Profile { Id = id, Name = "Zalo gốc", Source = original.DirectoryPath, InstalledVersion = original.Version });
            }
            var profile = Store.Load().SingleOrDefault(item => item.Id == id);
            if (profile == null) throw new InvalidOperationException("Không tìm thấy hồ sơ Zalo của shortcut.");
            return profile;
        }
    }
}
