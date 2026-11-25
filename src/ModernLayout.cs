using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private Border sourceBanner;
        private readonly TextBlock baseVersion = new TextBlock();
        private readonly TextBlock baseSignature = new TextBlock { Text = "Chưa xác minh" };
        private readonly Border profileNameError = new Border { Visibility = Visibility.Collapsed };
        private const string ProfileNameRequired = "Vui lòng nhập tên hồ sơ.";
        private TextBlock profileCount;
        private TextBlock profileStorageTotal;
        private StackPanel managerHeaderActions;

        private static Brush ModernBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private static void Detach(FrameworkElement element)
        {
            var parent = element.Parent as Panel;
            if (parent != null) parent.Children.Remove(element);
        }
        private Border Card(UIElement child)
        {
            return new Border { Child = child, Background = Brushes.White, BorderBrush = ModernBrush("#E2E8F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Effect = ((System.Windows.Media.Effects.DropShadowEffect)Resources["CardShadow"]).Clone() };
        }
        private static Border Badge(TextBlock text, string background, string foreground)
        {
            text.FontSize = 12; text.FontWeight = FontWeights.SemiBold; text.Foreground = ModernBrush(foreground);
            return new Border { Child = text, Background = ModernBrush(background), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(10, 0, 0, 0) };
        }
        private void ShowOriginal(OriginalInstallation original)
        {
            originalText.Text = original.DirectoryPath;
            baseVersion.Text = "v" + original.Version;
            baseSignature.Text = "✓ Chữ ký VNG hợp lệ"; baseSignature.Foreground = ModernBrush("#059669");
        }
        private void ApplyModernLayout()
        {
            using (var stream = typeof(ManagerWindow).Assembly.GetManifestResourceStream("ZaloManager.Theme.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
            using (var stream = typeof(ManagerWindow).Assembly.GetManifestResourceStream("ZaloManager.ModernContextMenu.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
            Title = "Zalo Manager"; Width = 1120; Height = 740; MinWidth = 960; MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            System.Windows.Media.Imaging.BitmapImage logo;
            using (var stream = typeof(ManagerWindow).Assembly.GetManifestResourceStream("ZaloManager.Logo.png"))
            {
                logo = new System.Windows.Media.Imaging.BitmapImage(); logo.BeginInit();
                logo.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                logo.StreamSource = stream; logo.EndInit(); logo.Freeze();
            }
            using (var stream = typeof(ManagerWindow).Assembly.GetManifestResourceStream("ZaloManager.Icon.ico"))
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Foreground = ModernBrush("#0F172A"); FontSize = 13;
            foreach (var element in new FrameworkElement[] { originalText, sourceToolbar, resultIcon, sourceResult, sourceProgress, toolbar, grid, status }) Detach(element);
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var heading = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new Image { Source = logo, Width = 48, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "Quản lý hồ sơ Zalo", FontSize = 22, FontWeight = FontWeights.Bold });
            titles.Children.Add(new TextBlock { Text = "Mỗi hồ sơ có bản sao chương trình và dữ liệu riêng. Đăng nhập trực tiếp trong cửa sổ Zalo.", FontSize = 12.5, Foreground = ModernBrush("#64748B"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(titles, 1); heading.Children.Add(titles);
            managerHeaderActions = CreateManagerHeaderActions();
            Grid.SetColumn(managerHeaderActions, 2); heading.Children.Add(managerHeaderActions);
            root.Children.Add(heading);

            var baseBody = new Grid(); baseBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); baseBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); baseBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var baseTop = new Grid(); baseTop.ColumnDefinitions.Add(new ColumnDefinition()); baseTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var baseInfo = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            var badges = new WrapPanel();
            badges.Children.Add(new TextBlock { Text = "Zalo gốc", FontSize = 14.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            baseVersion.Text = "Chưa nhận diện";
            badges.Children.Add(Badge(baseVersion, "#EFF6FF", "#0068FF")); badges.Children.Add(Badge(baseSignature, "#ECFDF5", "#64748B"));
            baseInfo.Children.Add(badges);
            originalText.Text = "Đang nhận diện bản cài đặt Zalo…"; originalText.FontFamily = new FontFamily("Consolas"); originalText.FontSize = 12;
            originalText.Foreground = ModernBrush("#64748B"); originalText.Margin = new Thickness(0, 8, 0, 0); originalText.TextWrapping = TextWrapping.NoWrap; originalText.TextTrimming = TextTrimming.CharacterEllipsis;
            originalText.SetBinding(ToolTipProperty, new Binding("Text") { Source = originalText });
            baseInfo.Children.Add(originalText); baseTop.Children.Add(baseInfo);
            sourceToolbar.Margin = new Thickness(0); sourceToolbar.VerticalAlignment = VerticalAlignment.Center;
            foreach (Button button in sourceToolbar.Children)
            {
                button.Style = (Style)Resources[button == detectButton ? "PrimaryButton" : "SecondaryButton"]; button.FontSize = 12;
                button.Content = button == detectButton ? "↻ Kiểm tra & Đồng bộ" : "⌖ Trỏ chọn Zalo gốc…";
            }
            Grid.SetColumn(sourceToolbar, 1); baseTop.Children.Add(sourceToolbar); baseBody.Children.Add(baseTop);
            var feedback = new Grid(); feedback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); feedback.ColumnDefinitions.Add(new ColumnDefinition());
            resultIcon.FontSize = 16; resultIcon.Width = 24; sourceResult.FontSize = 12.5; sourceResult.FontWeight = FontWeights.Medium; sourceResult.MaxWidth = Double.PositiveInfinity;
            feedback.Children.Add(resultIcon); Grid.SetColumn(sourceResult, 1); feedback.Children.Add(sourceResult);
            sourceBanner = new Border { Child = feedback, Background = ModernBrush("#F8FAFC"), BorderBrush = ModernBrush("#E2E8F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(sourceBanner, 1); baseBody.Children.Add(sourceBanner);
            sourceProgress.Foreground = ModernBrush("#0068FF"); sourceProgress.Background = ModernBrush("#DBEAFE"); Grid.SetRow(sourceProgress, 2); baseBody.Children.Add(sourceProgress);
            var baseCard = Card(baseBody); baseCard.Padding = new Thickness(18); baseCard.Margin = new Thickness(0, 0, 0, 16); Grid.SetRow(baseCard, 1); root.Children.Add(baseCard);

            var addRow = new Grid { Margin = new Thickness(0, 0, 0, 14) }; addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); addRow.ColumnDefinitions.Add(new ColumnDefinition());
            addRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            addRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            toolbar.Margin = new Thickness(0); name.Style = (Style)Resources["ModernTextBox"]; name.Width = 300; name.Height = 42; name.Margin = new Thickness(0); name.Text = "";
            toolbar.Children.Remove(name);
            var input = new Grid(); input.Children.Add(name);
            var placeholder = new TextBlock { Text = "Nhập tên tài khoản mới…", IsHitTestVisible = false, Foreground = ModernBrush("#94A3B8"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            placeholder.SetBinding(VisibilityProperty, new Binding("Text") { Source = name, Converter = new EmptyTextVisibilityConverter() }); input.Children.Add(placeholder);
            var errorOutline = new Border { BorderThickness = new Thickness(2), BorderBrush = ModernBrush("#DC2626"),
                CornerRadius = new CornerRadius(8), IsHitTestVisible = false };
            errorOutline.SetBinding(VisibilityProperty, new Binding("Visibility") { Source = profileNameError });
            input.Children.Add(errorOutline);
            toolbar.Children.Insert(0, input);
            System.Windows.Automation.AutomationProperties.SetName(name, "Tên hồ sơ mới");
            name.TextChanged += delegate
            {
                if (profileNameError.Visibility != Visibility.Visible || String.IsNullOrWhiteSpace(name.Text)) return;
                profileNameError.Visibility = Visibility.Collapsed;
                name.ClearValue(System.Windows.Automation.AutomationProperties.HelpTextProperty);
            };
            foreach (var child in toolbar.Children)
            {
                var button = child as Button;
                if (button == null) continue;
                button.Style = (Style)Resources["PrimaryButton"]; button.Content = "+ Thêm hồ sơ"; button.Height = 42; button.Margin = new Thickness(10, 0, 0, 0); button.Padding = new Thickness(16, 9, 16, 9);
            }
            addRow.Children.Add(toolbar);
            profileNameError.Background = ModernBrush("#FEF2F2");
            profileNameError.BorderBrush = ModernBrush("#FECACA");
            profileNameError.BorderThickness = new Thickness(1);
            profileNameError.CornerRadius = new CornerRadius(8);
            profileNameError.Width = name.Width;
            profileNameError.HorizontalAlignment = HorizontalAlignment.Left;
            profileNameError.Margin = new Thickness(0, 6, 0, 0);
            profileNameError.Padding = new Thickness(11, 6, 11, 6);
            profileNameError.Child = new TextBlock { Text = "⚠  " + ProfileNameRequired, FontWeight = FontWeights.SemiBold,
                FontSize = 13, Foreground = ModernBrush("#991B1B"), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(profileNameError, 1);
            addRow.Children.Add(profileNameError);
            var profileSummary = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            profileStorageTotal = new TextBlock { FontWeight = FontWeights.SemiBold };
            var storageTotalBadge = Badge(profileStorageTotal, "#E6EDF6", "#334155");
            storageTotalBadge.Margin = new Thickness(0, 0, 12, 0);
            storageTotalBadge.ToolTip = "Tổng cột Data chiếm dụng của các hồ sơ trong bảng, gồm dữ liệu và ứng dụng. Dấu ≥ cho biết chưa đo được đầy đủ.";
            profileSummary.Children.Add(storageTotalBadge);
            profileCount = new TextBlock { FontSize = 13, Foreground = ModernBrush("#64748B"), VerticalAlignment = VerticalAlignment.Center };
            profileSummary.Children.Add(profileCount);
            Grid.SetColumn(profileSummary, 1); addRow.Children.Add(profileSummary); Grid.SetRow(addRow, 2); root.Children.Add(addRow);
            StyleProfileTable();
            var tableContent = new Grid();
            tableContent.Children.Add(grid);
            tableContent.Children.Add(CreateProfileTableLoading());
            grid.IsEnabled = !loadingOnStartup;
            var table = Card(tableContent); table.ClipToBounds = true; Grid.SetRow(table, 3); root.Children.Add(table);
            grid.SizeChanged += delegate
            {
                grid.Clip = new RectangleGeometry(new Rect(0, 0, grid.ActualWidth, grid.ActualHeight), 11, 11);
                bool compact = grid.ActualWidth < 1150;
                double[] widths = grid.ActualWidth < 940
                    ? new double[] { 48, 112, 106, 100, 104, 116 }
                    : compact ? new double[] { 56, 130, 120, 110, 108, 120 } : new double[] { 72, 170, 140, 120, 130, 170 };
                for (int index = 0; index < widths.Length; index++)
                    if (grid.Columns[index].Width.Value != widths[index]) grid.Columns[index].Width = new DataGridLength(widths[index]);
            };
            status.FontSize = 12.5; status.Foreground = ModernBrush("#64748B"); status.Margin = new Thickness(0, 12, 0, 0); status.Text = "";
            Grid.SetRow(status, 4); root.Children.Add(status); RefreshStatus();
        }

        private StackPanel CreateManagerHeaderActions()
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), IsEnabled = !loadingOnStartup && !busy
            };
            const string startupLabel = "Khởi động cùng hệ thống";
            var startup = new ToggleButton
            {
                IsChecked = WindowsStartup.IsRegistered(), Background = ModernBrush("#EAF5FD"),
                Foreground = ModernBrush("#164562"), BorderBrush = ModernBrush("#C7E1F3"),
                BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7),
                Height = 36, Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Mở Manager khi đăng nhập Windows và tự mở các clone đã chọn."
            };
            var border = new FrameworkElementFactory(typeof(Border), "ToggleChrome");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
                border.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, ModernBrush("#DDEFFD"), "ToggleChrome"));
            var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(OpacityProperty, 0.45, "ToggleChrome"));
            template.Triggers.Add(hover);
            template.Triggers.Add(disabled);
            startup.Template = template;
            var body = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(MenuGlyph("\uE770"));
            body.Children.Add(new TextBlock { Text = startupLabel, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 10, 0) });
            var track = new Border { Width = 34, Height = 18, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center };
            var thumb = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            track.Child = thumb;
            body.Children.Add(track);
            startup.Content = body;
            System.Windows.Automation.AutomationProperties.SetName(startup, startupLabel);
            Action refresh = delegate
            {
                bool enabled = WindowsStartup.IsRegistered();
                startup.IsChecked = enabled;
                track.Background = ModernBrush(enabled ? "#0068FF" : "#64748B");
                thumb.HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                thumb.Margin = enabled ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            };
            refresh();
            startup.Click += delegate
            {
                try
                {
                    bool enabled = startup.IsChecked == true;
                    WindowsStartup.SetRegistration(enabled);
                    status.Text = enabled ? "Đã bật khởi động Zalo Manager cùng Windows." : "Đã tắt khởi động Zalo Manager cùng Windows.";
                }
                catch (Exception error) { Report(error); }
                finally { refresh(); }
            };
            actions.Children.Add(startup);

            var exit = new Button
            {
                Height = 36, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6),
                Style = (Style)Resources["DangerGhostButton"], ToolTip = "Thoát hoàn toàn Zalo Manager"
            };
            var exitContent = new StackPanel { Orientation = Orientation.Horizontal };
            exitContent.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 8,2 H 3 V 14 H 8 M 6,8 H 14 M 11,5 L 14,8 L 11,11"),
                Stroke = ModernBrush("#DC2626"), StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 14, Height = 14, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center
            });
            exitContent.Children.Add(new TextBlock { Text = "Thoát", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            exit.Content = exitContent;
            System.Windows.Automation.AutomationProperties.SetName(exit, "Thoát");
            exit.Click += delegate { TrayManager.RequestExit(); };
            actions.Children.Add(exit);
            return actions;
        }

        private void StyleProfileTable()
        {
            grid.Background = Brushes.White; grid.BorderThickness = new Thickness(0); grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            grid.RowHeight = 76; grid.ColumnHeaderHeight = 44; grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.CanUserResizeRows = false; grid.CanUserResizeColumns = false; grid.IsReadOnly = true;
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, ModernBrush("#F1F5F9"))); rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, ModernBrush("#F8FAFC"))); rowStyle.Triggers.Add(hover);
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, ModernBrush("#EFF6FF"))); rowStyle.Triggers.Add(selected); grid.RowStyle = rowStyle;
            var cellStyle = new Style(typeof(DataGridCell)); cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, ModernBrush("#0F172A"))); cellStyle.Triggers.Add(selectedCell); grid.CellStyle = cellStyle;
            var headerStyle = new Style(typeof(DataGridColumnHeader)); headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, ModernBrush("#F8FAFC"))); headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, ModernBrush("#64748B"))); headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 10.5)); headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold)); headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1))); headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, ModernBrush("#E2E8F0"))); headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 0, 0))); grid.ColumnHeaderStyle = headerStyle;
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Avatar", Width = 72, CellTemplate = ProfileAvatarTemplate() });
            var names = new FrameworkElementFactory(typeof(StackPanel)); names.SetValue(MarginProperty, new Thickness(12, 0, 8, 0)); names.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("Name")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); title.SetValue(TextBlock.FontSizeProperty, 14.0); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); names.AppendChild(title);
            var subtitle = new FrameworkElementFactory(typeof(TextBlock)); subtitle.SetBinding(TextBlock.TextProperty, new Binding("SubLabel")); subtitle.SetValue(TextBlock.ForegroundProperty, ModernBrush("#94A3B8")); subtitle.SetValue(TextBlock.FontSizeProperty, 11.5); names.AppendChild(subtitle);
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Tên hồ sơ", Width = 170, CellTemplate = new DataTemplate { VisualTree = names } });
            var storage = new FrameworkElementFactory(typeof(TextBlock));
            storage.SetBinding(TextBlock.TextProperty, new Binding("StorageUsage") { TargetNullValue = "Đang tính…" });
            storage.SetBinding(ToolTipProperty, new Binding("StorageDetails"));
            storage.SetValue(MarginProperty, new Thickness(12, 0, 0, 0)); storage.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            storage.SetValue(TextBlock.ForegroundProperty, ModernBrush("#334155"));
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Data chiếm dụng", Width = 140, CellTemplate = new DataTemplate { VisualTree = storage } });
            AddBadgeColumn("Trạng thái", "StatusText", "StatusBackground", "StatusForeground", 120, 12);
            AddColumn("Phiên bản", "Version", 130);
            var versionStyle = new Style(typeof(TextBlock)); versionStyle.Setters.Add(new Setter(MarginProperty, new Thickness(12, 0, 0, 0))); versionStyle.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center)); versionStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Consolas"))); versionStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0)); versionStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, ModernBrush("#334155"))); ((DataGridTextColumn)grid.Columns[4]).ElementStyle = versionStyle;
            AddBadgeColumn("Cập nhật", "UpdateStatus", "UpdateBackground", "UpdateForeground", 170, 6);
            AddActionsColumn(); grid.Columns[6].Width = new DataGridLength(1, DataGridLengthUnitType.Star); grid.Columns[6].MinWidth = 302;
            foreach (var column in grid.Columns)
            {
                var caption = new FrameworkElementFactory(typeof(TextBlock)); caption.SetBinding(TextBlock.TextProperty, new Binding { Converter = new TableHeaderConverter() });
                column.HeaderTemplate = new DataTemplate { VisualTree = caption };
            }
        }

        private DataTemplate ProfileAvatarTemplate()
        {
            const double size = 38;
            var circle = new CornerRadius(size / 2);
            var layers = new FrameworkElementFactory(typeof(Grid));
            var avatar = new FrameworkElementFactory(typeof(Border));
            avatar.SetValue(Border.CornerRadiusProperty, circle);
            avatar.SetBinding(Border.BackgroundProperty, new Binding("AvatarBackground"));
            var initials = new FrameworkElementFactory(typeof(TextBlock), "initials");
            initials.SetBinding(TextBlock.TextProperty, new Binding("Initials"));
            initials.SetBinding(VisibilityProperty, new Binding("InitialsVisibility"));
            initials.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            initials.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            initials.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            initials.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            avatar.AppendChild(initials);
            layers.AppendChild(avatar);

            var overlay = new FrameworkElementFactory(typeof(Border), "playOverlay");
            overlay.SetValue(Border.CornerRadiusProperty, circle);
            overlay.SetValue(Border.BackgroundProperty, ModernBrush("#990F172A"));
            overlay.SetValue(OpacityProperty, 0.0);
            overlay.SetValue(IsHitTestVisibleProperty, false);
            var play = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            play.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 0,0 L 11,7 L 0,14 Z"));
            play.SetValue(System.Windows.Shapes.Shape.FillProperty, Brushes.White);
            play.SetValue(MarginProperty, new Thickness(2, 0, 0, 0));
            play.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            play.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            overlay.AppendChild(play);
            layers.AppendChild(overlay);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = layers };
            foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
            {
                var active = new Trigger { Property = property, Value = true };
                active.Setters.Add(new Setter(OpacityProperty, 1.0, "playOverlay"));
                active.Setters.Add(new Setter(OpacityProperty, 0.0, "initials"));
                template.Triggers.Add(active);
            }

            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(WidthProperty, size);
            button.SetValue(HeightProperty, size);
            button.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            button.SetValue(CursorProperty, System.Windows.Input.Cursors.Hand);
            button.SetValue(FocusVisualStyleProperty, null);
            button.SetValue(Button.TemplateProperty, template);
            button.SetValue(Button.TagProperty, "Mở Zalo");
            button.SetValue(ToolTipProperty, "Mở hoặc hiện Zalo");
            button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Mở hoặc hiện Zalo");
            button.AddHandler(Button.ClickEvent, new RoutedEventHandler(RowAction));
            return new DataTemplate { VisualTree = button };
        }

        private void AddBadgeColumn(string header, string text, string background, string foreground, double width, double radius)
        {
            var border = new FrameworkElementFactory(typeof(Border)); border.SetBinding(Border.BackgroundProperty, new Binding(background)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius)); border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4)); border.SetValue(MarginProperty, new Thickness(12, 0, 6, 0)); border.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left); border.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            var content = new FrameworkElementFactory(typeof(TextBlock)); content.SetBinding(TextBlock.TextProperty, new Binding(text)); content.SetBinding(TextBlock.ForegroundProperty, new Binding(foreground)); content.SetBinding(ToolTipProperty, new Binding(text == "StatusText" ? "CdpStatus" : text)); content.SetValue(TextBlock.FontSizeProperty, 11.5); content.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); content.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); content.SetValue(MaxWidthProperty, width - 40); border.AppendChild(content);
            grid.Columns.Add(new DataGridTemplateColumn { Header = header, Width = width, CellTemplate = new DataTemplate { VisualTree = border } });
        }
    }
    public class EmptyTextVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) { return String.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
    public class TableHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) { return String.Equals(value as string, "Action") ? "THAO TÁC" : (value as string ?? "").ToUpperInvariant(); }
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
