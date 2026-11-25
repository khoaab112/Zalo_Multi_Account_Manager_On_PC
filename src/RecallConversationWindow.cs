using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        internal readonly System.Collections.Generic.Dictionary<string, Window> openRecallWindows =
            new System.Collections.Generic.Dictionary<string, Window>();

        public void RestoreOpenRecallWindows()
        {
            foreach (var window in System.Linq.Enumerable.ToList(openRecallWindows.Values))
            {
                if (window != null && window.IsLoaded)
                {
                    window.Show();
                    window.ShowInTaskbar = true;
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                    var h = new System.Windows.Interop.WindowInteropHelper(window);
                    if (h.Handle != IntPtr.Zero)
                    {
                        TrayManager.ShowWindowAsync(h.Handle, 9);
                        TrayManager.SetForegroundWindow(h.Handle);
                    }
                }
            }
        }

        private void ShowRecalledMessages(object sender, RoutedEventArgs args)
        {
            try
            {
                var profile = ((FrameworkElement)sender).DataContext as Profile;
                if (profile == null) return;
                OpenRecallWindow(profile);
            }
            catch (Exception error)
            {
                Store.Log(error);
                NoticeDialog.Show(this, "Tin đã thu hồi", "Không thể mở cửa sổ tin thu hồi: " + error.Message);
            }
        }

        public void OpenRecallWindow(Profile profile)
        {
            if (profile == null) return;
            try
            {
                Window existing;
                if (openRecallWindows.TryGetValue(profile.Id, out existing) && existing != null && existing.IsLoaded)
                {
                    if (existing.WindowState == WindowState.Minimized)
                        existing.WindowState = WindowState.Normal;
                    existing.Show();
                    existing.ShowInTaskbar = true;
                    existing.Activate();
                    existing.Focus();
                    var h = new System.Windows.Interop.WindowInteropHelper(existing);
                    if (h.Handle != IntPtr.Zero)
                    {
                        TrayManager.ShowWindowAsync(h.Handle, 9);
                        TrayManager.SetForegroundWindow(h.Handle);
                    }
                    return;
                }
                var window = CreateRecallWindow(profile);
                openRecallWindows[profile.Id] = window;
                window.Closed += delegate { openRecallWindows.Remove(profile.Id); };
                window.Show();
                window.Activate();
                window.Focus();
            }
            catch (Exception error)
            {
                Store.Log(error);
                NoticeDialog.Show(this, "Tin đã thu hồi", "Không thể mở cửa sổ tin thu hồi: " + error.Message);
            }
        }

        private Window CreateRecallWindow(Profile profile)
        {
            var dialog = new Window
            {
                Title = "Tin đã thu hồi · " + profile.Name, Owner = this, ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 650, Height = 500, MinWidth = 380, MinHeight = 330,
                Background = ModernBrush("#F3F6FA"), Foreground = ModernBrush("#0F172A"),
                FontFamily = FontFamily, FontSize = 14
            };
            var root = new DockPanel();
            var header = new DockPanel { Margin = new Thickness(18, 13, 18, 14), LastChildFill = true };
            var heading = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 19, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };
            var back = new Button { Content = "← Quay lại", Style = (Style)Resources["RowActionButton"], Margin = new Thickness(0, 0, 12, 0),
                Visibility = Visibility.Collapsed };
            var headerAvatar = new ContentControl { Margin = new Thickness(0, 0, 9, 0), Visibility = Visibility.Collapsed };
            var deleteAll = new Button { Content = "Xóa tất cả", Style = (Style)Resources["DangerGhostButton"],
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(deleteAll, Dock.Right);
            header.Children.Add(deleteAll);

            // Storage badge (Ô đỏ bên phải)
            var storageBadge = new Border
            {
                Background = ModernBrush("#F8FAFC"),
                BorderBrush = ModernBrush("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Help
            };
            var storagePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var storageIcon = new TextBlock { Text = "💾 ", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var storageLabel = new TextBlock { Text = "Lưu trữ: ", FontSize = 12, Foreground = ModernBrush("#64748B"), VerticalAlignment = VerticalAlignment.Center };
            var storageValue = new TextBlock { Text = "0 B", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ModernBrush("#0F172A"), VerticalAlignment = VerticalAlignment.Center };
            storagePanel.Children.Add(storageIcon);
            storagePanel.Children.Add(storageLabel);
            storagePanel.Children.Add(storageValue);
            storageBadge.Child = storagePanel;
            var storageMenu = new ContextMenu();
            var cleanMedia = new MenuItem { Header = "Xem tệp có thể dọn" };
            storageMenu.Items.Add(cleanMedia);
            storageBadge.ContextMenu = storageMenu;
            cleanMedia.Click += async delegate
            {
                cleanMedia.IsEnabled = false;
                try
                {
                    var report = await CdpMessageWatcher.ScanRecallMediaNow();
                    NoticeDialog.Show(dialog, "Báo cáo media", report == null
                        ? "Chưa thể quét media. Đợi lượt quét hiện tại hoặc kiểm tra trạng thái kho rồi thử lại."
                        : "Có " + report.CandidateCount + " tệp có thể dọn, chiếm " +
                            StorageMeasurement.Format(report.CandidateBytes) + " trên đĩa. Lượt quét chỉ lập báo cáo.");
                }
                catch (Exception error)
                {
                    Store.Log(error);
                    NoticeDialog.Show(dialog, "Báo cáo media", "Không thể hoàn tất lượt quét.");
                }
                finally { cleanMedia.IsEnabled = true; }
            };
            DockPanel.SetDock(storageBadge, Dock.Right);
            header.Children.Add(storageBadge);

            header.Children.Add(back);
            header.Children.Add(headerAvatar);
            var subtitle = new TextBlock { Foreground = ModernBrush("#64748B"), FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };

            var headingRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            headingRow.Children.Add(heading);

            // Toggle switch + Note button (Ô đỏ bên trái)
            bool isRecallEnabled = CdpMessageWatcher.IsRecallEnabled(profile.Id);

            var toggleTrack = new Border
            {
                Width = 40,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = ModernBrush(isRecallEnabled ? "#10B981" : "#CBD5E1"),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            var toggleThumb = new System.Windows.Shapes.Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = isRecallEnabled ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = isRecallEnabled ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0)
            };
            toggleTrack.Child = toggleThumb;

            var toggleLabel = new TextBlock
            {
                Text = isRecallEnabled ? "Bật" : "Tắt",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ModernBrush(isRecallEnabled ? "#10B981" : "#64748B"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var noteBtn = new Button
            {
                Content = "ⓘ Lưu ý",
                Style = (Style)Resources["RowActionButton"],
                Padding = new Thickness(6, 3, 6, 3),
                FontSize = 11,
                Foreground = ModernBrush("#475569"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Xem lưu ý về tính năng lưu tin nhắn thu hồi"
            };

            var toggleContainer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            toggleContainer.Children.Add(toggleTrack);
            toggleContainer.Children.Add(toggleLabel);
            toggleContainer.Children.Add(noteBtn);
            headingRow.Children.Add(toggleContainer);

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(headingRow);
            titles.Children.Add(subtitle);
            header.Children.Add(titles);

            const string RecallNoteDetails =
                "LƯU Ý KHI SỬ DỤNG TÍNH NĂNG TIN NHẮN THU HỒI:\n\n" +
                "1. Cơ chế hoạt động:\n" +
                "• Khi tính năng BẬT, ứng dụng tự động lắng nghe và lưu tạm thời nội dung (văn bản, ảnh, video, tin nhắn thoại, tệp đính kèm) ngay khi tin nhắn đến qua kết nối CDP nội bộ.\n" +
                "• Nhờ đó, nếu người gửi thu hồi tin nhắn, bạn vẫn xem lại được nội dung gốc.\n\n" +
                "2. Dung lượng ổ đĩa & Bộ nhớ (RAM):\n" +
                "• Dữ liệu tin nhắn và các file đa phương tiện (ảnh, video...) được lưu trực tiếp trên ổ cứng máy tính của bạn.\n" +
                "• Việc nhận nhiều tệp media lớn có thể làm tăng dung lượng đĩa và mức sử dụng RAM khi giải mã hình ảnh.\n\n" +
                "3. Quyền riêng tư & Quản lý:\n" +
                "• Kho tin nhắn trên máy được mã hóa. Các tệp đính kèm được lưu riêng trên máy.\n" +
                "• Bạn có thể tắt theo dõi, xóa bản ghi thu hồi và xem báo cáo các tệp không còn được sử dụng.";

            CdpMessageWatcher.RecallConversation selected = null;

            Action updateToggleVisual = delegate
            {
                bool enabled = CdpMessageWatcher.IsRecallEnabled(profile.Id);
                toggleTrack.Background = ModernBrush(enabled ? "#10B981" : "#CBD5E1");
                toggleThumb.HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                toggleThumb.Margin = enabled ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
                toggleLabel.Text = enabled ? "Bật" : "Tắt";
                toggleLabel.Foreground = ModernBrush(enabled ? "#10B981" : "#64748B");
                if (!enabled && selected == null)
                {
                    subtitle.Text = "Tính năng lưu tin thu hồi đang TẮT cho hồ sơ này";
                    subtitle.Visibility = Visibility.Visible;
                }
                else if (selected == null)
                {
                    subtitle.Visibility = Visibility.Collapsed;
                }
            };

            Action toggleAction = delegate
            {
                bool current = CdpMessageWatcher.IsRecallEnabled(profile.Id);
                if (!current)
                {
                    bool confirmed = ConfirmDialog.Ask(dialog, "Bật tính năng tin nhắn thu hồi",
                        "Bạn có muốn bật tính năng lưu tin thu hồi cho", profile.Name,
                        "Lưu ý: Tin nhắn và tệp đa phương tiện (ảnh, video) sẽ được tự động lưu tạm thời trên ổ đĩa máy tính ngay khi nhận để khôi phục khi bị thu hồi. Điều này có thể chiếm thêm dung lượng ổ đĩa.",
                        "Đã hiểu & Bật", false);
                    if (confirmed)
                    {
                        CdpMessageWatcher.SetRecallEnabled(profile.Id, true);
                        updateToggleVisual();
                    }
                }
                else
                {
                    CdpMessageWatcher.SetRecallEnabled(profile.Id, false);
                    updateToggleVisual();
                }
            };

            toggleTrack.MouseLeftButtonUp += delegate { toggleAction(); };
            toggleLabel.MouseLeftButtonUp += delegate { toggleAction(); };
            noteBtn.Click += delegate
            {
                NoticeDialog.Show(dialog, "Lưu ý về tính năng tin nhắn thu hồi", RecallNoteDetails);
            };
            var headerFrame = new Border { Child = header };
            DockPanel.SetDock(headerFrame, Dock.Top);
            root.Children.Add(headerFrame);
            var storageStatus = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap,
                Foreground = ModernBrush("#B91C1C"), Margin = new Thickness(18, 0, 18, 12) };
            DockPanel.SetDock(storageStatus, Dock.Bottom);
            root.Children.Add(storageStatus);

            const int pageSize = 60;
            int page = 0;
            var pageBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 18, 6),
                Visibility = Visibility.Collapsed };
            var older = new Button { Content = "Cũ hơn", Style = (Style)Resources["RowActionButton"] };
            var newer = new Button { Content = "Mới hơn", Style = (Style)Resources["RowActionButton"], Margin = new Thickness(8, 0, 0, 0) };
            pageBar.Children.Add(older);
            pageBar.Children.Add(newer);
            DockPanel.SetDock(pageBar, Dock.Top);
            root.Children.Add(pageBar);

            var items = new StackPanel { Margin = new Thickness(18, 2, 18, 14) };
            var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            root.Children.Add(scroll);
            dialog.Content = root;

            int renderedRevision = -1;
            var displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
            var cardTemplate = RecallCardTemplate();
            Action refresh = null;
            refresh = delegate
            {
                int revision = CdpMessageWatcher.RecallRevision;
                if (renderedRevision == revision) return;

                if (selected != null)
                {
                    toggleContainer.Visibility = Visibility.Collapsed;
                    var convInfo = CdpMessageWatcher.GetRecallConversationStorageInfo(profile.Id, selected.Id);
                    storageLabel.Text = "Hội thoại: ";
                    storageValue.Text = convInfo.FormattedTotal;
                    storageBadge.ToolTip = "Dung lượng cuộc trò chuyện này:\n" +
                        "• Tin nhắn văn bản: " + convInfo.FormattedText + " (" + convInfo.TextCount + " tin)\n" +
                        "• Tệp đa phương tiện: " + convInfo.FormattedMedia + " (" + convInfo.MediaCount + " tin có tệp)\n" +
                        "• Tổng dung lượng: " + convInfo.FormattedTotal;

                    var allMessages = CdpMessageWatcher.ReadRecallConversation(profile.Id, selected.Id);
                    page = Math.Min(page, Math.Max(0, (allMessages.Length - 1) / pageSize));
                    int end = Math.Max(0, allMessages.Length - page * pageSize);
                    var messages = allMessages.Skip(Math.Max(0, end - pageSize)).Take(pageSize).ToArray();
                    pageBar.Visibility = allMessages.Length > pageSize ? Visibility.Visible : Visibility.Collapsed;
                    older.IsEnabled = end > pageSize;
                    newer.IsEnabled = page > 0;
                    renderedRevision = CdpMessageWatcher.RecallRevision;
                    deleteAll.IsEnabled = messages.Length > 0;

                    bool sameMessages = messages.Length == displayedMessages.Length;
                    if (sameMessages)
                    {
                        for (int i = 0; i < messages.Length; i++)
                        {
                            var m1 = messages[i];
                            var m2 = displayedMessages[i];
                            if (!SameRecallBubble(m1, m2))
                            {
                                sameMessages = false;
                                break;
                            }
                        }
                    }

                    if (sameMessages && items.Children.Count > 0)
                    {
                        storageStatus.Text = CdpMessageWatcher.RecallStorageError;
                        storageStatus.Visibility = String.IsNullOrEmpty(storageStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                        return;
                    }

                    double offset = scroll.VerticalOffset;
                    bool scrollToEnd = messages.Length > displayedMessages.Length;
                    int start = 0;
                    if (items.Children.Count > 0 && displayedMessages.Length > 0 && scrollToEnd)
                    {
                        bool prefixMatches = true;
                        for (int i = 0; i < displayedMessages.Length; i++)
                        {
                            var m1 = messages[i];
                            var m2 = displayedMessages[i];
                            if (!SameRecallBubble(m1, m2))
                            {
                                prefixMatches = false;
                                break;
                            }
                        }
                        if (prefixMatches) start = displayedMessages.Length;
                        else items.Children.Clear();
                    }
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

                    storageStatus.Text = CdpMessageWatcher.RecallStorageError;
                    storageStatus.Visibility = String.IsNullOrEmpty(storageStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }
                renderedRevision = revision;

                heading.Text = "Tin đã thu hồi";
                pageBar.Visibility = Visibility.Collapsed;
                toggleContainer.Visibility = Visibility.Visible;
                updateToggleVisual();
                var totalInfo = CdpMessageWatcher.GetRecallStorageInfo(profile.Id);
                storageLabel.Text = "Nội dung: ";
                storageValue.Text = totalInfo.FormattedTotal;
                storageBadge.ToolTip = "Tổng dung lượng lưu trữ tin thu hồi:\n" +
                    "• Tin nhắn văn bản: " + totalInfo.FormattedText + " (" + totalInfo.TextCount + " tin)\n" +
                    "• Tệp đa phương tiện: " + totalInfo.FormattedMedia + " (" + totalInfo.MediaCount + " tin có tệp)\n" +
                    "• Dung lượng nội dung: " + totalInfo.FormattedTotal + "\nXem dung lượng trên đĩa ở màn hình chính.";

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
                        page = 0;
                        displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                        items.Children.Clear();
                        heading.Text = selected.Name;
                        subtitle.Text = "Tin thu hồi · " + profile.Name;
                        subtitle.Visibility = Visibility.Visible;
                        headerFrame.Background = Brushes.White;
                        headerFrame.BorderBrush = ModernBrush("#DCE5F0");
                        headerFrame.BorderThickness = new Thickness(0, 0, 0, 1);
                        items.Margin = new Thickness(18, 12, 18, 14);
                        headerAvatar.Content = RecallAvatar(selected);
                        headerAvatar.Visibility = Visibility.Visible;
                        back.Visibility = Visibility.Visible;
                        renderedRevision = -1;
                        refresh();
                    };
                    items.Children.Add(card);
                }
                if (conversations.Length == 0)
                    items.Children.Add(RecallEmpty("Chưa có tin thu hồi."));
            };
            Action changePage = delegate
            {
                items.Children.Clear();
                displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                renderedRevision = -1;
                refresh();
                if (page != 0) scroll.ScrollToTop();
            };
            older.Click += delegate { page++; changePage(); };
            newer.Click += delegate { page--; changePage(); };

            bool deleting = false;
            deleteAll.Click += async delegate
            {
                if (deleting) return;
                var conversation = selected;
                if (!ConfirmDialog.AskRecallDeletion(dialog, profile.Name, conversation == null ? null : conversation.Name)) return;
                deleting = true;
                try
                {
                    var result = await Task.Run(() => CdpMessageWatcher.DeleteRecalls(profile.Id, conversation == null ? null : conversation.Id));
                    if (result.Saved) RefreshRecallStorageAfterDeletion();
                    if (!dialog.IsLoaded) return;
                    refresh();
                    if (!result.Saved) NoticeDialog.Show(dialog, "Không xóa được tin", CdpMessageWatcher.RecallStorageError);
                    else NoticeDialog.Success(dialog, "Đã xóa tin thu hồi",
                        "Đã giải phóng " + StorageMeasurement.Format(result.ReleasedBytes) + " tệp đính kèm.",
                        conversation == null ? profile.Name : conversation.Name);
                }
                catch (Exception error)
                {
                    Store.Log(error);
                    if (dialog.IsLoaded) NoticeDialog.Show(dialog, "Xóa tin thu hồi", "Không hoàn tất được thao tác. Hãy kiểm tra lại danh sách.");
                }
                finally { deleting = false; }
            };
            back.Click += delegate
            {
                selected = null;
                toggleContainer.Visibility = Visibility.Visible;
                updateToggleVisual();
                back.Visibility = Visibility.Collapsed;
                headerAvatar.Visibility = Visibility.Collapsed;
                headerAvatar.Content = null;
                subtitle.Visibility = Visibility.Collapsed;
                headerFrame.Background = null;
                headerFrame.BorderThickness = new Thickness(0);
                items.Margin = new Thickness(18, 2, 18, 14);
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

        private static bool SameRecallBubble(CdpMessageWatcher.RecalledMessage left, CdpMessageWatcher.RecalledMessage right)
        {
            if (left.Id != right.Id || left.Content != right.Content || left.Outgoing != right.Outgoing ||
                left.DetectedAt != right.DetectedAt) return false;
            var a = left.Attachment;
            var b = right.Attachment;
            if (ReferenceEquals(a, b)) return true;
            return a != null && b != null && a.Kind == b.Kind && a.Data == b.Data && a.Mime == b.Mime &&
                a.PreviewData == b.PreviewData && a.PreviewMime == b.PreviewMime && a.FileName == b.FileName &&
                a.FromSource == b.FromSource && a.CaptureStatus == b.CaptureStatus;
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
            if (message.Outgoing)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "[Bạn đã gửi]",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ModernBrush("#0068FF"),
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }
            body.Children.Add(RecallMessageText(message.Content));
            var time = RecallTimestamp(message);
            time.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(time);
            var bubble = RecallWhiteBubble(body, new Thickness(14, 10, 14, 8));
            if (message.Outgoing)
            {
                bubble.Background = ModernBrush("#EAF3FF");
                bubble.BorderBrush = ModernBrush("#C6DDFF");
            }
            bubble.HorizontalAlignment = HorizontalAlignment.Left;
            return bubble;
        }
    }
}
