using System;
using System.Collections.Generic;
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
        private Border RecallAttachmentBubble(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var attachment = message.Attachment;
            if (attachment.Kind == "contact") return RecallContactBubble(message, owner);
            bool picture = attachment.Kind == "image" || attachment.Kind == "sticker";
            var image = picture ? DecodeRecallMediaImage(attachment) : null;
            if (image != null) return RecallPictureBubble(message, owner, image);

            bool audio = attachment.Kind == "audio";
            bool video = IsRecallVideo(message);
            if (video) return RecallVideoBubble(message, owner);
            bool saved = !String.IsNullOrEmpty(attachment.Data);
            string fileName = RecallAttachmentName(message);
            string title = audio ? "Tin nhắn thoại" : picture ? (attachment.Kind == "sticker" ? "Sticker" : "Ảnh") : fileName;
            var row = new Grid { MinHeight = 58 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tile = new Border { Width = 50, Height = 50, CornerRadius = new CornerRadius(10),
                Background = audio && saved ? Brushes.Transparent : ModernBrush(video ? "#26384D" : "#EAF2FF"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0) };
            row.Children.Add(tile);
            if (video && !String.IsNullOrEmpty(attachment.PreviewData))
            {
                var thumbnail = DecodeRecallImageBytes(attachment.PreviewData);
                if (thumbnail != null) tile.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            }
            string playIcon = "M 4,2 L 14,8 L 4,14 Z";
            string fileIcon = "M 4,1 H 10 L 14,5 V 15 H 4 Z M 10,1 V 5 H 14 M 6,8 H 12 M 6,11 H 12";
            bool readable = saved && !video && attachment.Kind == "file"
                && String.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase);
            if (saved && (video || readable))
            {
                string action = video ? "Xem video" : "Đọc tệp";
                var button = RecallIconButton(action, video ? playIcon : fileIcon, video ? "#26384D" : "#EAF2FF",
                    video ? "#FFFFFF" : "#1265CB", video ? 30 : 40,
                    delegate { if (readable) ShowRecallText(message, owner); else PlayRecallVideo(message, owner); }, video);
                if (video) button.Opacity = 0.92;
                tile.Child = button;
                tile.Cursor = System.Windows.Input.Cursors.Hand;
                tile.MouseLeftButtonUp += delegate { if (readable) ShowRecallText(message, owner); else PlayRecallVideo(message, owner); };
            }
            else if (!audio || !saved)
                tile.Child = RecallIcon(audio || video ? playIcon : fileIcon, ModernBrush(video ? "#FFFFFF" : "#1265CB"), audio || video);

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            if (audio && saved) AddRecallAudioPlayer(tile, details, message);
            else
            {
                var titleBlock = new TextBlock { Text = title, ToolTip = title, TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = ModernBrush("#111827") };
                if (saved && (video || readable))
                {
                    titleBlock.Cursor = System.Windows.Input.Cursors.Hand;
                    titleBlock.MouseLeftButtonUp += delegate { if (readable) ShowRecallText(message, owner); else PlayRecallVideo(message, owner); };
                }
                details.Children.Add(titleBlock);
                string description = saved ? RecallMediaSize(attachment.Data) + " · Đã lưu" : "Hệ thống không tải kịp";
                details.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    Foreground = saved ? ModernBrush("#64748B") : ModernBrush("#DC2626"),
                    FontStyle = saved ? FontStyles.Normal : FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 0) });
            }

            var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            if (saved) actions.Children.Add(RecallDownloadButton(message, owner));
            var time = RecallTimestamp(message);
            time.HorizontalAlignment = HorizontalAlignment.Center;
            time.Margin = new Thickness(0, saved ? 4 : 0, 0, 0);
            actions.Children.Add(time);

            var bubble = RecallWhiteBubble(row, new Thickness(14, 10, 14, 10));
            if (audio)
            {
                bubble.MaxWidth = 420;
                bubble.HorizontalAlignment = HorizontalAlignment.Left;
            }
            return bubble;
        }

        private Border RecallVideoBubble(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var attachment = message.Attachment;
            bool saved = !String.IsNullOrEmpty(attachment.Data);
            string fileName = RecallAttachmentName(message);
            BitmapSource thumbnail = !String.IsNullOrEmpty(attachment.PreviewData) ? DecodeRecallImageBytes(attachment.PreviewData) : null;

            var stage = new Grid { Width = 260, Height = 170, HorizontalAlignment = HorizontalAlignment.Left };
            var videoSurface = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = ModernBrush("#334155"),
                Background = thumbnail != null ? (Brush)new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill } : ModernBrush("#1E293B")
            };
            stage.Children.Add(videoSurface);

            string playIcon = "M 4,2 L 14,8 L 4,14 Z";
            if (saved)
            {
                var playButton = RecallIconButton("Xem video", playIcon, "#CC0F172A", "#FFFFFF", 50,
                    delegate { PlayRecallVideo(message, owner); }, true);
                playButton.BorderBrush = ModernBrush("#64748B");
                playButton.BorderThickness = new Thickness(1.5);
                playButton.HorizontalAlignment = HorizontalAlignment.Center;
                playButton.VerticalAlignment = VerticalAlignment.Center;
                stage.Children.Add(playButton);

                stage.Cursor = System.Windows.Input.Cursors.Hand;
                stage.MouseLeftButtonUp += delegate(object sender, System.Windows.Input.MouseButtonEventArgs args)
                {
                    if (!args.Handled) PlayRecallVideo(message, owner);
                };

                string sizeLabel = RecallMediaSize(attachment.Data);
                var badge = new Border
                {
                    Background = ModernBrush("#CC0F172A"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = "▶ " + sizeLabel,
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold
                    }
                };
                stage.Children.Add(badge);

                var download = RecallDownloadButton(message, owner);
                download.HorizontalAlignment = HorizontalAlignment.Right;
                download.VerticalAlignment = VerticalAlignment.Bottom;
                download.Margin = new Thickness(8);
                stage.Children.Add(download);
            }
            else
            {
                var fallback = new Border
                {
                    Background = ModernBrush("#CC0F172A"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 6, 12, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Hệ thống không tải kịp",
                        Foreground = ModernBrush("#F87171"),
                        FontStyle = FontStyles.Italic,
                        FontSize = 12
                    }
                };
                stage.Children.Add(fallback);
            }

            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(stage);

            if (!String.IsNullOrWhiteSpace(fileName) && fileName != "video.mp4" && fileName != "Video")
            {
                var titleText = new TextBlock
                {
                    Text = fileName,
                    ToolTip = fileName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 250,
                    FontSize = 12,
                    Foreground = ModernBrush("#334155"),
                    Margin = new Thickness(4, 4, 0, 0)
                };
                if (saved)
                {
                    titleText.Cursor = System.Windows.Input.Cursors.Hand;
                    titleText.MouseLeftButtonUp += delegate { PlayRecallVideo(message, owner); };
                }
                body.Children.Add(titleText);
            }

            var time = RecallTimestamp(message);
            time.Margin = new Thickness(9, 0, 9, 0);
            body.Children.Add(new Border
            {
                Child = time,
                Background = ModernBrush("#D6DEE8"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0, 3, 0, 3),
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            });

            return new Border { Child = body, Margin = new Thickness(0, 0, 0, 14) };
        }

        private Border RecallPictureBubble(CdpMessageWatcher.RecalledMessage message, Window owner, BitmapSource image)
        {
            bool sticker = message.Attachment.Kind == "sticker";
            var stage = new Grid { Width = sticker ? 140 : 220, Height = sticker ? 120 : 250, HorizontalAlignment = HorizontalAlignment.Left };
            var picture = new Border { CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(sticker ? 0 : 1), BorderBrush = ModernBrush("#D6DFE8"),
                Background = new ImageBrush(image) { Stretch = sticker ? Stretch.Uniform : Stretch.UniformToFill } };
            var preview = new Button { Template = RecallButtonTemplate(14), Content = picture, ToolTip = "Phóng to",
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Cursor = System.Windows.Input.Cursors.Hand };
            System.Windows.Automation.AutomationProperties.SetName(preview, "Phóng to");
            preview.Click += delegate { ShowRecallImage(DecodeRecallMediaImage(message.Attachment, 0) ?? image, message, owner); };
            stage.Children.Add(preview);
            if (!String.IsNullOrEmpty(message.Attachment.Data))
            {
                var download = RecallDownloadButton(message, owner);
                download.HorizontalAlignment = HorizontalAlignment.Right;
                download.VerticalAlignment = VerticalAlignment.Bottom;
                download.Margin = new Thickness(8);
                stage.Children.Add(download);
            }

            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(stage);
            if (!String.IsNullOrWhiteSpace(message.Content) && message.Content != (sticker ? "Sticker" : "Ảnh"))
            {
                var caption = RecallWhiteBubble(RecallMessageText(message.Content), new Thickness(10, 6, 10, 6));
                caption.MaxWidth = 380;
                caption.Margin = new Thickness(0, 6, 0, 0);
                body.Children.Add(caption);
            }
            var time = RecallTimestamp(message);
            time.Margin = new Thickness(9, 0, 9, 0);
            body.Children.Add(new Border { Child = time, Background = ModernBrush("#D6DEE8"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            return new Border { Child = body, Margin = new Thickness(0, 0, 0, 14) };
        }

        private Border RecallContactBubble(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var attachment = message.Attachment;
            string title = message.Content.StartsWith("Danh thiếp: ") ? message.Content.Substring(12) : message.Content;
            if (String.IsNullOrWhiteSpace(title)) title = "Danh thiếp";

            var card = new Grid { MinWidth = 240, MaxWidth = 320 };
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var infoRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition());

            var avatarCircle = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
                Background = ModernBrush("#DBEAFE"), Margin = new Thickness(0, 0, 12, 0),
                ClipToBounds = true
            };
            BitmapSource avatarImg = DecodeRecallMediaImage(attachment);
            if (avatarImg != null)
            {
                avatarCircle.Child = new Image { Source = avatarImg, Stretch = Stretch.UniformToFill };
            }
            else
            {
                avatarCircle.Child = new TextBlock
                {
                    Text = "👤", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            infoRow.Children.Add(avatarCircle);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = title, FontWeight = FontWeights.Bold, FontSize = 15,
                Foreground = ModernBrush("#0F172A"), TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = "Danh thiếp Zalo", FontSize = 12, Foreground = ModernBrush("#0284C7"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            infoRow.Children.Add(textStack);
            card.Children.Add(infoRow);

            var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var badge = new Border
            {
                Background = ModernBrush("#F1F5F9"), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = "📇 Danh thiếp", FontSize = 11, Foreground = ModernBrush("#475569") }
            };
            footer.Children.Add(badge);
            var time = RecallTimestamp(message);
            DockPanel.SetDock(time, Dock.Right);
            footer.Children.Add(time);
            Grid.SetRow(footer, 1);
            card.Children.Add(footer);

            return RecallWhiteBubble(card, new Thickness(14, 12, 14, 10));
        }

        private static Border RecallWhiteBubble(UIElement body, Thickness padding)
        {
            return new Border { Child = body, Margin = new Thickness(0, 0, 0, 14), Padding = padding,
                Background = Brushes.White, BorderBrush = ModernBrush("#D6DFE8"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14) };
        }

        private static TextBox RecallMessageText(string text)
        {
            return new TextBox { Text = FormatZaloEmoticons(text), IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI, Arial") };
        }

        private static string FormatZaloEmoticons(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            return text
                .Replace("8*)", "😎")
                .Replace(";-x", "🤐")
                .Replace(";-X", "🤐")
                .Replace(":;", "😅")
                .Replace("/-strong", "💪")
                .Replace("/-heart", "❤️")
                .Replace("/-break", "💔")
                .Replace("/-rose", "🌹")
                .Replace("/-fade", "🥀")
                .Replace("/-li", "👍")
                .Replace("/-share", "📢")
                .Replace("/-thanks", "🙏")
                .Replace(":>", "😏")
                .Replace(":o", "😮")
                .Replace(":O", "😮")
                .Replace(":-(", "😢")
                .Replace(":-((", "😭")
                .Replace(":-h", "👋")
                .Replace(":-H", "👋")
                .Replace(":-*", "😘")
                .Replace(":-d", "😀")
                .Replace(":-D", "😀")
                .Replace(":-p", "😛")
                .Replace(":-P", "😛")
                .Replace(";)", "😉")
                .Replace(":-)", "🙂");
        }

        private static TextBlock RecallTimestamp(CdpMessageWatcher.RecalledMessage message)
        {
            return new TextBlock { Text = message.DetectedAt.ToLocalTime().ToString("HH:mm"), FontSize = 11,
                Foreground = ModernBrush("#64748B") };
        }

        private static string RecallMediaSize(string data)
        {
            long bytes = CdpMessageWatcher.RecallMediaBytes(data);
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.##") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            return bytes + " B";
        }

        private static Button RecallDownloadButton(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var attachment = message.Attachment;
            string label = attachment.Kind == "image" || attachment.Kind == "sticker"
                ? (attachment.FromSource ? "Lưu ảnh…" : "Lưu bản xem trước…") : attachment.Kind == "audio" ? "Lưu ghi âm…" : "Tải về…";
            var button = RecallIconButton(label, "M 8,1 V 11 M 4,7 L 8,11 L 12,7 M 2,12 V 15 H 14 V 12", "#F8FAFC", "#334155", 32,
                delegate { SaveRecallMedia(message, owner); });
            button.BorderBrush = ModernBrush("#CBD5E1");
            button.BorderThickness = new Thickness(1);
            return button;
        }

        private static Button RecallIconButton(string label, string icon, string background, string foreground, double size,
            RoutedEventHandler click, bool filled = false)
        {
            var button = new Button { Width = size, Height = size, Template = RecallButtonTemplate(size / 2),
                Background = ModernBrush(background), Content = RecallIcon(icon, ModernBrush(foreground), filled), ToolTip = label,
                BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            if (click != null) button.Click += click;
            return button;
        }

        private static FrameworkElement RecallIcon(string icon, Brush color, bool filled)
        {
            return new System.Windows.Shapes.Path { Data = Geometry.Parse(icon), Fill = filled ? color : null,
                Stroke = filled ? null : color, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 14, Height = 14,
                Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }

        private static ControlTemplate RecallButtonTemplate(double radius)
        {
            var surface = new FrameworkElementFactory(typeof(Border), "surface");
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            surface.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
            foreach (var state in new[] { UIElement.IsMouseOverProperty, Button.IsPressedProperty, UIElement.IsEnabledProperty })
            {
                var trigger = new Trigger { Property = state, Value = state != UIElement.IsEnabledProperty };
                double opacity = state == UIElement.IsEnabledProperty ? 0.45 : state == Button.IsPressedProperty ? 0.70 : 0.86;
                trigger.Setters.Add(new Setter(UIElement.OpacityProperty, opacity, "surface"));
                template.Triggers.Add(trigger);
            }
            return template;
        }

        private static string RecallCaptureStatus(string status)
        {
            switch (status)
            {
                case "no-source": return "Manager chưa tìm được nguồn tải của tin này.";
                case "pending": return "Manager chưa lưu xong nội dung đính kèm.";
                case "redirect-limit": return "Nguồn tải chuyển hướng quá nhiều lần.";
                case "redirect-blocked": return "Nguồn tải chuyển hướng đến địa chỉ Manager không hỗ trợ.";
                case "redirect-policy": return "Nguồn tải chuyển hướng đến địa chỉ Manager không hỗ trợ.";
                case "redirect-missing-location": return "Máy chủ không cung cấp địa chỉ chuyển hướng tải tệp.";
                case "redirect-invalid-location": return "Máy chủ cung cấp địa chỉ chuyển hướng tải tệp không hợp lệ.";
                case "local-missing": return "Tệp đã được dọn khỏi bộ nhớ đệm.";
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

        private static BitmapSource DecodeRecallMediaImage(CdpMessageWatcher.RecallAttachment attachment, int width = 440)
        {
            if (width > 0)
                return DecodeRecallImageBytes(attachment.PreviewData, width) ?? DecodeRecallImageBytes(attachment.Data, width);
            return DecodeRecallImageBytes(attachment.Data, 0) ?? DecodeRecallImageBytes(attachment.PreviewData, 0);
        }

        private const long ImageCacheBudget = 24 * 1024 * 1024;
        private static readonly object ImageCacheGate = new object();
        private static readonly Dictionary<string, BitmapSource> ImageCache = new Dictionary<string, BitmapSource>();
        private static readonly Queue<string> ImageCacheOrder = new Queue<string>();
        private static long imageCacheBytes;

        private static long BitmapBytes(BitmapSource image)
        {
            return (long)image.PixelWidth * image.PixelHeight * Math.Max(4, (image.Format.BitsPerPixel + 7) / 8);
        }

        private static BitmapSource DecodeRecallImageBytes(string data, int width = 440)
        {
            if (String.IsNullOrEmpty(data)) return null;
            try
            {
                string key = width + ":" + data;
                BitmapSource cached;
                lock (ImageCacheGate)
                    if (width > 0 && ImageCache.TryGetValue(key, out cached)) return cached;

                using (var stream = data.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                    ? (Stream)File.OpenRead(data.Substring("local:".Length))
                    : new MemoryStream(Convert.FromBase64String(data), false))
                {
                    var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames[0];
                    int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
                    if (width == 0 && (long)frame.PixelWidth * frame.PixelHeight > 40000000) width = 2048;
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    if (width > 0 && longest > width)
                    {
                        if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = width;
                        else bitmap.DecodePixelHeight = width;
                    }
                    bitmap.EndInit();
                    bitmap.Freeze();
                    long bytes = BitmapBytes(bitmap) + key.Length * sizeof(char);
                    if (width > 0 && bytes <= ImageCacheBudget)
                    {
                        lock (ImageCacheGate)
                        {
                            if (ImageCache.TryGetValue(key, out cached)) return cached;
                            while (imageCacheBytes + bytes > ImageCacheBudget && ImageCacheOrder.Count > 0)
                            {
                                string oldest = ImageCacheOrder.Dequeue();
                                imageCacheBytes -= BitmapBytes(ImageCache[oldest]) + oldest.Length * sizeof(char);
                                ImageCache.Remove(oldest);
                            }
                            ImageCache.Add(key, bitmap);
                            ImageCacheOrder.Enqueue(key);
                            imageCacheBytes += bytes;
                        }
                    }
                    return bitmap;
                }
            }
            catch (FormatException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static void SaveRecallMedia(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = RecallAttachmentName(message), Filter = "Tất cả tệp|*.*" };
            if (dialog.ShowDialog(owner) != true) return;
            long savedLength = 0;
            try
            {
                if (message.Attachment != null && !String.IsNullOrEmpty(message.Attachment.Data)
                    && message.Attachment.Data.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                {
                    string localPath = message.Attachment.Data.Substring("local:".Length);
                    File.Copy(localPath, dialog.FileName, true);
                    savedLength = new FileInfo(dialog.FileName).Length;
                }
                else
                {
                    byte[] bytes = Convert.FromBase64String(message.Attachment.Data);
                    File.WriteAllBytes(dialog.FileName, bytes);
                    savedLength = bytes.LongLength;
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is FormatException)) throw;
                NoticeDialog.Show(owner, "Không lưu được bản sao", error.Message);
                return;
            }
            NoticeDialog.FileSaved(owner, dialog.FileName, savedLength);
        }

        private static bool IsRecallVideo(CdpMessageWatcher.RecalledMessage message)
        {
            if (message == null || message.Attachment == null) return false;
            if (message.Attachment.Kind == "video") return true;
            if ((message.Attachment.Mime ?? "").StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return true;
            if (IsVideoExtension(message.Attachment.FileName)) return true;
            if (IsVideoExtension(message.Content)) return true;
            return false;
        }

        private static bool IsVideoExtension(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string ext = Path.GetExtension(path.Trim()).ToLowerInvariant();
                return ext == ".mp4" || ext == ".m4v" || ext == ".webm" || ext == ".mov"
                    || ext == ".avi" || ext == ".mkv" || ext == ".flv" || ext == ".wmv"
                    || ext == ".3gp" || ext == ".ts";
            }
            catch { return false; }
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
