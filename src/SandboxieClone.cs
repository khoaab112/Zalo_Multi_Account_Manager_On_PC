using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZaloManagerPoc
{
    internal static class SandboxieClone
    {
        private static readonly string Install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sandboxie-Plus");
        private static readonly string Runner = Path.Combine(Install, "Start.exe");
        private static readonly string Ini = Path.Combine(Install, "SbieIni.exe");
        private static readonly Lazy<QueryProcess> Query = new Lazy<QueryProcess>(LoadQuery);
        private const int NotBoxed = unchecked((int)0xC000000B);

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private delegate int QueryProcess(IntPtr pid, StringBuilder box, IntPtr image, IntPtr sid, IntPtr session);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        internal static bool Installed { get { return File.Exists(Runner) && File.Exists(Ini); } }
        internal static string BoxName(Profile profile) { return "zm_" + profile.Id; }
        internal static string BoxRoot(Profile profile) { return Path.Combine(Store.Root, "Sandboxie", BoxName(profile)); }
        internal static string DataPath(Profile profile) { return Path.Combine(BoxRoot(profile), @"user\current\AppData\Roaming\ZaloData"); }

        internal static string ProcessBox(int id)
        {
            if (!Installed) return null;
            var box = new StringBuilder(34);
            int result = Query.Value(new IntPtr(id), box, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result == 0) return box.ToString();
            if (result == NotBoxed) return null;
            throw new IOException("Không xác minh được tiến trình Sandboxie (" + result.ToString("X8") + ").");
        }

        private static QueryProcess LoadQuery()
        {
            var module = LoadLibraryEx(Path.Combine(Install, "SbieDll.dll"), IntPtr.Zero, 0x1100);
            var address = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "SbieApi_QueryProcess");
            if (address == IntPtr.Zero) throw new IOException("Không đọc được Sandboxie-Plus. Kiểm tra lại bản cài đi kèm Manager.");
            return (QueryProcess)Marshal.GetDelegateForFunctionPointer(address, typeof(QueryProcess));
        }

        internal static void Configure(Profile profile)
        {
            if (!Installed) throw new InvalidOperationException("Cần cài Sandboxie-Plus bằng bộ cài Zalo Manager.");
            string box = BoxName(profile);
            string root = BoxRoot(profile);
            string old = Run(Ini, "query " + box + " FileRootPath").Trim();
            if (old.Length != 0 && !String.Equals(root, old, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Tên box đã thuộc một thư mục Sandboxie khác.");
            if (old.Length == 0 && Run(Ini, "query " + box + " Enabled").Trim().Length != 0)
                throw new IOException("Tên box đã được sử dụng.");
            string source = profile.Source;
            if (Path.GetFileName(source).StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase))
                source = Path.GetDirectoryName(source);
            Set(box, "FileRootPath", root);
            Set(box, "SeparateUserFolders", "y");
            Set(box, "AutoDelete", "n");
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Set(box, "WriteFilePath", Path.Combine(roaming, "ZaloData"));
            Run(Ini, "append " + box + " WriteFilePath " + Quote(Path.Combine(roaming, "ZaloSpace")));
            Set(box, "ReadFilePath", source);
            Set(box, "Enabled", "y");
        }

        private static void Set(string box, string key, string value)
        {
            Run(Ini, "set " + box + " " + key + " " + Quote(value));
        }

        internal static void Start(Profile profile)
        {
            Original.Validate(profile.Source);
            if (!String.Equals(profile.SourceHash, Instance.Hash(Path.Combine(profile.Source, "resources", "app.asar")), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Bản Zalo đã thay đổi. Chọn Kiểm tra & Đồng bộ để cập nhật hồ sơ.");
            VerifyConfigured(profile);
            Run(Runner, "/box:" + BoxName(profile) + " /silent " + Quote(profile.Executable));
        }

        internal static void Stop(Profile profile)
        {
            VerifyConfigured(profile);
            Run(Runner, "/box:" + BoxName(profile) + " /terminate");
        }

        private static void VerifyConfigured(Profile profile)
        {
            if (!Installed) throw new InvalidOperationException("Cần cài Sandboxie-Plus bằng bộ cài Zalo Manager.");
            string box = BoxName(profile);
            string current = Run(Ini, "query " + box + " FileRootPath").Trim();
            if (!String.Equals(current, BoxRoot(profile), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cấu hình box đã thay đổi. Chọn Kiểm tra & Đồng bộ để kiểm tra lại.");
        }

        private static string Run(string executable, string arguments)
        {
            var start = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(8000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("Sandboxie phản hồi quá chậm.");
                }
                if (process.ExitCode != 0)
                    throw new IOException("Sandboxie không hoàn tất thao tác (mã " + process.ExitCode + ").");
                return output.GetAwaiter().GetResult();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.TrimEnd(Path.DirectorySeparatorChar) + "\"";
        }
    }
}
