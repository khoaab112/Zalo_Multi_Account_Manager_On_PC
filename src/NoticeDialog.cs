using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ShapePath = System.Windows.Shapes.Path;

namespace ZaloManagerPoc
{
    public class NoticeDialog : Window
    {
        public NoticeDialog(string title, string message) : this(title, message, false, null, null, null) { }

        private NoticeDialog(string title, string message, bool success, string detail, string metadata, string path)
        {
            Title = title;
            Width = 576;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            using (var stream = typeof(NoticeDialog).Assembly.GetManifestResourceStream("ZaloManager.Theme.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));

            var layout = new DockPanel();
            Content = new Border { Child = layout, Margin = new Thickness(18), CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1), BorderBrush = ColorBrush("#CBD5E1"), Background = Brushes.White,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = .22 } };
            var footer = new DockPanel { Margin = new Thickness(22, 14, 22, 14) };
            var footerFrame = new Border { Child = footer, Background = ColorBrush("#F8FAFC"), CornerRadius = new CornerRadius(0, 0, 15, 15),
                BorderBrush = ColorBrush("#E2E8F0"), BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(footerFrame, Dock.Bottom);
            layout.Children.Add(footerFrame);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right);
            footer.Children.Add(actions);
            if (path != null)
            {
                var folderLabel = new StackPanel { Orientation = Orientation.Horizontal };
                var folderIcon = NoticeIcon("M 1,15 V 3 H 7 L 9,5 H 17 V 15 Z", "#334155");
                folderIcon.Width = folderIcon.Height = 14;
                folderIcon.Margin = new Thickness(0, 0, 7, 0);
                folderLabel.Children.Add(folderIcon);
                folderLabel.Children.Add(Label("Mở thư mục", 13, "#1E293B"));
                var open = new Button { Content = folderLabel, Height = 42, MinWidth = 128,
                    Margin = new Thickness(0, 0, 10, 0), Style = (Style)FindResource("SecondaryButton") };
                System.Windows.Automation.AutomationProperties.SetName(open, "Mở thư mục");
                open.Click += delegate
                {
                    try
                    {
                        string arguments = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" +
                            (Directory.Exists(path) ? path : Path.GetDirectoryName(path)) + "\"";
                        using (Process.Start("explorer.exe", arguments)) { }
                        DialogResult = true;
                    }
                    catch (Win32Exception error) { Show(this, "Không mở được thư mục", error.Message); }
                };
                actions.Children.Add(open);
            }
            var done = new Button { Content = "Hoàn tất", Height = 42, MinWidth = 108, IsDefault = true,
                Style = (Style)FindResource("PrimaryButton") };
            done.Click += delegate { DialogResult = true; };
            actions.Children.Add(done);
            var hint = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hint.Children.Add(new Border { Child = Label("Esc", 11, "#526175"), Padding = new Thickness(5, 3, 5, 3),
                CornerRadius = new CornerRadius(4), Background = ColorBrush("#F1F5F9"), BorderBrush = ColorBrush("#CBD5E1"), BorderThickness = new Thickness(1) });
            var hintText = Label("để đóng", 13, "#64748B");
            hintText.Margin = new Thickness(7, 0, 0, 0);
            hintText.VerticalAlignment = VerticalAlignment.Center;
            hint.Children.Add(hintText);
            footer.Children.Add(hint);

            var body = new StackPanel { Margin = new Thickness(22, 22, 22, 20) };
            layout.Children.Add(body);
            var header = new DockPanel { LastChildFill = true };
            body.Children.Add(header);
            var close = new Button { Content = "×", Width = 32, Height = 32, FontSize = 18, Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top, Style = (Style)FindResource("SecondaryButton"), ToolTip = "Đóng" };
            System.Windows.Automation.AutomationProperties.SetName(close, "Đóng");
            close.Click += delegate { DialogResult = false; };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            var badge = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(26),
                Background = ColorBrush(success ? "#DCFCE7" : "#FEF3C7"), VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 15, 0), Child = NoticeIcon(success ? "M 2,10 L 7,15 L 17,4" : "M 9,1 L 17,16 H 1 Z M 9,6 V 10 M 9,13 V 13.2",
                    success ? "#16A34A" : "#A16207") };
            header.Children.Add(badge);
            var titles = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var heading = Label(title, 20, "#0F172A");
            heading.FontWeight = FontWeights.Bold;
            titles.Children.Add(heading);
            var description = Label(message, 14, "#475569");
            titles.Children.Add(new ScrollViewer { Content = description, Margin = new Thickness(0, 5, 0, 0), MaxHeight = 185,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            header.Children.Add(titles);

            if (!String.IsNullOrWhiteSpace(detail))
            {
                var card = new DockPanel();
                var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(9), Background = ColorBrush("#EAF2FF"),
                    Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = NoticeIcon(path == null ? "M 2,16 H 16 M 4,16 V 5 H 14 V 16 M 7,8 H 11 M 7,11 H 11" :
                        "M 3,1 H 10 L 15,6 V 17 H 3 Z M 10,1 V 6 H 15 M 6,10 H 12 M 6,13 H 12", "#0068FF") };
                card.Children.Add(tile);
                var information = new StackPanel();
                var name = Label(detail, 14, "#0F172A");
                name.FontWeight = FontWeights.SemiBold;
                information.Children.Add(name);
                if (!String.IsNullOrWhiteSpace(metadata))
                {
                    var meta = Label(metadata, 12, "#64748B");
                    meta.Margin = new Thickness(0, 3, 0, 0);
                    information.Children.Add(meta);
                }
                card.Children.Add(information);
                body.Children.Add(new Border { Child = new ScrollViewer { Content = card, MaxHeight = 145,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                    Margin = new Thickness(0, 17, 0, 0), Padding = new Thickness(12), CornerRadius = new CornerRadius(11),
                    Background = ColorBrush("#F8FAFC"), BorderBrush = ColorBrush("#D8E0EA"), BorderThickness = new Thickness(1) });
            }

            Loaded += delegate { done.Focus(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                DialogResult = false;
            };
        }

        private static TextBlock Label(string text, double size, string color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = ColorBrush(color), TextWrapping = TextWrapping.Wrap };
        }

        private static Brush ColorBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }

        private static ShapePath NoticeIcon(string data, string color)
        {
            return new ShapePath { Data = Geometry.Parse(data), Stroke = ColorBrush(color), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Width = 20, Height = 20, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
        }

        public static void Show(Window owner, string title, string message)
        {
            ShowOwned(owner, new NoticeDialog(title, message));
        }

        internal static void Success(Window owner, string title, string message, string detail = null, string metadata = null, string path = null)
        {
            ShowOwned(owner, new NoticeDialog(title, message, true, detail, metadata, path));
        }

        internal static void FileSaved(Window owner, string path, long bytes, string title = "Tải tệp thành công")
        {
            Success(owner, title, "Tệp đã được lưu về máy.", Path.GetFileName(path),
                StorageMeasurement.Format(bytes) + " · " + Path.GetDirectoryName(path), path);
        }

        private static void ShowOwned(Window owner, NoticeDialog dialog)
        {
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowDialog();
        }
    }
}
