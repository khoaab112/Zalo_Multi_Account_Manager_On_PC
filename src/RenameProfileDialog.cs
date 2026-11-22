using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZaloManagerPoc
{
    public class RenameProfileDialog : Window
    {
        private readonly TextBox input;
        public string NewProfileName { get { return input.Text.Trim(); } }
        public RenameProfileDialog(string currentName, ResourceDictionary theme)
        {
            Title = "Đổi tên hồ sơ"; Width = 380; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Resources.MergedDictionaries.Add(theme);
            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20) }; Content = body;
            body.Children.Add(new TextBlock { Text = "Nhập tên Zalo của tài khoản này", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 12) });
            input = new TextBox { Text = currentName ?? "", Style = (Style)FindResource("ModernTextBox"), Height = 38, VerticalContentAlignment = VerticalAlignment.Center };
            body.Children.Add(input);
            var error = new TextBlock { Text = "Vui lòng nhập tên hồ sơ.", Foreground = Brushes.Firebrick, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            body.Children.Add(error);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancel = new Button { Content = "Hủy", IsCancel = true, Style = (Style)FindResource("SecondaryButton"), MinWidth = 80, Height = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { DialogResult = false; };
            var save = new Button { Content = "Lưu tên", IsDefault = true, Style = (Style)FindResource("PrimaryButton"), MinWidth = 92, Height = 36, Padding = new Thickness(14, 6, 14, 6) };
            save.Click += delegate
            {
                if (String.IsNullOrWhiteSpace(input.Text)) { error.Visibility = Visibility.Visible; input.Focus(); return; }
                DialogResult = true;
            };
            input.TextChanged += delegate { error.Visibility = Visibility.Collapsed; };
            actions.Children.Add(cancel); actions.Children.Add(save); body.Children.Add(actions);
            Loaded += delegate { input.Focus(); input.SelectAll(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            { if (args.Key == Key.Escape) { DialogResult = false; args.Handled = true; } };
        }
    }
}
