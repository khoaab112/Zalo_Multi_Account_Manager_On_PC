using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace ZaloManagerPoc
{
    public class NoticeDialog : Window
    {
        private static readonly TimeSpan AutoCloseDuration = TimeSpan.FromSeconds(3);
        private static NoticeDialog activeToast;
        private readonly DispatcherTimer autoCloseTimer;
        private readonly Border progressFill;
        private bool closed;

        private static readonly Lazy<ResourceDictionary> CachedTheme = new Lazy<ResourceDictionary>(LoadTheme);
        private static ResourceDictionary LoadTheme()
        {
            using (var stream = typeof(NoticeDialog).Assembly.GetManifestResourceStream("ZaloManager.Theme.xaml"))
                return (ResourceDictionary)XamlReader.Load(stream);
        }

        public NoticeDialog(string title, string message) : this(title, message, false, null, null, null) { }

        private NoticeDialog(string title, string message, bool success, string detail, string metadata, string path)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Resources.MergedDictionaries.Add(CachedTheme.Value);

            var layout = new DockPanel();
            var outer = new Border
            {
                Child = layout,
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = ColorBrush("#CBD5E1"),
                Background = Brushes.White,
                ClipToBounds = true,
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = .18 }
            };
            Content = outer;

            var progressTrack = new Grid { Height = 3, Background = ColorBrush("#F1F5F9") };
            progressFill = new Border
            {
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 392,
                Background = ColorBrush(success ? "#16A34A" : "#D97706")
            };
            progressTrack.Children.Add(progressFill);
            DockPanel.SetDock(progressTrack, Dock.Bottom);
            layout.Children.Add(progressTrack);

            if (path != null)
            {
                var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
                var footerFrame = new Border
                {
                    Child = footer,
                    Background = ColorBrush("#F8FAFC"),
                    BorderBrush = ColorBrush("#E2E8F0"),
                    BorderThickness = new Thickness(0, 1, 0, 0)
                };
                DockPanel.SetDock(footerFrame, Dock.Bottom);
                layout.Children.Add(footerFrame);
                var folderLabel = new StackPanel { Orientation = Orientation.Horizontal };
                var folderIcon = NoticeIcon("M 1,15 V 3 H 7 L 9,5 H 17 V 15 Z", "#334155");
                folderIcon.Width = folderIcon.Height = 14;
                folderIcon.Margin = new Thickness(0, 0, 7, 0);
                folderLabel.Children.Add(folderIcon);
                folderLabel.Children.Add(Label("Mở thư mục", 12.5, "#1E293B"));
                var open = new Button
                {
                    Content = folderLabel,
                    Height = 34,
                    MinWidth = 118,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Style = (Style)FindResource("SecondaryButton")
                };
                System.Windows.Automation.AutomationProperties.SetName(open, "Mở thư mục");
                open.Click += delegate
                {
                    try
                    {
                        string arguments = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" +
                            (Directory.Exists(path) ? path : Path.GetDirectoryName(path)) + "\"";
                        using (Process.Start("explorer.exe", arguments)) { }
                        Dismiss();
                    }
                    catch (Win32Exception error) { Show(Owner, "Không mở được thư mục", error.Message); }
                };
                footer.Children.Add(open);
            }

            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            layout.Children.Add(body);
            var header = new DockPanel { LastChildFill = true };
            body.Children.Add(header);
            var close = new Button
            {
                Content = "×",
                Width = 28,
                Height = 28,
                FontSize = 16,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Top,
                Style = (Style)FindResource("SecondaryButton"),
                ToolTip = "Đóng"
            };
            System.Windows.Automation.AutomationProperties.SetName(close, "Đóng");
            close.Click += delegate { Dismiss(); };
            DockPanel.SetDock(close, Dock.Right);
            header.Children.Add(close);
            var badge = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(19),
                Background = ColorBrush(success ? "#DCFCE7" : "#FEF3C7"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
                Child = NoticeIcon(success ? "M 2,10 L 7,15 L 17,4" : "M 9,1 L 17,16 H 1 Z M 9,6 V 10 M 9,13 V 13.2",
                    success ? "#16A34A" : "#A16207")
            };
            header.Children.Add(badge);
            var titles = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var heading = Label(title, 15, "#0F172A");
            heading.FontWeight = FontWeights.Bold;
            titles.Children.Add(heading);
            var description = Label(message, 13, "#475569");
            titles.Children.Add(new ScrollViewer
            {
                Content = description,
                Margin = new Thickness(0, 3, 0, 0),
                MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            });
            header.Children.Add(titles);

            autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDuration };
            autoCloseTimer.Tick += delegate { Dismiss(); };

            Loaded += delegate
            {
                PositionToast();
                StartCountdown();
            };
            SizeChanged += delegate { PositionToast(); };
            Closed += delegate
            {
                closed = true;
                autoCloseTimer.Stop();
                if (activeToast == this) activeToast = null;
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                Dismiss();
            };
        }

        private void StartCountdown()
        {
            autoCloseTimer.Stop();
            autoCloseTimer.Start();
            var shrink = new DoubleAnimation(progressFill.ActualWidth > 0 ? progressFill.ActualWidth : 392, 0, AutoCloseDuration);
            progressFill.BeginAnimation(WidthProperty, shrink);
        }

        private void PositionToast()
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : 140;
            var source = PresentationSource.FromVisual(this);
            var fromDevice = source != null && source.CompositionTarget != null
                ? source.CompositionTarget.TransformFromDevice
                : Matrix.Identity;

            if (Owner != null && Owner.IsVisible && Owner.WindowState != WindowState.Minimized)
            {
                var topRight = fromDevice.Transform(Owner.PointToScreen(new Point(Owner.ActualWidth, 0)));
                var handle = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
                var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                var workTopLeft = fromDevice.Transform(new Point(screen.Left, screen.Top));
                var workBottomRight = fromDevice.Transform(new Point(screen.Right, screen.Bottom));

                double targetLeft = topRight.X - width - 14;
                double targetTop = topRight.Y + 12;
                Left = Math.Max(workTopLeft.X, Math.Min(targetLeft, workBottomRight.X - width - 8));
                Top = Math.Max(workTopLeft.Y, Math.Min(targetTop, workBottomRight.Y - height - 8));
            }
            else
            {
                var area = SystemParameters.WorkArea;
                Left = area.Right - width - 12;
                Top = area.Top + 12;
            }
        }

        private void Dismiss()
        {
            if (closed) return;
            closed = true;
            autoCloseTimer.Stop();
            Close();
        }

        private static TextBlock Label(string text, double size, string color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = ColorBrush(color), TextWrapping = TextWrapping.Wrap };
        }

        private static Brush ColorBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }

        private static ShapePath NoticeIcon(string data, string color)
        {
            return new ShapePath
            {
                Data = Geometry.Parse(data),
                Stroke = ColorBrush(color),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
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
            if (activeToast != null && activeToast != dialog)
            {
                try { activeToast.Dismiss(); } catch { }
            }
            activeToast = dialog;
            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
                dialog.Show();
            }
            else
            {
                dialog.Topmost = true;
                dialog.ShowDialog();
            }
        }
    }
}
