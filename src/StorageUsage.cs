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
        public string Text { get { return (Incomplete ? "≥ " : "") + Format(DataBytes + AppBytes); } }
        public string Details { get { return "Data và bản khôi phục: " + Format(DataBytes) + "\nApp và các phiên bản được giữ lại: " + Format(AppBytes) +
            "\nTính dung lượng tệp trên đĩa; bỏ qua liên kết. Không gồm file xuất, tệp tải ngoài thư mục hoặc dữ liệu trên cloud." +
            (Incomplete ? "\nMột số tệp/thư mục không đọc được, số liệu chưa đầy đủ." : "") + "\nCập nhật dung lượng khoảng 2 phút/lần."; } }
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
            string originalRoot = profile.Source;
            if (profile.IsOriginal && !String.IsNullOrEmpty(originalRoot) && Path.GetFileName(originalRoot).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase))
                originalRoot = Path.GetDirectoryName(originalRoot);
            result.AppBytes = Count(profile.IsOriginal ? originalRoot : profile.DirectoryPath, result, seen);
            if (!profile.IsOriginal)
            {
                result.AppBytes += Count(Path.Combine(Store.Root, "Instances", profile.Id), result, seen);
                result.AppBytes += Count(Path.Combine(Store.Root, "Releases", profile.Id), result, seen);
            }
            return result;
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
                seen.Add(path);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    uint high; uint low = GetCompressedFileSizeW(path, out high);
                    if (low == UInt32.MaxValue && Marshal.GetLastWin32Error() != 0) { result.Incomplete = true; return 0; }
                    return checked((long)(((ulong)high << 32) | low));
                }
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
        private bool storageScanning;
        private readonly Dictionary<Profile, DateTime> storageChecked = new Dictionary<Profile, DateTime>();
        private async void RefreshStorageUsage()
        {
            if (storageScanning) return;
            var pending = rows.Where(p => !storageChecked.ContainsKey(p) || DateTime.UtcNow - storageChecked[p] > TimeSpan.FromMinutes(2)).ToArray();
            if (pending.Length == 0) return;
            storageScanning = true;
            try
            {
                foreach (var profile in pending)
                {
                    var usage = await Task.Run(() => StorageUsage.Measure(profile));
                    profile.StorageUsage = usage.Text; profile.StorageDetails = usage.Details;
                    storageChecked[profile] = DateTime.UtcNow;
                }
                grid.Items.Refresh();
            }
            catch (Exception exception) { Store.Log(exception); }
            finally { storageScanning = false; }
        }
    }
}
