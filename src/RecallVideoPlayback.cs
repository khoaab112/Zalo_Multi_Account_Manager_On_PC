using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZaloManagerPoc
{
    public partial class ManagerWindow
    {
        private void PlayRecallVideo(CdpMessageWatcher.RecalledMessage message, Window owner)
        {
            string path = null;
            try
            {
                path = WritePlaybackFile(message);
                CreateRecallVideoWindow(message, owner, new Uri(path)).ShowDialog();
            }
            catch (Exception error)
            {
                if (!IsPlaybackFileError(error)) throw;
                NoticeDialog.Show(owner, "Không mở được video", error.Message);
            }
            finally { DeletePlaybackFile(path); }
        }

        private Window CreateRecallVideoWindow(CdpMessageWatcher.RecalledMessage message, Window owner, Uri source)
        {
            var viewer = RecallMediaWindow(owner, RecallAttachmentName(message), 660);
            viewer.Width = 1060;
            viewer.MinWidth = 640;
            viewer.MinHeight = 400;
            viewer.Background = ModernBrush("#0F141E");
            viewer.Foreground = ModernBrush("#F1F5F9");

            var rotation = new RotateTransform();
            var player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.Uniform, LayoutTransform = rotation, Volume = 0.8 };
            var status = new TextBlock { Text = "Đang mở video…", Foreground = ModernBrush("#CBD5E1"),
                Background = ModernBrush("#DD0F141E"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var stage = new Grid { Background = Brushes.Black, Margin = new Thickness(16, 0, 16, 0), ClipToBounds = true };
            stage.Children.Add(player);
            stage.Children.Add(status);

            var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 12, 16, 12) };
            var download = VideoControl("↓  Tải về", "Tải video về", delegate { SaveRecallMedia(message, viewer); });
            top.Children.Add(download);
            var rotate = VideoControl("↻", "Xoay video 90°", delegate { rotation.Angle = (rotation.Angle + 90) % 360; });
            top.Children.Add(rotate);

            bool ready = false, playing = false, ended = false, updating = false, fullscreen = false;
            WindowState previousState = WindowState.Normal;
            WindowStyle previousStyle = viewer.WindowStyle;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            var sliderStyle = VideoSliderStyle();
            var timeline = VideoSlider("Tiến trình video", 1, 0, sliderStyle);
            timeline.IsEnabled = false;
            var time = new TextBlock { Text = "00:00 / --:--", MinWidth = 90, FontSize = 12,
                Foreground = ModernBrush("#E2E8F0"), VerticalAlignment = VerticalAlignment.Center };
            var progress = new DockPanel();
            DockPanel.SetDock(time, Dock.Right);
            time.Margin = new Thickness(12, 0, 0, 0);
            progress.Children.Add(time);
            progress.Children.Add(timeline);
            var volume = VideoSlider("Âm lượng", 1, player.Volume, sliderStyle);
            volume.Width = 84;
            volume.SmallChange = 0.05;

            Action refresh = delegate
            {
                TimeSpan position = ended && player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : player.Position;
                if (!timeline.IsMouseCaptureWithin)
                {
                    updating = true;
                    timeline.Value = position.TotalSeconds;
                    updating = false;
                }
                time.Text = PlaybackTime(position) + " / " + (player.NaturalDuration.HasTimeSpan
                    ? PlaybackTime(player.NaturalDuration.TimeSpan) : "--:--");
            };
            Action<double> seek = delegate(double seconds)
            {
                if (!ready || !player.NaturalDuration.HasTimeSpan) return;
                ended = false;
                player.Position = TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, timeline.Maximum)));
                refresh();
            };

            var play = VideoControl("▶", "Phát video", null);
            Action syncPlay = delegate { SetVideoControl(play, playing ? "Ⅱ" : "▶", playing ? "Tạm dừng video" : "Phát video"); };
            Action togglePlay = delegate
            {
                if (!ready) return;
                if (playing) { player.Pause(); timer.Stop(); }
                else
                {
                    if (ended) seek(0);
                    player.Play();
                    timer.Start();
                }
                playing = !playing;
                syncPlay();
                refresh();
            };
            play.Click += delegate { togglePlay(); };
            var restart = VideoControl("↶", "Phát lại từ đầu", delegate
            {
                if (!ready) return;
                seek(0);
                player.Play();
                playing = true;
                timer.Start();
                syncPlay();
            });
            var mute = VideoControl("Loa", "Tắt tiếng", null);
            Action syncMute = delegate { SetVideoControl(mute, player.IsMuted ? "Tắt tiếng" : "Loa", player.IsMuted ? "Bật tiếng" : "Tắt tiếng"); };
            Action toggleMute = delegate { player.IsMuted = !player.IsMuted; syncMute(); };
            mute.Click += delegate { toggleMute(); };
            volume.ValueChanged += delegate { player.Volume = volume.Value; if (player.IsMuted) toggleMute(); };
            var rate = VideoControl("1×", "Tốc độ phát", null);
            double[] rates = { 1, 1.25, 1.5, 2, 0.75 };
            int rateIndex = 0;
            rate.Click += delegate
            {
                if (!ready) return;
                rateIndex = (rateIndex + 1) % rates.Length;
                player.SpeedRatio = rates[rateIndex];
                SetVideoControl(rate, rates[rateIndex].ToString("0.##") + "×", "Tốc độ phát " + rates[rateIndex].ToString("0.##") + "×");
            };
            var fill = VideoControl("⛶", "Toàn màn hình", null);
            Action toggleFullscreen = delegate
            {
                if (!fullscreen)
                {
                    previousState = viewer.WindowState;
                    previousStyle = viewer.WindowStyle;
                    viewer.WindowState = WindowState.Normal;
                    viewer.WindowStyle = WindowStyle.None;
                    viewer.WindowState = WindowState.Maximized;
                }
                else
                {
                    viewer.WindowState = WindowState.Normal;
                    viewer.WindowStyle = previousStyle;
                    viewer.WindowState = previousState;
                }
                fullscreen = !fullscreen;
                SetVideoControl(fill, "⛶", fullscreen ? "Thoát toàn màn hình" : "Toàn màn hình");
            };
            fill.Click += delegate { toggleFullscreen(); };

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(play);
            left.Children.Add(restart);
            left.Children.Add(mute);
            left.Children.Add(volume);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(rate);
            right.Children.Add(fill);
            var controls = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            DockPanel.SetDock(right, Dock.Right);
            controls.Children.Add(right);
            controls.Children.Add(left);
            var panel = new StackPanel();
            panel.Children.Add(progress);
            panel.Children.Add(controls);
            var bar = new Border { Child = panel, Margin = new Thickness(16, 12, 16, 14), Padding = new Thickness(13, 6, 13, 9),
                Background = ModernBrush("#1A212E"), BorderBrush = ModernBrush("#303A4D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15) };
            var layout = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(bar, Dock.Bottom);
            layout.Children.Add(top);
            layout.Children.Add(bar);
            layout.Children.Add(stage);
            viewer.Content = layout;

            play.IsEnabled = restart.IsEnabled = rate.IsEnabled = false;
            timeline.ValueChanged += delegate { if (!updating) seek(timeline.Value); };
            timer.Tick += delegate { if (ready && playing) refresh(); };
            player.MediaOpened += delegate
            {
                ready = playing = true;
                status.Visibility = Visibility.Collapsed;
                play.IsEnabled = restart.IsEnabled = rate.IsEnabled = true;
                if (player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan > TimeSpan.Zero)
                {
                    timeline.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds;
                    timeline.IsEnabled = true;
                }
                syncPlay();
                refresh();
                timer.Start();
            };
            player.MediaEnded += delegate { timer.Stop(); ended = true; playing = false; syncPlay(); refresh(); };
            player.MediaFailed += delegate
            {
                ready = playing = false;
                timer.Stop();
                play.IsEnabled = restart.IsEnabled = rate.IsEnabled = timeline.IsEnabled = false;
                syncPlay();
                status.Text = "Windows không phát được định dạng này. Bạn có thể tải video về.";
                status.Visibility = Visibility.Visible;
            };
            viewer.Loaded += delegate { player.Source = source; player.Play(); };
            viewer.Closed += delegate { timer.Stop(); player.Close(); };
            stage.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ClickCount == 2) toggleFullscreen();
            };
            viewer.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                switch (args.Key)
                {
                    case Key.Escape: if (fullscreen) toggleFullscreen(); else viewer.Close(); break;
                    case Key.Space: togglePlay(); break;
                    case Key.Left: if (volume.IsKeyboardFocusWithin) return; seek(timeline.Value - 5); break;
                    case Key.Right: if (volume.IsKeyboardFocusWithin) return; seek(timeline.Value + 5); break;
                    case Key.M: toggleMute(); break;
                    case Key.F:
                    case Key.F11: toggleFullscreen(); break;
                    default: return;
                }
                args.Handled = true;
            };
            return viewer;
        }

        private static Button VideoControl(string text, string label, RoutedEventHandler click)
        {
            var button = new Button { Template = RecallButtonTemplate(16), MinWidth = 36, Height = 34,
                Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1),
                BorderBrush = ModernBrush("#3D4B62"), Background = ModernBrush("#283247"), Foreground = Brushes.White,
                FontSize = 14, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            SetVideoControl(button, text, label);
            if (click != null) button.Click += click;
            return button;
        }

        private static void SetVideoControl(Button button, string text, string label)
        {
            button.Content = text;
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }

        private static Slider VideoSlider(string label, double maximum, double value, Style style)
        {
            var slider = new Slider { Maximum = maximum, Value = value, Style = style, IsMoveToPointEnabled = true, Cursor = Cursors.Hand };
            AutomationProperties.SetName(slider, label);
            return slider;
        }

        private static Style VideoSliderStyle()
        {
            return (Style)XamlReader.Parse(@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Slider"">
    <Setter Property=""Height"" Value=""24"" />
    <Setter Property=""VerticalAlignment"" Value=""Center"" />
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""Slider"">
                <Track x:Name=""PART_Track"">
                    <Track.Resources>
                        <Style TargetType=""RepeatButton"">
                            <Setter Property=""Focusable"" Value=""False"" />
                            <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""RepeatButton"">
                                <Border Background=""Transparent""><Border Height=""4"" CornerRadius=""2"" Background=""{TemplateBinding Background}"" /></Border>
                            </ControlTemplate></Setter.Value></Setter>
                        </Style>
                    </Track.Resources>
                    <Track.DecreaseRepeatButton><RepeatButton Command=""Slider.DecreaseLarge"" Background=""#2481FF"" /></Track.DecreaseRepeatButton>
                    <Track.IncreaseRepeatButton><RepeatButton Command=""Slider.IncreaseLarge"" Background=""#586376"" /></Track.IncreaseRepeatButton>
                    <Track.Thumb><Thumb Width=""12"" Height=""12""><Thumb.Template><ControlTemplate TargetType=""Thumb"">
                        <Ellipse Fill=""#F1F5F9"" />
                    </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                </Track>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");
        }
    }
}
