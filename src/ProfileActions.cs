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
            if (destinationDirectory == null) RefreshShellShortcuts(profile, true);
            ShellIdentity.NotifyShortcut(path);
        }

        private static void SaveShortcut(string path, Profile profile)
        {
            ShellIdentity.SaveShortcut(path, profile, MakeIcon(profile));
        }

        internal static void RefreshShellShortcuts(Profile profile, bool createStartMenuShortcut)
        {
            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Zalo Manager");
            string startMenuShortcut = Path.Combine(programs, "Zalo-" + profile.Id + ".lnk");
            if (createStartMenuShortcut || File.Exists(startMenuShortcut))
            {
                Directory.CreateDirectory(programs);
                SaveShortcut(startMenuShortcut, profile);
                ShellIdentity.NotifyShortcut(startMenuShortcut);
            }
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
            string iconPath = Path.Combine(Path.GetDirectoryName(profile.AvatarPath), Path.GetFileNameWithoutExtension(profile.AvatarPath) + ".shell-v3.ico");
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
                    if (size == 256) bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                    else WriteDibIconFrame(bitmap, memory);
                    frames.Add(memory.ToArray());
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

        private static void WriteDibIconFrame(System.Drawing.Bitmap bitmap, Stream output)
        {
            int size = bitmap.Width;
            int maskStride = (size + 31) / 32 * 4;
            var mask = new byte[maskStride * size];
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
            {
                writer.Write(40);
                writer.Write(size);
                writer.Write(size * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                for (int i = 0; i < 6; i++) writer.Write(0);
                for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                {
                    var pixel = bitmap.GetPixel(column, size - row - 1);
                    writer.Write(pixel.ToArgb());
                    if (pixel.A == 0) mask[row * maskStride + column / 8] |= (byte)(0x80 >> (column % 8));
                }
                writer.Write(mask);
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr type, IntPtr icon, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendTextTimeout(IntPtr window, uint message, IntPtr type, string text, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        private static readonly Dictionary<string, System.Drawing.Icon> Icons = new Dictionary<string, System.Drawing.Icon>();
        private static readonly Dictionary<IntPtr, string> AppliedIcons = new Dictionary<IntPtr, string>();
        public static void ApplyAvatar(Profile profile)
        {
            var processes = Instance.Processes(profile);
            try { ApplyAvatar(profile, processes); }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        internal static void ApplyAvatar(Profile profile, List<Process> processes)
        {
            if (processes.Count == 0) return;
            System.Drawing.Icon icon = null;
            var windows = WindowActivation.Find(processes.Where(process => process.ProcessName == "Zalo").Select(process => process.Id))
                .Where(WindowActivation.IsWindowVisible).ToArray();
            if (windows.Length == 0) return;
            string title = String.IsNullOrWhiteSpace(profile.Name) ? "Zalo" : "Zalo - " + profile.Name;
            string iconKey = profile.Id + "|" + profile.AvatarPath + "|shell-v3";
            string iconPath = MakeIcon(profile);
            if (!String.IsNullOrEmpty(profile.AvatarPath) && !Icons.TryGetValue(iconKey, out icon))
            {
                string prefix = profile.Id + "|";
                foreach (string oldKey in Icons.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                {
                    Icons[oldKey].Dispose();
                    Icons.Remove(oldKey);
                }
                icon = new System.Drawing.Icon(iconPath, 32, 32);
                Icons.Add(iconKey, icon);
            }
            foreach (IntPtr dead in AppliedIcons.Keys.Where(h => !IsWindow(h)).ToList())
                AppliedIcons.Remove(dead);
            foreach (var window in windows)
            {
                IntPtr result;
                if (!String.Equals(WindowActivation.Title(window), title, StringComparison.Ordinal))
                    SendTextTimeout(window, 0x000C, IntPtr.Zero, title, 2, 200, out result);
                string key = WindowActivation.OwnerProcessId(window) + "|" + iconKey + "|" + title;
                string applied;
                if (AppliedIcons.TryGetValue(window, out applied) && applied == key) continue;
                if (icon != null &&
                    (SendMessageTimeout(window, 0x80, IntPtr.Zero, icon.Handle, 2, 200, out result) == IntPtr.Zero ||
                     SendMessageTimeout(window, 0x80, new IntPtr(1), icon.Handle, 2, 200, out result) == IntPtr.Zero))
                    continue;
                ShellIdentity.Window(window, profile, iconPath);
                AppliedIcons[window] = key;
            }
        }

        private static void CleanupDirectory(string path)
        {
            if (String.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try { Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void DeleteAvatarFiles(string avatarPath)
        {
            if (String.IsNullOrEmpty(avatarPath)) return;
            try { if (File.Exists(avatarPath)) File.Delete(avatarPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                string iconPath = Path.Combine(Path.GetDirectoryName(avatarPath), Path.GetFileNameWithoutExtension(avatarPath) + ".shell-v2.ico");
                if (File.Exists(iconPath)) File.Delete(iconPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Export(Profile profile, string destination)
        {
            RequireStopped(profile);
            if (!Directory.Exists(profile.DataPath)) throw new DirectoryNotFoundException("Hồ sơ chưa có dữ liệu để xuất.");
            string staging = Path.Combine(Store.Root, "Transfers", Guid.NewGuid().ToString("N"));
            string payload = Path.Combine(staging, "data");
            try
            {
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
            }
            finally { CleanupDirectory(staging); }
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
            try
            {
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
            finally { CleanupDirectory(staging); }
        }

        public static string ArchiveDeleted(Profile profile)
        {
            RequireStopped(profile);
            string deleted = Path.Combine(Store.Root, "Deleted", profile.Id + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(deleted);
            string data = ArchiveSource(profile);
            if (Directory.Exists(data)) Directory.Move(data, Path.Combine(deleted, "data"));
            return deleted;
        }

        internal static string ArchiveSource(Profile profile)
        {
            return profile.UsesSandboxie ? SandboxieClone.BoxRoot(profile) : profile.DataPath;
        }
    }

    public partial class ManagerWindow
    {
        private void AddActionsColumn()
        {
            var panel = new FrameworkElementFactory(typeof(WrapPanel));
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var power = RowButton("▶ Mở", "Mở Zalo", "PrimaryButton");
            power.Name = "profilePower";
            power.SetValue(Button.MinWidthProperty, 62.0);
            panel.AppendChild(power);

            panel.AppendChild(RowButton("Data", "Mở thư mục Data", "RowActionButton"));

            var more = RowButton("\uE712", "…", "RowActionButton");
            more.SetValue(Button.ToolTipProperty, "Thao tác khác · Tạo shortcut");
            more.SetValue(Button.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            panel.AppendChild(more);

            var remove = RowButton("\uE74D", "Xóa", "DangerGhostButton");
            remove.SetValue(Button.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe MDL2 Assets"));
            remove.SetBinding(Button.IsEnabledProperty, new Binding("IsOriginal") { Converter = new CloneEnabledConverter() });
            panel.AppendChild(remove);

            var template = new DataTemplate { VisualTree = panel };
            var running = new DataTrigger { Binding = new Binding("Status"), Value = "Đang chạy" };
            running.Setters.Add(new Setter(Button.ContentProperty, "■ Dừng", power.Name));
            running.Setters.Add(new Setter(Button.TagProperty, "⏹ Dừng Zalo", power.Name));
            running.Setters.Add(new Setter(Button.ToolTipProperty, "Dừng Zalo", power.Name));
            running.Setters.Add(new Setter(Button.StyleProperty, new System.Windows.DynamicResourceExtension("DangerGhostButton"), power.Name));
            template.Triggers.Add(running);
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Action", Width = 570, CellTemplate = template });
        }

        private FrameworkElementFactory RowButton(string content, string action, string style, RoutedEventHandler handler = null)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.ContentProperty, content);
            button.SetValue(Button.TagProperty, action);
            button.SetValue(Button.ToolTipProperty, action);
            button.SetResourceReference(Button.StyleProperty, style);
            button.SetValue(Button.PaddingProperty, new Thickness(9, 6, 9, 6));
            button.SetValue(Button.FontSizeProperty, 12.0);
            button.SetValue(Button.MarginProperty, new Thickness(0, 0, 6, 0));
            button.SetValue(Button.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.AddHandler(Button.ClickEvent, handler ?? new RoutedEventHandler(RowAction));
            return button;
        }

        private async void RowAction(object sender, RoutedEventArgs args)
        {
            var control = (FrameworkElement)sender; var profile = control.DataContext as Profile;
            if (profile == null || busy) return;
            string action = (string)control.Tag;
            if (action == "…") { ShowProfileMenu((Button)control, profile); return; }
            SetBusy(true);
            try
            {
                if (action == "Mở Zalo")
                {
                    bool shown = await Task.Run(() => Instance.OpenAndCheckWindow(profile));
                    if (shown)
                    {
                        NoticeDialog.Success(this, "Đã mở Zalo", "Cửa sổ Zalo của hồ sơ đã sẵn sàng.", profile.Name);
                        WindowActivation.Restore(profile);
                    }
                    else NoticeDialog.Show(this, "Chưa thấy cửa sổ Zalo", "Đã gửi yêu cầu mở Zalo cho hồ sơ “" + profile.Name + "”. Có thể Zalo vẫn đang khởi động.");
                }
                else if (action == "⏹ Dừng Zalo")
                {
                    if (!ConfirmDialog.Ask(this, "Dừng Zalo", "Dừng toàn bộ tiến trình Zalo của ", profile.Name, "Thao tác có thể ngắt cuộc gọi và dữ liệu đang ghi.", "Dừng Zalo")) return;
                    await Task.Run(() => Instance.Stop(profile));
                    RefreshStatus();
                    NoticeDialog.Success(this, "Đã dừng Zalo", "Zalo đã dừng cho hồ sơ này.", profile.Name);
                }
                else if (action == "Mở thư mục Data") { Directory.CreateDirectory(profile.DataPath); Process.Start("explorer.exe", "\"" + profile.DataPath + "\""); }
                else if (action == "Tạo Shortcut")
                {
                    ProfileActions.CreateShortcut(profile);
                    SaveProfileChanges(profile);
                    NoticeDialog.Success(this, "Tạo shortcut thành công", "Lối tắt của hồ sơ đã được tạo.",
                        Path.GetFileName(profile.ShortcutPath), Path.GetDirectoryName(profile.ShortcutPath), profile.ShortcutPath);
                }
                else if (action == "Xóa")
                {
                    ProfileActions.RequireStopped(profile);
                    if (!ConfirmDialog.Ask(this, "Xóa hồ sơ Zalo", "Xóa hồ sơ ", profile.Name, "Dữ liệu được chuyển vào thư mục Deleted để có thể lấy lại.", "Xóa hồ sơ")) return;
                    string location = await Task.Run(() => ProfileActions.ArchiveDeleted(profile));
                    try { Store.Save(profiles.Where(item => item != profile)); }
                    catch { if (Directory.Exists(Path.Combine(location, "data"))) Directory.Move(Path.Combine(location, "data"), ProfileActions.ArchiveSource(profile)); throw; }
                    profiles.Remove(profile); status.Text = "Đã xóa hồ sơ. Dữ liệu giữ tại " + location;
                    if (!String.IsNullOrEmpty(profile.ShortcutPath) && File.Exists(profile.ShortcutPath)) File.Delete(profile.ShortcutPath);
                    RefreshStatus();
                    NoticeDialog.Success(this, "Đã xóa hồ sơ", "Dữ liệu đã được chuyển vào thư mục lưu hồ sơ đã xóa.");
                }
            }
            catch (Exception exception) { Report(exception); }
            finally { SetBusy(false); RefreshStatus(); }
        }

        private void ShowProfileMenu(Button button, Profile profile)
        {
            var menu = CreateProfileMenu(profile);
            button.ContextMenu = menu; menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = -12; menu.VerticalOffset = -6; menu.DataContext = profile; menu.IsOpen = true;
        }

        private ContextMenu CreateProfileMenu(Profile profile)
        {
            bool loggedIn = profile.IsLoggedIn;
            var menu = new ContextMenu();
            menu.Style = (Style)Resources["ModernContextMenu"];
            menu.Resources.MergedDictionaries.Add(Resources);
            var shortcut = new MenuItem { Header = "Tạo shortcut", Tag = "Tạo Shortcut", DataContext = profile, Icon = MenuGlyph("\uE71B"), IsEnabled = loggedIn };
            shortcut.Click += RowAction;
            menu.Items.Add(shortcut);
            if (!profile.IsOriginal)
            {
                var startup = new MenuItem { IsCheckable = true, IsEnabled = loggedIn };
                UpdateStartupMenuItem(startup, profile.StartWithWindows);
                startup.Click += delegate
                {
                    SetProfileStartup(profile, startup.IsChecked);
                    UpdateStartupMenuItem(startup, profile.StartWithWindows);
                };
                menu.Items.Add(startup);
            }
            menu.Items.Add(new Separator { Style = (Style)Resources["ModernMenuSeparator"] });
            menu.Items.Add(new MenuItem { Header = "TÙY CHỈNH HỒ SƠ", Style = (Style)Resources["ModernMenuGroupLabel"] });
            var rename = new MenuItem { Header = "Cập nhật tên hồ sơ…", IsEnabled = loggedIn };
            var manualName = new MenuItem { Header = "Tự đổi…" };
            manualName.Click += delegate
            {
                var dialog = new RenameProfileDialog(profile.Name, Resources) { Owner = this };
                if (dialog.ShowDialog() == true && RunProfileEdit(profile, () => profile.Name = dialog.NewProfileName))
                    NoticeDialog.Success(this, "Đã cập nhật tên hồ sơ", "Tên mới đã được lưu.", profile.Name);
            };
            rename.Items.Add(manualName);
            var zaloName = new MenuItem { Header = "Cập nhật theo tên Zalo hiện tại" };
            zaloName.Click += async delegate { await UpdateFromZalo(profile, false); };
            rename.Items.Add(zaloName); menu.Items.Add(rename);
            var avatar = new MenuItem { Header = "Đổi avatar / biểu tượng app…", IsEnabled = loggedIn };
            var manualAvatar = new MenuItem { Header = "Tự đổi…" };
            manualAvatar.Click += delegate
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp", Title = "Chọn avatar cho tài khoản" };
                if (dialog.ShowDialog(this) != true) return;
                string previousAvatar = profile.AvatarPath;
                if (RunProfileEdit(profile, delegate
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(dialog.FileName); image.EndInit(); image.Freeze();
                    string path = Path.Combine(Store.Root, "Avatars", profile.Id + "-" + Guid.NewGuid().ToString("N") + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(path)) encoder.Save(stream);
                    profile.AvatarPath = path;
                }))
                {
                    ProfileActions.DeleteAvatarFiles(previousAvatar);
                    NoticeDialog.Success(this, "Đã cập nhật avatar", "Ảnh đại diện mới đã được lưu cho hồ sơ.", profile.Name);
                }
            };
            avatar.Items.Add(manualAvatar);
            var zaloAvatar = new MenuItem { Header = "Cập nhật theo avatar Zalo hiện tại" };
            zaloAvatar.Click += async delegate { await UpdateFromZalo(profile, true); };
            avatar.Items.Add(zaloAvatar); menu.Items.Add(avatar);
            rename.Icon = MenuGlyph("\uE70F"); avatar.Icon = MenuGlyph("\uEB9F");
            if (profile.IsOriginal) return menu;
            var export = new MenuItem { Header = "Xuất data…", IsEnabled = loggedIn }; export.Click += async delegate { await Transfer(profile, false); }; menu.Items.Add(export);
            var import = new MenuItem { Header = "Import data…", IsEnabled = loggedIn }; import.Click += async delegate { await Transfer(profile, true); }; menu.Items.Add(import);
            var importNew = new MenuItem { Header = "Nhập thành hồ sơ mới…", IsEnabled = loggedIn }; importNew.Click += async delegate { await Transfer(profile, true, true); }; menu.Items.Add(importNew);
            export.Icon = MenuGlyph("\uE898"); import.Icon = MenuGlyph("\uE8B5");
            menu.Items.Insert(menu.Items.IndexOf(export), new Separator { Style = (Style)Resources["ModernMenuSeparator"] });
            menu.Items.Insert(menu.Items.IndexOf(export), new MenuItem { Header = "DỮ LIỆU HỒ SƠ", Style = (Style)Resources["ModernMenuGroupLabel"] });
            return menu;
        }
        private static TextBlock MenuGlyph(string glyph)
        { return new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center }; }

        private static void UpdateStartupMenuItem(MenuItem item, bool enabled, string label = "Tự mở khi đăng nhập Windows")
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var badge = new Border
            {
                Background = ModernBrush(enabled ? "#DCFCE7" : "#FEE2E2"),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = enabled ? "Bật" : "Tắt",
                    Foreground = ModernBrush(enabled ? "#166534" : "#991B1B"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);
            item.Header = header;
            item.IsChecked = enabled;
            System.Windows.Automation.AutomationProperties.SetName(item, label);
        }

        private void SetProfileStartup(Profile profile, bool enabled)
        {
            if (busy || !profiles.Contains(profile)) return;
            bool previous = profile.StartWithWindows;
            profile.StartWithWindows = enabled;
            try
            {
                Store.Save(profiles);
                status.Text = enabled ? "Đã bật tự mở “" + profile.Name + "” khi đăng nhập Windows."
                    : "Đã tắt tự mở “" + profile.Name + "” khi đăng nhập Windows.";
                if (enabled && !WindowsStartup.IsRegistered())
                    status.Text += " Bật Khởi động cùng hệ thống ở đầu cửa sổ để áp dụng.";
            }
            catch (Exception error)
            {
                profile.StartWithWindows = previous;
                Report(error);
            }
        }

        private void SaveProfileChanges(Profile profile)
        {
            if (profile.IsOriginal) Store.SaveOriginal(profile);
            else Store.Save(profiles);
        }

        private bool RunProfileEdit(Profile profile, Action edit)
        {
            try
            {
                edit();
                SaveProfileChanges(profile);
                if (!String.IsNullOrEmpty(profile.ShortcutPath) && File.Exists(profile.ShortcutPath))
                {
                    ProfileActions.CreateShortcut(profile);
                    SaveProfileChanges(profile);
                }
                else ProfileActions.RefreshShellShortcuts(profile, false);
                ProfileActions.ApplyAvatar(profile);
                RefreshStatus();
                return true;
            }
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
                    string previousAvatar = profile.AvatarPath;
                    string path = await Task.Run(() => IdentityReader.DownloadAvatar(profile, identity));
                    if (!RunProfileEdit(profile, () => profile.AvatarPath = path)) return;
                    ProfileActions.DeleteAvatarFiles(previousAvatar);
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(identity.Name)) throw new InvalidDataException("Zalo chưa có tên hiển thị trong cache của tài khoản này.");
                    if (!RunProfileEdit(profile, () => profile.Name = identity.Name)) return;
                }
                status.Text = avatar ? "Đã cập nhật avatar hồ sơ theo Zalo hiện tại." : "Đã cập nhật tên hồ sơ theo Zalo hiện tại.";
                NoticeDialog.Success(this, avatar ? "Đã cập nhật avatar" : "Đã cập nhật tên hồ sơ", status.Text, profile.Name);
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
                        RefreshStatus();
                        NoticeDialog.Success(this, "Đã nhập thành hồ sơ mới", status.Text, target.Name);
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
                if (import)
                {
                    profileStorageRefreshRequested = true;
                    RefreshStatus();
                    NoticeDialog.Success(this, "Đã nhập dữ liệu", status.Text, profile.Name);
                }
                else NoticeDialog.FileSaved(this, path, new FileInfo(path).Length, "Xuất dữ liệu thành công");
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
