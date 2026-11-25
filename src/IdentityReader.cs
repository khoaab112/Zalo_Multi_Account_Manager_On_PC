using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ZaloManagerPoc
{
    [System.Runtime.Serialization.DataContract]
    public class AccountIdentity
    {
        [System.Runtime.Serialization.DataMember] public string AccountId;
        [System.Runtime.Serialization.DataMember(Name = "zName")] public string Name;
        [System.Runtime.Serialization.DataMember(Name = "avatar")] public string Avatar;
    }
    public static class IdentityReader
    {
        public static AccountIdentity Read(Profile profile)
        {
            if (!profile.IsOriginal) return ReadDirectory(profile.DataPath);
            string directory = profile.DataPath;
            string activeId = Directory.GetFiles(directory, "ftsm_*.db")
                .Select(path => new
                {
                    Id = Path.GetFileNameWithoutExtension(path).Substring(5),
                    UpdatedUtc = File.Exists(path + "-wal")
                        ? new[] { File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(path + "-wal") }.Max()
                        : File.GetLastWriteTimeUtc(path)
                })
                .Where(item => item.Id.Length > 0 && item.Id.All(Char.IsDigit))
                .OrderByDescending(item => item.UpdatedUtc)
                .Select(item => item.Id)
                .FirstOrDefault();
            if (String.IsNullOrEmpty(activeId))
                throw new InvalidDataException("Chưa xác định tài khoản trong Zalo gốc. Đăng nhập Zalo trước hoặc dùng Tự đổi.");
            string db = Path.Combine(directory, @"Database\_production\Storage.db");
            var identity = TryReadById(db, activeId);
            if (identity == null)
                throw new InvalidDataException("Chưa có tên và avatar của tài khoản Zalo gốc hiện tại trong cache. Thử lại sau khi Zalo đồng bộ.");
            return identity;
        }

        public static AccountIdentity ReadDirectory(string directory)
        {
            var ids = Directory.GetFiles(directory, "ftsm_*.db")
                .Select(path => Path.GetFileNameWithoutExtension(path).Substring(5))
                .Where(id => id.Length > 0 && id.All(Char.IsDigit)).Distinct().ToList();
            if (ids.Count != 1) throw new InvalidDataException("Chưa xác định duy nhất tài khoản trong hồ sơ. Đăng nhập Zalo trước hoặc dùng Tự đổi.");
            string db = Path.Combine(directory, @"Database\_production\Storage.db");
            var identity = TryReadById(db, ids[0]);
            if (identity == null) throw new InvalidDataException("Chưa có tên và avatar của chính tài khoản này trong cache. Thử lại sau khi Zalo đồng bộ.");
            return identity;
        }

        private static AccountIdentity TryReadById(string db, string accountId)
        {
            var rows = Query(db, "SELECT val FROM [info-cache] WHERE [key] IN ('0_" + accountId + "','1_" + accountId + "') ORDER BY [key] LIMIT 1", 1);
            if (rows.Count != 1) return null;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(rows[0][0])))
            {
                var identity = (AccountIdentity)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AccountIdentity)).ReadObject(stream);
                identity.AccountId = accountId;
                return identity;
            }
        }

        public static string DownloadAvatar(Profile profile, AccountIdentity identity)
        {
            // This executable is compiled with the Framework compiler. Its legacy TLS
            // defaults differ from PowerShell 7, so negotiate TLS 1.2 explicitly.
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            Uri uri;
            if (!Uri.TryCreate(identity.Avatar, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                !(uri.Host.EndsWith(".zadn.vn", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".zaloapp.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Địa chỉ avatar chưa thuộc nguồn HTTPS được hỗ trợ.");
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(uri);
            request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
            using (var response = (System.Net.HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK || response.ContentLength > 8 * 1024 * 1024)
                    throw new IOException("Không tải được avatar hoặc ảnh vượt 8 MiB.");
                using (var input = response.GetResponseStream()) using (var bytes = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    { if (bytes.Length + count > 8 * 1024 * 1024) throw new IOException("Avatar vượt 8 MiB."); bytes.Write(buffer, 0, count); }
                    bytes.Position = 0;
                    var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    image.StreamSource = bytes; image.EndInit(); image.Freeze();
                    string directory = Path.Combine(Store.Root, "Avatars"); Directory.CreateDirectory(directory);
                    string destination = Path.Combine(directory, profile.Id + "-" + Guid.NewGuid().ToString("N") + ".png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using (var output = File.Create(destination)) encoder.Save(output);
                    return destination;
                }
            }
        }
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
        public static List<string[]> Query(string path, string sql, int columns)
        {
            IntPtr db;
            int result = sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 1, IntPtr.Zero);
            try
            {
                if (result != 0) throw new IOException("Không mở được metadata Zalo ở chế độ chỉ đọc.");
                IntPtr statement;
                if (sqlite3_prepare_v2(db, Encoding.UTF8.GetBytes(sql + "\0"), -1, out statement, IntPtr.Zero) != 0)
                    throw new InvalidDataException("Cấu trúc metadata Zalo chưa được hỗ trợ.");
                try
                {
                    var rows = new List<string[]>();
                    while ((result = sqlite3_step(statement)) == 100)
                    {
                        var row = new string[columns];
                        for (int column = 0; column < columns; column++)
                        {
                            int size = sqlite3_column_bytes(statement, column);
                            if (size > 4 * 1024 * 1024) throw new InvalidDataException("Metadata vượt kích thước hỗ trợ.");
                            byte[] bytes = new byte[size];
                            if (size > 0) Marshal.Copy(sqlite3_column_text(statement, column), bytes, 0, size);
                            row[column] = Encoding.UTF8.GetString(bytes);
                        }
                        rows.Add(row);
                    }
                    if (result != 101) throw new IOException("Metadata đang bận. Thử lại sau.");
                    return rows;
                }
                finally { sqlite3_finalize(statement); }
            }
            finally { if (db != IntPtr.Zero) sqlite3_close(db); }
        }
    }
}
