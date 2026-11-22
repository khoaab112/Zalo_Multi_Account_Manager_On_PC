using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZaloManagerPoc
{
    public partial class SourcePicker
    {
        private Border statusBox;
        private Ellipse statusDot;
        private static Brush PickerBrush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
        private void InitializePickerDesign()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Grid design;
            using (var stream = typeof(SourcePicker).Assembly.GetManifestResourceStream("ZaloManager.Picker.xaml"))
                design = (Grid)XamlReader.Load(stream);
            Content = design;
            preview = (TextBlock)design.FindName("TxtStatus");
            statusBox = (Border)design.FindName("StatusBox"); statusDot = (Ellipse)design.FindName("StatusDot");
            var body = (StackPanel)statusBox.Parent;
            clickedApplication.FontSize = 12; clickedApplication.Foreground = PickerBrush("#334155");
            clickedApplication.Margin = new Thickness(0, 10, 0, 0); clickedApplication.Visibility = Visibility.Collapsed;
            body.Children.Add(clickedApplication);
            var card = (Border)design.Children[0]; var layout = (Grid)card.Child;
            var header = (Border)layout.Children[0];
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ChangedButton != MouseButton.Left || args.ButtonState != MouseButtonState.Pressed) return;
                var element = args.OriginalSource as DependencyObject;
                while (element != null && element != header)
                {
                    if (element is Button) return;
                    element = VisualTreeHelper.GetParent(element);
                }
                overlay.Hide(); targetBounds = Rect.Empty; hasPoint = false;
                DragMove(); args.Handled = true;
            };
            foreach (var button in DescendantButtons(design)) button.Click += delegate { DialogResult = false; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            { if (args.Key == Key.Escape) { DialogResult = false; args.Handled = true; } };
            SetHoverStatus("Rê chuột ra ngoài bảng chọn, đến ứng dụng Zalo.", false);
        }
        private static System.Collections.Generic.IEnumerable<Button> DescendantButtons(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var button = child as Button;
                if (button != null) yield return button;
                foreach (var nested in DescendantButtons(child)) yield return nested;
            }
        }
        public void SetHoverStatus(string message, bool isZaloTarget, bool error = false)
        {
            preview.Text = message;
            statusBox.Background = PickerBrush(error ? "#FEF2F2" : isZaloTarget ? "#ECFDF5" : "#F8FAFC");
            statusBox.BorderBrush = PickerBrush(error ? "#FECACA" : isZaloTarget ? "#A7F3D0" : "#E2E8F0");
            statusDot.Fill = PickerBrush(error ? "#EF4444" : isZaloTarget ? "#10B981" : "#94A3B8");
            preview.Foreground = PickerBrush(error ? "#B91C1C" : isZaloTarget ? "#065F46" : "#64748B");
            preview.FontWeight = isZaloTarget ? FontWeights.SemiBold : FontWeights.Normal;
            clickedApplication.Visibility = String.IsNullOrEmpty(clickedApplication.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
