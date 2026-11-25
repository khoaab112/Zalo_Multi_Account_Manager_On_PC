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
                Width = 1020, Height = 620, MinWidth = 720, MinHeight = 460,
                Background = ModernBrush("#F1F5F9"), Foreground = ModernBrush("#0F172A"),
                FontFamily = FontFamily, FontSize = 14
            };

            var splitGrid = new Grid();
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftPane = new DockPanel { Background = ModernBrush("#F8FAFC") };
            Grid.SetColumn(leftPane, 0);
            splitGrid.Children.Add(leftPane);

            var splitterLine = new Border { Background = ModernBrush("#E2E8F0") };
            Grid.SetColumn(splitterLine, 1);
            splitGrid.Children.Add(splitterLine);

            var rightPane = new DockPanel { Background = ModernBrush("#F1F5F9") };
            Grid.SetColumn(rightPane, 2);
            splitGrid.Children.Add(rightPane);

            // --- Left Header ---
            var leftHeaderBorder = new Border
            {
                Background = ModernBrush("#F8FAFC"),
                BorderBrush = ModernBrush("#E2E8F0"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 14, 16, 12)
            };
            var leftHeaderStack = new StackPanel();

            var leftTopRow = new DockPanel { LastChildFill = true };
            var deleteAll = new Button
            {
                Content = "Xóa tất cả",
                Style = (Style)Resources["DangerGhostButton"],
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(deleteAll, Dock.Right);
            leftTopRow.Children.Add(deleteAll);

            var heading = new TextBlock
            {
                Text = "Tin đã thu hồi",
                FontWeight = FontWeights.Bold,
                FontSize = 17,
                Foreground = ModernBrush("#1E293B"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            leftTopRow.Children.Add(heading);
            leftHeaderStack.Children.Add(leftTopRow);

            // Toggle switch + Note button + Storage badge row
            var leftControlsRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };

            var storageBadge = new Border
            {
                Background = Brushes.White,
                BorderBrush = ModernBrush("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4, 9, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Help
            };
            var storagePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            storagePanel.Children.Add(new TextBlock { Text = "💾 ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var storageLabel = new TextBlock { Text = "Nội dung: ", FontSize = 11.5, Foreground = ModernBrush("#64748B"), VerticalAlignment = VerticalAlignment.Center };
            var storageValue = new TextBlock { Text = "0 B", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = ModernBrush("#0F172A"), VerticalAlignment = VerticalAlignment.Center };
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
            leftControlsRow.Children.Add(storageBadge);

            bool isRecallEnabled = CdpMessageWatcher.IsRecallEnabled(profile.Id);
            var toggleTrack = new Border
            {
                Width = 38,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = ModernBrush(isRecallEnabled ? "#10B981" : "#CBD5E1"),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            var toggleThumb = new System.Windows.Shapes.Ellipse
            {
                Width = 14,
                Height = 14,
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
                VerticalAlignment = VerticalAlignment.Center
            };
            toggleContainer.Children.Add(toggleTrack);
            toggleContainer.Children.Add(toggleLabel);
            toggleContainer.Children.Add(noteBtn);
            DockPanel.SetDock(toggleContainer, Dock.Left);
            leftControlsRow.Children.Add(toggleContainer);
            leftHeaderStack.Children.Add(leftControlsRow);

            var subtitle = new TextBlock
            {
                Foreground = ModernBrush("#DC2626"),
                FontSize = 11.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            leftHeaderStack.Children.Add(subtitle);

            leftHeaderBorder.Child = leftHeaderStack;
            DockPanel.SetDock(leftHeaderBorder, Dock.Top);
            leftPane.Children.Add(leftHeaderBorder);

            var storageStatus = new TextBlock
            {
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ModernBrush("#B91C1C"),
                FontSize = 12,
                Margin = new Thickness(14, 6, 14, 10)
            };
            DockPanel.SetDock(storageStatus, Dock.Bottom);
            leftPane.Children.Add(storageStatus);

            var usersList = new StackPanel { Margin = new Thickness(12, 10, 12, 14) };
            var usersScroll = new ScrollViewer
            {
                Content = usersList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            };
            leftPane.Children.Add(usersScroll);

            // --- Right Header ---
            var rightHeaderBorder = new Border
            {
                Background = ModernBrush("#F8FAFC"),
                BorderBrush = ModernBrush("#E2E8F0"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 12, 20, 12),
                MinHeight = 60
            };
            var rightHeader = new DockPanel { LastChildFill = true };

            var deleteConversation = new Button
            {
                Content = "Xóa hội thoại",
                Style = (Style)Resources["DangerGhostButton"],
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(deleteConversation, Dock.Right);
            rightHeader.Children.Add(deleteConversation);

            var convStorageBadge = new Border
            {
                Background = Brushes.White,
                BorderBrush = ModernBrush("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Cursor = System.Windows.Input.Cursors.Help
            };
            var convStorageText = new TextBlock
            {
                FontSize = 11.5,
                Foreground = ModernBrush("#475569"),
                VerticalAlignment = VerticalAlignment.Center
            };
            convStorageBadge.Child = convStorageText;
            DockPanel.SetDock(convStorageBadge, Dock.Right);
            rightHeader.Children.Add(convStorageBadge);

            var headerAvatar = new ContentControl
            {
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(headerAvatar, Dock.Left);
            rightHeader.Children.Add(headerAvatar);

            var rightTitle = new TextBlock
            {
                Text = "Nội dung tin nhắn bị thu hồi",
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Foreground = ModernBrush("#334155"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            rightHeader.Children.Add(rightTitle);

            rightHeaderBorder.Child = rightHeader;
            DockPanel.SetDock(rightHeaderBorder, Dock.Top);
            rightPane.Children.Add(rightHeaderBorder);

            const int pageSize = 60;
            int page = 0;
            var pageBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 10, 20, 0),
                Visibility = Visibility.Collapsed
            };
            var older = new Button { Content = "Cũ hơn", Style = (Style)Resources["RowActionButton"] };
            var newer = new Button { Content = "Mới hơn", Style = (Style)Resources["RowActionButton"], Margin = new Thickness(8, 0, 0, 0) };
            pageBar.Children.Add(older);
            pageBar.Children.Add(newer);
            DockPanel.SetDock(pageBar, Dock.Top);
            rightPane.Children.Add(pageBar);

            var items = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
            var scroll = new ScrollViewer
            {
                Content = items,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            };
            rightPane.Children.Add(scroll);

            dialog.Content = splitGrid;

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
                if (!enabled)
                {
                    subtitle.Text = "Tính năng lưu tin thu hồi đang TẮT cho hồ sơ này";
                    subtitle.Visibility = Visibility.Visible;
                }
                else
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

            int renderedRevision = -1;
            var displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
            var cardTemplate = RecallCardTemplate();
            var selectedCardTemplate = RecallCardTemplate(selected: true);

            Action refresh = null;
            refresh = delegate
            {
                int revision = CdpMessageWatcher.RecallRevision;
                if (renderedRevision == revision) return;

                updateToggleVisual();
                var totalInfo = CdpMessageWatcher.GetRecallStorageInfo(profile.Id);
                storageValue.Text = totalInfo.FormattedTotal;
                storageBadge.ToolTip = "Tổng dung lượng lưu trữ tin thu hồi:\n" +
                    "• Tin nhắn văn bản: " + totalInfo.FormattedText + " (" + totalInfo.TextCount + " tin)\n" +
                    "• Tệp đa phương tiện: " + totalInfo.FormattedMedia + " (" + totalInfo.MediaCount + " tin có tệp)\n" +
                    "• Dung lượng nội dung: " + totalInfo.FormattedTotal + "\nXem dung lượng trên đĩa ở màn hình chính.";

                storageStatus.Text = CdpMessageWatcher.RecallStorageError;
                storageStatus.Visibility = String.IsNullOrEmpty(storageStatus.Text) ? Visibility.Collapsed : Visibility.Visible;

                var conversations = CdpMessageWatcher.GetRecallConversations(profile.Id);
                deleteAll.IsEnabled = conversations.Length > 0;

                if (conversations.Length == 0)
                {
                    selected = null;
                }
                else if (selected == null || !conversations.Any(c => c.Id == selected.Id))
                {
                    selected = conversations[0];
                    page = 0;
                    displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                    items.Children.Clear();
                }
                else
                {
                    selected = conversations.First(c => c.Id == selected.Id);
                }

                // Render right pane first so ReadRecallConversation clears unread for the active sender
                if (selected != null)
                {
                    rightTitle.Text = selected.Name;
                    headerAvatar.Content = RecallAvatar(selected, 32);
                    headerAvatar.Visibility = Visibility.Visible;
                    deleteConversation.Visibility = Visibility.Visible;
                    convStorageBadge.Visibility = Visibility.Visible;

                    var convInfo = CdpMessageWatcher.GetRecallConversationStorageInfo(profile.Id, selected.Id);
                    convStorageText.Text = "Hội thoại: " + convInfo.FormattedTotal;
                    convStorageBadge.ToolTip = "Dung lượng cuộc trò chuyện này:\n" +
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
                    deleteConversation.IsEnabled = allMessages.Length > 0;

                    bool sameMessages = messages.Length == displayedMessages.Length;
                    if (sameMessages)
                    {
                        for (int i = 0; i < messages.Length; i++)
                        {
                            if (!SameRecallBubble(messages[i], displayedMessages[i]))
                            {
                                sameMessages = false;
                                break;
                            }
                        }
                    }

                    if (!sameMessages || items.Children.Count == 0)
                    {
                        double offset = scroll.VerticalOffset;
                        bool scrollToEnd = messages.Length > displayedMessages.Length;
                        int start = 0;
                        if (items.Children.Count > 0 && displayedMessages.Length > 0 && scrollToEnd)
                        {
                            bool prefixMatches = true;
                            for (int i = 0; i < displayedMessages.Length; i++)
                            {
                                if (!SameRecallBubble(messages[i], displayedMessages[i]))
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
                            var bubble = RecallBubble(message, dialog);
                            if (!message.Outgoing)
                            {
                                var msgRow = new DockPanel { LastChildFill = true };
                                var miniAvatar = RecallAvatar(selected, 28);
                                miniAvatar.VerticalAlignment = VerticalAlignment.Top;
                                miniAvatar.Margin = new Thickness(0, 4, 10, 0);
                                DockPanel.SetDock(miniAvatar, Dock.Left);
                                msgRow.Children.Add(miniAvatar);
                                msgRow.Children.Add(bubble);
                                items.Children.Add(msgRow);
                            }
                            else
                            {
                                items.Children.Add(bubble);
                            }
                        }
                        if (messages.Length == 0) items.Children.Add(RecallEmpty("Cuộc trò chuyện này không còn tin thu hồi."));
                        if (scrollToEnd) scroll.ScrollToEnd();
                        else scroll.ScrollToVerticalOffset(offset);
                        displayedMessages = messages;
                    }

                    // Active sender was just read
                    selected.Unread = 0;
                }
                else
                {
                    rightTitle.Text = "Nội dung tin nhắn bị thu hồi";
                    headerAvatar.Visibility = Visibility.Collapsed;
                    headerAvatar.Content = null;
                    deleteConversation.Visibility = Visibility.Collapsed;
                    convStorageBadge.Visibility = Visibility.Collapsed;
                    pageBar.Visibility = Visibility.Collapsed;
                    items.Children.Clear();
                    displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                    items.Children.Add(RecallEmpty(conversations.Length == 0
                        ? "Chưa có tin nhắn thu hồi."
                        : "Chọn một người gửi bên trái để xem tin nhắn thu hồi."));
                }

                // Render left user list
                usersList.Children.Clear();
                foreach (var conversation in conversations)
                {
                    bool isActive = selected != null && conversation.Id == selected.Id;
                    var card = new Button
                    {
                        Content = RecallConversationCard(conversation),
                        Template = isActive ? selectedCardTemplate : cardTemplate,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(12, 10, 12, 10),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    System.Windows.Automation.AutomationProperties.SetName(card, conversation.Name + ", " + conversation.Unread + " tin chưa đọc");
                    var captured = conversation;
                    card.Click += delegate
                    {
                        if (selected != null && selected.Id == captured.Id) return;
                        selected = captured;
                        page = 0;
                        displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                        items.Children.Clear();
                        renderedRevision = -1;
                        refresh();
                    };
                    usersList.Children.Add(card);
                }
                if (conversations.Length == 0)
                    usersList.Children.Add(RecallEmpty("Chưa có tin thu hồi."));

                renderedRevision = CdpMessageWatcher.RecallRevision;
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
            Func<string, string, Task> performDelete = async (conversationId, conversationName) =>
            {
                if (deleting) return;
                if (!ConfirmDialog.AskRecallDeletion(dialog, profile.Name, conversationName)) return;
                deleting = true;
                try
                {
                    var result = await Task.Run(() => CdpMessageWatcher.DeleteRecalls(profile.Id, conversationId));
                    if (result.Saved) RefreshRecallStorageAfterDeletion();
                    if (!dialog.IsLoaded) return;
                    if (conversationId == null || (selected != null && selected.Id == conversationId))
                    {
                        selected = null;
                        displayedMessages = new CdpMessageWatcher.RecalledMessage[0];
                        items.Children.Clear();
                    }
                    renderedRevision = -1;
                    refresh();
                    if (!result.Saved) NoticeDialog.Show(dialog, "Không xóa được tin", CdpMessageWatcher.RecallStorageError);
                    else NoticeDialog.Success(dialog, "Đã xóa tin thu hồi",
                        "Đã giải phóng " + StorageMeasurement.Format(result.ReleasedBytes) + " tệp đính kèm.",
                        conversationName ?? profile.Name);
                }
                catch (Exception error)
                {
                    Store.Log(error);
                    if (dialog.IsLoaded) NoticeDialog.Show(dialog, "Xóa tin thu hồi", "Không hoàn tất được thao tác. Hãy kiểm tra lại danh sách.");
                }
                finally { deleting = false; }
            };

            deleteAll.Click += async delegate { await performDelete(null, null); };
            deleteConversation.Click += async delegate
            {
                if (selected != null) await performDelete(selected.Id, selected.Name);
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

        private static ControlTemplate RecallCardTemplate(bool selected = false)
        {
            var border = new FrameworkElementFactory(typeof(Border), "card");
            border.SetValue(Border.BackgroundProperty, selected ? ModernBrush("#EFF6FF") : Brushes.White);
            border.SetValue(Border.BorderBrushProperty, ModernBrush(selected ? "#93C5FD" : "#E2E8F0"));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(selected ? 1.5 : 1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            if (!selected)
            {
                foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty })
                {
                    var highlight = new Trigger { Property = property, Value = true };
                    highlight.Setters.Add(new Setter(Border.BackgroundProperty, ModernBrush("#F8FAFC"), "card"));
                    highlight.Setters.Add(new Setter(Border.BorderBrushProperty, ModernBrush("#93C5FD"), "card"));
                    template.Triggers.Add(highlight);
                }
            }
            return template;
        }
        private static UIElement RecallConversationCard(CdpMessageWatcher.RecallConversation conversation)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var avatar = RecallAvatar(conversation, 38);
            avatar.Margin = new Thickness(0, 0, 11, 0);
            row.Children.Add(avatar);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = conversation.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                Foreground = ModernBrush("#1E293B"), TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left });
            title.Children.Add(new TextBlock { Text = conversation.Latest.ToString("HH:mm · dd/MM"), FontSize = 11.5, Foreground = ModernBrush("#64748B"), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            if (conversation.Unread > 0)
            {
                var count = Badge(new TextBlock { Text = conversation.Unread.ToString(), HorizontalAlignment = HorizontalAlignment.Center },
                    "#B91C1C", "#FFFFFF");
                count.VerticalAlignment = VerticalAlignment.Center;
                count.Margin = new Thickness(8, 0, 0, 0);
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
