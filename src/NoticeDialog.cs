using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZaloManagerPoc
{
    public class NoticeDialog : Window
    {
        public NoticeDialog(string title, string message)
        {
            Title = title; Width = 420; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            using (var stream = typeof(NoticeDialog).Assembly.GetManifestResourceStream("ZaloManager.Theme.xaml"))
                Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
            var layout = new Grid(); Content = layout;
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new Grid { Margin = new Thickness(24, 26, 24, 24), MinHeight = 58 };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var warning = new Viewbox { Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Top };
            var icon = new Grid { Width = 48, Height = 48 };
            icon.Children.Add(new Path { Fill = new SolidColorBrush(Color.FromRgb(255, 171, 0)), Data = Geometry.Parse("M21,5 Q24,0 27,5 L45,37 Q48,42 42,42 L6,42 Q0,42 3,37 Z") });
            icon.Children.Add(new Path { Fill = Brushes.White, Data = Geometry.Parse("M22,15 L26,15 L26,28 L22,28 Z M24,31 A2,2 0 1 0 24,35 A2,2 0 1 0 24,31") });
            warning.Child = icon; body.Children.Add(warning);
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)), FontSize = 13.5, Margin = new Thickness(16, 2, 0, 0), LineHeight = 20 };
            var scroll = new ScrollViewer { Content = text, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(scroll, 1); body.Children.Add(scroll); layout.Children.Add(body);
            var footer = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)), Padding = new Thickness(24, 13, 24, 13) };
            var ok = new Button { Content = "OK", Width = 88, Height = 36, Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true, Style = (Style)FindResource("PrimaryButton") };
            ok.Click += delegate { DialogResult = true; };
            footer.Child = ok; Grid.SetRow(footer, 1); layout.Children.Add(footer);
            Loaded += delegate { ok.Focus(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) { DialogResult = false; args.Handled = true; } };
        }
        public static void Show(Window owner, string title, string message)
        {
            var dialog = new NoticeDialog(title, message);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowDialog();
        }
    }
}
