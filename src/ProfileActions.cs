using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ZaloManagerPoc
{
    [DataContract]
    public class DataArchive
    {
        [DataMember] public string ProfileId;
        [DataMember] public string Version;
        [DataMember] public Dictionary<string, string> Files;
        [DataMember] public int Format;
        [DataMember] public string AccountId;
        [DataMember] public string AccountName;
        [DataMember] public string CreatedUtc;
        [DataMember] public string Scope;
    }

    public static class ProfileActions
    {
        public static void TransferWithRestart(Profile profile, string path, bool import)
        {
            if (profile.IsOriginal) throw new InvalidOperationException("Thao tác dữ liệu dành cho hồ sơ clone.");
            // Reject invalid input before interrupting the current session.
            if (import) ValidateArchive(path, profile);
            else if (!Directory.Exists(profile.DataPath)) throw new DirectoryNotFoundException("Hồ sơ chưa có dữ liệu để xuất.");
            bool restart = Instance.IsRunning(profile);
            Exception failure = null;
            try
            {
                if (restart) Instance.Stop(profile);
                if (import) Import(profile, path); else Export(profile, path);
            }
            catch (Exception exception) { failure = exception; }
            if (restart)
            {
                try { Instance.Open(profile); }
                catch (Exception exception)
                {
                    if (failure != null) throw new AggregateException("Thao tác dữ liệu và mở lại Zalo đều gặp lỗi.", failure, exception);
                    throw new IOException("Thao tác dữ liệu đã hoàn tất nhưng chưa mở lại được Zalo. " + exception.Message, exception);
                }
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public static void RequireStopped(Profile profile)
        {
            if (profile.IsOriginal) throw new InvalidOperationException("Thao tác này dành cho hồ sơ clone.");
            if (Instance.IsRunning(profile)) throw new InvalidOperationException("Thoát hoàn toàn Zalo của hồ sơ này trước khi thao tác dữ liệu.");
        }

        public static void CreateShortcut(Profile profile, string destinationDirectory = null)
        {
            string safeName = String.Join("_", (profile.Name ?? "Zalo").Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
            if (safeName.Length > 70) safeName = safeName.Substring(0, 70);
            string fileName = "Zalo - " + safeName + " - " + profile.Id + ".lnk";
            string path = Path.Combine(destinationDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);
            string previous = profile.ShortcutPath;
            SaveShortcut(path, profile);
            profile.ShortcutPath = path;
            if (!String.IsNullOrEmpty(previous) && !String.Equals(previous, path, StringComparison.OrdinalIgnoreCase) && File.Exists(previous))
                File.Delete(previous);
            if (destinationDirectory == null) RefreshShellShortcuts(profile);
            ShellIdentity.NotifyShortcut(path);
        }

        private static void SaveShortcut(string path, Profile profile)
        {
            ShellIdentity.SaveShortcut(path, profile, MakeIcon(profile));
        }

        private static void RefreshShellShortcuts(Profile profile)
        {
            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Zalo Manager");
            Directory.CreateDirectory(programs);
            // Update the stable per-profile Start Menu link used by Windows taskbar grouping.
            SaveShortcut(Path.Combine(programs, "Zalo-" + profile.Id + ".lnk"), profile);
            string pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (!Directory.Exists(pinned)) return;
            foreach (string link in Directory.GetFiles(pinned, "*.lnk"))
            {
                if (!ShellIdentity.IsProfileShortcut(link, profile)) continue;
                SaveShortcut(link, profile); ShellIdentity.NotifyShortcut(link);
            }
        }

        public static string MakeIcon(Profile profile)
        {
            if (String.IsNullOrEmpty(profile.AvatarPath)) return profile.Executable;
            string iconPath = Path.Combine(Path.GetDirectoryName(profile.AvatarPath), Path.GetFileNameWithoutExtension(profile.AvatarPath) + ".shell-v2.ico");
            if (File.Exists(iconPath)) return iconPath;
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var frames = new List<byte[]>();
            using (var source = System.Drawing.Image.FromFile(profile.AvatarPath))
            {
                foreach (int size in sizes)
                using (var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var memory = new MemoryStream())
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source, 0, 0, size, size);
                    }
                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png); frames.Add(memory.ToArray());
                }
            }
            string temporary = iconPath + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int index = 0; index < sizes.Length; index++)
                {
                    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index])); writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(frames[index].Length); writer.Write(offset); offset += frames[index].Length;
                }
                foreach (byte[] frame in frames) writer.Write(frame);
            }
            File.Move(temporary, iconPath);
            return iconPath;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr type, IntPtr icon, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendTextTimeout(IntPtr window, uint message, IntPtr type, string text, uint flags, uint timeout, out IntPtr result);
        private static readonly Dictionary<string, System.Drawing.Icon> Icons = new Dictionary<string, System.Drawing.Icon>();
        private static readonly Dictionary<IntPtr, string> AppliedIcons = new Dictionary<IntPtr, string>();
        public static void ApplyAvatar(Profile profile)
        {
            var processes = Instance.Processes(profile);
            try
            {
                if (processes.Count == 0) return;
                System.Drawing.Icon icon = null;
                string iconKey = profile.Id + "|" + profile.AvatarPath + "|shell-v2";
                string key = iconKey + "|" + profile.Name;
                if (!String.IsNullOrEmpty(profile.AvatarPath) && !Icons.TryGetValue(iconKey, out icon))
                { icon = new System.Drawing.Icon(MakeIcon(profile)); Icons.Add(iconKey, icon); }
                var windows = WindowActivation.Find(processes.Where(process => process.ProcessName == "Zalo").Select(process => process.Id));
                foreach (var window in windows)
                {
                    string applied;
                    if (AppliedIcons.TryGetValue(window, out applied) && applied == key) continue;
                    ShellIdentity.Window(window, profile, MakeIcon(profile));
                    IntPtr result;

                    if (icon != null)
                    {
                        SendMessageTimeout(window, 0x80, IntPtr.Zero, icon.Handle, 2, 200, out result);
                        SendMessageTimeout(window, 0x80, new IntPtr(1), icon.Handle, 2, 200, out result);
                    }
                    AppliedIcons[window] = key;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        public static void Export(Profile profile, string destination)
        {
            RequireStopped(profile);
            if (!Directory.Exists(profile.DataPath)) throw new DirectoryNotFoundException("Hồ sơ chưa có dữ liệu để xuất.");
            string staging = Path.Combine(Store.Root, "Transfers", Guid.NewGuid().ToString("N"));
            string payload = Path.Combine(staging, "data");
            Instance.CopyDirectory(profile.DataPath, payload);
            RequireStopped(profile);
            var identity = IdentityReader.ReadDirectory(payload);
            var manifest = new DataArchive { Format = 2, ProfileId = profile.Id, Version = profile.Version,
                AccountId = identity.AccountId, AccountName = identity.Name, CreatedUtc = DateTime.UtcNow.ToString("o"),
                Scope = "CloneDataDirectoryOnly; external downloads, cloud-only media and operating-system credentials are not included; login and history restoration not verified",
                Files = new Dictionary<string, string>() };
            foreach (string file in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
                manifest.Files.Add(file.Substring(payload.Length + 1).Replace('\\', '/'), Instance.Hash(file));
            using (var file = File.Create(Path.Combine(staging, "manifest.json")))
                new DataContractJsonSerializer(typeof(DataArchive)).WriteObject(file, manifest);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            // Framework ZipFile emits backslashes for nested paths on Windows.
            // Write portable names explicitly so real nested Zalo data can round-trip.
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, file.Substring(staging.Length + 1).Replace('\\', '/'), CompressionLevel.Optimal);
            // Opening the archive verifies the output before publishing it.
            ValidateArchive(temporary, profile);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
            // Transfer staging is retained until explicitly cleaned up by the user.
        }

        public static void ValidateArchive(string archive, Profile profile)
        {
            using (var zip = ZipFile.OpenRead(archive)) ReadManifest(zip, profile);
        }

        private static DataArchive ReadManifest(ZipArchive zip, Profile profile)
        {
            var entry = zip.GetEntry("manifest.json");
            if (entry == null || entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("Bản xuất thiếu manifest hợp lệ.");
            DataArchive manifest;
            using (var stream = entry.Open()) manifest = (DataArchive)new DataContractJsonSerializer(typeof(DataArchive)).ReadObject(stream);
            if (manifest == null || manifest.Format != 2 || String.IsNullOrEmpty(manifest.AccountId) || !manifest.AccountId.All(Char.IsDigit))
                throw new InvalidDataException("Bản xuất cũ hoặc chưa xác định được ID Zalo. Hãy xuất lại bằng bản mới; không ghi đè dữ liệu hiện tại.");
            if (manifest.Version != profile.Version)
                throw new InvalidDataException("Phiên bản Zalo của bản xuất không khớp. Chưa xác minh khả năng chuyển đổi dữ liệu giữa các phiên bản.");
            CheckAccount(profile, manifest.AccountId);
            if (manifest.Files == null) throw new InvalidDataException("Thiếu danh mục tệp.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in zip.Entries)
            {
                string name = item.FullName;
                if (!names.Add(name) || name.Contains("\\") || name.Contains(":") || name.StartsWith("/") || name.Split('/').Any(part => part == ".." || part == "." || part.Length == 0))
                    throw new InvalidDataException("Đường dẫn trong bản xuất không hợp lệ.");
                if (name == "manifest.json") continue;
                string hash;
                if (!name.StartsWith("data/", StringComparison.Ordinal) || !manifest.Files.TryGetValue(name.Substring(5), out hash))
                    throw new InvalidDataException("Tệp chưa có trong danh mục kiểm tra.");
                using (var stream = item.Open()) using (var sha = System.Security.Cryptography.SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != hash)
                        throw new InvalidDataException("Bản xuất có tệp bị thay đổi hoặc hỏng.");
            }
            if (names.Count != manifest.Files.Count + 1) throw new InvalidDataException("Bản xuất thiếu tệp dữ liệu.");
            return manifest;
        }

        private static void CheckAccount(Profile profile, string accountId)
        {
            if (!Directory.Exists(profile.DataPath) || !Directory.EnumerateFileSystemEntries(profile.DataPath).Any()) return;
            if (IdentityReader.ReadDirectory(profile.DataPath).AccountId != accountId)
                throw new InvalidDataException("Bản xuất thuộc tài khoản Zalo khác. Không ghi đè hoặc gộp dữ liệu. Hãy chọn Nhập thành hồ sơ mới.");
        }

        public static void Import(Profile profile, string archive)
        {
            RequireStopped(profile);
            string staging = profile.DataPath + ".import-" + Guid.NewGuid().ToString("N");
            using (var zip = ZipFile.OpenRead(archive))
            {
                var manifest = ReadManifest(zip, profile);
                long total = 0;
                foreach (var entry in zip.Entries) total = checked(total + entry.Length);
                if (new DriveInfo(Path.GetPathRoot(staging)).AvailableFreeSpace < total + 64L * 1024 * 1024)
                    throw new IOException("Ổ đĩa thiếu dung lượng để import.");
                Directory.CreateDirectory(staging);
                foreach (var entry in zip.Entries.Where(item => item.FullName.StartsWith("data/", StringComparison.Ordinal)))
                {
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Substring(5).Replace('/', '\\')));
                    if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn import vượt thư mục hồ sơ.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
                }
                // Independently read the extracted database, not just the editable manifest.
                if (IdentityReader.ReadDirectory(staging).AccountId != manifest.AccountId)
                    throw new InvalidDataException("ID Zalo trong dữ liệu không khớp bản khai. Dữ liệu hiện tại được giữ nguyên.");
                CheckAccount(profile, manifest.AccountId);
            }
            RequireStopped(profile);
            string backup = profile.DataPath + ".before-import-" + Guid.NewGuid().ToString("N");
            bool moved = Directory.Exists(profile.DataPath);
            if (moved) Directory.Move(profile.DataPath, backup);
            try { Directory.Move(staging, profile.DataPath); }
            catch { if (moved) Directory.Move(backup, profile.DataPath); throw; }
        }

        public static string ArchiveDeleted(Profile profile)
        {
            RequireStopped(profile);
            string deleted = Path.Combine(Store.Root, "Deleted", profile.Id + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(deleted);
            // Move only the known data directory. Program generations stay available for recovery.
            if (Directory.Exists(profile.DataPath)) Directory.Move(profile.DataPath, Path.Combine(deleted, "data"));
            return deleted;
        }
    }

    public partial class ManagerWindow
    {
        private void AddActionsColumn()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            string[] labels = { "Mở Zalo", "⏹ Dừng Zalo", "Mở thư mục Data", "Tạo Shortcut", "Xóa", "…" };
            string[] display = { "▶ Mở", "■ Dừng", "Data", "↗ Shortcut", "\uE74D", "\uE712" };
            for (int index = 0; index < labels.Length; index++)
            {
                string label = labels[index];
                var button = new FrameworkElementFactory(typeof(Button));
                button.SetValue(Button.ContentProperty, display[index]); button.SetValue(Button.TagProperty, label); button.SetValue(Button.ToolTipProperty, label == "…" ? "Đổi tên, avatar, import / xuất data" : label);
                button.SetResourceReference(Button.StyleProperty, index == 0 ? "PrimaryButton" : index == 1 || index == 4 ? "DangerGhostButton" : "RowActionButton");
                button.SetValue(Button.PaddingProperty, new Thickness(9, 6, 9, 6)); button.SetValue(Button.FontSizeProperty, 12.0);
                button.SetValue(Button.MarginProperty, new Thickness(0, 0, 6, 0));
                if (index >= 4) button.SetValue(Button.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
                button.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
                if (label == "Xóa" || label == "…") button.SetBinding(Button.IsEnabledProperty, new Binding("IsOriginal") { Converter = new CloneEnabledConverter() });
                button.AddHandler(Button.ClickEvent, new RoutedEventHandler(RowAction)); panel.AppendChild(button);
            }
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Action", Width = 570, CellTemplate = new DataTemplate { VisualTree = panel } });
        }
        private async void RowAction(object sender, RoutedEventArgs args)
        {
            var button = (Button)sender; var profile = button.DataContext as Profile;
            if (profile == null || busy) return;
            string action = (string)button.Tag;
            if (action == "…") { ShowProfileMenu(button, profile); return; }
            SetBusy(true);
            try
            {
                if (action == "Mở Zalo")
                {
                    if (!profile.IsOriginal && profile.PatchRevision < 4 && !Instance.IsRunning(profile))
                    {
                        status.Text = "Đang nâng bản chương trình clone, giữ nguyên thư mục dữ liệu phiên cũ…";
                        var original = await Task.Run(() => Original.Detect());
                        var next = await Task.Run(() => Instance.PrepareUpdate(profile, original));
                        Store.Save(profiles.Select(item => item == profile ? next : item));
                        profiles[profiles.IndexOf(profile)] = next; profile = next;
                        if (!String.IsNullOrEmpty(profile.ShortcutPath)) ProfileActions.CreateShortcut(profile);
                    }
                    await Task.Run(() => Instance.Open(profile));
                }
                else if (action == "⏹ Dừng Zalo")
                {
                    if (!ConfirmDialog.Ask(this, "Dừng Zalo", "Dừng toàn bộ tiến trình Zalo của ", profile.Name, "Thao tác có thể ngắt cuộc gọi và dữ liệu đang ghi.", "Dừng Zalo")) return;
                    await Task.Run(() => Instance.Stop(profile));
                }
                else if (action == "Mở thư mục Data") { Directory.CreateDirectory(profile.DataPath); Process.Start("explorer.exe", "\"" + profile.DataPath + "\""); }
                else if (action == "Tạo Shortcut") { ProfileActions.CreateShortcut(profile); Store.Save(profiles); status.Text = "Đã tạo shortcut cho “" + profile.Name + "”."; }
                else if (action == "Xóa")
                {
                    ProfileActions.RequireStopped(profile);
                    if (!ConfirmDialog.Ask(this, "Xóa hồ sơ Zalo", "Xóa hồ sơ ", profile.Name, "Dữ liệu được chuyển vào thư mục Deleted để có thể lấy lại.", "Xóa hồ sơ")) return;
                    string location = await Task.Run(() => ProfileActions.ArchiveDeleted(profile));
                    try { Store.Save(profiles.Where(item => item != profile)); }
                    catch { if (Directory.Exists(Path.Combine(location, "data"))) Directory.Move(Path.Combine(location, "data"), profile.DataPath); throw; }
                    profiles.Remove(profile); status.Text = "Đã xóa hồ sơ. Dữ liệu giữ tại " + location;
                    if (!String.IsNullOrEmpty(profile.ShortcutPath) && File.Exists(profile.ShortcutPath)) File.Delete(profile.ShortcutPath);
                }
            }
            catch (Exception exception) { Report(exception); }
            finally { SetBusy(false); RefreshStatus(); }
        }

        private void ShowProfileMenu(Button button, Profile profile)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)Resources["ModernContextMenu"];
            menu.Resources.MergedDictionaries.Add(Resources);
            menu.Items.Add(new MenuItem { Header = "TÙY CHỈNH HỒ SƠ", Style = (Style)Resources["ModernMenuGroupLabel"] });
            var rename = new MenuItem { Header = "Cập nhật tên hồ sơ…" };
            var manualName = new MenuItem { Header = "Tự đổi…" };
            manualName.Click += delegate
            {
                var dialog = new RenameProfileDialog(profile.Name, Resources) { Owner = this };
                if (dialog.ShowDialog() == true) RunProfileEdit(profile, () => profile.Name = dialog.NewProfileName);
            };
            rename.Items.Add(manualName);
            var zaloName = new MenuItem { Header = "Cập nhật theo tên Zalo hiện tại" };
            zaloName.Click += async delegate { await UpdateFromZalo(profile, false); };
            rename.Items.Add(zaloName); menu.Items.Add(rename);
            var avatar = new MenuItem { Header = "Đổi avatar / biểu tượng app…" };
            var manualAvatar = new MenuItem { Header = "Tự đổi…" };
            manualAvatar.Click += delegate
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp", Title = "Chọn avatar cho tài khoản clone" };
                if (dialog.ShowDialog(this) != true) return;
                RunProfileEdit(profile, delegate
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(dialog.FileName); image.EndInit(); image.Freeze();
                    string path = Path.Combine(Store.Root, "Avatars", profile.Id + "-" + Guid.NewGuid().ToString("N") + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) encoder.Save(stream);
                    profile.AvatarPath = path; ProfileActions.ApplyAvatar(profile);
                });
            };
            avatar.Items.Add(manualAvatar);
            var zaloAvatar = new MenuItem { Header = "Cập nhật theo avatar Zalo hiện tại" };
            zaloAvatar.Click += async delegate { await UpdateFromZalo(profile, true); };
            avatar.Items.Add(zaloAvatar); menu.Items.Add(avatar);
            var export = new MenuItem { Header = "Xuất data…" }; export.Click += async delegate { await Transfer(profile, false); }; menu.Items.Add(export);
            var import = new MenuItem { Header = "Import data…" }; import.Click += async delegate { await Transfer(profile, true); }; menu.Items.Add(import);
            var importNew = new MenuItem { Header = "Nhập thành hồ sơ mới…" }; importNew.Click += async delegate { await Transfer(profile, true, true); }; menu.Items.Add(importNew);
            rename.Icon = MenuGlyph("\uE70F"); avatar.Icon = MenuGlyph("\uEB9F"); export.Icon = MenuGlyph("\uE898"); import.Icon = MenuGlyph("\uE8B5");
            menu.Items.Insert(menu.Items.IndexOf(export), new Separator { Style = (Style)Resources["ModernMenuSeparator"] });
            menu.Items.Insert(menu.Items.IndexOf(export), new MenuItem { Header = "DỮ LIỆU HỒ SƠ", Style = (Style)Resources["ModernMenuGroupLabel"] });
            button.ContextMenu = menu; menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = -12; menu.VerticalOffset = -6; menu.DataContext = profile; menu.IsOpen = true;
        }
        private static TextBlock MenuGlyph(string glyph)
        { return new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center }; }

        private bool RunProfileEdit(Profile profile, Action edit)
        {
            try { edit(); Store.Save(profiles); ProfileActions.CreateShortcut(profile); Store.Save(profiles); ProfileActions.ApplyAvatar(profile); RefreshStatus(); return true; }
            catch (Exception exception) { Report(exception); return false; }
        }
        private async Task UpdateFromZalo(Profile profile, bool avatar)
        {
            if (busy) return;
            SetBusy(true);
            try
            {
                status.Text = avatar ? "Đang lấy avatar của tài khoản Zalo…" : "Đang lấy tên tài khoản Zalo…";
                var identity = await Task.Run(() => IdentityReader.Read(profile));
                if (avatar)
                {
                    string path = await Task.Run(() => IdentityReader.DownloadAvatar(profile, identity));
                    if (!RunProfileEdit(profile, () => { profile.AvatarPath = path; ProfileActions.ApplyAvatar(profile); })) return;
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(identity.Name)) throw new InvalidDataException("Zalo chưa có tên hiển thị trong cache của tài khoản này.");
                    if (!RunProfileEdit(profile, () => profile.Name = identity.Name)) return;
                }
                status.Text = avatar ? "Đã cập nhật avatar cho hồ sơ, taskbar và shortcut Desktop." : "Đã cập nhật tên hồ sơ theo Zalo hiện tại.";
            }
            catch (Exception exception) { Report(exception); }
            finally { SetBusy(false); RefreshStatus(); }
        }
        private async Task Transfer(Profile profile, bool import, bool newProfile = false)
        {
            if (busy) return;
            SetBusy(true);
            try
            {
                string path;
                if (import)
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Dữ liệu Zalo Manager|*.zmdata", Title = "Chọn bản xuất của hồ sơ này" };
                    if (dialog.ShowDialog(this) != true) return; path = dialog.FileName;
                    if (!ConfirmDialog.Ask(this, "Import dữ liệu", newProfile ? "Tạo hồ sơ riêng từ bản xuất cho " : "Khôi phục dữ liệu cho ", profile.Name, newProfile ? "Không thay dữ liệu hồ sơ đang chọn. Bản xuất cần cùng phiên bản Zalo. Có thể phải đăng nhập lại; chưa xác minh khôi phục lịch sử." : "App tự dừng clone rồi mở lại nếu trước đó đang chạy. Cuộc gọi sẽ bị ngắt. Giữ bản dữ liệu cũ. Chỉ nhập cùng tài khoản và phiên bản. Có thể phải đăng nhập lại.", "Import dữ liệu", false)) return;
                    if (newProfile)
                    {
                        string id;
                        var random = new Random();
                        do { id = random.Next(100000, 1000000).ToString(); }
                        while (profiles.Any(p => p.Id == id) || Directory.Exists(Path.Combine(Store.Root, "Instances", id)) || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zm" + id)));
                        var target = new Profile { Id = id, Name = "Hồ sơ khôi phục", Source = profile.Source, InstalledVersion = profile.InstalledVersion };
                        await Task.Run(() => ProfileActions.ValidateArchive(path, target));
                        await Task.Run(() => Instance.Prepare(target));
                        await Task.Run(() => ProfileActions.Import(target, path));
                        target.Name = IdentityReader.Read(target).Name ?? target.Name;
                        profiles.Add(target); Store.Save(profiles); grid.SelectedItem = target;
                        status.Text = "Đã nhập vào hồ sơ mới, chưa mở Zalo. Cần kiểm tra đăng nhập, tin nhắn và tệp.";
                        return;
                    }
                }
                else
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Dữ liệu Zalo Manager|*.zmdata", FileName = "Zalo-" + profile.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), DefaultExt = ".zmdata" };
                    if (dialog.ShowDialog(this) != true) return; path = dialog.FileName;
                }
                status.Text = "Đang xử lý dữ liệu. Clone đang chạy sẽ được tự dừng và mở lại sau khi hoàn tất…";
                await Task.Run(() => ProfileActions.TransferWithRestart(profile, path, import));
                status.Text = import ? "Đã thay dữ liệu, kiểm tra ID tài khoản và giữ bản cũ. Cần kiểm tra đăng nhập, tin nhắn và tệp trong Zalo." : "Đã xuất thư mục data của clone và kiểm tra SHA-256. Không gồm tệp tải ngoài thư mục; chưa xác minh khôi phục phiên đăng nhập.";
            }
            catch (Exception exception) { Report(exception); }
            finally { SetBusy(false); RefreshStatus(); }
        }
    }
    public class CloneEnabledConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, System.Globalization.CultureInfo culture) { return !(bool)value; }
        public object ConvertBack(object value, Type type, object parameter, System.Globalization.CultureInfo culture) { throw new NotSupportedException(); }
    }
}
