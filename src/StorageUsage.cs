using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ZaloManagerPoc
{
    public sealed class StorageMeasurement
    {
        public long DataBytes, AppBytes;
        public bool Incomplete;
        public long TotalBytes { get { return DataBytes + AppBytes; } }
        public string Text { get { return (Incomplete ? "≥ " : "") + Format(TotalBytes); } }
        public string Details { get { return "Data và bản khôi phục: " + Format(DataBytes) + "\nApp và các phiên bản được giữ lại: " + Format(AppBytes) +
            "\nTính dung lượng tệp trên đĩa; bỏ qua liên kết. Không gồm file xuất, tệp tải ngoài thư mục hoặc dữ liệu trên cloud." +
            (Incomplete ? "\nMột số tệp/thư mục không đọc được, số liệu chưa đầy đủ." : "") +
            "\nDung lượng được đo khoảng 2 phút/lần và ngay sau khi nhập hoặc xóa hồ sơ."; } }
        public static string Format(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = bytes; int index = 0;
            while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
            return value.ToString(index == 0 ? "0" : "0.##") + " " + units[index];
        }
    }

    public static class StorageUsage
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSizeW(string path, out uint high);
        public static StorageMeasurement Measure(Profile profile)
        {
            var result = new StorageMeasurement();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MeasureData(profile, result, seen);
            MeasureApp(profile, result, seen);
            return result;
        }

        private static void MeasureData(Profile profile, StorageMeasurement result, HashSet<string> seen)
        {
            result.DataBytes = Count(profile.DataPath, result, seen);
            if (!profile.IsOriginal)
            {
                try
                {
                    string parent = Path.GetDirectoryName(profile.DataPath), prefix = Path.GetFileName(profile.DataPath);
                    foreach (var path in Directory.GetDirectories(parent, prefix + ".*"))
                        if (Path.GetFileName(path).StartsWith(prefix + ".before-import-", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(path).StartsWith(prefix + ".import-", StringComparison.OrdinalIgnoreCase))
                            result.DataBytes += Count(path, result, seen);
                }
                catch (IOException) { result.Incomplete = true; }
                catch (UnauthorizedAccessException) { result.Incomplete = true; }
            }
        }

        private static void MeasureApp(Profile profile, StorageMeasurement result, HashSet<string> seen)
        {
            if (profile.IsOriginal) return;
            result.AppBytes = Count(profile.UsesSandboxie ? SandboxieClone.BoxRoot(profile) : profile.DirectoryPath, result, seen);
            result.AppBytes += Count(Path.Combine(Store.Root, "Instances", profile.Id), result, seen);
            result.AppBytes += Count(Path.Combine(Store.Root, "Releases", profile.Id), result, seen);
        }

        internal static long GetFileStorageBytes(string path, StorageMeasurement result)
        {
            uint high;
            uint low = GetCompressedFileSizeW(path, out high);
            if (low == UInt32.MaxValue && Marshal.GetLastWin32Error() != 0) { result.Incomplete = true; return 0; }
            return checked((long)(((ulong)high << 32) | low));
        }

        private static long Count(string path, StorageMeasurement result, HashSet<string> seen)
        {
            if (String.IsNullOrEmpty(path)) return 0;
            try
            {
                path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                if (seen.Contains(path)) return 0;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (FileNotFoundException) { return 0; }
                catch (DirectoryNotFoundException) { return 0; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Incomplete = true; return 0; }
                if ((attributes & FileAttributes.Directory) == 0)
                    return GetFileStorageBytes(path, result);
                if (!seen.Add(path)) return 0;
                long total = 0;
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) total += Count(child, result, seen);
                return total;
            }
            catch (IOException) { result.Incomplete = true; return 0; }
            catch (UnauthorizedAccessException) { result.Incomplete = true; return 0; }
        }
    }

    public partial class ManagerWindow
    {
        private static readonly TimeSpan StorageCheckInterval = TimeSpan.FromMinutes(2);
        private bool profileStorageScanning;
        private bool profileStorageRefreshRequested;
        private DateTime nextProfileStorageCheck;
        private bool storageClosed;

        private void CloseStorageMonitoring()
        {
            storageClosed = true;
        }

        private void RefreshProfileStorageTotal()
        {
            if (profileStorageTotal == null) return;
            var measured = rows.Where(profile => profile.MeasuredStorage != null).Select(profile => profile.MeasuredStorage).ToArray();
            bool pending = measured.Length < rows.Count;
            string total = pending && measured.Length == 0 ? "Đang tính…"
                : ((pending || measured.Any(usage => usage.Incomplete)) ? "≥ " : "")
                    + StorageMeasurement.Format(measured.Sum(usage => usage.TotalBytes));
            profileStorageTotal.Text = "Tổng: " + total;
        }

        private void RefreshStorageUsage()
        {
            if (storageClosed) return;
            RefreshProfileStorageTotal();
            RefreshProfileStorage();
        }

        private async void RefreshProfileStorage()
        {
            if (storageClosed || profileStorageScanning || rows.Count == 0) return;
            if (!profileStorageRefreshRequested && DateTime.UtcNow < nextProfileStorageCheck) return;
            profileStorageRefreshRequested = false;
            profileStorageScanning = true;
            var snapshot = rows.ToArray();
            StorageMeasurement[] measurements = null;
            try
            {
                measurements = await Task.Run(() => snapshot.Select(StorageUsage.Measure).ToArray()).ConfigureAwait(false);
            }
            catch (Exception exception) { Store.Log(exception); }
            try
            {
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    profileStorageScanning = false;
                    if (storageClosed) return;
                    if (profileStorageRefreshRequested)
                    {
                        RefreshProfileStorage();
                        return;
                    }
                    nextProfileStorageCheck = DateTime.UtcNow + StorageCheckInterval;
                    if (measurements == null) return;
                    for (int index = 0; index < snapshot.Length; index++)
                        snapshot[index].MeasuredStorage = measurements[index];
                    grid.Items.Refresh();
                    RefreshProfileStorageTotal();
                })).Task.ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
        }
    }
}
