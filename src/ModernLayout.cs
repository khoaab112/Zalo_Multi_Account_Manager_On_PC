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
        private TextBlock profileCount;

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

            var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            heading.Children.Add(new Image { Source = logo, Width = 48, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "Quản lý hồ sơ Zalo", FontSize = 22, FontWeight = FontWeights.Bold });
            titles.Children.Add(new TextBlock { Text = "Mỗi hồ sơ có bản sao chương trình và dữ liệu riêng. Đăng nhập trực tiếp trong cửa sổ Zalo.", FontSize = 12.5, Foreground = ModernBrush("#64748B"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            heading.Children.Add(titles); root.Children.Add(heading);

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
            toolbar.Margin = new Thickness(0); name.Style = (Style)Resources["ModernTextBox"]; name.Width = 300; name.Height = 42; name.Margin = new Thickness(0); name.Text = "";
            toolbar.Children.Remove(name);
            var input = new Grid(); input.Children.Add(name);
            var placeholder = new TextBlock { Text = "Nhập tên tài khoản mới…", IsHitTestVisible = false, Foreground = ModernBrush("#94A3B8"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            placeholder.SetBinding(VisibilityProperty, new Binding("Text") { Source = name, Converter = new EmptyTextVisibilityConverter() }); input.Children.Add(placeholder); toolbar.Children.Insert(0, input);
            foreach (var child in toolbar.Children)
            {
                var button = child as Button;
                if (button == null) continue;
                button.Style = (Style)Resources["PrimaryButton"]; button.Content = "+ Thêm hồ sơ"; button.Height = 42; button.Margin = new Thickness(10, 0, 0, 0); button.Padding = new Thickness(16, 9, 16, 9);
            }
            addRow.Children.Add(toolbar);
            profileCount = new TextBlock { FontSize = 13, Foreground = ModernBrush("#64748B"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(profileCount, 1); addRow.Children.Add(profileCount); Grid.SetRow(addRow, 2); root.Children.Add(addRow);
            StyleProfileTable();
            var table = Card(grid); table.ClipToBounds = true; Grid.SetRow(table, 3); root.Children.Add(table);
            grid.SizeChanged += delegate
            {
                grid.Clip = new RectangleGeometry(new Rect(0, 0, grid.ActualWidth, grid.ActualHeight), 11, 11);
                bool compact = grid.ActualWidth < 1150;
                double[] widths = compact ? new double[] { 56, 130, 120, 110, 108, 120 } : new double[] { 72, 170, 140, 120, 130, 170 };
                for (int index = 0; index < widths.Length; index++)
                    if (grid.Columns[index].Width.Value != widths[index]) grid.Columns[index].Width = new DataGridLength(widths[index]);
            };
            status.FontSize = 12.5; status.Foreground = ModernBrush("#64748B"); status.Margin = new Thickness(0, 12, 0, 0); status.Text = "Sẵn sàng · Cập nhật được thực hiện thủ công.";
            Grid.SetRow(status, 4); root.Children.Add(status); RefreshStatus();
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
            var avatar = new FrameworkElementFactory(typeof(Border)); avatar.SetValue(WidthProperty, 38.0); avatar.SetValue(HeightProperty, 38.0); avatar.SetValue(Border.CornerRadiusProperty, new CornerRadius(19)); avatar.SetBinding(Border.BackgroundProperty, new Binding("AvatarBackground"));
            var initials = new FrameworkElementFactory(typeof(TextBlock)); initials.SetBinding(TextBlock.TextProperty, new Binding("Initials")); initials.SetBinding(VisibilityProperty, new Binding("InitialsVisibility")); initials.SetValue(TextBlock.ForegroundProperty, Brushes.White); initials.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); initials.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); initials.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); avatar.AppendChild(initials);
            grid.Columns.Add(new DataGridTemplateColumn { Header = "Avatar", Width = 72, CellTemplate = new DataTemplate { VisualTree = avatar } });
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
            AddActionsColumn(); grid.Columns[6].Width = new DataGridLength(1, DataGridLengthUnitType.Star); grid.Columns[6].MinWidth = 350;
            foreach (var column in grid.Columns)
            {
                var caption = new FrameworkElementFactory(typeof(TextBlock)); caption.SetBinding(TextBlock.TextProperty, new Binding { Converter = new TableHeaderConverter() });
                column.HeaderTemplate = new DataTemplate { VisualTree = caption };
            }
        }
        private void AddBadgeColumn(string header, string text, string background, string foreground, double width, double radius)
        {
            var border = new FrameworkElementFactory(typeof(Border)); border.SetBinding(Border.BackgroundProperty, new Binding(background)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius)); border.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4)); border.SetValue(MarginProperty, new Thickness(12, 0, 6, 0)); border.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left); border.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            var content = new FrameworkElementFactory(typeof(TextBlock)); content.SetBinding(TextBlock.TextProperty, new Binding(text)); content.SetBinding(TextBlock.ForegroundProperty, new Binding(foreground)); content.SetBinding(ToolTipProperty, new Binding(text)); content.SetValue(TextBlock.FontSizeProperty, 11.5); content.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); content.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); content.SetValue(MaxWidthProperty, width - 40); border.AppendChild(content);
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
