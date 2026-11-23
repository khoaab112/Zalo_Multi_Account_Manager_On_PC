using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private void AddRecallMedia(StackPanel body, CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var attachment = message.Attachment;
            if (attachment == null || attachment.Kind == "emoji") return;
            bool picture = attachment.Kind == "image" || attachment.Kind == "sticker";
            var image = picture ? DecodeRecallMediaImage(attachment) : null;
            if (image != null)
            {
                body.Children.Add(new Image
                {
                    Source = image, MaxWidth = 320, MaxHeight = 260, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0)
                });
            }

            if (!String.IsNullOrEmpty(attachment.Data))
            {
                var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (image != null)
                    actions.Children.Add(RecallMediaButton("Phóng to", delegate { ShowRecallImage(image, message, owner); }));
                else if (attachment.Kind == "audio" || IsRecallVideo(message))
                    actions.Children.Add(RecallMediaButton(attachment.Kind == "audio" ? "Nghe" : "Xem video",
                        delegate { PlayRecallMedia(message, owner); }));
                else if (attachment.Kind == "file" && String.Equals(Path.GetExtension(RecallAttachmentName(message)), ".txt", StringComparison.OrdinalIgnoreCase))
                    actions.Children.Add(RecallMediaButton("Đọc tệp", delegate { ShowRecallText(message, owner); }));
                actions.Children.Add(RecallMediaButton(picture ? (attachment.FromSource ? "Lưu ảnh…" : "Lưu bản xem trước…")
                    : attachment.Kind == "audio" ? "Lưu ghi âm…" : "Tải về…",
                    delegate { SaveRecallMedia(message, owner); }));
                body.Children.Add(actions);
                return;
            }

            body.Children.Add(new TextBlock
            {
                Text = RecallCaptureStatus(attachment.CaptureStatus),
                FontSize = 11, Foreground = ModernBrush("#64748B"), Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        private static string RecallCaptureStatus(string status)
        {
            switch (status)
            {
                case "no-source": return "Manager chưa tìm được nguồn tải của tin này.";
                case "pending": return "Manager chưa lưu xong nội dung đính kèm.";
                case "redirect-limit": return "Nguồn tải chuyển hướng quá nhiều lần.";
                case "redirect-blocked": return "Nguồn tải chuyển hướng đến địa chỉ Manager không hỗ trợ.";
                case "http-401":
                case "http-403": return "Máy chủ từ chối quyền tải nội dung đính kèm.";
                case "too-large": return "Tệp vượt giới hạn lưu 16 MiB.";
                case "wrong-type": return "Nguồn tải không trả về đúng nội dung đính kèm.";
                case "checksum-mismatch": return "Bản tải về không khớp thông tin kiểm tra của tệp gốc.";
                case "incomplete": return "Dữ liệu tải về bị thiếu.";
                case "secure-channel": return "Không thiết lập được kết nối HTTPS với nguồn tải.";
                case "network-timeout": return "Quá thời gian tải nội dung đính kèm.";
                case "network":
                case "interrupted": return "Kết nối tải nội dung bị gián đoạn.";
                default:
                    int code;
                    if (status != null && status.StartsWith("http-", StringComparison.Ordinal)
                        && Int32.TryParse(status.Substring(5), out code))
                        return "Nguồn tải trả về HTTP " + code + ".";
                    return "Chưa có bản sao nội dung đính kèm.";
            }
        }

        private Button RecallMediaButton(string label, RoutedEventHandler click)
        {
            var button = new Button { Content = label, Style = (Style)Resources["RowActionButton"], Margin = new Thickness(0, 0, 6, 4) };
            button.Click += click;
            return button;
        }

        private static Window RecallMediaWindow(Window owner, string title, double height)
        {
            return new Window
            {
                Owner = owner, Title = title, Width = 760, Height = height, MinWidth = 340, MinHeight = 190,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = ModernBrush("#F3F6FA")
            };
        }

        private static string ReadRecallText(string data)
        {
            const int limit = 256 * 1024;
            if (data.Length > ((limit + 2) / 3) * 4) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(data);
                if (bytes.Length > limit) return null;
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    return reader.ReadToEnd();
            }
            catch (FormatException) { return null; }
            catch (DecoderFallbackException) { return null; }
        }

        private static void ShowRecallText(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            string text = ReadRecallText(message.Attachment.Data);
            if (text == null)
            {
                NoticeDialog.Show(owner, "Không đọc được bản xem trước", "Hãy dùng Tải về để mở tệp bằng trình soạn thảo phù hợp.");
                return;
            }
            var viewer = RecallMediaWindow(owner, RecallAttachmentName(message), 460);
            viewer.Content = new TextBox
            {
                Text = text, IsReadOnly = true, Margin = new Thickness(16), Padding = new Thickness(12),
                FontFamily = new FontFamily("Consolas"), FontSize = 14, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            viewer.ShowDialog();
        }

        private void ShowRecallImage(BitmapSource source, CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            CreateRecallImageViewer(source, message, owner).ShowDialog();
        }

        private Window CreateRecallImageViewer(BitmapSource source, CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var viewer = RecallMediaWindow(owner, message.Content, 540);
            viewer.WindowStyle = WindowStyle.None;
            viewer.ResizeMode = ResizeMode.NoResize;
            viewer.WindowState = WindowState.Maximized;
            viewer.Background = ModernBrush("#111827");
            viewer.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs args)
            {
                if (args.Key != System.Windows.Input.Key.Escape) return;
                args.Handled = true;
                viewer.Close();
            };

            var toolbar = new DockPanel { Background = ModernBrush("#F3F6FA"), LastChildFill = false };
            var close = RecallMediaButton("Đóng (Esc)", delegate { viewer.Close(); });
            close.Margin = new Thickness(8, 8, 12, 8);
            DockPanel.SetDock(close, Dock.Right);
            toolbar.Children.Add(close);

            var controls = new WrapPanel { Margin = new Thickness(12, 8, 8, 4), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(controls, Dock.Left);
            toolbar.Children.Add(controls);
            var zoom = new Slider { Minimum = 0.01, Maximum = 4, Value = 1, Width = 145, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(zoom, "Mức phóng to");
            var label = new TextBlock { Margin = new Thickness(9, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center };
            var image = new Image { Source = source, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var stage = new Grid();
            stage.Children.Add(image);
            var scroll = new ScrollViewer { Content = stage, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) };
            stage.SetBinding(FrameworkElement.MinWidthProperty, new System.Windows.Data.Binding("ViewportWidth") { Source = scroll });
            stage.SetBinding(FrameworkElement.MinHeightProperty, new System.Windows.Data.Binding("ViewportHeight") { Source = scroll });

            bool fitting = true;
            bool changingFit = false;
            Action update = delegate
            {
                image.Width = source.PixelWidth * zoom.Value;
                image.Height = source.PixelHeight * zoom.Value;
                label.Text = Math.Round(zoom.Value * 100) + "%";
            };
            Action fit = delegate
            {
                if (scroll.ActualWidth <= 0 || scroll.ActualHeight <= 0) return;
                double scale = Math.Min(scroll.ActualWidth / source.PixelWidth, scroll.ActualHeight / source.PixelHeight);
                changingFit = true;
                try
                {
                    zoom.Maximum = Math.Max(4, scale * 2);
                    zoom.Value = Math.Max(zoom.Minimum, scale);
                    scroll.ScrollToHome();
                    scroll.ScrollToLeftEnd();
                }
                finally { changingFit = false; }
            };

            zoom.ValueChanged += delegate
            {
                if (!changingFit) fitting = false;
                update();
            };
            scroll.SizeChanged += delegate { if (fitting) fit(); };
            viewer.ContentRendered += delegate { if (fitting) fit(); };
            update();

            controls.Children.Add(zoom);
            controls.Children.Add(label);
            controls.Children.Add(RecallMediaButton("Vừa màn hình", delegate { fitting = true; fit(); }));
            controls.Children.Add(RecallMediaButton(message.Attachment.FromSource ? "Lưu ảnh…" : "Lưu bản xem trước…",
                delegate { SaveRecallMedia(message, viewer); }));
            controls.Children.Add(new TextBlock
            {
                Text = source.PixelWidth + " × " + source.PixelHeight + " px" + (message.Attachment.FromSource ? "" : " · Bản xem trước"),
                Margin = new Thickness(6, 0, 0, 0), Foreground = ModernBrush("#475569"), VerticalAlignment = VerticalAlignment.Center
            });

            var layout = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);
            layout.Children.Add(scroll);
            viewer.Content = layout;
            return viewer;
        }

        private void PlayRecallMedia(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            string extension = Path.GetExtension(RecallAttachmentName(message));
            string path = Path.Combine(Path.GetTempPath(), "zalo-recall-" + Guid.NewGuid().ToString("N") + extension);
            MediaElement player = null;
            try
            {
                File.WriteAllBytes(path, Convert.FromBase64String(message.Attachment.Data));
                var viewer = RecallMediaWindow(owner, message.Content, IsRecallVideo(message) ? 510 : 200);
                player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close,
                    Stretch = Stretch.Uniform, Margin = new Thickness(12, 0, 12, 12) };
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 8) };
                player.MediaFailed += delegate { status.Text = "Windows không phát được định dạng này. Bạn có thể lưu tệp để mở bằng trình phát khác."; };
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
                actions.Children.Add(RecallMediaButton("Phát", delegate { player.Play(); }));
                actions.Children.Add(RecallMediaButton("Tạm dừng", delegate { player.Pause(); }));
                actions.Children.Add(RecallMediaButton("Từ đầu", delegate { player.Stop(); player.Play(); }));
                actions.Children.Add(RecallMediaButton("Tải về…", delegate { SaveRecallMedia(message, viewer); }));
                var layout = new DockPanel();
                DockPanel.SetDock(actions, Dock.Top);
                DockPanel.SetDock(status, Dock.Top);
                layout.Children.Add(actions);
                layout.Children.Add(status);
                layout.Children.Add(player);
                viewer.Content = layout;
                viewer.Loaded += delegate { player.Source = new Uri(path); player.Play(); };
                viewer.ShowDialog();
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is FormatException)) throw;
                NoticeDialog.Show(owner, "Không mở được nội dung", error.Message);
            }
            finally
            {
                if (player != null) player.Close();
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static BitmapSource DecodeRecallMediaImage(CdpMessageWatcher.RecallAttachment attachment)
        {
            return DecodeRecallImageBytes(attachment.Data) ?? DecodeRecallImageBytes(attachment.PreviewData);
        }

        private static BitmapSource DecodeRecallImageBytes(string data)
        {
            if (String.IsNullOrEmpty(data)) return null;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(data), false))
                {
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames[0];
                    if ((long)frame.PixelWidth * frame.PixelHeight > 40000000) return null;
                    var pixels = new WriteableBitmap(frame);
                    pixels.Freeze();
                    return pixels;
                }
            }
            catch (FormatException) { return null; }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static void SaveRecallMedia(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = RecallAttachmentName(message), Filter = "Tất cả tệp|*.*" };
            if (dialog.ShowDialog(owner) != true) return;
            try { File.WriteAllBytes(dialog.FileName, Convert.FromBase64String(message.Attachment.Data)); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is FormatException)) throw;
                NoticeDialog.Show(owner, "Không lưu được bản sao", error.Message);
            }
        }

        private static bool IsRecallVideo(CdpMessageWatcher.RecalledMessage message)
        {
            return message.Attachment.Kind == "video"
                || (message.Attachment.Kind == "file" && (message.Attachment.Mime ?? "").StartsWith("video/", StringComparison.OrdinalIgnoreCase));
        }

        private static string RecallAttachmentName(CdpMessageWatcher.RecalledMessage message)
        {
            var attachment = message.Attachment;
            string mime = (attachment.Mime ?? "").Split(';')[0].Trim().ToLowerInvariant();
            if (!String.IsNullOrWhiteSpace(attachment.FileName)) return Path.GetFileName(attachment.FileName);
            if (attachment.Kind == "image" || attachment.Kind == "sticker")
            {
                string extension = mime == "image/jpeg" ? ".jpg" : mime == "image/webp" ? ".webp" : mime == "image/gif" ? ".gif" : ".png";
                return (attachment.Kind == "sticker" ? "sticker" : "anh-thu-hoi") + extension;
            }
            if (attachment.Kind == "audio")
            {
                switch (mime)
                {
                    case "audio/mpeg": return "ghi-am.mp3";
                    case "audio/mp4": return "ghi-am.m4a";
                    case "audio/ogg": return "ghi-am.ogg";
                    case "audio/wav":
                    case "audio/x-wav": return "ghi-am.wav";
                    case "audio/webm": return "ghi-am.webm";
                    case "audio/aac":
                    case "audio/aacp":
                    case "audio/x-aac": return "ghi-am.aac";
                    default: return "ghi-am.bin";
                }
            }

            string safeName = String.Join("_", message.Content.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
            if (safeName.Length > 120) safeName = safeName.Substring(0, 120).TrimEnd('.');
            if (!String.IsNullOrWhiteSpace(safeName) && Path.HasExtension(safeName)) return safeName;
            if (IsRecallVideo(message))
            {
                switch (mime)
                {
                    case "video/mp4": return "video.mp4";
                    case "video/webm": return "video.webm";
                    case "video/quicktime": return "video.mov";
                    default: return "video.bin";
                }
            }
            return String.IsNullOrWhiteSpace(safeName) || safeName == "Tệp đính kèm" ? "tep-dinh-kem.bin" : safeName;
        }
    }
}
