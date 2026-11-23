using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private void ShowRecalledMessages(object sender, RoutedEventArgs args)
        {
            var profile = ((Button)sender).DataContext as Profile;
            if (profile != null) CreateRecallWindow(profile).ShowDialog();
        }

        private Window CreateRecallWindow(Profile profile)
        {
            var dialog = new Window
            {
                Title = "Tin đã thu hồi · " + profile.Name, Owner = this, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 760, Height = 570, MinWidth = 410, MinHeight = 330,
                Background = ModernBrush("#F3F6FA"), Foreground = ModernBrush("#0F172A"),
                FontFamily = FontFamily, FontSize = 14
            };
            var root = new DockPanel();
            var header = new DockPanel { Margin = new Thickness(18, 13, 18, 14), LastChildFill = true };
            var heading = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 19, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            var back = new Button { Content = "‹  Quay lại", Template = RecallButtonTemplate(19), MinWidth = 102, Height = 38,
                Background = ModernBrush("#EFF6FF"), BorderBrush = ModernBrush("#BFDBFE"), BorderThickness = new Thickness(1),
                Foreground = ModernBrush("#075BC6"), FontWeight = FontWeights.SemiBold, FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed };
            var headerDivider = new Border { Width = 1, Height = 36, Background = ModernBrush("#E2E8F0"),
                Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var headerAvatar = new ContentControl { Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var deleteAll = new Button { Content = "Xóa tất cả", Style = (Style)Resources["DangerGhostButton"],
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(deleteAll, Dock.Right);
            header.Children.Add(deleteAll);
            header.Children.Add(back);
            header.Children.Add(headerDivider);
            header.Children.Add(headerAvatar);
            var subtitle = new TextBlock { Foreground = ModernBrush("#64748B"), FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            var usageText = new TextBlock { FontWeight = FontWeights.SemiBold };
            var usageBadge = Badge(usageText, "#E6EDF6", "#334155");
            usageBadge.Margin = new Thickness(12, 0, 0, 0);
            usageBadge.VerticalAlignment = VerticalAlignment.Center;
            usageBadge.ToolTip = "Dung lượng nội dung tin thu hồi của hồ sơ này, gồm văn bản, tệp đính kèm và bản xem trước. Kho dùng chung có thêm phần mã hóa và dữ liệu quản lý.";
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition());
            titleRow.Children.Add(heading);
            Grid.SetColumn(usageBadge, 1);
            titleRow.Children.Add(usageBadge);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(titleRow);
            titles.Children.Add(subtitle);
            header.Children.Add(titles);
            var headerFrame = new Border { Child = header };
            Action fitDetailHeader = delegate
            {
                bool compact = headerFrame.ActualWidth > 0 && headerFrame.ActualWidth < 500;
                header.Margin = compact ? new Thickness(14, 13, 14, 14) : new Thickness(20, 13, 20, 14);
                back.MinWidth = compact ? 80 : 102;
                headerDivider.Margin = compact ? new Thickness(8, 0, 8, 0) : new Thickness(14, 0, 14, 0);
                headerAvatar.Margin = new Thickness(0, 0, compact ? 8 : 12, 0);
                deleteAll.MinWidth = compact ? 86 : 104;
            };
            headerFrame.SizeChanged += delegate { if (back.Visibility == Visibility.Visible) fitDetailHeader(); };
            DockPanel.SetDock(headerFrame, Dock.Top);
            root.Children.Add(headerFrame);
            var storageStatus = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap,
                Foreground = ModernBrush("#B91C1C"), Margin = new Thickness(18, 0, 18, 12) };
            DockPanel.SetDock(storageStatus, Dock.Bottom);
            root.Children.Add(storageStatus);

            var items = new StackPanel { Margin = new Thickness(18, 2, 18, 14) };
            var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            root.Children.Add(scroll);
            dialog.Content = root;

            CdpMessageWatcher.RecallConversation selected = null;
            int renderedRevision = -1;
            var displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
            var cardTemplate = RecallCardTemplate();
            Action refresh = null;
            refresh = delegate
            {
                int revision = CdpMessageWatcher.RecallRevision;
                if (renderedRevision == revision) return;
                renderedRevision = revision;

                if (selected != null)
                {
                    var messages = CdpMessageWatcher.ReadRecallConversation(profile.Id, selected.Id)
                        .OrderBy(message => message.DetectedAt).ToArray();
                    deleteAll.IsEnabled = messages.Length > 0;
                    if (!messages.SequenceEqual(displayedMessages) || items.Children.Count == 0)
                    {
                        double offset = scroll.VerticalOffset;
                        bool scrollToEnd = messages.Length > displayedMessages.Length;
                        int start = 0;
                        if (items.Children.Count > 0 && displayedMessages.Length > 0 && scrollToEnd
                            && messages.Take(displayedMessages.Length).SequenceEqual(displayedMessages))
                            start = displayedMessages.Length;
                        else items.Children.Clear();

                        DateTime? day = start > 0 ? (DateTime?)messages[start - 1].DetectedAt.LocalDateTime.Date : null;
                        for (int index = start; index < messages.Length; index++)
                        {
                            var message = messages[index];
                            DateTime messageDay = message.DetectedAt.LocalDateTime.Date;
                            if (day != messageDay)
                            {
                                items.Children.Add(RecallDayDivider(messageDay));
                                day = messageDay;
                            }
                            items.Children.Add(RecallBubble(message, dialog));
                        }
                        if (messages.Length == 0) items.Children.Add(RecallEmpty("Cuộc trò chuyện này không còn tin thu hồi."));
                        if (scrollToEnd) scroll.ScrollToEnd();
                        else scroll.ScrollToVerticalOffset(offset);
                        displayedMessages = messages;
                    }
                    storageStatus.Text = CdpMessageWatcher.RecallStorageError;
                    storageStatus.Visibility = String.IsNullOrEmpty(storageStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                heading.Text = "Tin đã thu hồi";
                long? contentBytes = CdpMessageWatcher.GetRecallContentBytes(profile.Id);
                usageText.Text = "Đã lưu: " + (contentBytes.HasValue ? StorageMeasurement.Format(contentBytes.Value) : "—");
                items.Children.Clear();
                var conversations = CdpMessageWatcher.GetRecallConversations(profile.Id);
                deleteAll.IsEnabled = conversations.Length > 0;
                storageStatus.Text = CdpMessageWatcher.RecallStorageError;
                storageStatus.Visibility = String.IsNullOrEmpty(storageStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                foreach (var conversation in conversations)
                {
                    var card = new Button
                    {
                        Content = RecallConversationCard(conversation), Template = cardTemplate,
                        Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(11),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    System.Windows.Automation.AutomationProperties.SetName(card, conversation.Name + ", " + conversation.Unread + " tin chưa đọc");
                    card.Click += delegate
                    {
                        selected = conversation;
                        displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                        items.Children.Clear();
                        heading.Text = selected.Name;
                        heading.ToolTip = selected.Name;
                        usageBadge.Visibility = Visibility.Collapsed;
                        Grid.SetColumnSpan(heading, titleRow.ColumnDefinitions.Count);
                        subtitle.Text = "Tin thu hồi · " + profile.Name;
                        subtitle.Visibility = Visibility.Visible;
                        headerFrame.Background = Brushes.White;
                        headerFrame.BorderBrush = ModernBrush("#E2E8F0");
                        headerFrame.BorderThickness = new Thickness(0, 0, 0, 1);
                        deleteAll.Template = RecallButtonTemplate(19);
                        deleteAll.Height = 38;
                        fitDetailHeader();
                        scroll.Background = ModernBrush("#E6ECF2");
                        items.Margin = new Thickness(24, 18, 24, 16);
                        items.MaxWidth = 560;
                        headerAvatar.Content = RecallAvatar(selected, 44);
                        headerAvatar.Visibility = Visibility.Visible;
                        headerDivider.Visibility = Visibility.Visible;
                        back.Visibility = Visibility.Visible;
                        renderedRevision = -1;
                        refresh();
                    };
                    items.Children.Add(card);
                }
                if (conversations.Length == 0)
                    items.Children.Add(RecallEmpty("Chưa có tin thu hồi."));
            };
            deleteAll.Click += delegate
            {
                var conversation = selected;
                if (!ConfirmDialog.AskRecallDeletion(dialog, profile.Name, conversation == null ? null : conversation.Name)) return;
                bool saved = conversation == null
                    ? CdpMessageWatcher.DeleteAllRecalls(profile.Id)
                    : CdpMessageWatcher.DeleteRecallConversation(profile.Id, conversation.Id);
                if (!saved) NoticeDialog.Show(dialog, "Không xóa được tin", CdpMessageWatcher.RecallStorageError);
                refresh();
                if (saved) NoticeDialog.Success(dialog, "Đã xóa tin thu hồi", "Các tin thu hồi đã được xóa khỏi máy.",
                    conversation == null ? profile.Name : conversation.Name);
            };
            back.Click += delegate
            {
                selected = null;
                back.Visibility = Visibility.Collapsed;
                headerDivider.Visibility = Visibility.Collapsed;
                headerAvatar.Visibility = Visibility.Collapsed;
                headerAvatar.Content = null;
                subtitle.Visibility = Visibility.Collapsed;
                heading.ToolTip = null;
                Grid.SetColumnSpan(heading, 1);
                usageBadge.Visibility = Visibility.Visible;
                headerFrame.Background = null;
                headerFrame.BorderThickness = new Thickness(0);
                header.Margin = new Thickness(18, 13, 18, 14);
                deleteAll.ClearValue(Control.TemplateProperty);
                deleteAll.ClearValue(MinWidthProperty);
                deleteAll.ClearValue(HeightProperty);
                scroll.Background = null;
                items.Margin = new Thickness(18, 2, 18, 14);
                items.MaxWidth = Double.PositiveInfinity;
                renderedRevision = -1;
                refresh();
                scroll.ScrollToTop();
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += delegate { refresh(); };
            dialog.Loaded += delegate { timer.Start(); };
            dialog.Closed += delegate { timer.Stop(); };
            refresh();
            return dialog;
        }

        private static ControlTemplate RecallCardTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border), "card");
            border.SetValue(Border.BackgroundProperty, Brushes.White);
            border.SetValue(Border.BorderBrushProperty, ModernBrush("#DCE5F0"));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
            {
                var highlight = new Trigger { Property = property, Value = true };
                highlight.Setters.Add(new Setter(Border.BackgroundProperty, ModernBrush("#EAF2FF"), "card"));
                highlight.Setters.Add(new Setter(Border.BorderBrushProperty, ModernBrush("#93B8EB"), "card"));
                template.Triggers.Add(highlight);
            }
            return template;
        }
        private static UIElement RecallConversationCard(CdpMessageWatcher.RecallConversation conversation)
        {
            var row = new Grid { MinWidth = 235 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var avatar = RecallAvatar(conversation);
            avatar.Margin = new Thickness(0, 0, 11, 0);
            row.Children.Add(avatar);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = conversation.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 380 });
            title.Children.Add(new TextBlock { Text = conversation.Latest.ToString("HH:mm · dd/MM"), FontSize = 11, Foreground = ModernBrush("#64748B") });
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            if (conversation.Unread > 0)
            {
                var count = Badge(new TextBlock { Text = conversation.Unread.ToString(), HorizontalAlignment = HorizontalAlignment.Center },
                    "#B91C1C", "#FFFFFF");
                count.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(count, 2);
                row.Children.Add(count);
            }
            return row;
        }

        private static Grid RecallAvatar(CdpMessageWatcher.RecallConversation conversation, double size = 34)
        {
            var avatar = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var circle = new Ellipse { Fill = ModernBrush("#E0ECFF") };
            avatar.Children.Add(circle);
            var image = DecodeRecallAvatar(conversation.AvatarPng);
            if (image != null)
            {
                circle.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            else
            {
                string initial = System.Globalization.StringInfo.GetNextTextElement(conversation.Name);
                avatar.Children.Add(new TextBlock
                {
                    Text = initial.ToUpperInvariant(), Foreground = ModernBrush("#245398"), FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            }
            return avatar;
        }

        private static BitmapSource DecodeRecallAvatar(string png)
        {
            if (String.IsNullOrEmpty(png) || png.Length > 30000) return null;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(png), false))
                {
                    var image = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    return image.PixelWidth == 48 && image.PixelHeight == 48 ? image : null;
                }
            }
            catch (FormatException) { return null; }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static TextBlock RecallEmpty(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, Foreground = ModernBrush("#475569") };
        }

        private static Border RecallDayDivider(DateTime day)
        {
            DateTime today = DateTime.Today;
            string label = day == today ? "Hôm nay, " + day.ToString("dd/MM")
                : day == today.AddDays(-1) ? "Hôm qua, " + day.ToString("dd/MM")
                : day.ToString("dddd, dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
            return new Border
            {
                Child = new TextBlock { Text = label, FontSize = 12, Foreground = ModernBrush("#475569") },
                Background = ModernBrush("#D6DEE8"), CornerRadius = new CornerRadius(13),
                HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 5, 14, 5),
                Margin = new Thickness(0, 4, 0, 16)
            };
        }

        private Border RecallBubble(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            if (message.Attachment != null && message.Attachment.Kind != "emoji")
                return RecallAttachmentBubble(message, owner);
            var body = new StackPanel();
            body.Children.Add(RecallMessageText(message.Content));
            var time = RecallTimestamp(message);
            time.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(time);
            var bubble = RecallWhiteBubble(body, new Thickness(14, 10, 14, 8));
            bubble.HorizontalAlignment = HorizontalAlignment.Left;
            return bubble;
        }
    }
}
