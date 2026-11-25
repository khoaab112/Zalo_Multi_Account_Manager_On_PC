using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private Border profileTableLoading;
        private TextBlock profileLoadingCaption;

        private Border CreateProfileTableLoading()
        {
            var frames = LoadProfileLoadingFrames();
            var animation = new Image { Source = frames[0], Width = 92, Stretch = Stretch.Uniform };
            profileLoadingCaption = new TextBlock
            {
                Text = "Đang tải hồ sơ…",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(animation);
            content.Children.Add(profileLoadingCaption);
            profileTableLoading = new Border
            {
                Background = ModernBrush("#990F172A"),
                CornerRadius = new CornerRadius(11),
                Child = content,
                Visibility = loadingOnStartup || busy ? Visibility.Visible : Visibility.Collapsed
            };

            int frame = 0;
            var animationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
            animationTimer.Tick += delegate
            {
                frame = (frame + 1) % frames.Length;
                animation.Source = frames[frame];
            };
            profileTableLoading.IsVisibleChanged += delegate
            {
                animationTimer.Stop();
                frame = 0;
                animation.Source = frames[0];
                if (profileTableLoading.IsVisible && SystemParameters.ClientAreaAnimation) animationTimer.Start();
            };
            return profileTableLoading;
        }

        private static BitmapSource[] LoadProfileLoadingFrames()
        {
            using (var stream = typeof(ManagerWindow).Assembly.GetManifestResourceStream("ZaloManager.ProfileLoading.gif"))
            using (var gif = System.Drawing.Image.FromStream(stream))
            using (var canvas = new System.Drawing.Bitmap(gif.Width, gif.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(canvas))
            using (var png = new MemoryStream())
            {
                var dimension = System.Drawing.Imaging.FrameDimension.Time;
                var frames = new BitmapSource[gif.GetFrameCount(dimension)];
                for (int index = 0; index < frames.Length; index++)
                {
                    gif.SelectActiveFrame(dimension, index);
                    graphics.Clear(System.Drawing.Color.Transparent);
                    graphics.DrawImageUnscaled(gif, 0, 0);
                    png.SetLength(0);
                    canvas.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    png.Position = 0;
                    var frame = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    frames[index] = frame;
                }
                return frames;
            }
        }
    }
}
